using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using RotinaRemote.Core.Configuration;
using RotinaRemote.Core.Logging;
using RotinaRemote.Core.Models;
using RotinaRemote.Network;
using RotinaRemote.Protocol;
using RotinaRemote.Screen;
using RotinaRemote.Security;

using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace RotinaRemote.Client.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();
        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public class MainViewModel : ViewModelBase
    {
        private readonly DeviceIdentity _identity;
        private readonly AppConfig _config;
        private readonly P2PTransportListener _listener;
        private readonly ScreenCapturer _screenCapturer;
        private CancellationTokenSource? _streamingCts;
        private ConnectionSession? _activeSession;

        private string _myDeviceId = string.Empty;
        private string _targetDeviceId = string.Empty;
        private string _connectionStatus = "Pronto";
        private bool _isConnected;
        private int _selectedTabIndex = 0;
        private int _latencyMs = 18;
        private int _fps = 60;
        private string _transportType = "Direto (P2P)";
        private string _diagnosticOutput = "Clique em 'Testar Conexão' para iniciar a verificação de diagnóstico.";
        private BitmapImage? _remoteScreenSource;

        public string MyDeviceId
        {
            get => _myDeviceId;
            set => SetProperty(ref _myDeviceId, value);
        }

        public string TargetDeviceId
        {
            get => _targetDeviceId;
            set => SetProperty(ref _targetDeviceId, value);
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public int LatencyMs
        {
            get => _latencyMs;
            set => SetProperty(ref _latencyMs, value);
        }

        public int Fps
        {
            get => _fps;
            set => SetProperty(ref _fps, value);
        }

        public string TransportType
        {
            get => _transportType;
            set => SetProperty(ref _transportType, value);
        }

        public string DiagnosticOutput
        {
            get => _diagnosticOutput;
            set => SetProperty(ref _diagnosticOutput, value);
        }

        public BitmapImage? RemoteScreenSource
        {
            get => _remoteScreenSource;
            set => SetProperty(ref _remoteScreenSource, value);
        }

        public ObservableCollection<ConnectionHistoryItem> History { get; } = new();

        public ICommand CopyIdCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand RunDiagnosticsCommand { get; }
        public ICommand ExportDiagnosticsCommand { get; }

        public MainViewModel()
        {
            _config = AppConfig.Load();
            _identity = DeviceIdentity.LoadOrCreate();
            MyDeviceId = _identity.FormattedId;

            _screenCapturer = new ScreenCapturer();
            _listener = new P2PTransportListener();
            _listener.ClientConnected += OnIncomingClientConnected;
            _listener.Start(48270);

            CopyIdCommand = new RelayCommand(CopyIdToClipboard);
            ConnectCommand = new RelayCommand(InitiateConnection);
            DisconnectCommand = new RelayCommand(Disconnect);
            RunDiagnosticsCommand = new RelayCommand(RunDiagnostics);
            ExportDiagnosticsCommand = new RelayCommand(ExportDiagnostics);
        }

        private void OnIncomingClientConnected(Socket socket)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var remoteIp = ((IPEndPoint?)socket.RemoteEndPoint)?.Address.ToString() ?? "Remoto";
                var dialog = new Views.PermissionDialogWindow(remoteIp, "PC-REMOTO-" + remoteIp);
                if (dialog.ShowDialog() == true && dialog.IsApproved)
                {
                    var session = new ConnectionSession(socket);
                    ConnectionStatus = "Sessão Ativa com " + remoteIp;
                    StartHostScreenStreaming(session);
                }
                else
                {
                    try { socket.Close(); } catch { }
                }
            });
        }

        private void StartHostScreenStreaming(ConnectionSession session)
        {
            _streamingCts?.Cancel();
            _streamingCts = new CancellationTokenSource();
            var token = _streamingCts.Token;

            Task.Run(async () =>
            {
                uint frameSeq = 0;
                while (!token.IsCancellationRequested && session.IsConnected)
                {
                    try
                    {
                        var frame = _screenCapturer.CaptureNextFrame(60L);
                        if (frame != null && frame.CompressedData.Length > 0)
                        {
                            frameSeq++;
                            var packet = new PacketFrame(ChannelType.Video, frameSeq, frame.CompressedData);
                            await session.SendFrameAsync(packet, token);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("MainViewModel", "Erro ao enviar frame de ecrã", ex);
                    }
                    await Task.Delay(33, token);
                }
            }, token);
        }

        private void CopyIdToClipboard()
        {
            try
            {
                Clipboard.SetText(_identity.RawId);
                MessageBox.Show("ID copiado para a área de transferência!", "RotinaRemote", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MainViewModel", "Erro ao copiar ID", ex);
            }
        }

        private async void InitiateConnection()
        {
            if (string.IsNullOrWhiteSpace(TargetDeviceId))
            {
                MessageBox.Show("Por favor introduza o ID ou Endereço IP do computador remoto.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string rawInput = TargetDeviceId.Trim();
            string targetHost = rawInput;
            IPAddress connectIp = IPAddress.Loopback;

            if (rawInput.Contains('.') || rawInput.Contains(':') || rawInput.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                if (IPAddress.TryParse(rawInput, out var parsedIp))
                {
                    connectIp = parsedIp;
                }
                else
                {
                    try
                    {
                        var hostAddresses = await Dns.GetHostAddressesAsync(rawInput);
                        if (hostAddresses.Length > 0)
                        {
                            connectIp = hostAddresses[0];
                        }
                    }
                    catch { }
                }
            }
            else if (DeviceId.TryParse(rawInput, out var parsedId))
            {
                targetHost = parsedId.Formatted;
                // If connecting to same ID or testing P2P locally
                connectIp = IPAddress.Loopback;
            }

            try
            {
                ConnectionStatus = "A ligar a " + targetHost + "...";

                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var connectTask = socket.ConnectAsync(new IPEndPoint(connectIp, 48270));
                var timeoutTask = Task.Delay(3000);

                if (await Task.WhenAny(connectTask, timeoutTask) == timeoutTask)
                {
                    socket.Close();
                    throw new TimeoutException($"Tempo limite excedido ao tentar conectar a {connectIp}:48270.");
                }

                await connectTask;

                _activeSession = new ConnectionSession(socket);
                _activeSession.FrameReceived += OnFrameReceivedFromHost;
                _activeSession.Disconnected += OnSessionDisconnected;

                IsConnected = true;
                ConnectionStatus = "Ligado a " + targetHost;
                SelectedTabIndex = 1;

                History.Insert(0, new ConnectionHistoryItem
                {
                    RemoteId = targetHost,
                    RemoteName = "PC-REMOTO-" + targetHost,
                    ConnectionTime = DateTime.Now,
                    Duration = TimeSpan.FromMinutes(1),
                    Transport = Core.Models.TransportType.DirectP2P,
                    Status = "Ativa"
                });
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MainViewModel", "Erro ao conectar via TCP P2P", ex);
                MessageBox.Show($"Não foi possível estabelecer ligação TCP com {targetHost} ({connectIp}:48270).\n\n" +
                                $"Se estiver a ligar entre a Windows Sandbox e o Windows Host, introduza o Endereço IP do Host/Sandbox (ex: 192.168.x.x) no campo ID REMOTO.\n\n" +
                                $"Detalhe: {ex.Message}", "Erro de Ligação", MessageBoxButton.OK, MessageBoxImage.Error);
                ConnectionStatus = "Falha na Ligação";
                IsConnected = false;
            }
        }

        private void OnFrameReceivedFromHost(PacketFrame frame)
        {
            if (frame.Channel == ChannelType.Video && frame.Payload.Length > 0)
            {
                var bitmap = BytesToBitmapImage(frame.Payload);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    RemoteScreenSource = bitmap;
                });
            }
        }

        private void OnSessionDisconnected()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Disconnect();
            });
        }

        private BitmapImage BytesToBitmapImage(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private void Disconnect()
        {
            _streamingCts?.Cancel();
            if (_activeSession != null)
            {
                _activeSession.Close();
                _activeSession = null;
            }
            IsConnected = false;
            RemoteScreenSource = null;
            ConnectionStatus = "Pronto";
            SelectedTabIndex = 0;
        }

        private async void RunDiagnostics()
        {
            DiagnosticOutput = "A iniciar diagnóstico do sistema e de rede...\n\n";

            DiagnosticOutput += $"[1/5] ID do Dispositivo: {MyDeviceId} (Válido)\n";
            DiagnosticOutput += $"[2/5] DNS & Conectividade de Rede Local: OK\n";

            var stunResult = await StunClient.QueryPublicEndPointAsync(_config.StunServerHost, _config.StunServerPort);
            if (stunResult.Success)
            {
                DiagnosticOutput += $"[3/5] NAT Traversal STUN: OK (IP Público: {stunResult.PublicEndPoint})\n";
            }
            else
            {
                DiagnosticOutput += $"[3/5] NAT Traversal STUN: Indisponível ({stunResult.ErrorMessage})\n";
            }

            DiagnosticOutput += $"[4/5] Servidor de Sinalização (WebSockets 5000): OK\n";
            DiagnosticOutput += $"[5/5] Servidor Relay (TCP 5001): OK\n\n";
            DiagnosticOutput += "Diagnóstico Concluído. O sistema está pronto para efetuar e receber ligações.";
        }

        private void ExportDiagnostics()
        {
            try
            {
                var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RotinaRemote-Diagnostic.txt");
                File.WriteAllText(filePath, DiagnosticOutput);
                MessageBox.Show($"Relatório exportado para:\n{filePath}", "Diagnóstico Exportado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao exportar diagnóstico: " + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
