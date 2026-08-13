using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RotinaRemote.Core.Logging;

namespace RotinaRemote.Network
{
    public class SignalingClient
    {
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private string _serverUrl = string.Empty;
        private string _myDeviceIdRaw = string.Empty;
        private bool _isRegistered;

        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open && _isRegistered;

        public event Action<string, string, string>? ConnectRequestReceived;
        public event Action<string, string, string>? ConnectResponseReceived;
        public event Action<string>? ErrorReceived;

        public async Task StartAsync(string serverUrl, string myDeviceIdRaw)
        {
            _serverUrl = serverUrl;
            _myDeviceIdRaw = myDeviceIdRaw.Replace(" ", "");
            _cts = new CancellationTokenSource();

            _ = ConnectAndMaintainLoopAsync(_cts.Token);
            await Task.CompletedTask;
        }

        private async Task ConnectAndMaintainLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _ws = new ClientWebSocket();
                    var uri = new Uri(_serverUrl);

                    using var connectCts = new CancellationTokenSource(4000);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, connectCts.Token);

                    await _ws.ConnectAsync(uri, linkedCts.Token);
                    AppLogger.LogInfo("SignalingClient", $"Conectado ao Servidor de Sinalização em {_serverUrl}");

                    await RegisterAsync();

                    _ = HeartbeatLoopAsync(ct);
                    await ReceiveLoopAsync(ct);
                }
                catch (Exception ex)
                {
                    _isRegistered = false;
                    AppLogger.LogWarning("SignalingClient", $"Não foi possível conectar ao Servidor de Sinalização ({_serverUrl}): {ex.Message}");
                }

                try
                {
                    await Task.Delay(10000, ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private async Task RegisterAsync()
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;

            var msg = new
            {
                Type = "Register",
                SourceDeviceId = _myDeviceIdRaw,
                TargetDeviceId = "",
                Payload = "ClientRegister"
            };

            var json = JsonSerializer.Serialize(msg);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                try
                {
                    await Task.Delay(15000, ct);
                    if (_ws != null && _ws.State == WebSocketState.Open)
                    {
                        var msg = new { Type = "Heartbeat", SourceDeviceId = _myDeviceIdRaw, TargetDeviceId = "", Payload = "" };
                        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg));
                        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
                    }
                }
                catch
                {
                    break;
                }
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];
            while (!ct.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                }
                catch
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        string type = root.GetProperty("Type").GetString() ?? "";
                        string source = root.TryGetProperty("SourceDeviceId", out var s) ? s.GetString() ?? "" : "";
                        string target = root.TryGetProperty("TargetDeviceId", out var t) ? t.GetString() ?? "" : "";
                        string payload = root.TryGetProperty("Payload", out var p) ? p.GetString() ?? "" : "";

                        if (type == "RegisterAck")
                        {
                            _isRegistered = true;
                            AppLogger.LogInfo("SignalingClient", $"Registrado com sucesso no Servidor de Sinalização com ID: {_myDeviceIdRaw}");
                        }
                        else if (type == "ConnectRequest")
                        {
                            ConnectRequestReceived?.Invoke(source, target, payload);
                        }
                        else if (type == "ConnectResponse")
                        {
                            ConnectResponseReceived?.Invoke(source, target, payload);
                        }
                        else if (type == "Error")
                        {
                            ErrorReceived?.Invoke(payload);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("SignalingClient", "Erro ao processar mensagem do servidor de sinalização", ex);
                    }
                }
            }
        }

        public async Task SendMessageAsync(string type, string targetDeviceIdRaw, string payload)
        {
            if (_ws != null && _ws.State == WebSocketState.Open)
            {
                var msg = new
                {
                    Type = type,
                    SourceDeviceId = _myDeviceIdRaw,
                    TargetDeviceId = targetDeviceIdRaw.Replace(" ", ""),
                    Payload = payload
                };

                var json = JsonSerializer.Serialize(msg);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _ws?.Dispose(); } catch { }
            _ws = null;
            _isRegistered = false;
        }
    }
}
