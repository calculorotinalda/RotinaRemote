using System;
using System.IO;

namespace RotinaRemote.Protocol
{
    public enum ChannelType : byte
    {
        Control = 0x00,
        Video = 0x01,
        Input = 0x02,
        File = 0x03,
        Clipboard = 0x04
    }

    public class PacketFrame
    {
        public static readonly byte[] MagicBytes = new byte[] { 0x52, 0x52 }; // "RR"
        public ChannelType Channel { get; set; }
        public uint SequenceNumber { get; set; }
        public long Timestamp { get; set; }
        public byte[] Payload { get; set; } = Array.Empty<byte>();

        public PacketFrame() { }

        public PacketFrame(ChannelType channel, uint sequenceNumber, byte[] payload)
        {
            Channel = channel;
            SequenceNumber = sequenceNumber;
            Timestamp = DateTime.UtcNow.Ticks;
            Payload = payload ?? Array.Empty<byte>();
        }

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(MagicBytes);
            writer.Write((byte)Channel);
            writer.Write(SequenceNumber);
            writer.Write(Timestamp);
            writer.Write(Payload.Length);
            writer.Write(Payload);

            return ms.ToArray();
        }

        public static bool TryDeserialize(byte[] buffer, out PacketFrame? frame)
        {
            frame = null;
            if (buffer == null || buffer.Length < 19) return false; // 2 + 1 + 4 + 8 + 4 = 19 bytes header

            try
            {
                using var ms = new MemoryStream(buffer);
                using var reader = new BinaryReader(ms);

                var magic1 = reader.ReadByte();
                var magic2 = reader.ReadByte();
                if (magic1 != MagicBytes[0] || magic2 != MagicBytes[1]) return false;

                var channel = (ChannelType)reader.ReadByte();
                var sequenceNumber = reader.ReadUInt32();
                var timestamp = reader.ReadInt64();
                var payloadLen = reader.ReadInt32();

                if (payloadLen < 0 || ms.Length - ms.Position < payloadLen) return false;

                var payload = reader.ReadBytes(payloadLen);

                frame = new PacketFrame
                {
                    Channel = channel,
                    SequenceNumber = sequenceNumber,
                    Timestamp = timestamp,
                    Payload = payload
                };
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
