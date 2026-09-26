using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using RotinaRemote.Core.Logging;

namespace RotinaRemote.Screen
{
    public class DisplayModeInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int RefreshRate { get; set; }
        public int BitsPerPixel { get; set; }

        public override string ToString() => $"{Width}x{Height} @ {RefreshRate}Hz ({BitsPerPixel}bpp)";
    }

    public static class DisplayResolutionManager
    {
        #region Win32 API Imports & Structs

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

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int ENUM_REGISTRY_SETTINGS = -2;

        private const int DM_PELSWIDTH = 0x00080000;
        private const int DM_PELSHEIGHT = 0x00100000;
        private const int DM_BITSPERPEL = 0x00040000;
        private const int DM_DISPLAYFREQUENCY = 0x00400000;

        private const int CDS_UPDATEREGISTRY = 0x01;
        private const int CDS_TEST = 0x02;
        private const int CDS_FULLSCREEN = 0x04;

        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const int DISP_CHANGE_RESTART = 1;
        private const int DISP_CHANGE_FAILED = -1;
        private const int DISP_CHANGE_BADMODE = -2;
        private const int DISP_CHANGE_NOTUPDATED = -3;
        private const int DISP_CHANGE_BADFLAGS = -4;
        private const int DISP_CHANGE_BADPARAM = -5;

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int ChangeDisplaySettings(ref DEVMODE lpDevMode, int dwflags);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern int ChangeDisplaySettings(IntPtr lpDevMode, int dwflags);

        #endregion

        private static readonly object _syncLock = new object();
        private static bool _isResolutionOverridden = false;
        private static Size _originalResolution = Size.Empty;

        /// <summary>
        /// Obtém a resolução física atual do ecrã primário.
        /// </summary>
        public static Size GetCurrentResolution()
        {
            var dm = CreateDevMode();
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) != 0)
            {
                return new Size(dm.dmPelsWidth, dm.dmPelsHeight);
            }
            return new Size(
                System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 1920,
                System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Height ?? 1080);
        }

        /// <summary>
        /// Lista os modos de resolução suportados pelo monitor.
        /// </summary>
        public static List<DisplayModeInfo> GetSupportedDisplayModes()
        {
            var list = new List<DisplayModeInfo>();
            var seen = new HashSet<string>();
            var dm = CreateDevMode();
            int modeNum = 0;

            while (EnumDisplaySettings(null, modeNum, ref dm) != 0)
            {
                string key = $"{dm.dmPelsWidth}x{dm.dmPelsHeight}@{dm.dmBitsPerPel}";
                if (dm.dmBitsPerPel >= 16 && !seen.Contains(key))
                {
                    seen.Add(key);
                    list.Add(new DisplayModeInfo
                    {
                        Width = dm.dmPelsWidth,
                        Height = dm.dmPelsHeight,
                        RefreshRate = dm.dmDisplayFrequency,
                        BitsPerPixel = dm.dmBitsPerPel
                    });
                }
                modeNum++;
            }
            return list;
        }

        /// <summary>
        /// Altera dinamicamente a resolução do ecrã do host para coincidir com a resolução do cliente.
        /// Retorna true se a resolução foi alterada com sucesso, ou false se o monitor não suporta o modo.
        /// </summary>
        public static bool TryAdjustHostResolution(int targetWidth, int targetHeight, out string resultMessage)
        {
            lock (_syncLock)
            {
                resultMessage = string.Empty;
                if (targetWidth <= 320 || targetHeight <= 240)
                {
                    resultMessage = $"Resolução alvo inválida ({targetWidth}x{targetHeight}).";
                    return false;
                }

                var current = GetCurrentResolution();
                if (current.Width == targetWidth && current.Height == targetHeight)
                {
                    resultMessage = $"O ecrã do anfitrião já se encontra na resolução do cliente ({targetWidth}x{targetHeight}).";
                    AppLogger.LogInfo("DisplayResolution", resultMessage);
                    return true;
                }

                if (!_isResolutionOverridden)
                {
                    _originalResolution = current;
                }

                // Procura o melhor modo compatível
                var dm = CreateDevMode();
                bool foundMode = false;
                int bestModeNum = 0;
                int modeNum = 0;

                while (EnumDisplaySettings(null, modeNum, ref dm) != 0)
                {
                    if (dm.dmPelsWidth == targetWidth && dm.dmPelsHeight == targetHeight && dm.dmBitsPerPel >= 32)
                    {
                        bestModeNum = modeNum;
                        foundMode = true;
                        break;
                    }
                    else if (dm.dmPelsWidth == targetWidth && dm.dmPelsHeight == targetHeight && dm.dmBitsPerPel >= 16)
                    {
                        bestModeNum = modeNum;
                        foundMode = true;
                    }
                    modeNum++;
                }

                if (!foundMode)
                {
                    resultMessage = $"O monitor do anfitrião não suporta o modo nativo {targetWidth}x{targetHeight}. Será aplicado escalonamento de vídeo automático.";
                    AppLogger.LogWarning("DisplayResolution", resultMessage);
                    return false;
                }

                // Carrega os parâmetros exatos do modo
                EnumDisplaySettings(null, bestModeNum, ref dm);
                dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;

                // Testa previamente se o Windows aceita o modo
                int testResult = ChangeDisplaySettings(ref dm, CDS_TEST);
                if (testResult != DISP_CHANGE_SUCCESSFUL)
                {
                    resultMessage = $"Teste de modo de vídeo {targetWidth}x{targetHeight} falhou (Código Win32: {testResult}).";
                    AppLogger.LogWarning("DisplayResolution", resultMessage);
                    return false;
                }

                // Aplica dinamicamente sem reiniciar
                int changeResult = ChangeDisplaySettings(ref dm, 0);
                if (changeResult == DISP_CHANGE_SUCCESSFUL)
                {
                    _isResolutionOverridden = true;
                    resultMessage = $"Resolução do anfitrião ajustada com SUCESSO para {targetWidth}x{targetHeight} (Resolução do cliente).";
                    AppLogger.LogInfo("DisplayResolution", resultMessage);
                    return true;
                }
                else
                {
                    resultMessage = $"Falha ao aplicar resolução {targetWidth}x{targetHeight} (Código: {changeResult}).";
                    AppLogger.LogError("DisplayResolution", resultMessage);
                    return false;
                }
            }
        }

        /// <summary>
        /// Restaura a resolução original do monitor do anfitrião quando a sessão termina.
        /// </summary>
        public static void RestoreOriginalResolution()
        {
            lock (_syncLock)
            {
                if (!_isResolutionOverridden) return;

                try
                {
                    int res = ChangeDisplaySettings(IntPtr.Zero, 0);
                    if (res == DISP_CHANGE_SUCCESSFUL)
                    {
                        AppLogger.LogInfo("DisplayResolution", $"Resolução original do anfitrião restaurada com sucesso ({_originalResolution.Width}x{_originalResolution.Height}).");
                    }
                    else
                    {
                        AppLogger.LogWarning("DisplayResolution", $"Restauração de resolução padrão retornou código {res}.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("DisplayResolution", "Erro ao restaurar resolução de ecrã original", ex);
                }
                finally
                {
                    _isResolutionOverridden = false;
                    _originalResolution = Size.Empty;
                }
            }
        }

        private static DEVMODE CreateDevMode()
        {
            var dm = new DEVMODE();
            dm.dmDeviceName = new string('\0', 32);
            dm.dmFormName = new string('\0', 32);
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            return dm;
        }
    }
}
