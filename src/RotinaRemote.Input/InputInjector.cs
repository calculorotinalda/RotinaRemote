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
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        private const uint KEYEVENTF_KEYUP = 0x0002;

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

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        #endregion

        public static void InjectMouse(MouseEventType type, double normalizedX, double normalizedY, int wheelDelta = 0)
        {
            try
            {
                int screenWidth = GetSystemMetrics(SM_CXSCREEN);
                int screenHeight = GetSystemMetrics(SM_CYSCREEN);

                int targetX = (int)(normalizedX * screenWidth);
                int targetY = (int)(normalizedY * screenHeight);

                // 1. Position cursor to exact target pixel coordinates
                SetCursorPos(targetX, targetY);

                if (type == MouseEventType.Move)
                {
                    return;
                }

                // 2. Perform mouse click/wheel actions
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
                    int absX = (int)(normalizedX * 65535.0);
                    int absY = (int)(normalizedY * 65535.0);

                    var input = new INPUT
                    {
                        type = INPUT_MOUSE,
                        U = new InputUnion
                        {
                            mi = new MOUSEINPUT
                            {
                                dx = absX,
                                dy = absY,
                                mouseData = (uint)wheelDelta,
                                dwFlags = clickFlags | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    };

                    uint sent = SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
                    
                    if (sent == 0)
                    {
                        // Fallback to legacy mouse_event API if SendInput is blocked by OS/UIPI.
                        // dx=0, dy=0 because SetCursorPos already positioned the cursor at (targetX, targetY).
                        mouse_event(clickFlags, 0, 0, (uint)wheelDelta, UIntPtr.Zero);
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
                    // Fallback to legacy keybd_event API if SendInput is blocked
                    keybd_event((byte)virtualKeyCode, 0, dwFlags, UIntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("InputInjector", "Erro ao injetar evento de teclado", ex);
            }
        }
    }
}
