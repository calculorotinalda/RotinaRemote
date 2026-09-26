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

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        private const int DESKTOPHORZRES = 118;
        private const int DESKTOPVERTRES = 117;

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
                    }
                    else
                    {
                        _cachedBounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                    }

                    // Correção automática de DPI: se o Windows virtualizou as dimensões do ecrã (ex: 125% ou 150%),
                    // garante que a captura e o mapeamento de coordenadas operam nas dimensões físicas reais de hardware.
                    IntPtr hdc = GetDC(IntPtr.Zero);
                    if (hdc != IntPtr.Zero)
                    {
                        try
                        {
                            int dHorzRes = GetDeviceCaps(hdc, DESKTOPHORZRES);
                            int dVertRes = GetDeviceCaps(hdc, DESKTOPVERTRES);
                            if (dHorzRes > 0 && dVertRes > 0)
                            {
                                if (_cachedBounds.Width < dHorzRes || _cachedBounds.Height < dVertRes)
                                {
                                    _cachedBounds = new Rectangle(_cachedBounds.X, _cachedBounds.Y, dHorzRes, dVertRes);
                                }
                            }
                        }
                        finally
                        {
                            ReleaseDC(IntPtr.Zero, hdc);
                        }
                    }

                    return _cachedBounds;
                }
                catch
                {
                    if (_cachedBounds.Width > 0) return _cachedBounds;
                    return new Rectangle(0, 0, 1920, 1080);
                }
            }
        }

        private int _targetWidth = 0;
        private int _targetHeight = 0;

        public void SetTargetResolution(int width, int height)
        {
            if (width > 0 && height > 0)
            {
                _targetWidth = width;
                _targetHeight = height;
                AppLogger.LogInfo("ScreenCapturer", $"Resolução alvo de streaming ajustada para {_targetWidth}x{_targetHeight} (Resolução do cliente).");
            }
        }

        public void ClearTargetResolution()
        {
            _targetWidth = 0;
            _targetHeight = 0;
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

                Bitmap outputBitmap = bitmap;
                bool needsDispose = false;

                // Escalonamento adaptativo para a resolução do computador cliente se configurada
                if (_targetWidth > 0 && _targetHeight > 0 && (_targetWidth != bounds.Width || _targetHeight != bounds.Height))
                {
                    var scaledBitmap = new Bitmap(_targetWidth, _targetHeight, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(scaledBitmap))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.DrawImage(bitmap, 0, 0, _targetWidth, _targetHeight);
                    }
                    outputBitmap = scaledBitmap;
                    needsDispose = true;
                }

                try
                {
                    outputBitmap.SetResolution(96f, 96f);

                    // Compress as JPEG with adjustable quality
                    using var ms = new MemoryStream();
                    var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                    if (jpegEncoder != null)
                    {
                        using var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                        outputBitmap.Save(ms, jpegEncoder, encoderParams);
                    }
                    else
                    {
                        outputBitmap.Save(ms, ImageFormat.Jpeg);
                    }

                    _frameCounter++;
                    return new CapturedFrame
                    {
                        MonitorIndex = _selectedMonitorIndex,
                        FrameIndex = _frameCounter,
                        Width = outputBitmap.Width,
                        Height = outputBitmap.Height,
                        CompressedData = ms.ToArray(),
                        IsKeyFrame = true
                    };
                }
                finally
                {
                    if (needsDispose)
                    {
                        outputBitmap.Dispose();
                    }
                }
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
