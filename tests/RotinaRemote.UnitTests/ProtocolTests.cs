using System;
using System.Text;
using System.Threading.Tasks;
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

        [Fact]
        public void Diagnostic_ScreenMetrics_Test()
        {
            int cx = sizeof(RotinaRemote.Input.MouseEventType);
            Assert.True(cx > 0);
        }

        [Fact]
        public async Task GeoLocationService_ShouldReturnValidLocation()
        {
            var geo = await RotinaRemote.Core.Services.GeoLocationService.GetGeoLocationAsync("123456789");
            Assert.NotNull(geo);
            Assert.False(string.IsNullOrWhiteSpace(geo.City));
            Assert.False(string.IsNullOrWhiteSpace(geo.Country));
        }

        [Fact]
        public void HandshakeRequestPayload_Serialization_ShouldIncludeGeoFields()
        {
            var payload = new HandshakeRequestPayload
            {
                ClientDeviceId = "729 710 658",
                ClientName = "PC-TEST",
                ClientIp = "176.78.199.35",
                City = "Lisboa",
                Country = "Portugal",
                Location = "Lisboa, Portugal"
            };

            var bytes = MessageSerializer.SerializeJson(payload);
            Assert.NotNull(bytes);

            var deserialized = MessageSerializer.DeserializeJson<HandshakeRequestPayload>(bytes);
            Assert.NotNull(deserialized);
            Assert.Equal("729 710 658", deserialized!.ClientDeviceId);
            Assert.Equal("176.78.199.35", deserialized.ClientIp);
            Assert.Equal("Lisboa", deserialized.City);
            Assert.Equal("Portugal", deserialized.Country);
        }
    }
}
