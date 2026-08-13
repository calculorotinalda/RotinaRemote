using System.Windows;
using System.Windows.Input;
using RotinaRemote.Client.ViewModels;
using RotinaRemote.Input;
using RotinaRemote.Protocol;

namespace RotinaRemote.Client.Views
{
    public partial class MainWindow : Window
    {
        private Point _lastMousePos;

        public MainWindow()
        {
            InitializeComponent();
        }

        private MainViewModel? ViewModel => DataContext as MainViewModel;

        private bool GetNormalizedCoordinates(Point pos, out double normX, out double normY)
        {
            normX = 0;
            normY = 0;

            if (RemoteScreenImage.ActualWidth <= 0 || RemoteScreenImage.ActualHeight <= 0) return false;
            if (RemoteScreenImage.Source == null) return false;

            double imgWidth = RemoteScreenImage.Source.Width;
            double imgHeight = RemoteScreenImage.Source.Height;

            if (imgWidth <= 0 || imgHeight <= 0) return false;

            double ctrlWidth = RemoteScreenImage.ActualWidth;
            double ctrlHeight = RemoteScreenImage.ActualHeight;

            double scale = System.Math.Min(ctrlWidth / imgWidth, ctrlHeight / imgHeight);
            double dispWidth = imgWidth * scale;
            double dispHeight = imgHeight * scale;

            double offsetX = (ctrlWidth - dispWidth) / 2.0;
            double offsetY = (ctrlHeight - dispHeight) / 2.0;

            double relX = pos.X - offsetX;
            double relY = pos.Y - offsetY;

            normX = relX / dispWidth;
            normY = relY / dispHeight;

            return (normX >= 0.0 && normX <= 1.0 && normY >= 0.0 && normY <= 1.0);
        }

        private void OnRemoteScreenMouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(RemoteScreenImage);
            if (System.Math.Abs(pos.X - _lastMousePos.X) < 1.0 && System.Math.Abs(pos.Y - _lastMousePos.Y) < 1.0)
                return;

            _lastMousePos = pos;

            if (GetNormalizedCoordinates(pos, out double normX, out double normY))
            {
                SendMouseInput(MouseEventType.Move, normX, normY);
            }
        }

        private void OnRemoteScreenMouseDown(object sender, MouseButtonEventArgs e)
        {
            RemoteScreenImage.Focus();
            RemoteScreenImage.CaptureMouse();
            var pos = e.GetPosition(RemoteScreenImage);
            if (GetNormalizedCoordinates(pos, out double normX, out double normY))
            {
                MouseEventType mouseType = e.ChangedButton switch
                {
                    MouseButton.Left => MouseEventType.LeftDown,
                    MouseButton.Right => MouseEventType.RightDown,
                    MouseButton.Middle => MouseEventType.MiddleDown,
                    _ => MouseEventType.LeftDown
                };

                SendMouseInput(mouseType, normX, normY);
                e.Handled = true;
            }
        }

        private void OnRemoteScreenMouseUp(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(RemoteScreenImage);
            if (GetNormalizedCoordinates(pos, out double normX, out double normY))
            {
                MouseEventType mouseType = e.ChangedButton switch
                {
                    MouseButton.Left => MouseEventType.LeftUp,
                    MouseButton.Right => MouseEventType.RightUp,
                    MouseButton.Middle => MouseEventType.MiddleUp,
                    _ => MouseEventType.LeftUp
                };

                SendMouseInput(mouseType, normX, normY);
                e.Handled = true;
            }

            if (RemoteScreenImage.IsMouseCaptured)
            {
                RemoteScreenImage.ReleaseMouseCapture();
            }
        }

        private void OnRemoteScreenMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var pos = e.GetPosition(RemoteScreenImage);
            if (GetNormalizedCoordinates(pos, out double normX, out double normY))
            {
                SendMouseInput(MouseEventType.WheelVertical, normX, normY, e.Delta);
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            var vm = ViewModel;
            if (vm != null && vm.IsConnected && vm.SelectedTabIndex == 1)
            {
                Key realKey = e.Key == Key.System ? e.SystemKey : e.Key;
                int vkey = KeyInterop.VirtualKeyFromKey(realKey);
                if (vkey > 0)
                {
                    SendKeyboardInput(KeyEventType.KeyDown, (ushort)vkey);
                    e.Handled = true;
                }
            }
        }

        private void OnWindowKeyUp(object sender, KeyEventArgs e)
        {
            var vm = ViewModel;
            if (vm != null && vm.IsConnected && vm.SelectedTabIndex == 1)
            {
                Key realKey = e.Key == Key.System ? e.SystemKey : e.Key;
                int vkey = KeyInterop.VirtualKeyFromKey(realKey);
                if (vkey > 0)
                {
                    SendKeyboardInput(KeyEventType.KeyUp, (ushort)vkey);
                    e.Handled = true;
                }
            }
        }

        private void SendMouseInput(MouseEventType mouseType, double normX, double normY, int wheelDelta = 0)
        {
            var vm = ViewModel;
            if (vm != null && vm.IsConnected)
            {
                var payload = new InputPacketPayload
                {
                    Type = ProtocolInputType.Mouse,
                    MouseType = (byte)mouseType,
                    NormX = normX,
                    NormY = normY,
                    WheelDelta = wheelDelta
                };
                vm.SendInputToRemoteHost(payload);
            }
        }

        private void SendKeyboardInput(KeyEventType keyType, ushort vkey)
        {
            var vm = ViewModel;
            if (vm != null && vm.IsConnected)
            {
                var payload = new InputPacketPayload
                {
                    Type = ProtocolInputType.Keyboard,
                    KeyType = (byte)keyType,
                    VirtualKeyCode = vkey
                };
                vm.SendInputToRemoteHost(payload);
            }
        }
    }
}
