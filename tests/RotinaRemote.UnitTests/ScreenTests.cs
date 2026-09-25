using System;
using System.Runtime.InteropServices;
using RotinaRemote.Screen;
using Xunit;

namespace RotinaRemote.UnitTests
{
    public class ScreenTests
    {
        private readonly Xunit.Abstractions.ITestOutputHelper _output;

        public ScreenTests(Xunit.Abstractions.ITestOutputHelper output)
        {
            _output = output;
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public short dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [Fact]
        public void ScreenCapturer_CaptureNextFrame_ShouldReturnValidFrameData()
        {
            using var capturer = new ScreenCapturer();
            var frame = capturer.CaptureNextFrame(50L);

            int smCx = GetSystemMetrics(0);
            int smCy = GetSystemMetrics(1);
            int vCx = GetSystemMetrics(78);
            int vCy = GetSystemMetrics(79);

            IntPtr hdc = GetDC(IntPtr.Zero);
            int horzRes = GetDeviceCaps(hdc, 8); // HORZRES
            int vertRes = GetDeviceCaps(hdc, 10); // VERTRES
            int dHorzRes = GetDeviceCaps(hdc, 118); // DESKTOPHORZRES
            int dVertRes = GetDeviceCaps(hdc, 117); // DESKTOPVERTRES
            ReleaseDC(IntPtr.Zero, hdc);

            _output.WriteLine($"Screen.PrimaryScreen.Bounds: {System.Windows.Forms.Screen.PrimaryScreen?.Bounds}");
            _output.WriteLine($"ScreenCapturer CurrentBounds: {capturer.CurrentBounds}");
            _output.WriteLine($"SM_CXSCREEN: {smCx}, SM_CYSCREEN: {smCy}");
            _output.WriteLine($"SM_CXVIRTUALSCREEN: {vCx}, SM_CYVIRTUALSCREEN: {vCy}");
            _output.WriteLine($"HORZRES: {horzRes}, VERTRES: {vertRes}");
            _output.WriteLine($"DESKTOPHORZRES: {dHorzRes}, DESKTOPVERTRES: {dVertRes}");
            _output.WriteLine($"Frame: {frame?.Width}x{frame?.Height}");

            DEVMODE dm = default;
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (EnumDisplaySettings(null, -1, ref dm))
            {
                _output.WriteLine($"EnumDisplaySettings: {dm.dmPelsWidth}x{dm.dmPelsHeight} at ({dm.dmPositionX},{dm.dmPositionY})");
            }

            Assert.NotNull(frame);
            Assert.True(frame!.Width > 0, "Largura do frame deve ser superior a 0.");
            Assert.True(frame.Height > 0, "Altura do frame deve ser superior a 0.");
            Assert.NotNull(frame.CompressedData);
            Assert.True(frame.CompressedData.Length > 100, "Tamanho dos dados comprimidos do JPEG deve ser superior a 100 bytes.");
        }
    }
}
