using System;
using System.Runtime.InteropServices;
using RotinaRemote.Core.Logging;

namespace RotinaRemote.Input
{
    public enum MouseEventType
    {
        Move,
        LeftDown,
        LeftUp,
        RightDown,
        RightUp,
        MiddleDown,
        MiddleUp,
        WheelHorizontal,
        WheelVertical
    }

    public enum KeyEventType
    {
        KeyDown,
        KeyUp
    }

    public static class InputInjector
    {
        #region Win32 API Structs & Imports
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static int _lastClickX = -1;
        private static int _lastClickY = -1;
        private static DateTime _lastClickTime = DateTime.MinValue;
        private static bool _isLeftButtonDown = false;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool BlockInput(bool fBlockIt);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;
        #endregion

        public static void InjectMouse(
            MouseEventType type,
            double normalizedX,
            double normalizedY,
            int wheelDelta = 0,
            int screenX = 0,
            int screenY = 0,
            int screenWidth = 0,
            int screenHeight = 0)
        {
            try
            {
                normalizedX = Math.Clamp(normalizedX, 0.0, 1.0);
                normalizedY = Math.Clamp(normalizedY, 0.0, 1.0);

                int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
                int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
                int vWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
                int vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

                if (vWidth <= 0 || vHeight <= 0)
                {
                    vLeft = 0;
                    vTop = 0;
                    vWidth = GetSystemMetrics(SM_CXSCREEN);
                    vHeight = GetSystemMetrics(SM_CYSCREEN);
                }

                if (vWidth <= 0) vWidth = screenWidth > 0 ? screenWidth : 1920;
                if (vHeight <= 0) vHeight = screenHeight > 0 ? screenHeight : 1080;

                int targetX;
                int targetY;

                if (screenWidth > 0 && screenHeight > 0)
                {
                    targetX = screenX + (int)Math.Round(normalizedX * (screenWidth - 1));
                    targetY = screenY + (int)Math.Round(normalizedY * (screenHeight - 1));
                }
                else
                {
                    targetX = vLeft + (int)Math.Round(normalizedX * (vWidth - 1));
                    targetY = vTop + (int)Math.Round(normalizedY * (vHeight - 1));
                }

                var now = DateTime.UtcNow;

                // Estabilização rigorosa de duplo-clique:
                // Se um novo clique ocorrer dentro de 550ms a menos de 15px do anterior,
                // fixa a coordenada exatamente idêntica para o Windows gerar WM_LBUTTONDBLCLK com 100% de fiabilidade.
                if (type == MouseEventType.LeftDown)
                {
                    _isLeftButtonDown = true;
                    if ((now - _lastClickTime).TotalMilliseconds <= 550 &&
                        _lastClickX >= 0 &&
                        Math.Abs(targetX - _lastClickX) <= 15 &&
                        Math.Abs(targetY - _lastClickY) <= 15)
                    {
                        targetX = _lastClickX;
                        targetY = _lastClickY;
                    }
                    else
                    {
                        _lastClickX = targetX;
                        _lastClickY = targetY;
                    }
                    _lastClickTime = now;
                }
                else if (type == MouseEventType.LeftUp)
                {
                    _isLeftButtonDown = false;
                    if ((now - _lastClickTime).TotalMilliseconds <= 550 && _lastClickX >= 0 &&
                        Math.Abs(targetX - _lastClickX) <= 15 && Math.Abs(targetY - _lastClickY) <= 15)
                    {
                        targetX = _lastClickX;
                        targetY = _lastClickY;
                    }
                }
                else if (type == MouseEventType.Move && _isLeftButtonDown && _lastClickX >= 0)
                {
                    // Se o botão está premido mas o movimento é inferior a 6px (micro-movimento acidental da mão durante um clique),
                    // não move o cursor para evitar converter o clique num arrasto (drag-and-drop) indesejado.
                    if (Math.Abs(targetX - _lastClickX) < 6 && Math.Abs(targetY - _lastClickY) < 6)
                    {
                        return;
                    }
                }

                // Normalização absoluta (0 a 65535) para o desktop virtual do Windows
                int absX = (int)Math.Round(((double)(targetX - vLeft) * 65535.0) / Math.Max(1, vWidth - 1));
                int absY = (int)Math.Round(((double)(targetY - vTop) * 65535.0) / Math.Max(1, vHeight - 1));
                absX = Math.Clamp(absX, 0, 65535);
                absY = Math.Clamp(absY, 0, 65535);

                // 1. Posiciona sempre o cursor do ecrã de forma síncrona
                SetCursorPos(targetX, targetY);

                if (type == MouseEventType.Move)
                {
                    mouse_event(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, (uint)absX, (uint)absY, 0, UIntPtr.Zero);
                    return;
                }

                // 2. Ações de clique / scroll
                uint clickFlags = 0;
                switch (type)
                {
                    case MouseEventType.LeftDown:
                        clickFlags = MOUSEEVENTF_LEFTDOWN;
                        break;
                    case MouseEventType.LeftUp:
                        clickFlags = MOUSEEVENTF_LEFTUP;
                        break;
                    case MouseEventType.RightDown:
                        clickFlags = MOUSEEVENTF_RIGHTDOWN;
                        break;
                    case MouseEventType.RightUp:
                        clickFlags = MOUSEEVENTF_RIGHTUP;
                        break;
                    case MouseEventType.MiddleDown:
                        clickFlags = MOUSEEVENTF_MIDDLEDOWN;
                        break;
                    case MouseEventType.MiddleUp:
                        clickFlags = MOUSEEVENTF_MIDDLEUP;
                        break;
                    case MouseEventType.WheelVertical:
                        clickFlags = MOUSEEVENTF_WHEEL;
                        break;
                }

                if (clickFlags != 0)
                {
                    if (clickFlags == MOUSEEVENTF_WHEEL)
                    {
                        var wheelInput = new INPUT
                        {
                            type = INPUT_MOUSE,
                            U = new InputUnion
                            {
                                mi = new MOUSEINPUT
                                {
                                    dx = 0,
                                    dy = 0,
                                    mouseData = (uint)wheelDelta,
                                    dwFlags = MOUSEEVENTF_WHEEL,
                                    time = 0,
                                    dwExtraInfo = IntPtr.Zero
                                }
                            }
                        };
                        uint sent = SendInput(1, new[] { wheelInput }, Marshal.SizeOf(typeof(INPUT)));
                        if (sent == 0)
                        {
                            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)wheelDelta, UIntPtr.Zero);
                        }
                    }
                    else
                    {
                        // IMPORTANTE: NÃO chamar SetForegroundWindow!
                        // O Windows ativa nativamente a janela sob o cursor ao receber cliques.
                        // Chamar SetForegroundWindow manualmente provocava reativações e cancelava
                        // tanto o evento de clique como os menus de contexto (Right Click) e o duplo-clique.

                        // 1. Garante que o cursor está exatamente no ponto alvo no driver de hardware (Sandbox / Físico / VM)
                        mouse_event(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, (uint)absX, (uint)absY, 0, UIntPtr.Zero);

                        // 2. Dispara o evento de botão de forma estacionária no ponto atual do cursor
                        mouse_event(clickFlags, 0, 0, (uint)wheelDelta, UIntPtr.Zero);

                        // 3. Dispara também via SendInput estacionário para compatibilidade total com todas as janelas Win32/WPF/UWP
                        try
                        {
                            var clickInput = new INPUT
                            {
                                type = INPUT_MOUSE,
                                U = new InputUnion
                                {
                                    mi = new MOUSEINPUT
                                    {
                                        dx = 0,
                                        dy = 0,
                                        mouseData = (uint)wheelDelta,
                                        dwFlags = clickFlags,
                                        time = 0,
                                        dwExtraInfo = IntPtr.Zero
                                    }
                                }
                            };
                            SendInput(1, new[] { clickInput }, Marshal.SizeOf(typeof(INPUT)));
                        }
                        catch { }

                        AppLogger.LogInfo("InputInjector", $"Injetado evento de rato: {type} em ({targetX}, {targetY}) [abs: {absX}, {absY}]");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("InputInjector", "Erro ao injetar evento de rato", ex);
            }
        }

        public static void InjectKeyboard(KeyEventType type, ushort virtualKeyCode)
        {
            try
            {
                uint dwFlags = 0;
                if (type == KeyEventType.KeyUp)
                {
                    dwFlags |= KEYEVENTF_KEYUP;
                }

                var input = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    U = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = virtualKeyCode,
                            wScan = 0,
                            dwFlags = dwFlags,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };

                uint sent = SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
                if (sent == 0)
                {
                    keybd_event((byte)virtualKeyCode, 0, dwFlags, UIntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("InputInjector", "Erro ao injetar evento de teclado", ex);
            }
        }

        public static bool SetBlockLocalInput(bool block)
        {
            try
            {
                return BlockInput(block);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("InputInjector", $"Erro ao alterar BlockInput para {block}", ex);
                return false;
            }
        }
    }
}
