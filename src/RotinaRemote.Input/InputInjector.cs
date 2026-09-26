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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDesktop(uint dwThreadId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

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
                    IntPtr curDesk = GetThreadDesktop(GetCurrentThreadId());
                    if (curDesk != hDesktop)
                    {
                        bool setOk = SetThreadDesktop(hDesktop);
                        if (!setOk)
                        {
                            int err = Marshal.GetLastWin32Error();
                            AppLogger.LogDebug("RemoteSession", $"[HOST DESKTOP] SetThreadDesktop falhou com Win32 Error={err}.");
                        }
                    }
                    CloseDesktop(hDesktop);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogDebug("RemoteSession", $"[HOST DESKTOP] Erro ao associar input desktop: {ex.Message}");
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

                // Cálculo de coordenadas absolutas normalizadas (0 a 65535) exigidas pelo subsistema HID e SendInput
                int absX = (int)Math.Round(((double)(targetX - vLeft) * 65535.0) / Math.Max(1, vWidth - 1));
                int absY = (int)Math.Round(((double)(targetY - vTop) * 65535.0) / Math.Max(1, vHeight - 1));
                absX = Math.Clamp(absX, 0, 65535);
                absY = Math.Clamp(absY, 0, 65535);

                var now = DateTime.UtcNow;

                // Estabilização para duplo-clique no mesmo pixel
                if (type == MouseEventType.LeftDown)
                {
                    _isLeftButtonDown = true;
                    if ((now - _lastClickTime).TotalMilliseconds <= 550 &&
                        _lastClickX >= 0 &&
                        Math.Abs(targetX - _lastClickX) <= 5 &&
                        Math.Abs(targetY - _lastClickY) <= 5)
                    {
                        targetX = _lastClickX;
                        targetY = _lastClickY;
                        absX = (int)Math.Round(((double)(targetX - vLeft) * 65535.0) / Math.Max(1, vWidth - 1));
                        absY = (int)Math.Round(((double)(targetY - vTop) * 65535.0) / Math.Max(1, vHeight - 1));
                        absX = Math.Clamp(absX, 0, 65535);
                        absY = Math.Clamp(absY, 0, 65535);
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
                        Math.Abs(targetX - _lastClickX) <= 5 && Math.Abs(targetY - _lastClickY) <= 5)
                    {
                        targetX = _lastClickX;
                        targetY = _lastClickY;
                        absX = (int)Math.Round(((double)(targetX - vLeft) * 65535.0) / Math.Max(1, vWidth - 1));
                        absY = (int)Math.Round(((double)(targetY - vTop) * 65535.0) / Math.Max(1, vHeight - 1));
                        absX = Math.Clamp(absX, 0, 65535);
                        absY = Math.Clamp(absY, 0, 65535);
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
                        Math.Abs(targetX - _lastRightClickX) <= 5 &&
                        Math.Abs(targetY - _lastRightClickY) <= 5)
                    {
                        targetX = _lastRightClickX;
                        targetY = _lastRightClickY;
                        absX = (int)Math.Round(((double)(targetX - vLeft) * 65535.0) / Math.Max(1, vWidth - 1));
                        absY = (int)Math.Round(((double)(targetY - vTop) * 65535.0) / Math.Max(1, vHeight - 1));
                        absX = Math.Clamp(absX, 0, 65535);
                        absY = Math.Clamp(absY, 0, 65535);
                    }
                }
                else if (type == MouseEventType.Move && _isLeftButtonDown && _lastClickX >= 0)
                {
                    if (Math.Abs(targetX - _lastClickX) < 3 && Math.Abs(targetY - _lastClickY) < 3)
                    {
                        return;
                    }
                }

                // 1. Move o ponteiro visual do Windows
                SetCursorPos(targetX, targetY);

                // Tratamento específico de movimento
                if (type == MouseEventType.Move)
                {
                    uint moveFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
                    var moveInput = new INPUT
                    {
                        type = INPUT_MOUSE,
                        U = new InputUnion
                        {
                            mi = new MOUSEINPUT
                            {
                                dx = absX,
                                dy = absY,
                                mouseData = 0,
                                dwFlags = moveFlags,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    };
                    uint mSent = SendInput(1, new[] { moveInput }, Marshal.SizeOf(typeof(INPUT)));
                    if (mSent == 0)
                    {
                        mouse_event(moveFlags, (uint)absX, (uint)absY, 0, UIntPtr.Zero);
                    }
                    return;
                }

                // Tratamento de Scroll
                if (type == MouseEventType.WheelVertical)
                {
                    uint wheelFlags = MOUSEEVENTF_WHEEL | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;
                    var wheelInput = new INPUT
                    {
                        type = INPUT_MOUSE,
                        U = new InputUnion
                        {
                            mi = new MOUSEINPUT
                            {
                                dx = absX,
                                dy = absY,
                                mouseData = (uint)wheelDelta,
                                dwFlags = wheelFlags,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    };
                    uint wSent = SendInput(1, new[] { wheelInput }, Marshal.SizeOf(typeof(INPUT)));
                    if (wSent == 0)
                    {
                        mouse_event(wheelFlags, (uint)absX, (uint)absY, (uint)wheelDelta, UIntPtr.Zero);
                    }
                    AppLogger.LogInfo("RemoteSession", $"[HOST WHEEL] Roda vertical disparada com delta={wheelDelta} em ({targetX}, {targetY}).");
                    return;
                }

                // 2. Ações de clique (LeftDown, LeftUp, RightDown, RightUp, MiddleDown, MiddleUp)
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
                }

                if (clickFlags != 0)
                {
                    uint clickDwFlags = clickFlags | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

                    // Despacha pacote atómico com 2 eventos em sequência:
                    // 1. Move para a coordenada exata absX/absY (força hover, hit-testing e ativação da janela/controlo)
                    // 2. Dispara o evento de clique do botão no ponto exato
                    var inputs = new INPUT[2];
                    inputs[0] = new INPUT
                    {
                        type = INPUT_MOUSE,
                        U = new InputUnion
                        {
                            mi = new MOUSEINPUT
                            {
                                dx = absX,
                                dy = absY,
                                mouseData = 0,
                                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    };
                    inputs[1] = new INPUT
                    {
                        type = INPUT_MOUSE,
                        U = new InputUnion
                        {
                            mi = new MOUSEINPUT
                            {
                                dx = absX,
                                dy = absY,
                                mouseData = 0,
                                dwFlags = clickDwFlags,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    };

                    uint sent = SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
                    if (sent >= 1)
                    {
                        AppLogger.LogInfo("RemoteSession", $"[HOST SUCCESS] SendInput disparado com SUCESSO para {type} em ({targetX}, {targetY}) [abs: {absX}, {absY}].");
                    }
                    else
                    {
                        int err = Marshal.GetLastWin32Error();
                        AppLogger.LogError("RemoteSession", $"[HOST ERROR] SendInput retornou 0 para {type} em ({targetX}, {targetY}) [Win32={err}]. A disparar fallback mouse_event...");
                        mouse_event(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, (uint)absX, (uint)absY, 0, UIntPtr.Zero);
                        mouse_event(clickDwFlags, (uint)absX, (uint)absY, 0, UIntPtr.Zero);
                        AppLogger.LogInfo("RemoteSession", $"[HOST FALLBACK] Fallback mouse_event executado para {type} em ({targetX}, {targetY}).");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("RemoteSession", $"[HOST EXCEPTION] Erro ao injetar evento de rato {type}", ex);
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
