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

    public class LanDiscoveryService
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
            string senderDeviceId = parts[1].Trim();

            // Ignore self
            if (senderDeviceId.Equals(_myDeviceIdRaw, StringComparison.OrdinalIgnoreCase)) return;

            if (int.TryParse(parts[2], out int targetPort))
            {
                if (header == "RR_BEACON" || header == "RR_RESPONSE")
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
                else if (header == "RR_QUERY")
                {
                    string queriedId = parts[1].Trim();
                    if (queriedId.Equals(_myDeviceIdRaw, StringComparison.OrdinalIgnoreCase))
                    {
                        // Reply to sender that we are here!
                        SendBeaconTo(senderEndPoint);
                    }
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

        public void BroadcastBeacon()
        {
            if (_udpListener == null || string.IsNullOrEmpty(_myDeviceIdRaw)) return;

            try
            {
                string payload = $"RR_BEACON|{_myDeviceIdRaw}|{_myTcpPort}";
                byte[] data = Encoding.UTF8.GetBytes(payload);

                // Broadcast to global broadcast
                _udpListener.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

                // Broadcast to all active network interface broadcast addresses (useful for Hyper-V Sandbox virtual switch)
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
            }
            catch (Exception ex)
            {
                AppLogger.LogError("LanDiscovery", "Erro ao transmitir beacon LAN", ex);
            }
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

        public async Task<IPAddress?> ResolveDeviceIdAsync(string targetDeviceIdRaw, int timeoutMs = 1200)
        {
            string cleanId = targetDeviceIdRaw.Replace(" ", "");

            // 1. Check cache first
            if (DiscoveredPeers.TryGetValue(cleanId, out var peer))
            {
                if ((DateTime.UtcNow - peer.LastSeen).TotalSeconds < 15)
                {
                    return peer.IpAddress;
                }
            }

            // 2. Query over broadcast if not in cache
            if (_udpListener != null)
            {
                try
                {
                    string payload = $"RR_QUERY|{cleanId}|{_myTcpPort}";
                    byte[] data = Encoding.UTF8.GetBytes(payload);
                    _udpListener.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
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
    }
}
