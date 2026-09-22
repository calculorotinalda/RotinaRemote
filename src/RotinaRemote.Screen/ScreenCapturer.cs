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
        private const uint SRCCOPY = 0x00CC0020;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

        private int _selectedMonitorIndex = 0;
        private uint _frameCounter = 0;

        public static List<ScreenInfo> GetMonitors()
        {
            var list = new List<ScreenInfo>();
            var screens = System.Windows.Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                list.Add(new ScreenInfo
                {
                    Index = i,
                    DeviceName = screen.DeviceName,
                    Bounds = screen.Bounds,
                    IsPrimary = screen.Primary
                });
            }
            return list;
        }

        private Rectangle _cachedBounds = Rectangle.Empty;
        private DateTime _lastBoundsCheck = DateTime.MinValue;

        public void SelectMonitor(int index)
        {
            _selectedMonitorIndex = index;
            _lastBoundsCheck = DateTime.MinValue;
        }

        public Rectangle CurrentBounds
        {
            get
            {
                var now = DateTime.UtcNow;
                if (_cachedBounds.Width > 0 && (now - _lastBoundsCheck).TotalSeconds < 3.0)
                {
                    return _cachedBounds;
                }

                _lastBoundsCheck = now;
                try
                {
                    var screens = System.Windows.Forms.Screen.AllScreens;
                    if (_selectedMonitorIndex >= 0 && _selectedMonitorIndex < screens.Length)
                    {
                        _cachedBounds = screens[_selectedMonitorIndex].Bounds;
                        return _cachedBounds;
                    }
                    _cachedBounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                    return _cachedBounds;
                }
                catch
                {
                    if (_cachedBounds.Width > 0) return _cachedBounds;
                    return new Rectangle(0, 0, 1920, 1080);
                }
            }
        }

        public CapturedFrame? CaptureNextFrame(long quality = 60L)
        {
            try
            {
                var bounds = CurrentBounds;
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                }

                using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                bool captured = false;

                // Method 1: Win32 GDI BitBlt directly from primary screen DC
                IntPtr screenDc = GetDC(IntPtr.Zero);
                if (screenDc != IntPtr.Zero)
                {
                    try
                    {
                        using var g = Graphics.FromImage(bitmap);
                        IntPtr destDc = g.GetHdc();
                        try
                        {
                            captured = BitBlt(destDc, 0, 0, bounds.Width, bounds.Height, screenDc, bounds.X, bounds.Y, SRCCOPY);
                        }
                        finally
                        {
                            g.ReleaseHdc(destDc);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogWarning("ScreenCapturer", $"BitBlt falhou: {ex.Message}");
                    }
                    finally
                    {
                        ReleaseDC(IntPtr.Zero, screenDc);
                    }
                }

                // Method 2: Fallback to System.Drawing CopyFromScreen
                if (!captured)
                {
                    try
                    {
                        using var g = Graphics.FromImage(bitmap);
                        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                        captured = true;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogWarning("ScreenCapturer", $"CopyFromScreen falhou: {ex.Message}");
                    }
                }

                // Method 3: Fallback placeholder frame if screen cannot be read (e.g., UAC, locked session or headless test)
                if (!captured)
                {
                    using var g = Graphics.FromImage(bitmap);
                    g.Clear(Color.FromArgb(20, 20, 25));
                    using var font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold);
                    g.DrawString("RotinaRemote — Ecrã Temporariamente Indisponível", font, Brushes.LightGray, new PointF(40, 40));
                }

                bitmap.SetResolution(96f, 96f);

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
