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

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        private const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_MINIMIZE = 6;
        private const int SW_MAXIMIZE = 3;
        private const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private const uint WM_NCHITTEST = 0x0084;
        private const uint WM_NCLBUTTONDOWN = 0x00A1;
        private const uint WM_NCLBUTTONUP = 0x00A2;
        private const uint WM_SYSCOMMAND = 0x0112;

        private const IntPtr SC_MINIMIZE = (IntPtr)0xF020;
        private const IntPtr SC_MAXIMIZE = (IntPtr)0xF030;
        private const IntPtr SC_RESTORE = (IntPtr)0xF120;
        private const IntPtr SC_CLOSE = (IntPtr)0xF060;

        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTSYSMENU = 3;
        private const int HTMINBUTTON = 8;
        private const int HTMAXBUTTON = 9;
        private const int HTCLOSE = 20;

        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_MBUTTONDOWN = 0x0207;
        private const uint WM_MBUTTONUP = 0x0208;
        private const uint MK_LBUTTON = 0x0001;
        private const uint MK_RBUTTON = 0x0002;
        private const uint MK_MBUTTON = 0x0010;

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

        private static DateTime _lastDesktopCheck = DateTime.MinValue;

        /// <summary>
        /// Garante que a thread de injeção (mesmo de background/pool) está associada ao desktop interativo ativo do utilizador.
        /// </summary>
        private static void EnsureInputDesktop()
        {
            if ((DateTime.UtcNow - _lastDesktopCheck).TotalSeconds < 1.0)
                return;
            _lastDesktopCheck = DateTime.UtcNow;

            try
            {
                IntPtr hDesktop = OpenInputDesktop(0, false, DESKTOP_ALL_ACCESS);
                if (hDesktop != IntPtr.Zero)
                {
                    IntPtr curDesk = GetThreadDesktop(GetCurrentThreadId());
                    if (curDesk != hDesktop)
                    {
                        SetThreadDesktop(hDesktop);
                    }
                    CloseDesktop(hDesktop);
                }
            }
            catch { }
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
                    // Canal 1: SendInput atómico:
                    // inputs[0]: Posiciona o cursor nas coordenadas exatas
                    // inputs[1]: Dispara o clique sem flag MOVE para não gerar cancelamento de clique por arrasto em botões de título (minimizar/maximizar)
                    uint clickDwFlags = clickFlags | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

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
                        // Canal 2: mouse_event fallback caso SendInput seja bloqueado por privilégios/UIPI
                        int err = Marshal.GetLastWin32Error();
                        AppLogger.LogWarning("RemoteSession", $"[HOST FALLBACK] SendInput retornou 0 para {type} em ({targetX}, {targetY}) [Win32={err}]. Usando mouse_event...");
                        try
                        {
                            mouse_event(clickFlags, 0, 0, 0, UIntPtr.Zero);
                        }
                        catch { }
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

        public static void MinimizeActiveWindow()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    IntPtr root = GetAncestor(fg, GA_ROOT);
                    IntPtr target = root != IntPtr.Zero ? root : fg;
                    PostMessage(target, WM_SYSCOMMAND, SC_MINIMIZE, IntPtr.Zero);
                    ShowWindow(target, SW_MINIMIZE);
                    AppLogger.LogInfo("InputInjector", $"[WINDOW ACTION] Minimizar disparado para a janela ativa ({target}).");
                }
            }
            catch { }
        }

        public static void MaximizeOrRestoreActiveWindow()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    IntPtr root = GetAncestor(fg, GA_ROOT);
                    IntPtr target = root != IntPtr.Zero ? root : fg;
                    bool zoomed = IsZoomed(target);
                    IntPtr cmd = zoomed ? SC_RESTORE : SC_MAXIMIZE;
                    PostMessage(target, WM_SYSCOMMAND, cmd, IntPtr.Zero);
                    ShowWindow(target, zoomed ? SW_RESTORE : SW_MAXIMIZE);
                    AppLogger.LogInfo("InputInjector", $"[WINDOW ACTION] Maximizar/Restaurar ({cmd}) disparado para a janela ativa ({target}).");
                }
            }
            catch { }
        }

        public static void CloseActiveWindow()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    IntPtr root = GetAncestor(fg, GA_ROOT);
                    IntPtr target = root != IntPtr.Zero ? root : fg;
                    PostMessage(target, WM_SYSCOMMAND, SC_CLOSE, IntPtr.Zero);
                    AppLogger.LogInfo("InputInjector", $"[WINDOW ACTION] Fechar Janela disparado para a janela ativa ({target}).");
                }
            }
            catch { }
        }
    }
}
