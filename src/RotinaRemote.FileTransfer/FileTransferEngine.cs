using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using RotinaRemote.Core.Logging;
using RotinaRemote.Protocol;

namespace RotinaRemote.FileTransfer
{
    public class FileTransferProgress
    {
        public string TransferId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public double ProgressPercentage => TotalBytes > 0 ? (double)BytesTransferred / TotalBytes * 100.0 : 0.0;
        public double SpeedMBps { get; set; }
        public TimeSpan ETA { get; set; }
        public bool IsComplete { get; set; }
        public bool IsOutgoing { get; set; }
    }

    public class FileTransferEngine
    {
        public const int ChunkSize = 64 * 1024; // 64 KB por bloco

        public event Action<FileTransferProgress>? ProgressReported;
        public event Action<string, long>? FileReceived;

        private class IncomingTransferState
        {
            public string TransferId { get; set; } = string.Empty;
            public string OriginalFileName { get; set; } = string.Empty;
            public string DestinationPath { get; set; } = string.Empty;
            public FileStream? Stream { get; set; }
            public long TotalBytes { get; set; }
            public long BytesReceived { get; set; }
            public DateTime StartTime { get; set; } = DateTime.UtcNow;
            public IncrementalHash Hasher { get; set; } = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }

        private readonly ConcurrentDictionary<string, IncomingTransferState> _incomingTransfers = new();

        public static string GetDefaultDownloadDirectory()
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop))
                {
                    return desktop;
                }
            }
            catch { }

            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloads = Path.Combine(userProfile, "Downloads");
                if (Directory.Exists(downloads))
                {
                    return downloads;
                }
                Directory.CreateDirectory(downloads);
                return downloads;
            }
            catch { }

            string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Downloads");
            if (!Directory.Exists(fallback))
            {
                Directory.CreateDirectory(fallback);
            }
            return fallback;
        }

        public async Task<string> SendFileAsync(
            string filePath,
            Func<FileTransferPayload, Task> sendPacketFunc,
            Action<FileTransferProgress>? onProgress = null,
            CancellationToken ct = default)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("O ficheiro a enviar não foi encontrado.", filePath);
            }

            var fileInfo = new FileInfo(filePath);
            string fileName = fileInfo.Name;
            long totalBytes = fileInfo.Length;
            string transferId = Guid.NewGuid().ToString("N");

            AppLogger.LogInfo("FileTransfer", $"[SEND START] Iniciando envio do ficheiro '{fileName}' ({totalBytes} bytes, ID={transferId}).");

            // 1. Notificar início da transferência
            var startPayload = new FileTransferPayload
            {
                Action = FileTransferAction.Start,
                TransferId = transferId,
                FileName = fileName,
                TotalBytes = totalBytes,
                Offset = 0
            };
            await sendPacketFunc(startPayload);

            var startTime = DateTime.UtcNow;
            long bytesSent = 0;
            byte[] buffer = new byte[ChunkSize];

            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, useAsync: true))
            {
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    sha256.AppendData(buffer, 0, read);

                    byte[] chunkBytes = new byte[read];
                    Buffer.BlockCopy(buffer, 0, chunkBytes, 0, read);

                    var chunkPayload = new FileTransferPayload
                    {
                        Action = FileTransferAction.Chunk,
                        TransferId = transferId,
                        FileName = fileName,
                        TotalBytes = totalBytes,
                        Offset = bytesSent,
                        DataBase64 = Convert.ToBase64String(chunkBytes)
                    };
                    await sendPacketFunc(chunkPayload);

                    bytesSent += read;

                    double elapsedSec = Math.Max(0.001, (DateTime.UtcNow - startTime).TotalSeconds);
                    double speedMBps = (bytesSent / (1024.0 * 1024.0)) / elapsedSec;
                    double remainingBytes = Math.Max(0, totalBytes - bytesSent);
                    TimeSpan eta = speedMBps > 0.01 ? TimeSpan.FromSeconds(remainingBytes / (speedMBps * 1024.0 * 1024.0)) : TimeSpan.Zero;

                    var progress = new FileTransferProgress
                    {
                        TransferId = transferId,
                        FileName = fileName,
                        BytesTransferred = bytesSent,
                        TotalBytes = totalBytes,
                        SpeedMBps = speedMBps,
                        ETA = eta,
                        IsComplete = bytesSent >= totalBytes,
                        IsOutgoing = true
                    };

                    ProgressReported?.Invoke(progress);
                    onProgress?.Invoke(progress);
                }
            }

            // 2. Notificar conclusão com hash SHA256
            byte[] hashBytes = sha256.GetHashAndReset();
            string hashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

            var completePayload = new FileTransferPayload
            {
                Action = FileTransferAction.Complete,
                TransferId = transferId,
                FileName = fileName,
                TotalBytes = totalBytes,
                Offset = bytesSent,
                Sha256 = hashHex
            };
            await sendPacketFunc(completePayload);

            AppLogger.LogInfo("FileTransfer", $"[SEND COMPLETE] Ficheiro '{fileName}' enviado com sucesso ({totalBytes} bytes, SHA256={hashHex}).");
            return transferId;
        }

        public async Task<string?> HandleIncomingPayloadAsync(FileTransferPayload payload, string? targetDirectory = null)
        {
            if (payload == null) return null;

            string targetDir = !string.IsNullOrEmpty(targetDirectory) ? targetDirectory : GetDefaultDownloadDirectory();
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            switch (payload.Action)
            {
                case FileTransferAction.Start:
                {
                    string safeName = Path.GetFileName(payload.FileName);
                    if (string.IsNullOrWhiteSpace(safeName))
                    {
                        safeName = $"ficheiro_{DateTime.Now:yyyyMMdd_HHmmss}.dat";
                    }

                    string destPath = Path.Combine(targetDir, safeName);
                    // Evitar sobreposição gerando nome único caso já exista
                    if (File.Exists(destPath))
                    {
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(safeName);
                        string ext = Path.GetExtension(safeName);
                        int count = 1;
                        do
                        {
                            destPath = Path.Combine(targetDir, $"{nameWithoutExt} ({count}){ext}");
                            count++;
                        } while (File.Exists(destPath));
                    }

                    var stream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize, useAsync: true);
                    var state = new IncomingTransferState
                    {
                        TransferId = payload.TransferId,
                        OriginalFileName = safeName,
                        DestinationPath = destPath,
                        Stream = stream,
                        TotalBytes = payload.TotalBytes,
                        BytesReceived = 0,
                        StartTime = DateTime.UtcNow
                    };

                    _incomingTransfers[payload.TransferId] = state;
                    AppLogger.LogInfo("FileTransfer", $"[RECV START] Ficheiro '{safeName}' iniciado. Destino='{destPath}', Tamanho={payload.TotalBytes} bytes.");

                    ProgressReported?.Invoke(new FileTransferProgress
                    {
                        TransferId = payload.TransferId,
                        FileName = safeName,
                        BytesTransferred = 0,
                        TotalBytes = payload.TotalBytes,
                        IsComplete = false,
                        IsOutgoing = false
                    });

                    return destPath;
                }

                case FileTransferAction.Chunk:
                {
                    if (_incomingTransfers.TryGetValue(payload.TransferId, out var state) && state.Stream != null)
                    {
                        byte[] chunkBytes = Convert.FromBase64String(payload.DataBase64);
                        if (chunkBytes.Length > 0)
                        {
                            if (state.Stream.Position != payload.Offset)
                            {
                                state.Stream.Seek(payload.Offset, SeekOrigin.Begin);
                            }

                            await state.Stream.WriteAsync(chunkBytes.AsMemory(0, chunkBytes.Length));
                            state.BytesReceived += chunkBytes.Length;
                            state.Hasher.AppendData(chunkBytes);

                            double elapsedSec = Math.Max(0.001, (DateTime.UtcNow - state.StartTime).TotalSeconds);
                            double speedMBps = (state.BytesReceived / (1024.0 * 1024.0)) / elapsedSec;

                            ProgressReported?.Invoke(new FileTransferProgress
                            {
                                TransferId = state.TransferId,
                                FileName = state.OriginalFileName,
                                BytesTransferred = state.BytesReceived,
                                TotalBytes = state.TotalBytes,
                                SpeedMBps = speedMBps,
                                IsComplete = false,
                                IsOutgoing = false
                            });
                        }
                    }
                    return null;
                }

                case FileTransferAction.Complete:
                {
                    if (_incomingTransfers.TryRemove(payload.TransferId, out var state) && state.Stream != null)
                    {
                        await state.Stream.FlushAsync();
                        state.Stream.Close();
                        await state.Stream.DisposeAsync();

                        byte[] hashBytes = state.Hasher.GetHashAndReset();
                        string localHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                        AppLogger.LogInfo("FileTransfer", $"[RECV COMPLETE] Ficheiro '{state.OriginalFileName}' concluído ({state.BytesReceived} bytes). Gravado em: '{state.DestinationPath}'. Hash={localHash}");

                        ProgressReported?.Invoke(new FileTransferProgress
                        {
                            TransferId = state.TransferId,
                            FileName = state.OriginalFileName,
                            BytesTransferred = state.BytesReceived,
                            TotalBytes = state.TotalBytes,
                            IsComplete = true,
                            IsOutgoing = false
                        });

                        FileReceived?.Invoke(state.DestinationPath, state.BytesReceived);
                        return state.DestinationPath;
                    }
                    return null;
                }

                case FileTransferAction.Cancel:
                {
                    if (_incomingTransfers.TryRemove(payload.TransferId, out var state))
                    {
                        try
                        {
                            if (state.Stream != null)
                            {
                                state.Stream.Close();
                                await state.Stream.DisposeAsync();
                            }
                            if (File.Exists(state.DestinationPath))
                            {
                                File.Delete(state.DestinationPath);
                            }
                        }
                        catch { }
                        AppLogger.LogWarning("FileTransfer", $"[RECV CANCEL] Transferência do ficheiro '{state.OriginalFileName}' cancelada.");
                    }
                    return null;
                }
            }

            return null;
        }
    }
}
