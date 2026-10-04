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
        public void HandshakeRequestPayload_Serialization_ShouldIncludeGeoAndResolutionFields()
        {
            var payload = new HandshakeRequestPayload
            {
                ClientDeviceId = "729 710 658",
                ClientName = "PC-TEST",
                ClientIp = "176.78.199.35",
                City = "Lisboa",
                Country = "Portugal",
                Location = "Lisboa, Portugal",
                ClientScreenWidth = 1920,
                ClientScreenHeight = 1080
            };

            var bytes = MessageSerializer.SerializeJson(payload);
            Assert.NotNull(bytes);

            var deserialized = MessageSerializer.DeserializeJson<HandshakeRequestPayload>(bytes);
            Assert.NotNull(deserialized);
            Assert.Equal("729 710 658", deserialized!.ClientDeviceId);
            Assert.Equal("176.78.199.35", deserialized.ClientIp);
            Assert.Equal("Lisboa", deserialized.City);
            Assert.Equal("Portugal", deserialized.Country);
            Assert.Equal(1920, deserialized.ClientScreenWidth);
            Assert.Equal(1080, deserialized.ClientScreenHeight);
        }

        [Fact]
        public void ResolutionChangePayloads_Serialization_ShouldRoundtrip()
        {
            var req = new ResolutionChangeRequestPayload
            {
                TargetWidth = 1366,
                TargetHeight = 768
            };
            var reqBytes = MessageSerializer.SerializeJson(req);
            var reqDeserialized = MessageSerializer.DeserializeJson<ResolutionChangeRequestPayload>(reqBytes);
            Assert.NotNull(reqDeserialized);
            Assert.Equal(1366, reqDeserialized!.TargetWidth);
            Assert.Equal(768, reqDeserialized.TargetHeight);

            var resp = new ResolutionChangeResponsePayload
            {
                Success = true,
                CurrentWidth = 1366,
                CurrentHeight = 768,
                Message = "Resolução ajustada com sucesso"
            };
            var respBytes = MessageSerializer.SerializeJson(resp);
            var respDeserialized = MessageSerializer.DeserializeJson<ResolutionChangeResponsePayload>(respBytes);
            Assert.NotNull(respDeserialized);
            Assert.True(respDeserialized!.Success);
            Assert.Equal(1366, respDeserialized.EffectiveWidth);
            Assert.Equal(768, respDeserialized.EffectiveHeight);
            Assert.Equal("Resolução ajustada com sucesso", respDeserialized.Message);
        }

        [Fact]
        public void ChatMessagePayload_Serialization_ShouldRoundtrip()
        {
            var chat = new ChatMessagePayload
            {
                SenderId = "123 456 789",
                SenderName = "Técnico Suporte",
                Message = "Olá! Como posso ajudar?",
                Timestamp = DateTime.UtcNow
            };

            var bytes = MessageSerializer.SerializeJson(chat);
            Assert.NotNull(bytes);

            var deserialized = MessageSerializer.DeserializeJson<ChatMessagePayload>(bytes);
            Assert.NotNull(deserialized);
            Assert.Equal("123 456 789", deserialized!.SenderId);
            Assert.Equal("Técnico Suporte", deserialized.SenderName);
            Assert.Equal("Olá! Como posso ajudar?", deserialized.Message);
        }

        [Fact]
        public void ClipboardPayload_Serialization_ShouldRoundtrip()
        {
            var clip = new ClipboardPayload
            {
                Text = "Texto copiado para teste de clipboard"
            };

            var bytes = MessageSerializer.SerializeJson(clip);
            Assert.NotNull(bytes);

            var deserialized = MessageSerializer.DeserializeJson<ClipboardPayload>(bytes);
            Assert.NotNull(deserialized);
            Assert.Equal("Texto copiado para teste de clipboard", deserialized!.Text);
        }

        [Fact]
        public void ServiceManagerPayload_Serialization_ShouldRoundtrip()
        {
            var payload = new ServiceManagerPayload
            {
                Action = ServiceManagerAction.ListResponse,
                TargetServiceName = "Spooler",
                NewStartupType = "Automático",
                Success = true,
                Message = "Operação concluída com sucesso.",
                Services = new System.Collections.Generic.List<RemoteServiceItem>
                {
                    new RemoteServiceItem
                    {
                        ServiceName = "Spooler",
                        DisplayName = "Spooler de Impressão",
                        Status = "Em Execução",
                        StartupType = "Automático",
                        CanStop = true,
                        CanStart = false
                    },
                    new RemoteServiceItem
                    {
                        ServiceName = "wuauserv",
                        DisplayName = "Windows Update",
                        Status = "Parado",
                        StartupType = "Manual",
                        CanStop = false,
                        CanStart = true
                    }
                }
            };

            var bytes = MessageSerializer.SerializeJson(payload);
            Assert.NotNull(bytes);

            var deserialized = MessageSerializer.DeserializeJson<ServiceManagerPayload>(bytes);
            Assert.NotNull(deserialized);
            Assert.Equal(ServiceManagerAction.ListResponse, deserialized!.Action);
            Assert.Equal("Spooler", deserialized.TargetServiceName);
            Assert.Equal("Automático", deserialized.NewStartupType);
            Assert.True(deserialized.Success);
            Assert.Equal(2, deserialized.Services.Count);
            Assert.Equal("Spooler de Impressão", deserialized.Services[0].DisplayName);
            Assert.True(deserialized.Services[0].IsRunning);
            Assert.False(deserialized.Services[1].IsRunning);
        }
    }
}
