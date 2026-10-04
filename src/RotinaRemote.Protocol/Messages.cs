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
        DisconnectNotice,
        ResolutionChangeRequest,
        ResolutionChangeResponse,
        RemoteWindowControl
    }

    public class HandshakeRequestPayload
    {
        public string ClientDeviceId { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string EphemeralPublicKey { get; set; } = string.Empty; // Base64 ECDH Public Key
        public string Version { get; set; } = "1.0.0";
        public string ClientIp { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int ClientScreenWidth { get; set; }
        public int ClientScreenHeight { get; set; }
    }

    public class SignalingConnectRequestPayload
    {
        public string CallerDeviceId { get; set; } = string.Empty;
        public string CallerIp { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int ClientScreenWidth { get; set; }
        public int ClientScreenHeight { get; set; }
    }

    public class ResolutionChangeRequestPayload
    {
        public int TargetWidth { get; set; }
        public int TargetHeight { get; set; }
    }

    public class ResolutionChangeResponsePayload
    {
        public bool Success { get; set; }
        public int CurrentWidth { get; set; }
        public int CurrentHeight { get; set; }
        public int EffectiveWidth { get => CurrentWidth; set => CurrentWidth = value; }
        public int EffectiveHeight { get => CurrentHeight; set => CurrentHeight = value; }
        public string Message { get; set; } = string.Empty;
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

    public class ChatMessagePayload
    {
        public string MessageId { get; set; } = Guid.NewGuid().ToString("N");
        public string SenderId { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class ClipboardPayload
    {
        public string Text { get; set; } = string.Empty;
        public long Timestamp { get; set; } = DateTime.UtcNow.Ticks;
    }

    public enum RemoteWindowAction : byte
    {
        MinimizeActiveWindow = 0,
        MaximizeActiveWindow = 1,
        CloseActiveWindow = 2,
        MinimizeHostRotina = 3,
        MaximizeHostRotina = 4
    }

    public class RemoteWindowControlPayload
    {
        public RemoteWindowAction Action { get; set; }
    }

    public enum FileTransferAction : byte
    {
        Start = 1,
        Chunk = 2,
        Complete = 3,
        Cancel = 4
    }

    public class FileTransferPayload
    {
        public FileTransferAction Action { get; set; }
        public string TransferId { get; set; } = Guid.NewGuid().ToString("N");
        public string FileName { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public long Offset { get; set; }
        public string DataBase64 { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
    }

    public enum ProcessManagerAction : byte
    {
        ListRequest = 1,
        ListResponse = 2,
        KillRequest = 3,
        KillResponse = 4
    }

    public class RemoteProcessItem
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string MainWindowTitle { get; set; } = string.Empty;
        public long MemoryBytes { get; set; }
        public string MemoryFormatted => $"{MemoryBytes / (1024.0 * 1024.0):F1} MB";
        public bool IsResponding { get; set; } = true;
        public string Status => IsResponding ? "Em Execução" : "Não Responde";
    }

    public class ProcessManagerPayload
    {
        public ProcessManagerAction Action { get; set; }
        public int TargetProcessId { get; set; }
        public List<RemoteProcessItem> Processes { get; set; } = new();
        public bool Success { get; set; } = true;
        public string Message { get; set; } = string.Empty;
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
