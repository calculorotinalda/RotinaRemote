using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RotinaRemote.Core.Logging;

namespace RotinaRemote.SignalingServer
{
    public class PeerSession
    {
        public string DeviceId { get; set; } = string.Empty;
        public WebSocket Socket { get; set; } = default!;
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    }

    public class SignalingMessage
    {
        public string Type { get; set; } = string.Empty; // Register, ConnectRequest, ConnectResponse, SignalData, Heartbeat
        public string SourceDeviceId { get; set; } = string.Empty;
        public string TargetDeviceId { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }

    public class Program
    {
        private static readonly ConcurrentDictionary<string, PeerSession> _peers = new();

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            
            string port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

            var app = builder.Build();
            app.UseWebSockets();

            app.Map("/ws", async context =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await HandleWebSocketConnectionAsync(webSocket);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            });

            // Handle GET and HEAD requests for Render.com health checks
            app.MapMethods("/", new[] { "GET", "HEAD" }, () => "RotinaRemote Signaling Server OK - Active Peers: " + _peers.Count);
            app.MapMethods("/healthz", new[] { "GET", "HEAD" }, () => "OK");

            AppLogger.LogInfo("SignalingServer", $"Servidor de Sinalização iniciado na porta {port}.");
            app.Run();
        }

        private static async Task HandleWebSocketConnectionAsync(WebSocket socket)
        {
            var buffer = new byte[8192];
            string registeredDeviceId = string.Empty;

            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        var msg = JsonSerializer.Deserialize<SignalingMessage>(json);
                        if (msg != null)
                        {
                            await ProcessSignalingMessageAsync(socket, msg, devId => registeredDeviceId = devId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("SignalingServer", "Erro no WebSocket peer", ex);
            }
            finally
            {
                if (!string.IsNullOrEmpty(registeredDeviceId))
                {
                    _peers.TryRemove(registeredDeviceId, out _);
                    AppLogger.LogInfo("SignalingServer", $"Peer {registeredDeviceId} desconectado. Total peers: {_peers.Count}");
                }
            }
        }

        private static async Task ProcessSignalingMessageAsync(WebSocket socket, SignalingMessage msg, Action<string> onRegistered)
        {
            switch (msg.Type)
            {
                case "Register":
                    if (!string.IsNullOrEmpty(msg.SourceDeviceId))
                    {
                        var session = new PeerSession { DeviceId = msg.SourceDeviceId, Socket = socket };
                        _peers[msg.SourceDeviceId] = session;
                        onRegistered(msg.SourceDeviceId);
                        AppLogger.LogInfo("SignalingServer", $"Peer registrado com ID: {msg.SourceDeviceId}. Total: {_peers.Count}");

                        var ack = new SignalingMessage { Type = "RegisterAck", TargetDeviceId = msg.SourceDeviceId, Payload = "RegisteredSuccessfully" };
                        await SendJsonAsync(socket, ack);
                    }
                    break;

                case "ConnectRequest":
                case "ConnectResponse":
                case "SignalData":
                    if (_peers.TryGetValue(msg.TargetDeviceId, out var targetSession) && targetSession.Socket.State == WebSocketState.Open)
                    {
                        await SendJsonAsync(targetSession.Socket, msg);
                    }
                    else
                    {
                        var errorMsg = new SignalingMessage
                        {
                            Type = "Error",
                            TargetDeviceId = msg.SourceDeviceId,
                            Payload = "PeerNãoEncontradoOuOffline"
                        };
                        await SendJsonAsync(socket, errorMsg);
                    }
                    break;

                case "Heartbeat":
                    var pong = new SignalingMessage { Type = "HeartbeatPong", TargetDeviceId = msg.SourceDeviceId };
                    await SendJsonAsync(socket, pong);
                    break;
            }
        }

        private static async Task SendJsonAsync(WebSocket socket, SignalingMessage msg)
        {
            var json = JsonSerializer.Serialize(msg);
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}
