using System;
using System.Runtime.InteropServices;
using RotinaRemote.Input;
using Xunit;
using Xunit.Abstractions;

namespace RotinaRemote.UnitTests
{
    public class InputTests
    {
        private readonly ITestOutputHelper _output;

        public InputTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void TestInputInjectionAndStructSizes()
        {
            _output.WriteLine($"IntPtr size: {IntPtr.Size}");
            InputInjector.InjectMouse(MouseEventType.Move, 0.5, 0.5);
            InputInjector.InjectMouse(MouseEventType.LeftDown, 0.5, 0.5);
            InputInjector.InjectMouse(MouseEventType.LeftUp, 0.5, 0.5);
            _output.WriteLine($"LastWin32Error: {Marshal.GetLastWin32Error()}");
        }

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [Fact]
        public void TestSendInputDirect()
        {
            int cbSize = Marshal.SizeOf(typeof(INPUT));
            _output.WriteLine($"cbSize = {cbSize}");

            var inputSingle = new INPUT
            {
                type = 0,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 100,
                        dy = 100,
                        dwFlags = 0x8000 | 0x0001, // ABSOLUTE | MOVE
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            uint res1 = SendInput(1, new[] { inputSingle }, cbSize);
            int err1 = Marshal.GetLastWin32Error();
            _output.WriteLine($"res1 = {res1}, err = {err1}");

            var inputs2 = new INPUT[2];
            inputs2[0] = inputSingle;
            inputs2[1] = new INPUT
            {
                type = 0,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        dwFlags = 0x0002, // LEFTDOWN
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            uint res2 = SendInput(2, inputs2, cbSize);
            int err2 = Marshal.GetLastWin32Error();
            _output.WriteLine($"res2 = {res2}, err = {err2}");

            var inputCombined = new INPUT
            {
                type = 0,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 100,
                        dy = 100,
                        dwFlags = 0x8000 | 0x4000 | 0x0002, // ABSOLUTE | VIRTUALDESK | LEFTDOWN
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            uint res3 = SendInput(1, new[] { inputCombined }, cbSize);
            int err3 = Marshal.GetLastWin32Error();
            _output.WriteLine($"res3 = {res3}, err = {err3}");
        }
    }
}
