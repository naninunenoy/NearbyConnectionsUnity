using System;

namespace NearbyConnections
{
    public enum NearbyStrategy { PointToPoint, Star }

    public sealed class NearbyOptions
    {
        public string ServiceId { get; set; }
        public NearbyStrategy Strategy { get; set; }
    }

    public enum NearbyError
    {
        PermissionDenied = 1,
        UnsupportedPlatform,
        ServiceUnavailable,
        InvalidState,
        EndpointNotFound,
        PayloadTooLarge,
        NativeFailure,
        QueueOverflow,
        Timeout
    }

    public sealed class NearbyException : Exception
    {
        public NearbyError Error { get; }
        public int? NativeCode { get; }

        internal NearbyException(NearbyError error, string message, int? nativeCode = null)
            : base(message)
        {
            Error = error;
            NativeCode = nativeCode;
        }
    }

    public readonly struct NearbyEndpoint
    {
        public string Id { get; }
        public string Name { get; }

        internal NearbyEndpoint(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    public readonly struct NearbyConnectionRequest
    {
        public long Id { get; }
        public NearbyEndpoint Endpoint { get; }
        public string AuthenticationCode { get; }
        public bool IsIncoming { get; }

        internal NearbyConnectionRequest(long id, NearbyEndpoint endpoint, string code, bool incoming)
        {
            Id = id;
            Endpoint = endpoint;
            AuthenticationCode = code;
            IsIncoming = incoming;
        }
    }

    public enum NearbyConnectionStatus { Connected, Rejected, Failed }

    public readonly struct NearbyConnectionResult
    {
        public string EndpointId { get; }
        public long RequestId { get; }
        public NearbyConnectionStatus Status { get; }
        public int NativeCode { get; }

        internal NearbyConnectionResult(string endpointId, long requestId, NearbyConnectionStatus status, int nativeCode)
        {
            EndpointId = endpointId;
            RequestId = requestId;
            Status = status;
            NativeCode = nativeCode;
        }
    }
}
