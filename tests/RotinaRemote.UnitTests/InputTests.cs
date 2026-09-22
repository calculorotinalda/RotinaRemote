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
            // Call InjectMouse to see if any exception or issue happens
            _output.WriteLine($"IntPtr size: {IntPtr.Size}");
            InputInjector.InjectMouse(MouseEventType.Move, 0.5, 0.5);
            InputInjector.InjectMouse(MouseEventType.LeftDown, 0.5, 0.5);
            InputInjector.InjectMouse(MouseEventType.LeftUp, 0.5, 0.5);
        }
    }
}
