using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RotinaRemote.Core.Logging;

namespace RotinaRemote.RelayServer
{
    public class RelaySessionPair
    {
        public string SessionId { get; set; } = string.Empty;
        public Socket? PeerA { get; set; }
        public Socket? PeerB { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Program
    {
        private static readonly ConcurrentDictionary<string, RelaySessionPair> _sessions = new();

        public static async Task Main(string[] args)
        {
            AppLogger.LogInfo("RelayServer", "Servidor Relay RotinaRemote iniciando na porta 5001...");
            var listener = new TcpListener(IPAddress.Any, 5001);
            listener.Start();

            while (true)
            {
                try
                {
                    var clientSocket = await listener.AcceptSocketAsync();
                    _ = HandleRelayClientAsync(clientSocket);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("RelayServer", "Erro ao aceitar conexão relay", ex);
                }
            }
        }

        private static async Task HandleRelayClientAsync(Socket socket)
        {
            try
            {
                // Read 36-byte GUID session handshake token
                var headerBuffer = new byte[36];
                int read = 0;
                while (read < 36)
                {
                    int r = await socket.ReceiveAsync(headerBuffer.AsMemory(read, 36 - read), SocketFlags.None);
                    if (r <= 0) return;
                    read += r;
                }

                var sessionId = System.Text.Encoding.UTF8.GetString(headerBuffer).Trim();

                var pair = _sessions.GetOrAdd(sessionId, id => new RelaySessionPair { SessionId = id });

                lock (pair)
                {
                    if (pair.PeerA == null)
                    {
                        pair.PeerA = socket;
                        AppLogger.LogInfo("RelayServer", $"Sessão {sessionId}: Peer A conectado.");
                    }
                    else if (pair.PeerB == null)
                    {
                        pair.PeerB = socket;
                        AppLogger.LogInfo("RelayServer", $"Sessão {sessionId}: Peer B conectado. Iniciando ponte bidirecional.");
                        _ = PipeStreamsAsync(pair.PeerA, pair.PeerB, sessionId);
                        _ = PipeStreamsAsync(pair.PeerB, pair.PeerA, sessionId);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("RelayServer", "Erro no tratamento do cliente relay", ex);
            }
        }

        private static async Task PipeStreamsAsync(Socket source, Socket destination, string sessionId)
        {
            var buffer = new byte[65536]; // 64 KB buffer
            try
            {
                while (source.Connected && destination.Connected)
                {
                    int bytesRead = await source.ReceiveAsync(buffer.AsMemory(), SocketFlags.None);
                    if (bytesRead <= 0) break;

                    await destination.SendAsync(buffer.AsMemory(0, bytesRead), SocketFlags.None);
                }
            }
            catch
            {
                // Normal termination
            }
            finally
            {
                _sessions.TryRemove(sessionId, out _);
                try { source.Dispose(); } catch { }
                try { destination.Dispose(); } catch { }
                AppLogger.LogInfo("RelayServer", $"Ponte da sessão {sessionId} encerrada.");
            }
        }
    }
}
