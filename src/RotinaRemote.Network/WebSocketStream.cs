using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace RotinaRemote.Network
{
    public class WebSocketStream : Stream
    {
        private readonly ClientWebSocket _ws;
        private readonly bool _ownsSocket;
        private readonly byte[] _readBuffer = new byte[65536];
        private int _readBufferOffset = 0;
        private int _readBufferCount = 0;

        public WebSocketStream(ClientWebSocket ws, bool ownsSocket = true)
        {
            _ws = ws ?? throw new ArgumentNullException(nameof(ws));
            _ownsSocket = ownsSocket;
        }

        public override bool CanRead => _ws.State == WebSocketState.Open;
        public override bool CanSeek => false;
        public override bool CanWrite => _ws.State == WebSocketState.Open;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            if (_ws.State != WebSocketState.Open)
                return 0;

            // If we have buffered bytes from a previous receive, drain them first
            if (_readBufferCount > 0)
            {
                int toCopy = Math.Min(_readBufferCount, destination.Length);
                new ReadOnlySpan<byte>(_readBuffer, _readBufferOffset, toCopy).CopyTo(destination.Span);
                _readBufferOffset += toCopy;
                _readBufferCount -= toCopy;
                return toCopy;
            }

            var result = await _ws.ReceiveAsync(destination, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return 0;
            }

            return result.Count;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return await ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_ws.State != WebSocketState.Open)
                throw new IOException("WebSocket is not open.");

            await _ws.SendAsync(buffer, WebSocketMessageType.Binary, true, cancellationToken);
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _ownsSocket)
            {
                try
                {
                    if (_ws.State == WebSocketState.Open)
                    {
                        _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None).GetAwaiter().GetResult();
                    }
                }
                catch { }
                try { _ws.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
