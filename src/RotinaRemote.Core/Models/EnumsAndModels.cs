using System;

namespace RotinaRemote.Core.Models
{
    [Flags]
    public enum SessionPermission
    {
        None = 0,
        ViewScreen = 1 << 0,
        ControlMouse = 1 << 1,
        ControlKeyboard = 1 << 2,
        FileTransfer = 1 << 3,
        ClipboardSync = 1 << 4,
        LockLocalInput = 1 << 5,
        AdministrativeActions = 1 << 6,
        All = ViewScreen | ControlMouse | ControlKeyboard | FileTransfer | ClipboardSync | LockLocalInput | AdministrativeActions
    }

    public enum ConnectionState
    {
        Initializing,
        Ready,
        Connecting,
        LocatingPeer,
        Negotiating,
        Authenticating,
        ConnectedDirect,
        ConnectedRelay,
        Reconnecting,
        Disconnected,
        ConnectionLost,
        Error
    }

    public enum TransportType
    {
        DirectP2P,
        Relay
    }

    public class ConnectionMetrics
    {
        public int LatencyMs { get; set; }
        public int Fps { get; set; }
        public double BandwidthKbps { get; set; }
        public TransportType Transport { get; set; } = TransportType.DirectP2P;
        public long PacketsSent { get; set; }
        public long PacketsReceived { get; set; }
        public double LossRate { get; set; }
    }

    public class ConnectionHistoryItem
    {
        public string RemoteId { get; set; } = string.Empty;
        public string RemoteName { get; set; } = string.Empty;
        public DateTime ConnectionTime { get; set; }
        public TimeSpan Duration { get; set; }
        public TransportType Transport { get; set; }
        public string Status { get; set; } = "Concluída";
    }

    public class GeoLocationInfo
    {
        public string CallerDeviceId { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
    }

    public class SignalingEndpointInfo
    {
        public bool Accepted { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public string LocalIp { get; set; } = string.Empty;
        public string PublicIp { get; set; } = string.Empty;
        public int Port { get; set; } = 48270;
        public string RelaySessionId { get; set; } = string.Empty;
        public string RelayServerUrl { get; set; } = string.Empty;
        public string PermissionMode { get; set; } = "FullControl"; // "FullControl" ou "OnlyRead"
        public GeoLocationInfo? CallerInfo { get; set; }
    }
}

