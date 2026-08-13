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
}
