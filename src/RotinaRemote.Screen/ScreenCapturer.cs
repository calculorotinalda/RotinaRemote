using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RotinaRemote.Core.Logging;

namespace RotinaRemote.Screen
{
    public class ScreenInfo
    {
        public int Index { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public Rectangle Bounds { get; set; }
        public bool IsPrimary { get; set; }
    }

    public class CapturedFrame
    {
        public int MonitorIndex { get; set; }
        public uint FrameIndex { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] CompressedData { get; set; } = Array.Empty<byte>();
        public bool IsKeyFrame { get; set; }
    }

    public class ScreenCapturer : IDisposable
    {
        private int _selectedMonitorIndex = 0;
        private uint _frameCounter = 0;

        public static List<ScreenInfo> GetMonitors()
        {
            var list = new List<ScreenInfo>();
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                list.Add(new ScreenInfo
                {
                    Index = i,
                    DeviceName = screens[i].DeviceName,
                    Bounds = screens[i].Bounds,
                    IsPrimary = screens[i].Primary
                });
            }
            return list;
        }

        public void SelectMonitor(int index)
        {
            _selectedMonitorIndex = index;
        }

        public CapturedFrame? CaptureNextFrame(long quality = 60L)
        {
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                if (_selectedMonitorIndex < 0 || _selectedMonitorIndex >= screens.Length)
                {
                    _selectedMonitorIndex = 0;
                }

                var screen = screens[_selectedMonitorIndex];
                var bounds = screen.Bounds;

                using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                }

                // Compress as JPEG with adjustable quality
                using var ms = new MemoryStream();
                var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                if (jpegEncoder != null)
                {
                    using var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                    bitmap.Save(ms, jpegEncoder, encoderParams);
                }
                else
                {
                    bitmap.Save(ms, ImageFormat.Jpeg);
                }

                _frameCounter++;
                return new CapturedFrame
                {
                    MonitorIndex = _selectedMonitorIndex,
                    FrameIndex = _frameCounter,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    CompressedData = ms.ToArray(),
                    IsKeyFrame = true
                };
            }
            catch (Exception ex)
            {
                AppLogger.LogError("ScreenCapturer", "Erro ao capturar frame do ecrã", ex);
                return null;
            }
        }

        private ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageEncoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        public void Dispose()
        {
            // Cleanup resources
        }
    }
}
