using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using RotinaRemote.Core.Logging;

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
    }

    public class FileTransferEngine
    {
        public const int ChunkSize = 64 * 1024; // 64 KB per chunk

        public event Action<FileTransferProgress>? ProgressReported;

        public async Task<string> CalculateSha256Async(string filePath, CancellationToken ct = default)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream, ct);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public async Task ProcessIncomingChunkAsync(string destinationPath, long offset, byte[] chunkData, CancellationToken ct = default)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(destinationPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            stream.Seek(offset, SeekOrigin.Begin);
            await stream.WriteAsync(chunkData.AsMemory(0, chunkData.Length), ct);
        }
    }
}
