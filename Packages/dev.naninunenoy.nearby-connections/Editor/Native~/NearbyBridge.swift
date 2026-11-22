import Foundation
import NearbyConnections

// All mutable state is confined to the main queue, including SDK completion handlers.
private final class NearbyBridge: AdvertiserDelegate, DiscovererDelegate, ConnectionManagerDelegate {
    let manager: ConnectionManager
    let advertiser: Advertiser
    let discoverer: Discoverer
    var closed = false
    var messages: [([String: Any], Data)] = []
    var queuedBytes = 0
    var payload = Data()
    var names: [String: String] = [:]
    var incoming: Set<String> = []
    var connected: Set<String> = []
    var nextRequest: Int64 = 0
    var requestByEndpoint: [String: Int64] = [:]
    var verifications: [Int64: (Bool) -> Void] = [:]

    init(service: String, strategy: Int32) {
        manager = ConnectionManager(serviceID: service, strategy: strategy == 0 ? .pointToPoint : .star)
        advertiser = Advertiser(connectionManager: manager)
        discoverer = Discoverer(connectionManager: manager)
        manager.delegate = self; advertiser.delegate = self; discoverer.delegate = self
    }
    func enqueue(_ fields: [String: Any], _ bytes: Data = Data()) {
        guard !closed else { return }
        if messages.count >= 256 || queuedBytes + bytes.count > 4 * 1024 * 1024 {
            messages = [(["kind": 7], Data())]; queuedBytes = 0
            return
        }
        if messages.first?.0["kind"] as? Int == 7 { return }
        messages.append((fields, bytes)); queuedBytes += bytes.count
    }
    func complete(_ operation: Int32, _ error: Error?) {
        DispatchQueue.main.async { [weak self] in
            guard let self = self, !self.closed else { return }
            let e = error as NSError?
            self.enqueue(["kind": 0, "operation": operation, "error": e == nil ? 0 : 7,
                          "nativeCode": e?.code ?? 0, "text": e.map { "\($0.domain): \($0.localizedDescription)" } ?? ""])
        }
    }
    func submit(_ operation: Int32, _ command: Int32, _ endpoint: String, _ name: String, _ request: Int64, _ bytes: Data) {
        guard !closed else { return }
        let done: (Error?) -> Void = { [weak self] error in self?.complete(operation, error) }
        switch command {
        case 0: advertiser.startAdvertising(using: Data(name.utf8), mediums: [.ble, .wifiLAN], completionHandler: done)
        case 1: advertiser.stopAdvertising(completionHandler: done)
        case 2: discoverer.startDiscovery(mediums: [.ble, .wifiLAN], completionHandler: done)
        case 3: discoverer.stopDiscovery(completionHandler: done)
        case 4: discoverer.requestConnection(to: endpoint, using: Data(name.utf8), mediums: [.ble, .wifiLAN], completionHandler: done)
        case 5, 6:
            guard let handler = verifications.removeValue(forKey: request) else {
                enqueue(["kind": 0, "operation": operation, "error": 4, "text": "Unknown or already answered request."]); return
            }
            handler(command == 5); complete(operation, nil)
        case 7: _ = manager.send(bytes, to: [endpoint], completionHandler: done)
        case 8: manager.disconnect(from: endpoint, completionHandler: done)
        default: enqueue(["kind": 0, "operation": operation, "error": 4, "text": "Unknown command."])
        }
    }
    func close() {
        closed = true
        for handler in verifications.values { handler(false) }
        verifications.removeAll()
        advertiser.stopAdvertising(); discoverer.stopDiscovery()
        for endpoint in connected { manager.disconnect(from: endpoint) }
        messages.removeAll(); payload = Data(); queuedBytes = 0
        manager.delegate = nil; advertiser.delegate = nil; discoverer.delegate = nil
    }
    func advertiser(_ advertiser: Advertiser, didReceiveConnectionRequestFrom endpointID: EndpointID, with context: Data, connectionRequestHandler: @escaping (Bool) -> Void) {
        guard !closed else { connectionRequestHandler(false); return }
        incoming.insert(endpointID); names[endpointID] = String(decoding: context, as: UTF8.self)
        // Accept only the SDK's preliminary request; the verification handler remains pending for the user.
        connectionRequestHandler(true)
    }
    func discoverer(_ discoverer: Discoverer, didFind endpointID: EndpointID, with context: Data) {
        let name = String(decoding: context, as: UTF8.self); names[endpointID] = name
        enqueue(["kind": 1, "endpoint": endpointID, "text": name])
    }
    func discoverer(_ discoverer: Discoverer, didLose endpointID: EndpointID) {
        enqueue(["kind": 2, "endpoint": endpointID])
    }
    func connectionManager(_ connectionManager: ConnectionManager, didReceive verificationCode: String, from endpointID: EndpointID, verificationHandler: @escaping (Bool) -> Void) {
        guard !closed else { verificationHandler(false); return }
        nextRequest += 1; let request = nextRequest
        if let old = requestByEndpoint.updateValue(request, forKey: endpointID), let handler = verifications.removeValue(forKey: old) { handler(false) }
        verifications[request] = verificationHandler
        enqueue(["kind": 3, "request": request, "endpoint": endpointID, "text": names[endpointID] ?? "", "code": verificationCode, "incoming": incoming.contains(endpointID)])
    }
    func connectionManager(_ connectionManager: ConnectionManager, didChangeTo state: ConnectionState, for endpointID: EndpointID) {
        switch state {
        case .connecting: break
        case .connected:
            connected.insert(endpointID)
            let request = requestByEndpoint.removeValue(forKey: endpointID) ?? 0
            verifications.removeValue(forKey: request); incoming.remove(endpointID)
            enqueue(["kind": 4, "endpoint": endpointID, "request": request, "result": 0])
        case .rejected, .disconnected:
            let wasConnected = connected.remove(endpointID) != nil
            let request = requestByEndpoint.removeValue(forKey: endpointID) ?? 0
            verifications.removeValue(forKey: request); incoming.remove(endpointID)
            if wasConnected { enqueue(["kind": 5, "endpoint": endpointID]) }
            else { enqueue(["kind": 4, "endpoint": endpointID, "request": request, "result": state == .rejected ? 1 : 2]) }
        @unknown default: break
        }
    }
    func connectionManager(_ connectionManager: ConnectionManager, didReceive data: Data, withID payloadID: PayloadID, from endpointID: EndpointID) {
        enqueue(["kind": 6, "endpoint": endpointID, "length": data.count], data)
    }
    func connectionManager(_ connectionManager: ConnectionManager, didReceive stream: InputStream, withID payloadID: PayloadID, from endpointID: EndpointID, cancellationToken token: CancellationToken) { token.cancel() }
    func connectionManager(_ connectionManager: ConnectionManager, didStartReceivingResourceWithID payloadID: PayloadID, from endpointID: EndpointID, at localURL: URL, withName name: String, cancellationToken token: CancellationToken) { token.cancel() }
    func connectionManager(_ connectionManager: ConnectionManager, didReceiveTransferUpdate update: TransferUpdate, from endpointID: EndpointID, forPayload payloadID: PayloadID) {}
}

private var bridge: NearbyBridge?
@_cdecl("NC_Create") public func ncCreate(_ service: UnsafePointer<CChar>, _ strategy: Int32) { bridge = NearbyBridge(service: String(cString: service), strategy: strategy) }
@_cdecl("NC_Close") public func ncClose() { bridge?.close(); bridge = nil }
@_cdecl("NC_Submit") public func ncSubmit(_ operation: Int32, _ command: Int32, _ endpoint: UnsafePointer<CChar>, _ name: UnsafePointer<CChar>, _ request: Int64, _ bytes: UnsafePointer<UInt8>?, _ length: Int32) {
    let data = length > 0 && bytes != nil ? Data(bytes: bytes!, count: Int(length)) : Data()
    bridge?.submit(operation, command, String(cString: endpoint), String(cString: name), request, data)
}
@_cdecl("NC_Poll") public func ncPoll() -> UnsafeMutablePointer<CChar>? {
    guard let b = bridge, !b.messages.isEmpty else { return nil }
    let (fields, data) = b.messages.removeFirst(); b.queuedBytes -= data.count; b.payload = data
    guard let json = try? JSONSerialization.data(withJSONObject: fields), let string = String(data: json, encoding: .utf8) else { return nil }
    return strdup(string)
}
@_cdecl("NC_CopyPayload") public func ncCopyPayload(_ destination: UnsafeMutablePointer<UInt8>?, _ length: Int32) -> Int32 {
    guard let data = bridge?.payload, data.count == Int(length) else { return -1 }
    if !data.isEmpty { guard let destination = destination else { return -1 }; data.copyBytes(to: destination, count: data.count) }
    bridge?.payload = Data(); return length
}
@_cdecl("NC_Free") public func ncFree(_ pointer: UnsafeMutablePointer<CChar>?) { free(pointer) }
