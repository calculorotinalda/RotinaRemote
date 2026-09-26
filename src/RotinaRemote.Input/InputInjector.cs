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
        private static int _lastRightClickX = -1;
        private static int _lastRightClickY = -1;

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetThreadDesktop(IntPtr hDesktop);

        private const uint DESKTOP_ALL_ACCESS = 0x01FF;

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        /// <summary>
        /// Garante que a thread de injeção (mesmo de background/pool) está associada ao desktop interativo ativo do utilizador.
        /// </summary>
        private static void EnsureInputDesktop()
        {
            try
            {
                IntPtr hDesktop = OpenInputDesktop(0, false, DESKTOP_ALL_ACCESS);
                if (hDesktop != IntPtr.Zero)
                {
                    SetThreadDesktop(hDesktop);
                }
            }
            catch
            {
                // Ignora se já estiver associado ou em sessão restrita
            }
        }
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
                EnsureInputDesktop();

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

                // Estabilização rigorosa de clique e duplo-clique:
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
                else if (type == MouseEventType.RightDown)
                {
                    _lastRightClickX = targetX;
                    _lastRightClickY = targetY;
                }
                else if (type == MouseEventType.RightUp)
                {
                    if (_lastRightClickX >= 0 &&
                        Math.Abs(targetX - _lastRightClickX) <= 15 &&
                        Math.Abs(targetY - _lastRightClickY) <= 15)
                    {
                        targetX = _lastRightClickX;
                        targetY = _lastRightClickY;
                    }
                }
                else if (type == MouseEventType.Move && _isLeftButtonDown && _lastClickX >= 0)
                {
                    if (Math.Abs(targetX - _lastClickX) < 6 && Math.Abs(targetY - _lastClickY) < 6)
                    {
                        return;
                    }
                }

                // 1. Posiciona o cursor do Windows no ponto exato em píxeis
                SetCursorPos(targetX, targetY);

                if (type == MouseEventType.Move)
                {
                    mouse_event(MOUSEEVENTF_MOVE, 0, 0, 0, UIntPtr.Zero);
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

                    uint sent = SendInput(1, new[] { clickInput }, Marshal.SizeOf(typeof(INPUT)));
                    if (sent == 0)
                    {
                        // Fallback imediato para mouse_event na posição do cursor
                        mouse_event(clickFlags, 0, 0, (uint)wheelDelta, UIntPtr.Zero);
                    }

                    AppLogger.LogInfo("InputInjector", $"Injetado evento de rato: {type} em ({targetX}, {targetY})");
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
                EnsureInputDesktop();

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
