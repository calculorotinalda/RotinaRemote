using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using RotinaRemote.Core.Logging;

namespace RotinaRemote.Network
{
    public class StunResult
    {
        public bool Success { get; set; }
        public IPEndPoint? PublicEndPoint { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public static class StunClient
    {
        public static async Task<StunResult> QueryPublicEndPointAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            try
            {
                using var client = new UdpClient();
                client.Client.ReceiveTimeout = 3000;
                client.Client.SendTimeout = 3000;

                // STUN Binding Request (20 bytes header)
                var request = new byte[20];
                request[0] = 0x00; request[1] = 0x01; // Message Type: Binding Request
                request[2] = 0x00; request[3] = 0x00; // Message Length: 0
                // Magic Cookie: 0x2112A442
                request[4] = 0x21; request[5] = 0x12; request[6] = 0xA4; request[7] = 0x42;
                // Transaction ID (12 random bytes)
                RandomNumberGenerator.Fill(request.AsSpan(8, 12));

                var remoteEP = new IPEndPoint(IPAddress.Any, 0);
                var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
                if (addresses.Length == 0)
                {
                    return new StunResult { Success = false, ErrorMessage = "Endereço STUN não encontrado via DNS." };
                }

                var serverEP = new IPEndPoint(addresses[0], port);
                await client.SendAsync(request, request.Length, serverEP);

                var receiveTask = client.ReceiveAsync(cancellationToken);
                var completedTask = await Task.WhenAny(receiveTask.AsTask(), Task.Delay(3000, cancellationToken));

                if (completedTask == receiveTask.AsTask())
                {
                    var result = await receiveTask;
                    var buffer = result.Buffer;

                    if (buffer.Length >= 32)
                    {
                        // Parse XOR-MAPPED-ADDRESS or MAPPED-ADDRESS attributes
                        for (int i = 20; i < buffer.Length - 4;)
                        {
                            ushort type = (ushort)((buffer[i] << 8) | buffer[i + 1]);
                            ushort length = (ushort)((buffer[i + 2] << 8) | buffer[i + 3]);

                            if (type == 0x0020 && length >= 8) // XOR-MAPPED-ADDRESS
                            {
                                ushort portNum = (ushort)(((buffer[i + 6] << 8) | buffer[i + 7]) ^ 0x2112);
                                byte[] ipBytes = new byte[4];
                                ipBytes[0] = (byte)(buffer[i + 8] ^ 0x21);
                                ipBytes[1] = (byte)(buffer[i + 9] ^ 0x12);
                                ipBytes[2] = (byte)(buffer[i + 10] ^ 0xA4);
                                ipBytes[3] = (byte)(buffer[i + 11] ^ 0x42);

                                var publicIp = new IPAddress(ipBytes);
                                return new StunResult
                                {
                                    Success = true,
                                    PublicEndPoint = new IPEndPoint(publicIp, portNum)
                                };
                            }
                            i += 4 + length;
                        }
                    }
                }

                return new StunResult { Success = false, ErrorMessage = "Timeout ou resposta STUN inválida." };
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning("StunClient", $"Falha ao consultar servidor STUN {host}:{port}: {ex.Message}");
                return new StunResult { Success = false, ErrorMessage = ex.Message };
            }
        }
    }
}
