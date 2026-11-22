using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NearbyConnections;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

internal sealed class DemoConnection
{
    private readonly Dictionary<string, NearbyEndpoint> discovered = new();
    private readonly Dictionary<long, NearbyConnectionRequest> requests = new();
    private readonly Dictionary<string, string> connected = new();
    private NearbyClient client;
    private bool busy;

    public event Action Opened;
    public event Action Changed;
    public event Action<string> StatusChanged;
    public event Action<string, string> Connected;
    public event Action<string> Disconnected;
    public event Action<string, byte[]> Received;
    public event Action Ended;

    public bool Active => client != null || busy;
    public bool IsHost { get; private set; }
    public string LocalName { get; private set; }
    public IEnumerable<NearbyEndpoint> Discovered => discovered.Values.Where(peer =>
        !connected.ContainsKey(peer.Id) && !requests.Values.Any(request => request.Endpoint.Id == peer.Id));
    public IEnumerable<NearbyConnectionRequest> Requests => requests.Values;
    public IReadOnlyDictionary<string, string> Peers => connected;
    public int SessionVersion { get; private set; }

    public async Task Start(bool host, string name, string serviceId, NearbyStrategy strategy)
    {
        if (Active) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        var missing = NearbyClient.GetMissingAndroidPermissions();
        if (missing.Length != 0)
        {
            Report("Allow Nearby permissions, then tap Host or Join again.");
            Permission.RequestUserPermissions(missing);
            return;
        }
#endif
        IsHost = host;
        LocalName = name;
        busy = true;
        NearbyClient current = null;
        try
        {
            current = NearbyClient.Create(new NearbyOptions { ServiceId = serviceId, Strategy = strategy });
            client = current;
            Attach(current);
            Opened?.Invoke();
            Report(host ? "Starting advertising…" : "Starting discovery…");
            Changed?.Invoke();
            if (host) await current.StartAdvertisingAsync(name);
            else await current.StartDiscoveryAsync();
            if (client == current && connected.Count == 0 && requests.Count == 0)
                Report(host ? "Advertising. Compare codes when a request arrives." : "Discovering nearby hosts.");
        }
        catch (Exception error)
        {
            if (client == current) End(error.Message);
        }
        finally
        {
            if (client == current && busy)
            {
                busy = false;
                Changed?.Invoke();
            }
        }
    }

    private void Attach(NearbyClient current)
    {
        current.EndpointFound += peer =>
        {
            discovered[peer.Id] = peer;
            Changed?.Invoke();
        };
        current.EndpointLost += id =>
        {
            discovered.Remove(id);
            Changed?.Invoke();
        };
        current.ConnectionInitiated += request =>
        {
            requests[request.Id] = request;
            Changed?.Invoke();
        };
        current.ConnectionResult += result =>
        {
            requests.Remove(result.RequestId, out var request);
            var peer = request.Endpoint;
            if (result.Status == NearbyConnectionStatus.Connected)
            {
                if (string.IsNullOrEmpty(peer.Name)) discovered.TryGetValue(result.EndpointId, out peer);
                string name = string.IsNullOrEmpty(peer.Name) ? result.EndpointId : peer.Name;
                connected[result.EndpointId] = name;
                Report($"Connected to {name}.");
                Connected?.Invoke(result.EndpointId, name);
            }
            else Report($"{result.Status} ({result.NativeCode}).");
            Changed?.Invoke();
        };
        current.Disconnected += RemovePeer;
        current.Faulted += (id, error) =>
        {
            if (id == null) End(error.Message);
            else
            {
                if (!current.GetConnectedEndpointIds().Contains(id)) RemovePeer(id);
                Report(error.Message);
            }
        };
        current.BytesReceived += (id, bytes) =>
        {
            if (connected.ContainsKey(id)) Received?.Invoke(id, bytes);
        };
    }

    private void RemovePeer(string id)
    {
        if (!connected.Remove(id, out var peer)) return;
        Report($"Disconnected from {peer}.");
        Disconnected?.Invoke(id);
        Changed?.Invoke();
    }

    public Task Connect(string id) => client.RequestConnectionAsync(id, LocalName);

    public Task Respond(long id, bool accept) => accept ? client.AcceptConnectionAsync(id) : client.RejectConnectionAsync(id);

    public Task Disconnect(string id) => client.DisconnectAsync(id);

    public Task Send(string id, byte[] bytes) => client.SendBytesAsync(id, bytes);

    public void Report(string message) => StatusChanged?.Invoke(message);

    public void End(string message)
    {
        SessionVersion++;
        var current = client;
        client = null;
        busy = false;
        if (current != null) _ = current.ShutdownAsync();
        discovered.Clear();
        requests.Clear();
        connected.Clear();
        Ended?.Invoke();
        Report(message);
        Changed?.Invoke();
    }
}
