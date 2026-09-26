using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RotinaRemote.Core.Logging;

namespace RotinaRemote.Network
{
    public class DiscoveredPeerInfo
    {
        public string DeviceIdRaw { get; set; } = string.Empty;
        public IPAddress IpAddress { get; set; } = IPAddress.Any;
        public int Port { get; set; } = 48270;
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }

    public class LanDiscoveryService : IDisposable
    {
        private const int DiscoveryPort = 48271;
        private UdpClient? _udpListener;
        private CancellationTokenSource? _cts;
        private string _myDeviceIdRaw = string.Empty;
        private int _myTcpPort = 48270;

        public ConcurrentDictionary<string, DiscoveredPeerInfo> DiscoveredPeers { get; } = new();

        public string? LocalIP
        {
            get
            {
                try
                {
                    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        {
                            foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                            {
                                if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                                {
                                    return ip.Address.ToString();
                                }
                            }
                        }
                    }
                }
                catch { }
                return null;
            }
        }

        public void Start(string myDeviceIdRaw, int tcpPort = 48270)
        {
            _myDeviceIdRaw = myDeviceIdRaw.Replace(" ", "");
            _myTcpPort = tcpPort;
            _cts = new CancellationTokenSource();

            try
            {
                _udpListener = new UdpClient();
                _udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                // Disable WSAECONNRESET (10054) on Windows so ICMP unreachable doesn't fail ReceiveAsync
                try
                {
                    const int SIO_UDP_CONNRESET = -1744830452;
                    _udpListener.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
                catch { }

                _udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _udpListener.EnableBroadcast = true;

                AppLogger.LogInfo("LanDiscovery", $"Serviço de Descoberta LAN ativo na porta UDP {DiscoveryPort}");

                _ = ListenAsync(_cts.Token);
                _ = BroadcastLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("LanDiscovery", "Falha ao iniciar descoberta LAN UDP", ex);
            }
        }

        private async Task ListenAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _udpListener != null)
            {
                try
                {
                    var result = await _udpListener.ReceiveAsync(ct);
                    string message = Encoding.UTF8.GetString(result.Buffer);

                    ProcessIncomingPacket(message, result.RemoteEndPoint);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("LanDiscovery", "Erro ao receber pacote UDP LAN", ex);
                }
            }
        }

        private void ProcessIncomingPacket(string message, IPEndPoint senderEndPoint)
        {
            var parts = message.Split('|');
            if (parts.Length < 3) return;

            string header = parts[0];

            if (header == "RR_BEACON" || header == "RR_RESPONSE")
            {
                string senderDeviceId = parts[1].Trim();

                // Ignore self
                if (senderDeviceId.Equals(_myDeviceIdRaw, StringComparison.OrdinalIgnoreCase)) return;

                if (int.TryParse(parts[2], out int targetPort))
                {
                    var peer = new DiscoveredPeerInfo
                    {
                        DeviceIdRaw = senderDeviceId,
                        IpAddress = senderEndPoint.Address,
                        Port = targetPort,
                        LastSeen = DateTime.UtcNow
                    };

                    DiscoveredPeers[senderDeviceId] = peer;
                    AppLogger.LogInfo("LanDiscovery", $"Peer descoberto na LAN: ID={senderDeviceId}, IP={senderEndPoint.Address}:{targetPort}");
                }
            }
            else if (header == "RR_QUERY")
            {
                string queriedTargetId = parts[1].Trim();
                // Respond if someone is asking for THIS device!
                if (queriedTargetId.Equals(_myDeviceIdRaw, StringComparison.OrdinalIgnoreCase))
                {
                    SendBeaconTo(senderEndPoint);
                    AppLogger.LogInfo("LanDiscovery", $"Respondido a RR_QUERY de {senderEndPoint} para ID {_myDeviceIdRaw}");
                }
            }
        }

        private async Task BroadcastLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                BroadcastBeacon();
                try
                {
                    await Task.Delay(3000, ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public static System.Collections.Generic.List<IPAddress> GetNeighborIpAddresses()
        {
            var list = new System.Collections.Generic.List<IPAddress>();
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("arp", "-a")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(1000);
                    foreach (var line in output.Split('\n'))
                    {
                        var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && IPAddress.TryParse(parts[0], out var ip))
                        {
                            if (ip.AddressFamily == AddressFamily.InterNetwork)
                            {
                                string ipStr = ip.ToString();
                                if (!ipStr.StartsWith("224.") && !ipStr.StartsWith("239.") && !ipStr.EndsWith(".255"))
                                {
                                    list.Add(ip);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        private void SendToAllBroadcasts(byte[] data)
        {
            if (_udpListener == null) return;

            try
            {
                // 1. Global broadcast
                _udpListener.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

                // 2. Broadcast to all active network interface broadcast addresses
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var unicast in ipProps.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var bytes = unicast.Address.GetAddressBytes();
                            var maskBytes = unicast.IPv4Mask?.GetAddressBytes();
                            if (maskBytes != null && maskBytes.Length == 4)
                            {
                                byte[] broadcastBytes = new byte[4];
                                for (int i = 0; i < 4; i++)
                                {
                                    broadcastBytes[i] = (byte)(bytes[i] | ~maskBytes[i]);
                                }
                                var bcastIp = new IPAddress(broadcastBytes);
                                _udpListener.Send(data, data.Length, new IPEndPoint(bcastIp, DiscoveryPort));
                            }
                        }
                    }
                }

                // 3. Unicast to all active ARP neighbors (crucial for Windows Sandbox / Hyper-V switch)
                var neighbors = GetNeighborIpAddresses();
                foreach (var neighborIp in neighbors)
                {
                    try
                    {
                        _udpListener.Send(data, data.Length, new IPEndPoint(neighborIp, DiscoveryPort));
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("LanDiscovery", "Erro ao transmitir pacote UDP de broadcast/unicast", ex);
            }
        }

        public void BroadcastBeacon()
        {
            if (_udpListener == null || string.IsNullOrEmpty(_myDeviceIdRaw)) return;

            string payload = $"RR_BEACON|{_myDeviceIdRaw}|{_myTcpPort}";
            byte[] data = Encoding.UTF8.GetBytes(payload);
            SendToAllBroadcasts(data);
        }

        private void SendBeaconTo(IPEndPoint targetEndPoint)
        {
            if (_udpListener == null || string.IsNullOrEmpty(_myDeviceIdRaw)) return;

            try
            {
                string payload = $"RR_RESPONSE|{_myDeviceIdRaw}|{_myTcpPort}";
                byte[] data = Encoding.UTF8.GetBytes(payload);
                _udpListener.Send(data, data.Length, targetEndPoint);
            }
            catch { }
        }

        public async Task<IPAddress?> ResolveDeviceIdAsync(string targetDeviceIdRaw, int timeoutMs = 3500)
        {
            string cleanId = targetDeviceIdRaw.Replace(" ", "");

            // 1. Check cache first (valid for 5 minutes)
            if (DiscoveredPeers.TryGetValue(cleanId, out var peer))
            {
                if ((DateTime.UtcNow - peer.LastSeen).TotalSeconds < 300)
                {
                    return peer.IpAddress;
                }
            }

            // 2. Query over all broadcast & unicast neighbor interfaces
            if (_udpListener != null)
            {
                try
                {
                    string payload = $"RR_QUERY|{cleanId}|{_myTcpPort}";
                    byte[] data = Encoding.UTF8.GetBytes(payload);
                    SendToAllBroadcasts(data);
                }
                catch { }

                // Wait briefly for response
                int waited = 0;
                while (waited < timeoutMs)
                {
                    await Task.Delay(100);
                    waited += 100;

                    if (DiscoveredPeers.TryGetValue(cleanId, out peer))
                    {
                        return peer.IpAddress;
                    }

                    // Re-transmit query halfway through timeout
                    if (waited == 1500)
                    {
                        try
                        {
                            string payload = $"RR_QUERY|{cleanId}|{_myTcpPort}";
                            byte[] data = Encoding.UTF8.GetBytes(payload);
                            SendToAllBroadcasts(data);
                        }
                        catch { }
                    }
                }
            }

            return null;
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _udpListener?.Close(); } catch { }
            _udpListener = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
