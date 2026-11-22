#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace NearbyConnections
{
    public sealed partial class NearbyClient
    {
        [DllImport("__Internal")] private static extern void NC_Create(string service, int strategy);
        [DllImport("__Internal")] private static extern void NC_Close();
        [DllImport("__Internal")] private static extern void NC_Submit(int operation, int command, string endpoint, string name, long request, byte[] data, int length);
        [DllImport("__Internal")] private static extern IntPtr NC_Poll();
        [DllImport("__Internal")] private static extern int NC_CopyPayload(byte[] destination, int length);
        [DllImport("__Internal")] private static extern void NC_Free(IntPtr value);

        #pragma warning disable CS0649 // Populated by JsonUtility.
        [Serializable, UnityEngine.Scripting.Preserve] private sealed class IosMessage
        {
            public int kind, operation, error, nativeCode, result, length;
            public long request;
            public string endpoint, text, code;
            public bool incoming;
        }
        #pragma warning restore CS0649
        private sealed class IosOperation
        {
            public readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly double Deadline = Time.realtimeSinceStartupAsDouble + 30;
        }
        private readonly Dictionary<int, IosOperation> iosPending = new Dictionary<int, IosOperation>();
        private int iosNextOperation;
        private void CreateIos(NearbyOptions options) => NC_Create(options.ServiceId, (int)options.Strategy);
        private Task SubmitIos(Command command, string endpoint = "", string name = "", long request = 0, byte[] bytes = null)
        {
            if (closed) throw new NearbyException(NearbyError.InvalidState, "The Nearby session has ended.");
            if (iosPending.Count >= 64) throw new NearbyException(NearbyError.InvalidState, "Too many pending Nearby operations.");
            var op = new IosOperation();
            int id = ++iosNextOperation;
            iosPending.Add(id, op);
            try { NC_Submit(id, (int)command, endpoint, name, request, bytes, bytes?.Length ?? 0); }
            catch { iosPending.Remove(id); throw; }
            return op.Completion.Task;
        }
        private void CloseIos()
        {
            NC_Close();
            foreach (var op in iosPending.Values) op.Completion.TrySetCanceled();
            iosPending.Clear();
            EndpointFound = null; EndpointLost = null; ConnectionInitiated = null; ConnectionResult = null;
            Disconnected = null; BytesReceived = null; Faulted = null;
        }
        private void UpdateIos()
        {
            for (int i = 0; i < 64 && !closed; i++)
            {
                IntPtr pointer = NC_Poll();
                if (pointer == IntPtr.Zero) break;
                IosMessage message;
                try { message = JsonUtility.FromJson<IosMessage>(Marshal.PtrToStringAnsi(pointer)); }
                finally { NC_Free(pointer); }
                if (message == null) { FailIos("Invalid native message."); return; }
                switch (message.kind)
                {
                    case 0:
                        if (!iosPending.Remove(message.operation, out var operation)) break;
                        if (message.error == 0) operation.Completion.TrySetResult(true);
                        else operation.Completion.TrySetException(new NearbyException((NearbyError)message.error, message.text, message.nativeCode));
                        break;
                    case 1: NotifyIos(EndpointFound, new NearbyEndpoint(message.endpoint, message.text)); break;
                    case 2: NotifyIos(EndpointLost, message.endpoint); break;
                    case 3: NotifyIos(ConnectionInitiated, new NearbyConnectionRequest(message.request, new NearbyEndpoint(message.endpoint, message.text), message.code, message.incoming)); break;
                    case 4:
                        var status = (NearbyConnectionStatus)message.result;
                        if (status == NearbyConnectionStatus.Connected) connected.Add(message.endpoint);
                        NotifyIos(ConnectionResult, new NearbyConnectionResult(message.endpoint, message.request, status, message.nativeCode));
                        break;
                    case 5: connected.Remove(message.endpoint); NotifyIos(Disconnected, message.endpoint); break;
                    case 6:
                        var data = new byte[message.length];
                        if (NC_CopyPayload(data, data.Length) != data.Length) { FailIos("Invalid native payload."); return; }
                        NotifyIos(BytesReceived, message.endpoint, data); break;
                    case 7: FailIos("The Nearby receive queue overflowed.", NearbyError.QueueOverflow); return;
                }
            }
            foreach (var op in iosPending.Values)
                if (Time.realtimeSinceStartupAsDouble >= op.Deadline) { FailIos("A Nearby operation timed out.", NearbyError.Timeout); break; }
        }
        private void FailIos(string message, NearbyError error = NearbyError.NativeFailure)
        {
            var exception = new NearbyException(error, message);
            var handlers = Faulted;
            foreach (var op in iosPending.Values) op.Completion.TrySetException(exception);
            iosPending.Clear();
            Close();
            NotifyIos(handlers, null, exception);
        }
        private static void NotifyIos<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList()) try { handler(value); } catch (Exception e) { Debug.LogException(e); }
        }
        private static void NotifyIos<T, U>(Action<T, U> handlers, T a, U b)
        {
            if (handlers == null) return;
            foreach (Action<T, U> handler in handlers.GetInvocationList()) try { handler(a, b); } catch (Exception e) { Debug.LogException(e); }
        }
    }
}
#endif
