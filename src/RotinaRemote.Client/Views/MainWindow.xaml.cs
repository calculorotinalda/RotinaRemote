using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RotinaRemote.Client.ViewModels;
using RotinaRemote.Input;
using RotinaRemote.Protocol;

namespace RotinaRemote.Client.Views
{
    public partial class MainWindow : Window
    {
        private Point _lastMousePos;
        private bool _isUpdatingPassword = false;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
                _isUpdatingPassword = true;
                TargetPasswordBox.Password = ViewModel.TargetPassword ?? string.Empty;
                UnattendedPasswordBox.Password = ViewModel.UnattendedPassword ?? string.Empty;
                _isUpdatingPassword = false;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isUpdatingPassword || ViewModel == null) return;

            if (e.PropertyName == nameof(MainViewModel.TargetPassword))
            {
                if (TargetPasswordBox.Password != ViewModel.TargetPassword)
                {
                    _isUpdatingPassword = true;
                    TargetPasswordBox.Password = ViewModel.TargetPassword ?? string.Empty;
                    _isUpdatingPassword = false;
                }
            }
            else if (e.PropertyName == nameof(MainViewModel.UnattendedPassword))
            {
                if (UnattendedPasswordBox.Password != ViewModel.UnattendedPassword)
                {
                    _isUpdatingPassword = true;
                    UnattendedPasswordBox.Password = ViewModel.UnattendedPassword ?? string.Empty;
                    _isUpdatingPassword = false;
                }
            }
        }

        private void TargetPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingPassword || ViewModel == null) return;
            _isUpdatingPassword = true;
            ViewModel.TargetPassword = TargetPasswordBox.Password;
            _isUpdatingPassword = false;
        }

        private void UnattendedPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingPassword || ViewModel == null) return;
            _isUpdatingPassword = true;
            ViewModel.UnattendedPassword = UnattendedPasswordBox.Password;
            _isUpdatingPassword = false;
        }

        private MainViewModel? ViewModel => DataContext as MainViewModel;

        private DateTime _lastMouseMoveTime = DateTime.MinValue;

        private bool GetNormalizedCoordinates(Point pos, out double normX, out double normY)
        {
            normX = 0;
            normY = 0;

            if (RemoteScreenImage.ActualWidth <= 0 || RemoteScreenImage.ActualHeight <= 0) return false;
            if (RemoteScreenImage.Source is not System.Windows.Media.Imaging.BitmapSource bmp) return false;

            double imgWidth = bmp.PixelWidth;
            double imgHeight = bmp.PixelHeight;

            if (imgWidth <= 0 || imgHeight <= 0) return false;

            double ctrlWidth = RemoteScreenImage.ActualWidth;
            double ctrlHeight = RemoteScreenImage.ActualHeight;

            double scale = System.Math.Min(ctrlWidth / imgWidth, ctrlHeight / imgHeight);
            double dispWidth = imgWidth * scale;
            double dispHeight = imgHeight * scale;

            if (dispWidth <= 0 || dispHeight <= 0) return false;

            double offsetX = (ctrlWidth - dispWidth) / 2.0;
            double offsetY = (ctrlHeight - dispHeight) / 2.0;

            double relX = pos.X - offsetX;
            double relY = pos.Y - offsetY;

            // Reject clicks outside the rendered remote desktop (black borders)
            if (relX < 0 || relX > dispWidth || relY < 0 || relY > dispHeight) return false;

            normX = System.Math.Clamp(relX / dispWidth, 0.0, 1.0);
            normY = System.Math.Clamp(relY / dispHeight, 0.0, 1.0);

            return true;
        }

        private double _lastLeftNormX = -1;
        private double _lastLeftNormY = -1;
        private DateTime _lastLeftClickTime = DateTime.MinValue;
        private bool _isClientMouseDown = false;

        private void OnRemoteScreenMouseMove(object sender, MouseEventArgs e)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastMouseMoveTime).TotalMilliseconds < 16)
                return;

            var pos = e.GetPosition(RemoteScreenImage);
            if (System.Math.Abs(pos.X - _lastMousePos.X) < 1.0 && System.Math.Abs(pos.Y - _lastMousePos.Y) < 1.0)
                return;

            _lastMousePos = pos;
            _lastMouseMoveTime = now;

            if (GetNormalizedCoordinates(pos, out double normX, out double normY))
            {
                // Se o botão do rato estiver premido (durante um clique ou duplo-clique),
                // só envia movimento se o deslocamento for significativo (arrasto intencional),
                // evitando converter um clique em arrasto (drag) por micro-movimento da mão.
                if (_isClientMouseDown && _lastLeftNormX >= 0)
                {
                    double deltaX = System.Math.Abs(normX - _lastLeftNormX);
                    double deltaY = System.Math.Abs(normY - _lastLeftNormY);
                    if (deltaX < 0.006 && deltaY < 0.006)
                    {
                        return;
                    }
                }

                SendMouseInput(MouseEventType.Move, normX, normY);
            }
        }

        private void OnRemoteScreenMouseDown(object sender, MouseButtonEventArgs e)
        {
            RemoteScreenImage.Focus();
            RemoteScreenImage.CaptureMouse();
            var pos = e.GetPosition(RemoteScreenImage);
            if (!GetNormalizedCoordinates(pos, out double normX, out double normY))
            {
                RotinaRemote.Core.Logging.AppLogger.LogWarning("RemoteSession", $"[CLIENT MOUSE DOWN] Clique {e.ChangedButton} ignorado fora dos limites do ecrã remoto em ({pos.X:F1}, {pos.Y:F1}).");
                return;
            }

            var now = DateTime.UtcNow;

            if (e.ChangedButton == MouseButton.Left)
            {
                _isClientMouseDown = true;

                // Deteção e estabilização de duplo-clique do WPF:
                if (e.ClickCount >= 2 && _lastLeftNormX >= 0 &&
                    System.Math.Abs(normX - _lastLeftNormX) < 0.006 &&
                    System.Math.Abs(normY - _lastLeftNormY) < 0.006)
                {
                    normX = _lastLeftNormX;
                    normY = _lastLeftNormY;
                    RotinaRemote.Core.Logging.AppLogger.LogInfo("RemoteSession", $"[CLIENT DOUBLE CLICK] Duplo-clique detetado (ClickCount={e.ClickCount}). Coordenadas estabilizadas em ({normX:F4}, {normY:F4}).");
                }
                else
                {
                    _lastLeftNormX = normX;
                    _lastLeftNormY = normY;
                }
                _lastLeftClickTime = now;
            }

            MouseEventType mouseType = e.ChangedButton switch
            {
                MouseButton.Left => MouseEventType.LeftDown,
                MouseButton.Right => MouseEventType.RightDown,
                MouseButton.Middle => MouseEventType.MiddleDown,
                _ => MouseEventType.LeftDown
            };

            RotinaRemote.Core.Logging.AppLogger.LogInfo("RemoteSession", $"[CLIENT MOUSE DOWN] Botão={e.ChangedButton}, Tipo={mouseType}, ClickCount={e.ClickCount}, Posição=({pos.X:F1}, {pos.Y:F1}), Normalizado=({normX:F4}, {normY:F4})");
            SendMouseInput(mouseType, normX, normY);
            e.Handled = true;
        }

        private void OnRemoteScreenMouseUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var pos = e.GetPosition(RemoteScreenImage);
                if (!GetNormalizedCoordinates(pos, out double normX, out double normY))
                {
                    // Fallback to clamped coordinates if released in letterbox border so mouse button never gets stuck
                    if (RemoteScreenImage.Source is System.Windows.Media.Imaging.BitmapSource bmp &&
                        RemoteScreenImage.ActualWidth > 0 && RemoteScreenImage.ActualHeight > 0 &&
                        bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
                    {
                        double ctrlWidth = RemoteScreenImage.ActualWidth;
                        double ctrlHeight = RemoteScreenImage.ActualHeight;
                        double scale = System.Math.Min(ctrlWidth / bmp.PixelWidth, ctrlHeight / bmp.PixelHeight);
                        double dispWidth = bmp.PixelWidth * scale;
                        double dispHeight = bmp.PixelHeight * scale;
                        if (dispWidth > 0 && dispHeight > 0)
                        {
                            double offsetX = (ctrlWidth - dispWidth) / 2.0;
                            double offsetY = (ctrlHeight - dispHeight) / 2.0;
                            double relX = System.Math.Clamp(pos.X - offsetX, 0.0, dispWidth);
                            double relY = System.Math.Clamp(pos.Y - offsetY, 0.0, dispHeight);
                            normX = System.Math.Clamp(relX / dispWidth, 0.0, 1.0);
                            normY = System.Math.Clamp(relY / dispHeight, 0.0, 1.0);
                        }
                    }
                }

                if (e.ChangedButton == MouseButton.Left)
                {
                    _isClientMouseDown = false;
                    var now = DateTime.UtcNow;
                    if ((now - _lastLeftClickTime).TotalMilliseconds <= 550 && _lastLeftNormX >= 0 &&
                        System.Math.Abs(normX - _lastLeftNormX) < 0.006 &&
                        System.Math.Abs(normY - _lastLeftNormY) < 0.006)
                    {
                        normX = _lastLeftNormX;
                        normY = _lastLeftNormY;
                    }
                }

                MouseEventType mouseType = e.ChangedButton switch
                {
                    MouseButton.Left => MouseEventType.LeftUp,
                    MouseButton.Right => MouseEventType.RightUp,
                    MouseButton.Middle => MouseEventType.MiddleUp,
                    _ => MouseEventType.LeftUp
                };

                RotinaRemote.Core.Logging.AppLogger.LogInfo("RemoteSession", $"[CLIENT MOUSE UP] Botão={e.ChangedButton}, Tipo={mouseType}, Posição=({pos.X:F1}, {pos.Y:F1}), Normalizado=({normX:F4}, {normY:F4})");
                SendMouseInput(mouseType, normX, normY);
                e.Handled = true;
            }
            finally
            {
                if (RemoteScreenImage.IsMouseCaptured)
                {
                    RemoteScreenImage.ReleaseMouseCapture();
                }
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
            if (vm == null)
            {
                RotinaRemote.Core.Logging.AppLogger.LogError("RemoteSession", $"[CLIENT ERROR] Falha ao enviar clique {mouseType}: ViewModel é nulo.");
                return;
            }

            if (!vm.IsConnected)
            {
                if (mouseType != MouseEventType.Move)
                {
                    RotinaRemote.Core.Logging.AppLogger.LogWarning("RemoteSession", $"[CLIENT WARNING] Clique {mouseType} em ({normX:F4}, {normY:F4}) não enviado: Nenhuma sessão remota ativa (IsConnected = false).");
                }
                return;
            }

            var payload = new InputPacketPayload
            {
                Type = ProtocolInputType.Mouse,
                MouseType = (byte)mouseType,
                NormX = normX,
                NormY = normY,
                WheelDelta = wheelDelta
            };
            if (mouseType != MouseEventType.Move)
            {
                RotinaRemote.Core.Logging.AppLogger.LogInfo("RemoteSession", $"[CLIENT SEND] Enviando clique {mouseType} em ({normX:F4}, {normY:F4}) via {vm.TransportType}.");
            }
            vm.SendInputToRemoteHost(payload);
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
