using System;
using System.Text.Json;
using RotinaRemote.Core.Models;

namespace RotinaRemote.Protocol
{
    public enum ControlMessageType
    {
        HandshakeRequest,
        HandshakeResponse,
        SessionAuthRequest,
        SessionAuthResponse,
        PermissionGrant,
        HeartbeatPing,
        HeartbeatPong,
        DisconnectNotice
    }

    public class HandshakeRequestPayload
    {
        public string ClientDeviceId { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string EphemeralPublicKey { get; set; } = string.Empty; // Base64 ECDH Public Key
        public string Version { get; set; } = "1.0.0";
    }

    public class HandshakeResponsePayload
    {
        public bool Accepted { get; set; }
        public string HostDeviceId { get; set; } = string.Empty;
        public string HostName { get; set; } = string.Empty;
        public string EphemeralPublicKey { get; set; } = string.Empty; // Base64 ECDH Public Key
        public string SessionId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class SessionAuthRequestPayload
    {
        public string SessionId { get; set; } = string.Empty;
        public string ClientDeviceId { get; set; } = string.Empty;
        public string RequestedPermissions { get; set; } = "All";
    }

    public class PermissionGrantPayload
    {
        public string SessionId { get; set; } = string.Empty;
        public bool Approved { get; set; }
        public SessionPermission GrantedPermissions { get; set; }
        public string RejectReason { get; set; } = string.Empty;
    }

    public class HeartbeatPayload
    {
        public long Timestamp { get; set; } = DateTime.UtcNow.Ticks;
        public int CurrentFps { get; set; }
        public int LatencyMs { get; set; }
    }

    public static class MessageSerializer
    {
        public static byte[] SerializeJson<T>(T payload)
        {
            return JsonSerializer.SerializeToUtf8Bytes(payload);
        }

        public static T? DeserializeJson<T>(byte[] data)
        {
            if (data == null || data.Length == 0) return default;
            return JsonSerializer.Deserialize<T>(data);
        }
    }
}
