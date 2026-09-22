using System;
using RotinaRemote.Screen;
using Xunit;

namespace RotinaRemote.UnitTests
{
    public class ScreenTests
    {
        [Fact]
        public void ScreenCapturer_CaptureNextFrame_ShouldReturnValidFrameData()
        {
            using var capturer = new ScreenCapturer();
            var frame = capturer.CaptureNextFrame(50L);

            Assert.NotNull(frame);
            Assert.True(frame!.Width > 0, "Largura do frame deve ser superior a 0.");
            Assert.True(frame.Height > 0, "Altura do frame deve ser superior a 0.");
            Assert.NotNull(frame.CompressedData);
            Assert.True(frame.CompressedData.Length > 100, "Tamanho dos dados comprimidos do JPEG deve ser superior a 100 bytes.");
        }
    }
}
