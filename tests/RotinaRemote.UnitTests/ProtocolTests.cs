using System;
using System.Text;
using RotinaRemote.Protocol;
using Xunit;

namespace RotinaRemote.UnitTests
{
    public class ProtocolTests
    {
        [Fact]
        public void PacketFrame_SerializeAndDeserialize_ShouldMatchOriginal()
        {
            var originalPayload = Encoding.UTF8.GetBytes("Test Payload RotinaRemote");
            var frame = new PacketFrame(ChannelType.Control, 105, originalPayload);

            var serialized = frame.Serialize();
            Assert.NotNull(serialized);
            Assert.True(serialized.Length > 19);

            bool success = PacketFrame.TryDeserialize(serialized, out var deserialized);
            Assert.True(success);
            Assert.NotNull(deserialized);
            Assert.Equal(ChannelType.Control, deserialized!.Channel);
            Assert.Equal(105U, deserialized.SequenceNumber);
            Assert.Equal(originalPayload, deserialized.Payload);
        }

        [Fact]
        public void PacketFrame_InvalidMagicBytes_ShouldFailDeserialization()
        {
            var invalidFrame = new byte[] { 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            bool success = PacketFrame.TryDeserialize(invalidFrame, out var deserialized);
            Assert.False(success);
            Assert.Null(deserialized);
        }
    }
}
