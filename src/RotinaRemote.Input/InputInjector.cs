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

                if (vWidth <= 0) vWidth = 1920;
                if (vHeight <= 0) vHeight = 1080;

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

                // Calculate absolute coordinates (0 to 65535) required by Windows Sandbox / Hyper-V synthetic mouse
                int absX = (int)Math.Round(((double)(targetX - vLeft) * 65535.0) / Math.Max(1, vWidth - 1));
                int absY = (int)Math.Round(((double)(targetY - vTop) * 65535.0) / Math.Max(1, vHeight - 1));
                absX = Math.Clamp(absX, 0, 65535);
                absY = Math.Clamp(absY, 0, 65535);

                // 1. Move cursor visually
                SetCursorPos(targetX, targetY);

                if (type == MouseEventType.Move)
                {
                    // Dispatch WM_MOUSEMOVE with absolute coordinates to update raw input queue
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
                                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    };

                    uint res = SendInput(1, new[] { moveInput }, Marshal.SizeOf(typeof(INPUT)));
                    if (res == 0)
                    {
                        mouse_event(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, (uint)absX, (uint)absY, 0, UIntPtr.Zero);
                    }
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
                        // To guarantee the click registers at the exact target location across all Windows environments
                        // (including Windows Sandbox, Hyper-V synthetic mouse, RDP, and multimonitor setups):
                        // 1) The click event MUST have MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK along with clickFlags.
                        //    Without MOUSEEVENTF_MOVE, the OS discards dx and dy and clicks at the driver's stale position.
                        // 2) We send a batch: Move input to (absX, absY) followed by the button event at (absX, absY).
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
                                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | clickFlags,
                                    time = 0,
                                    dwExtraInfo = IntPtr.Zero
                                }
                            }
                        };

                        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                        if (sent < inputs.Length)
                        {
                            // Fallback to mouse_event with absolute move + click flags
                            mouse_event(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, (uint)absX, (uint)absY, 0, UIntPtr.Zero);
                            mouse_event(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | clickFlags, (uint)absX, (uint)absY, 0, UIntPtr.Zero);
                        }

                        AppLogger.LogInfo("InputInjector", $"Injetado evento de rato: {type} em ({targetX}, {targetY}) [abs: {absX}, {absY}, SendInput: {sent}]");
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
