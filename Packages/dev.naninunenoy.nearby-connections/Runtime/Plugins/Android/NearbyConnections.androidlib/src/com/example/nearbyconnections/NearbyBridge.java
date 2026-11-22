package com.example.nearbyconnections;

import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;

import com.google.android.gms.common.ConnectionResult;
import com.google.android.gms.common.GoogleApiAvailability;
import com.google.android.gms.common.api.ApiException;
import com.google.android.gms.nearby.Nearby;
import com.google.android.gms.nearby.connection.AdvertisingOptions;
import com.google.android.gms.nearby.connection.ConnectionInfo;
import com.google.android.gms.nearby.connection.ConnectionLifecycleCallback;
import com.google.android.gms.nearby.connection.ConnectionResolution;
import com.google.android.gms.nearby.connection.ConnectionsClient;
import com.google.android.gms.nearby.connection.ConnectionsStatusCodes;
import com.google.android.gms.nearby.connection.DiscoveredEndpointInfo;
import com.google.android.gms.nearby.connection.DiscoveryOptions;
import com.google.android.gms.nearby.connection.EndpointDiscoveryCallback;
import com.google.android.gms.nearby.connection.Payload;
import com.google.android.gms.nearby.connection.PayloadCallback;
import com.google.android.gms.nearby.connection.PayloadTransferUpdate;
import com.google.android.gms.nearby.connection.Strategy;
import com.google.android.gms.tasks.Task;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.Executor;
import java.util.function.Supplier;

public final class NearbyBridge {
    private static final int COMPLETED = 0, FOUND = 1, LOST = 2, INITIATED = 3;
    private static final int RESULT = 4, DISCONNECTED = 5, BYTES = 6, OVERFLOW = 7;
    private static final int PERMISSION = 1, UNAVAILABLE = 3, INVALID_STATE = 4;
    private static final int ENDPOINT_NOT_FOUND = 5, TOO_LARGE = 6, NATIVE_FAILURE = 7;
    private static final int MAX_EVENTS = 256, MAX_PAYLOAD_EVENTS = 128;
    private static final int MAX_RECEIVE_BYTES = 4 * 1024 * 1024;
    // Every bridge runs on the same Android looper. Keep request IDs distinct across sessions.
    private static long nextRequest;

    public static final class Event {
        public final int kind;
        public int operation, error, nativeCode, result;
        public long request;
        public String endpoint, text, code;
        public boolean incoming;
        public byte[] bytes;

        Event(int kind, String endpoint) {
            this.kind = kind;
            this.endpoint = endpoint;
        }
    }

    private static final class Peer {
        final ConnectionLifecycleCallback lifecycle;
        final long request;
        boolean approved;
        boolean connected;

        Peer(ConnectionLifecycleCallback lifecycle, long request) {
            this.lifecycle = lifecycle;
            this.request = request;
        }
    }

    private final Handler main = new Handler(Looper.getMainLooper());
    private final Executor callbacks = command -> main.post(command);
    private final Context context;
    private final String service;
    private final Strategy strategy;
    private final ArrayDeque<Event> events = new ArrayDeque<>();
    private final Map<String, Peer> peers = new HashMap<>();
    private final Map<Long, String> requests = new HashMap<>();
    private final Map<String, ConnectionLifecycleCallback> outgoing = new HashMap<>();
    private final Set<String> discovered = new HashSet<>();
    private ConnectionsClient client;
    private ConnectionLifecycleCallback advertising;
    private EndpointDiscoveryCallback discovery;
    private int queuedBytes;
    private int queuedPayloads;
    private boolean closed;

    public NearbyBridge(Context context, String service, int strategy) {
        this.context = context.getApplicationContext();
        this.service = service;
        this.strategy = strategy == 0 ? Strategy.P2P_POINT_TO_POINT : Strategy.P2P_STAR;
    }

    public static String[] missingPermissions(Context context) {
        int api = Build.VERSION.SDK_INT;
        ArrayList<String> permissions = new ArrayList<>();
        if (api <= 32) permissions.add("android.permission.ACCESS_COARSE_LOCATION");
        if (api >= 29 && api <= 32) permissions.add("android.permission.ACCESS_FINE_LOCATION");
        if (api >= 31) {
            permissions.add("android.permission.BLUETOOTH_ADVERTISE");
            permissions.add("android.permission.BLUETOOTH_CONNECT");
            permissions.add("android.permission.BLUETOOTH_SCAN");
        }
        if (api >= 33) permissions.add("android.permission.NEARBY_WIFI_DEVICES");
        if (api >= 37 && context.getApplicationInfo().targetSdkVersion >= 37) {
            permissions.add("android.permission.ACCESS_LOCAL_NETWORK");
        }
        permissions.removeIf(permission -> context.checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED);
        return permissions.toArray(new String[0]);
    }

    public synchronized Event poll() {
        Event event = events.poll();
        if (event != null && event.bytes != null) {
            queuedBytes -= event.bytes.length;
            queuedPayloads--;
        }
        return event;
    }

    private boolean enqueue(Event event) {
        synchronized (this) {
            if (closed) return false;
            if (event.bytes != null) {
                if (queuedPayloads >= MAX_PAYLOAD_EVENTS ||
                        event.bytes.length > MAX_RECEIVE_BYTES - queuedBytes) return false;
                queuedBytes += event.bytes.length;
                queuedPayloads++;
            }
            if (events.size() < MAX_EVENTS) {
                events.add(event);
                return true;
            }
            clearQueue();
            events.add(new Event(OVERFLOW, null));
        }
        stop();
        return false;
    }

    private void clearQueue() {
        events.clear();
        queuedBytes = 0;
        queuedPayloads = 0;
    }

    public void close() {
        main.post(() -> {
            synchronized (this) {
                clearQueue();
            }
            stop();
        });
    }

    private void stop() {
        if (closed) return;
        closed = true;
        if (client != null) {
            cleanup(client::stopAdvertising);
            cleanup(client::stopDiscovery);
            cleanup(client::stopAllEndpoints);
        }
        peers.clear();
        requests.clear();
        outgoing.clear();
        discovered.clear();
        advertising = null;
        discovery = null;
    }

    private static void cleanup(Runnable action) {
        try {
            action.run();
        } catch (RuntimeException ex) {
            android.util.Log.w("NearbyConnections", "Nearby cleanup failed", ex);
        }
    }

    public void submit(int operation, int command, String endpoint, String name, long request, byte[] bytes) {
        main.post(() -> {
            if (closed) return;
            try {
                if (client == null) {
                    int status = GoogleApiAvailability.getInstance().isGooglePlayServicesAvailable(context);
                    if (status != ConnectionResult.SUCCESS) {
                        complete(operation, UNAVAILABLE, status, "Google Play services unavailable");
                        return;
                    }
                    client = Nearby.getConnectionsClient(context);
                }
                if ((command == 0 || command == 2 || command == 4) && missingPermissions(context).length != 0) {
                    complete(operation, PERMISSION, 0, "Nearby permissions are required");
                    return;
                }
                switch (command) {
                    case 0: startAdvertising(operation, name); break;
                    case 1:
                        client.stopAdvertising();
                        advertising = null;
                        complete(operation, 0, 0, null);
                        break;
                    case 2: startDiscovery(operation); break;
                    case 3: stopDiscovery(operation); break;
                    case 4: requestConnection(operation, endpoint, name); break;
                    case 5: respond(operation, request, true); break;
                    case 6: respond(operation, request, false); break;
                    case 7: send(operation, endpoint, bytes); break;
                    case 8: disconnect(operation, endpoint); break;
                    default: complete(operation, INVALID_STATE, 0, "Unknown command");
                }
            } catch (RuntimeException ex) {
                fail(operation, ex);
            }
        });
    }

    private void startAdvertising(int operation, String name) {
        if (advertising != null) {
            complete(operation, INVALID_STATE, 0, "Advertising is already active");
            return;
        }
        ConnectionLifecycleCallback callback = lifecycle(null);
        advertising = callback;
        run(operation, () -> client.startAdvertising(name, service, callback,
                new AdvertisingOptions.Builder().setStrategy(strategy).build()),
                () -> { if (advertising == callback) advertising = null; });
    }

    private void startDiscovery(int operation) {
        if (discovery != null) {
            complete(operation, INVALID_STATE, 0, "Discovery is already active");
            return;
        }
        EndpointDiscoveryCallback callback = new EndpointDiscoveryCallback() {
            @Override public void onEndpointFound(String endpoint, DiscoveredEndpointInfo info) {
                if (closed || discovery != this || !service.equals(info.getServiceId())) return;
                discovered.add(endpoint);
                Event event = new Event(FOUND, endpoint);
                event.text = info.getEndpointName();
                enqueue(event);
            }

            @Override public void onEndpointLost(String endpoint) {
                if (closed || discovery != this || !discovered.remove(endpoint)) return;
                enqueue(new Event(LOST, endpoint));
            }
        };
        discovery = callback;
        run(operation, () -> client.startDiscovery(service, callback,
                new DiscoveryOptions.Builder().setStrategy(strategy).build()), () -> {
            if (discovery == callback) {
                discovery = null;
                clearDiscovery();
            }
        });
    }

    private void stopDiscovery(int operation) {
        client.stopDiscovery();
        discovery = null;
        clearDiscovery();
        complete(operation, 0, 0, null);
    }

    private void clearDiscovery() {
        String[] endpoints = discovered.toArray(new String[0]);
        discovered.clear();
        for (String endpoint : endpoints) enqueue(new Event(LOST, endpoint));
    }

    private void requestConnection(int operation, String endpoint, String name) {
        if (peers.containsKey(endpoint) || outgoing.containsKey(endpoint)) {
            complete(operation, INVALID_STATE, 0, "Connection already pending or established");
            return;
        }
        if (!discovered.contains(endpoint)) {
            complete(operation, ENDPOINT_NOT_FOUND, 0, "Endpoint is no longer discovered");
            return;
        }
        ConnectionLifecycleCallback callback = lifecycle(endpoint);
        outgoing.put(endpoint, callback);
        run(operation, () -> client.requestConnection(name, endpoint, callback),
                () -> outgoing.remove(endpoint, callback));
    }

    private ConnectionLifecycleCallback lifecycle(String requestedEndpoint) {
        return new ConnectionLifecycleCallback() {
            @Override public void onConnectionInitiated(String endpoint, ConnectionInfo info) {
                if (closed || peers.containsKey(endpoint)) return;
                if (requestedEndpoint == null ? advertising != this : outgoing.get(endpoint) != this) return;
                outgoing.remove(endpoint, this);
                Peer peer = new Peer(this, ++nextRequest);
                peers.put(endpoint, peer);
                requests.put(peer.request, endpoint);
                Event event = new Event(INITIATED, endpoint);
                event.request = peer.request;
                event.text = info.getEndpointName();
                event.code = info.getAuthenticationDigits();
                event.incoming = info.isIncomingConnection();
                enqueue(event);
            }

            @Override public void onConnectionResult(String endpoint, ConnectionResolution resolution) {
                if (closed) return;
                int status = resolution.getStatus().getStatusCode();
                Peer peer = peers.get(endpoint);
                if (peer == null || peer.lifecycle != this) {
                    if (outgoing.remove(endpoint, this) && status != ConnectionsStatusCodes.STATUS_OK) {
                        publishResult(endpoint, 0, status);
                    }
                    return;
                }
                requests.remove(peer.request);
                peer.connected = status == ConnectionsStatusCodes.STATUS_OK;
                if (!peer.connected) peers.remove(endpoint);
                publishResult(endpoint, peer.request, status);
            }

            @Override public void onDisconnected(String endpoint) {
                if (closed) return;
                Peer peer = peers.get(endpoint);
                if (peer != null && peer.lifecycle == this) {
                    peers.remove(endpoint);
                    publishEnded(endpoint, peer);
                } else if (outgoing.remove(endpoint, this)) {
                    publishResult(endpoint, 0, ConnectionsStatusCodes.STATUS_ERROR);
                }
            }
        };
    }

    private void publishResult(String endpoint, long request, int status) {
        Event event = new Event(RESULT, endpoint);
        event.request = request;
        event.nativeCode = status;
        event.result = status == ConnectionsStatusCodes.STATUS_OK ? 0 :
                status == ConnectionsStatusCodes.STATUS_CONNECTION_REJECTED ? 1 : 2;
        enqueue(event);
    }

    private void publishEnded(String endpoint, Peer peer) {
        requests.remove(peer.request);
        if (peer.connected) {
            enqueue(new Event(DISCONNECTED, endpoint));
        } else {
            publishResult(endpoint, peer.request, ConnectionsStatusCodes.STATUS_ERROR);
        }
    }

    private void respond(int operation, long request, boolean accept) {
        String endpoint = requests.remove(request);
        if (endpoint == null) {
            complete(operation, INVALID_STATE, 0, "Connection request is no longer pending");
            return;
        }
        Peer peer = peers.get(endpoint);
        peer.approved = accept;
        run(operation,
                () -> accept ? client.acceptConnection(endpoint, payloadCallback(peer)) : client.rejectConnection(endpoint),
                () -> {
                    if (peers.remove(endpoint, peer)) {
                        cleanup(() -> client.disconnectFromEndpoint(endpoint));
                        publishEnded(endpoint, peer);
                    }
                });
    }

    private PayloadCallback payloadCallback(Peer peer) {
        return new PayloadCallback() {
            @Override public void onPayloadReceived(String endpoint, Payload payload) {
                if (closed || peers.get(endpoint) != peer || !peer.approved || payload.getType() != Payload.Type.BYTES) return;
                byte[] bytes = payload.asBytes();
                if (bytes == null) return;
                Event event = new Event(BYTES, endpoint);
                event.bytes = bytes.clone();
                if (!enqueue(event) && !closed) {
                    peers.remove(endpoint);
                    cleanup(() -> client.disconnectFromEndpoint(endpoint));
                    publishEnded(endpoint, peer);
                    enqueue(new Event(OVERFLOW, endpoint));
                }
            }

            @Override public void onPayloadTransferUpdate(String endpoint, PayloadTransferUpdate update) { }
        };
    }

    private void send(int operation, String endpoint, byte[] data) {
        Peer peer = peers.get(endpoint);
        if (peer == null || !peer.connected) {
            complete(operation, INVALID_STATE, 0, "Endpoint is not connected");
        } else if (data.length > ConnectionsClient.MAX_BYTES_DATA_SIZE) {
            complete(operation, TOO_LARGE, 0, "Byte payload is too large");
        } else {
            run(operation, () -> client.sendPayload(endpoint, Payload.fromBytes(data)), () -> { });
        }
    }

    private void disconnect(int operation, String endpoint) {
        Peer peer = peers.get(endpoint);
        if (peer == null && !outgoing.containsKey(endpoint)) {
            complete(operation, ENDPOINT_NOT_FOUND, 0, "Endpoint has no connection");
            return;
        }
        client.disconnectFromEndpoint(endpoint);
        outgoing.remove(endpoint);
        peers.remove(endpoint);
        if (peer != null) publishEnded(endpoint, peer);
        else publishResult(endpoint, 0, ConnectionsStatusCodes.STATUS_ERROR);
        complete(operation, 0, 0, null);
    }

    private void run(int operation, Supplier<Task<Void>> action, Runnable onFailure) {
        try {
            action.get().addOnCompleteListener(callbacks, task -> {
                if (closed) return;
                if (task.isSuccessful()) {
                    complete(operation, 0, 0, null);
                } else {
                    onFailure.run();
                    fail(operation, task.getException());
                }
            });
        } catch (RuntimeException ex) {
            onFailure.run();
            fail(operation, ex);
        }
    }

    private void fail(int operation, Exception ex) {
        int status = ex instanceof ApiException ? ((ApiException) ex).getStatusCode() : 0;
        int error = NATIVE_FAILURE;
        if (ex instanceof SecurityException) error = PERMISSION;
        switch (status) {
            case ConnectionsStatusCodes.MISSING_PERMISSION_NEARBY_WIFI_DEVICES:
            case ConnectionsStatusCodes.MISSING_PERMISSION_BLUETOOTH:
            case ConnectionsStatusCodes.MISSING_PERMISSION_BLUETOOTH_ADMIN:
            case ConnectionsStatusCodes.MISSING_PERMISSION_ACCESS_WIFI_STATE:
            case ConnectionsStatusCodes.MISSING_PERMISSION_CHANGE_WIFI_STATE:
            case ConnectionsStatusCodes.MISSING_PERMISSION_ACCESS_COARSE_LOCATION:
            case ConnectionsStatusCodes.MISSING_PERMISSION_ACCESS_FINE_LOCATION:
            case ConnectionsStatusCodes.MISSING_PERMISSION_BLUETOOTH_SCAN:
            case ConnectionsStatusCodes.MISSING_PERMISSION_BLUETOOTH_ADVERTISE:
            case ConnectionsStatusCodes.MISSING_PERMISSION_BLUETOOTH_CONNECT:
                error = PERMISSION;
                break;
            case ConnectionsStatusCodes.STATUS_ENDPOINT_UNKNOWN:
                error = ENDPOINT_NOT_FOUND;
                break;
            case ConnectionsStatusCodes.STATUS_ALREADY_CONNECTED_TO_ENDPOINT:
            case ConnectionsStatusCodes.STATUS_ALREADY_ADVERTISING:
            case ConnectionsStatusCodes.STATUS_ALREADY_DISCOVERING:
            case ConnectionsStatusCodes.STATUS_ALREADY_HAVE_ACTIVE_STRATEGY:
            case ConnectionsStatusCodes.STATUS_NOT_CONNECTED_TO_ENDPOINT:
            case ConnectionsStatusCodes.STATUS_OUT_OF_ORDER_API_CALL:
                error = INVALID_STATE;
                break;
        }
        complete(operation, error, status, ex == null ? "Nearby operation canceled" : ex.toString());
    }

    private void complete(int operation, int error, int status, String message) {
        Event event = new Event(COMPLETED, null);
        event.operation = operation;
        event.error = error;
        event.nativeCode = status;
        event.text = message;
        enqueue(event);
    }
}
