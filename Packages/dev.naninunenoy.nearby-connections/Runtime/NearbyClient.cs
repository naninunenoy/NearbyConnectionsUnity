using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace NearbyConnections
{
    public sealed class NearbyClient
    {
        // Nearby Connections Android ConnectionsClient.MAX_BYTES_DATA_SIZE.
        public const int MaxBytesPayloadSize = 1047552;

#if !UNITY_ANDROID || UNITY_EDITOR
#pragma warning disable CS0067 // Events are raised by the Android player.
#endif
        public event Action<NearbyEndpoint> EndpointFound;
        public event Action<string> EndpointLost;
        public event Action<NearbyConnectionRequest> ConnectionInitiated;
        public event Action<NearbyConnectionResult> ConnectionResult;
        public event Action<string> Disconnected;
        public event Action<string, byte[]> BytesReceived;
        public event Action<string, NearbyException> Faulted;
#if !UNITY_ANDROID || UNITY_EDITOR
#pragma warning restore CS0067
#endif

        private static int mainThread;
        private static NearbyClient active;
        private readonly HashSet<string> connected = new HashSet<string>();
        private NearbyPump pump;
        private bool closed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            mainThread = Thread.CurrentThread.ManagedThreadId;
            active?.Close();
        }

        private NearbyClient() { }

        public static NearbyClient Create(NearbyOptions options)
        {
            CheckMainThread();
            if (options == null) throw new ArgumentNullException(nameof(options));
            RequireText(options.ServiceId, nameof(options.ServiceId));
            if (!Enum.IsDefined(typeof(NearbyStrategy), options.Strategy))
                throw new ArgumentOutOfRangeException(nameof(options.Strategy));

#if UNITY_ANDROID && !UNITY_EDITOR
            if (active != null)
                throw new NearbyException(NearbyError.InvalidState, "A Nearby client is already active.");

            var client = new NearbyClient();
            using (var activity = CurrentActivity())
                client.bridge = new AndroidJavaObject(BridgeClass, activity, options.ServiceId, (int)options.Strategy);

            var host = new GameObject("Nearby Connections");
            UnityEngine.Object.DontDestroyOnLoad(host);
            client.pump = host.AddComponent<NearbyPump>();
            client.pump.Client = client;
            active = client;
            return client;
#else
            throw Unsupported();
#endif
        }

        public static string[] GetMissingAndroidPermissions()
        {
            CheckMainThread();
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var activity = CurrentActivity())
            using (var bridgeType = new AndroidJavaClass(BridgeClass))
                return bridgeType.CallStatic<string[]>("missingPermissions", activity);
#else
            throw Unsupported();
#endif
        }

        public string[] GetConnectedEndpointIds()
        {
            CheckMainThread();
            var endpoints = new string[connected.Count];
            connected.CopyTo(endpoints);
            return endpoints;
        }

        public Task StartAdvertisingAsync(string name) =>
            Submit(Command.StartAdvertising, name: RequireText(name, nameof(name)));

        public Task StopAdvertisingAsync() => Submit(Command.StopAdvertising);

        public Task StartDiscoveryAsync() => Submit(Command.StartDiscovery);

        public Task StopDiscoveryAsync() => Submit(Command.StopDiscovery);

        public Task RequestConnectionAsync(string endpointId, string localName) =>
            Submit(Command.RequestConnection, RequireText(endpointId, nameof(endpointId)), RequireText(localName, nameof(localName)));

        public Task AcceptConnectionAsync(long requestId) =>
            Submit(Command.AcceptConnection, requestId: requestId);

        public Task RejectConnectionAsync(long requestId) =>
            Submit(Command.RejectConnection, requestId: requestId);

        public Task SendBytesAsync(string endpointId, byte[] bytes)
        {
            CheckMainThread();
            RequireText(endpointId, nameof(endpointId));
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length > MaxBytesPayloadSize)
                throw new NearbyException(NearbyError.PayloadTooLarge, "Byte payload is too large.");
            if (!connected.Contains(endpointId))
                throw new NearbyException(NearbyError.InvalidState, "Endpoint is not connected.");

#if UNITY_ANDROID && !UNITY_EDITOR
            var copy = new sbyte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, copy, 0, bytes.Length);
            return Submit(Command.SendBytes, endpointId, bytes: copy);
#else
            throw Unsupported();
#endif
        }

        public Task DisconnectAsync(string endpointId) =>
            Submit(Command.Disconnect, RequireText(endpointId, nameof(endpointId)));

        public Task ShutdownAsync()
        {
            CheckMainThread();
            Close();
            return Task.CompletedTask;
        }

        private enum Command
        {
            StartAdvertising, StopAdvertising, StartDiscovery, StopDiscovery,
            RequestConnection, AcceptConnection, RejectConnection, SendBytes, Disconnect
        }

        private Task Submit(Command command, string endpoint = "", string name = "", long requestId = 0, sbyte[] bytes = null)
        {
            CheckMainThread();
            if (closed)
                throw new NearbyException(NearbyError.InvalidState, "The Nearby session has ended.");

#if UNITY_ANDROID && !UNITY_EDITOR
            const int maxPendingOperations = 64;
            if (pending.Count >= maxPendingOperations)
                throw new NearbyException(NearbyError.InvalidState, "Too many pending Nearby operations.");

            var operation = new PendingOperation();
            int id = ++nextOperation;
            pending.Add(id, operation);
            try
            {
                // An empty array keeps the JNI signature stable for commands without a payload.
                bridge.Call("submit", id, (int)command, endpoint, name, requestId, bytes ?? Array.Empty<sbyte>());
            }
            catch (Exception)
            {
                pending.Remove(id);
                throw;
            }
            return operation.Completion.Task;
#else
            throw Unsupported();
#endif
        }

        internal void Close()
        {
            if (closed) return;
            closed = true;
            connected.Clear();
            if (active == this) active = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            EndpointFound = null;
            EndpointLost = null;
            ConnectionInitiated = null;
            ConnectionResult = null;
            Disconnected = null;
            BytesReceived = null;
            Faulted = null;
            foreach (PendingOperation operation in pending.Values)
                operation.Completion.TrySetCanceled();
            pending.Clear();
            try
            {
                bridge.Call("close");
            }
            catch (AndroidJavaException exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                bridge.Dispose();
            }
#endif
            if (pump != null)
            {
                pump.Client = null;
                UnityEngine.Object.Destroy(pump.gameObject);
                pump = null;
            }
        }

        internal void Update()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                for (int count = 0; count < 64 && !closed; count++)
                {
                    using var message = bridge.Call<AndroidJavaObject>("poll");
                    if (message == null) break;
                    Dispatch(message);
                }

                if (closed) return;
                foreach (var operation in pending.Values)
                {
                    if (Time.realtimeSinceStartupAsDouble < operation.Deadline) continue;
                    FailSession(new NearbyException(NearbyError.Timeout, "A Nearby operation timed out."));
                    break;
                }
            }
            catch (AndroidJavaException exception)
            {
                FailSession(new NearbyException(NearbyError.NativeFailure, exception.Message));
            }
#endif
        }

        private static void CheckMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread)
                throw new InvalidOperationException("Use Nearby Connections on the Unity main thread during play.");
        }

        private static string RequireText(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A non-empty value is required.", parameter);
            return value;
        }

        private static NearbyException Unsupported() =>
            new NearbyException(NearbyError.UnsupportedPlatform, "Nearby Connections requires an Android Player.");

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string BridgeClass = "com.example.nearbyconnections.NearbyBridge";
        private readonly Dictionary<int, PendingOperation> pending = new Dictionary<int, PendingOperation>();
        private AndroidJavaObject bridge;
        private int nextOperation;

        private sealed class PendingOperation
        {
            public readonly TaskCompletionSource<bool> Completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public readonly double Deadline = Time.realtimeSinceStartupAsDouble + 30;
        }

        private static AndroidJavaObject CurrentActivity()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        private void Dispatch(AndroidJavaObject message)
        {
            int kind = message.Get<int>("kind");
            if (kind == 0)
            {
                CompleteOperation(message);
                return;
            }

            string endpoint = message.Get<string>("endpoint");
            switch (kind)
            {
                case 1:
                    Notify(EndpointFound, new NearbyEndpoint(endpoint, message.Get<string>("text")));
                    break;
                case 2:
                    Notify(EndpointLost, endpoint);
                    break;
                case 3:
                    var request = new NearbyConnectionRequest(message.Get<long>("request"),
                        new NearbyEndpoint(endpoint, message.Get<string>("text")),
                        message.Get<string>("code"), message.Get<bool>("incoming"));
                    Notify(ConnectionInitiated, request);
                    break;
                case 4:
                    var status = (NearbyConnectionStatus)message.Get<int>("result");
                    if (status == NearbyConnectionStatus.Connected)
                        connected.Add(endpoint);
                    Notify(ConnectionResult, new NearbyConnectionResult(endpoint, message.Get<long>("request"),
                        status, message.Get<int>("nativeCode")));
                    break;
                case 5:
                    connected.Remove(endpoint);
                    Notify(Disconnected, endpoint);
                    break;
                case 6:
                    var signed = message.Get<sbyte[]>("bytes");
                    var bytes = new byte[signed.Length];
                    Buffer.BlockCopy(signed, 0, bytes, 0, bytes.Length);
                    Notify(BytesReceived, endpoint, bytes);
                    break;
                case 7:
                    var overflow = new NearbyException(NearbyError.QueueOverflow, "The Nearby receive queue overflowed.");
                    if (endpoint == null) FailSession(overflow);
                    else
                    {
                        connected.Remove(endpoint);
                        Notify(Faulted, endpoint, overflow);
                    }
                    break;
            }
        }

        private void CompleteOperation(AndroidJavaObject message)
        {
            int id = message.Get<int>("operation");
            if (!pending.Remove(id, out PendingOperation operation)) return;
            int error = message.Get<int>("error");
            if (error == 0)
            {
                operation.Completion.TrySetResult(true);
                return;
            }

            int status = message.Get<int>("nativeCode");
            operation.Completion.TrySetException(new NearbyException((NearbyError)error,
                message.Get<string>("text"), status == 0 ? (int?)null : status));
        }

        private void FailSession(NearbyException exception)
        {
            var handlers = Faulted;
            foreach (var operation in pending.Values)
                operation.Completion.TrySetException(exception);
            pending.Clear();
            Close();
            Notify(handlers, null, exception);
        }

        private static void Notify<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try { handler(value); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        private static void Notify<T1, T2>(Action<T1, T2> handlers, T1 first, T2 second)
        {
            if (handlers == null) return;
            foreach (Action<T1, T2> handler in handlers.GetInvocationList())
            {
                try { handler(first, second); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
#endif
    }
}
