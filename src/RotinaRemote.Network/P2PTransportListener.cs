using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RotinaRemote.Core.Logging;
using RotinaRemote.Protocol;

namespace RotinaRemote.Network
{
    public class P2PTransportListener
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;

        public event Action<Socket>? ClientConnected;

        public int LocalPort { get; private set; }

        public void Start(int port = 0)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            LocalPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _cts = new CancellationTokenSource();

            AppLogger.LogInfo("P2PListener", $"Listener TCP iniciado na porta {LocalPort}");

            _ = AcceptConnectionsAsync(_cts.Token);
        }

        private async Task AcceptConnectionsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var socket = await _listener.AcceptSocketAsync(ct);
                    AppLogger.LogInfo("P2PListener", $"Cliente conectado de {socket.RemoteEndPoint}");
                    ClientConnected?.Invoke(socket);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("P2PListener", "Erro ao aceitar conexão", ex);
                }
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
            AppLogger.LogInfo("P2PListener", "Listener TCP parado.");
        }
    }

    public class ConnectionSession
    {
        private readonly Socket _socket;
        private readonly NetworkStream _stream;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public event Action<PacketFrame>? FrameReceived;
        public event Action? Disconnected;

        public bool IsConnected => _socket.Connected;
        public IPEndPoint? RemoteEndPoint => (IPEndPoint?)_socket.RemoteEndPoint;

        public ConnectionSession(Socket socket)
        {
            _socket = socket;
            _stream = new NetworkStream(socket, ownsSocket: true);
            _ = ReceiveLoopAsync(_cts.Token);
        }

        public async Task SendFrameAsync(PacketFrame frame, CancellationToken ct = default)
        {
            if (!IsConnected) return;

            try
            {
                var bytes = frame.Serialize();
                await _stream.WriteAsync(bytes.AsMemory(0, bytes.Length), ct);
                await _stream.FlushAsync(ct);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ConnectionSession", "Erro ao enviar frame", ex);
                Close();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var headerBuffer = new byte[19]; // Header length
            try
            {
                while (!ct.IsCancellationRequested && IsConnected)
                {
                    // Read header
                    int readHeader = 0;
                    while (readHeader < 19)
                    {
                        int r = await _stream.ReadAsync(headerBuffer.AsMemory(readHeader, 19 - readHeader), ct);
                        if (r <= 0) throw new Exception("Conexão fechada pelo peer.");
                        readHeader += r;
                    }

                    // Check magic bytes "RR"
                    if (headerBuffer[0] != PacketFrame.MagicBytes[0] || headerBuffer[1] != PacketFrame.MagicBytes[1])
                    {
                        AppLogger.LogWarning("ConnectionSession", "Pacote com MagicBytes inválidos recebido.");
                        continue;
                    }

                    int payloadLen = BitConverter.ToInt32(headerBuffer, 15);
                    if (payloadLen <= 0 || payloadLen > 50_000_000)
                    {
                        AppLogger.LogWarning("ConnectionSession", $"PayloadLen inválido recebido: {payloadLen}");
                        break;
                    }

                    var totalFrame = new byte[19 + payloadLen];
                    Array.Copy(headerBuffer, 0, totalFrame, 0, 19);

                    int readPayload = 0;
                    while (readPayload < payloadLen)
                    {
                        int r = await _stream.ReadAsync(totalFrame.AsMemory(19 + readPayload, payloadLen - readPayload), ct);
                        if (r <= 0) throw new Exception("Conexão fechada durante o payload.");
                        readPayload += r;
                    }

                    if (PacketFrame.TryDeserialize(totalFrame, out var frame) && frame != null)
                    {
                        FrameReceived?.Invoke(frame);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogInfo("ConnectionSession", $"Sessão encerrada: {ex.Message}");
            }
            finally
            {
                Close();
            }
        }

        public void Close()
        {
            _cts.Cancel();
            try { _stream.Dispose(); } catch { }
            try { _socket.Dispose(); } catch { }
            Disconnected?.Invoke();
        }
    }
}
