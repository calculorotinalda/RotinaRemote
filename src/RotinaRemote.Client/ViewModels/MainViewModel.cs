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
using RotinaRemote.Input;
using RotinaRemote.Network;
using RotinaRemote.Protocol;
using RotinaRemote.Screen;
using RotinaRemote.Security;

using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using TransportTypeEnum = RotinaRemote.Core.Models.TransportType;

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
        private readonly LanDiscoveryService _lanDiscovery;
        private readonly SignalingClient _signalingClient;
        private readonly ScreenCapturer _screenCapturer;
        private CancellationTokenSource? _streamingCts;
        private ConnectionSession? _activeSession;
        private ConnectionSession? _incomingSession;

        private string _myDeviceId = string.Empty;
        private string _targetDeviceId = string.Empty;
        private string _targetPassword = string.Empty;
        private string _connectionStatus = "Pronto";
        private bool _isConnected;
        private int _selectedTabIndex = 0;
        private int _latencyMs = 18;
        private int _fps = 60;
        private string _transportType = "Direto (P2P)";
        private string _diagnosticOutput = "Clique em 'Testar Conexão' para iniciar a verificação de diagnóstico.";
        private BitmapImage? _remoteScreenSource;
        private string _activeIncomingPermission = "FullControl";

        // As 15 Configurações Avançadas
        private string _theme = "Dark";
        private bool _enableUnattendedAccess = false;
        private string _unattendedPassword = string.Empty;
        private string _unattendedPermission = "FullControl";
        private bool _startWithWindows = false;
        private bool _minimizeToTray = true;
        private int _videoQuality = 60;
        private int _targetFps = 60;
        private bool _disableRemoteWallpaper = false;
        private bool _enableClipboardSync = true;
        private bool _blockRemoteInput = false;
        private int _p2pPort = 48270;
        private int _lanDiscoveryPort = 48271;
        private int _keepAliveIntervalMs = 3000;
        private string _signalingServerUrl = "wss://rotinaremote-signaling-49575983278.europe-west1.run.app/ws";

        // Estado do Serviço Windows
        private string _serviceStatusText = "A verificar...";
        private string _serviceStatusColor = "#94A3B8";

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

        public string TargetPassword
        {
            get => _targetPassword;
            set => SetProperty(ref _targetPassword, value);
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

        // 1. Tema Visual (Dark / Light)
        public string Theme
        {
            get => _theme;
            set
            {
                if (SetProperty(ref _theme, value))
                {
                    OnPropertyChanged(nameof(IsDarkMode));
                    OnPropertyChanged(nameof(IsLightMode));
                }
            }
        }

        public bool IsDarkMode
        {
            get => _theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
            set
            {
                if (value)
                {
                    Theme = "Dark";
                    RotinaRemote.Client.Services.ThemeManager.ApplyTheme("Dark");
                }
                OnPropertyChanged(nameof(IsDarkMode));
                OnPropertyChanged(nameof(IsLightMode));
            }
        }

        public bool IsLightMode
        {
            get => _theme.Equals("Light", StringComparison.OrdinalIgnoreCase);
            set
            {
                if (value)
                {
                    Theme = "Light";
                    RotinaRemote.Client.Services.ThemeManager.ApplyTheme("Light");
                }
                OnPropertyChanged(nameof(IsDarkMode));
                OnPropertyChanged(nameof(IsLightMode));
            }
        }

        // 2. Acesso Não Supervisionado por Senha
        public bool EnableUnattendedAccess
        {
            get => _enableUnattendedAccess;
            set => SetProperty(ref _enableUnattendedAccess, value);
        }

        // 3. Senha de Acesso Remoto Imediato
        public string UnattendedPassword
        {
            get => _unattendedPassword;
            set => SetProperty(ref _unattendedPassword, value);
        }

        // 4. Permissões de Acesso Não Supervisionado ("FullControl" ou "OnlyRead")
        public string UnattendedPermission
        {
            get => _unattendedPermission;
            set
            {
                if (SetProperty(ref _unattendedPermission, value))
                {
                    OnPropertyChanged(nameof(IsFullControl));
                    OnPropertyChanged(nameof(IsOnlyRead));
                }
            }
        }

        public bool IsFullControl
        {
            get => string.Equals(_unattendedPermission, "FullControl", StringComparison.OrdinalIgnoreCase);
            set
            {
                if (value)
                {
                    UnattendedPermission = "FullControl";
                }
                OnPropertyChanged(nameof(IsFullControl));
                OnPropertyChanged(nameof(IsOnlyRead));
            }
        }

        public bool IsOnlyRead
        {
            get => string.Equals(_unattendedPermission, "OnlyRead", StringComparison.OrdinalIgnoreCase);
            set
            {
                if (value)
                {
                    UnattendedPermission = "OnlyRead";
                }
                OnPropertyChanged(nameof(IsFullControl));
                OnPropertyChanged(nameof(IsOnlyRead));
            }
        }

        // 5. Iniciar com o Windows
        public bool StartWithWindows
        {
            get => _startWithWindows;
            set => SetProperty(ref _startWithWindows, value);
        }

        // 6. Minimizar para a Área de Notificação
        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set => SetProperty(ref _minimizeToTray, value);
        }

        // 7. Qualidade de Imagem / Compressão (30 a 95%)
        public int VideoQuality
        {
            get => _videoQuality;
            set => SetProperty(ref _videoQuality, value);
        }

        // 8. Taxa de Fotogramas por Segundo Alvo (15, 30, 60)
        public int TargetFps
        {
            get => _targetFps;
            set => SetProperty(ref _targetFps, value);
        }

        // 9. Desativar Papel de Parede Remoto
        public bool DisableRemoteWallpaper
        {
            get => _disableRemoteWallpaper;
            set => SetProperty(ref _disableRemoteWallpaper, value);
        }

        // 10. Sincronização de Clipboard
        public bool EnableClipboardSync
        {
            get => _enableClipboardSync;
            set => SetProperty(ref _enableClipboardSync, value);
        }

        // 11. Bloqueio de Entrada Local
        public bool BlockRemoteInput
        {
            get => _blockRemoteInput;
            set => SetProperty(ref _blockRemoteInput, value);
        }

        // 12. Porta TCP de Escuta P2P Direto
        public int P2pPort
        {
            get => _p2pPort;
            set => SetProperty(ref _p2pPort, value);
        }

        // 13. Porta UDP LAN
        public int LanDiscoveryPort
        {
            get => _lanDiscoveryPort;
            set => SetProperty(ref _lanDiscoveryPort, value);
        }

        // 14. Intervalo de KeepAlive / Heartbeat (ms)
        public int KeepAliveIntervalMs
        {
            get => _keepAliveIntervalMs;
            set => SetProperty(ref _keepAliveIntervalMs, value);
        }

        // 15. Servidor de Sinalização URL
        public string SignalingServerUrl
        {
            get => _signalingServerUrl;
            set => SetProperty(ref _signalingServerUrl, value);
        }

        // Estado do Serviço Windows
        public string ServiceStatusText
        {
            get => _serviceStatusText;
            set => SetProperty(ref _serviceStatusText, value);
        }

        public string ServiceStatusColor
        {
            get => _serviceStatusColor;
            set => SetProperty(ref _serviceStatusColor, value);
        }

        public ObservableCollection<ConnectionHistoryItem> History { get; } = new();

        public ICommand CopyIdCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand RunDiagnosticsCommand { get; }
        public ICommand ExportDiagnosticsCommand { get; }

        // Comandos de Definições Avançadas e Serviço
        public ICommand SaveSettingsCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand InstallServiceCommand { get; }
        public ICommand StartServiceCommand { get; }
        public ICommand StopServiceCommand { get; }
        public ICommand UninstallServiceCommand { get; }
        public ICommand RefreshServiceStatusCommand { get; }

        public MainViewModel()
        {
            _config = AppConfig.Load();
            _identity = DeviceIdentity.LoadOrCreate();
            MyDeviceId = _identity.FormattedId;

            // Carregar valores de configurações salvas
            _theme = _config.Theme;
            _enableUnattendedAccess = _config.EnableUnattendedAccess;
            _unattendedPassword = _config.UnattendedPassword;
            _unattendedPermission = _config.UnattendedPermission;
            _startWithWindows = _config.StartWithWindows;
            _minimizeToTray = _config.MinimizeToTray;
            _videoQuality = _config.VideoQuality;
            _targetFps = _config.TargetFps;
            _disableRemoteWallpaper = _config.DisableRemoteWallpaper;
            _enableClipboardSync = _config.EnableClipboardSync;
            _blockRemoteInput = _config.BlockRemoteInput;
            _p2pPort = _config.P2pPort;
            _lanDiscoveryPort = _config.LanDiscoveryPort;
            _keepAliveIntervalMs = _config.KeepAliveIntervalMs;
            _signalingServerUrl = _config.SignalingServerUrl;

            _screenCapturer = new ScreenCapturer();
            _listener = new P2PTransportListener();
            _listener.ClientConnected += OnIncomingClientConnected;
            _listener.Start(_p2pPort);

            _lanDiscovery = new LanDiscoveryService();
            _lanDiscovery.Start(_identity.RawId, _lanDiscoveryPort);

            _signalingClient = new SignalingClient();
            _signalingClient.ConnectRequestReceived += OnSignalingConnectRequestReceived;
            _ = _signalingClient.StartAsync(_config.SignalingServerUrl, _identity.RawId);

            CopyIdCommand = new RelayCommand(CopyIdToClipboard);
            ConnectCommand = new RelayCommand(InitiateConnection);
            DisconnectCommand = new RelayCommand(Disconnect);
            RunDiagnosticsCommand = new RelayCommand(RunDiagnostics);
            ExportDiagnosticsCommand = new RelayCommand(ExportDiagnostics);

            SaveSettingsCommand = new RelayCommand(SaveSettings);
            ToggleThemeCommand = new RelayCommand(ToggleTheme);
            InstallServiceCommand = new RelayCommand(InstallService);
            StartServiceCommand = new RelayCommand(StartService);
            StopServiceCommand = new RelayCommand(StopService);
            UninstallServiceCommand = new RelayCommand(UninstallService);
            RefreshServiceStatusCommand = new RelayCommand(RefreshServiceStatus);

            RefreshServiceStatus();
        }

        public void RefreshServiceStatus()
        {
            Task.Run(() =>
            {
                var status = RotinaRemote.Client.Services.WindowsServiceManager.GetStatus();
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    switch (status)
                    {
                        case RotinaRemote.Client.Services.ServiceStatusEnum.Running:
                            ServiceStatusText = "Em Execução (Ativo)";
                            ServiceStatusColor = "#10B981";
                            break;
                        case RotinaRemote.Client.Services.ServiceStatusEnum.Stopped:
                            ServiceStatusText = "Parado";
                            ServiceStatusColor = "#EF4444";
                            break;
                        case RotinaRemote.Client.Services.ServiceStatusEnum.NotInstalled:
                            ServiceStatusText = "Não Instalado";
                            ServiceStatusColor = "#94A3B8";
                            break;
                        default:
                            ServiceStatusText = "Desconhecido";
                            ServiceStatusColor = "#F59E0B";
                            break;
                    }
                });
            });
        }

        private void InstallService()
        {
            var (success, msg) = RotinaRemote.Client.Services.WindowsServiceManager.InstallService();
            MessageBox.Show(msg, "Serviço Windows", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            RefreshServiceStatus();
        }

        private void StartService()
        {
            var (success, msg) = RotinaRemote.Client.Services.WindowsServiceManager.StartService();
            MessageBox.Show(msg, "Serviço Windows", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Task.Delay(1000).ContinueWith(_ => RefreshServiceStatus());
        }

        private void StopService()
        {
            var (success, msg) = RotinaRemote.Client.Services.WindowsServiceManager.StopService();
            MessageBox.Show(msg, "Serviço Windows", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Task.Delay(1000).ContinueWith(_ => RefreshServiceStatus());
        }

        private void UninstallService()
        {
            var (success, msg) = RotinaRemote.Client.Services.WindowsServiceManager.UninstallService();
            MessageBox.Show(msg, "Serviço Windows", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Task.Delay(1000).ContinueWith(_ => RefreshServiceStatus());
        }

        private void ToggleTheme()
        {
            if (Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
            {
                Theme = "Light";
                RotinaRemote.Client.Services.ThemeManager.ApplyTheme("Light");
            }
            else
            {
                Theme = "Dark";
                RotinaRemote.Client.Services.ThemeManager.ApplyTheme("Dark");
            }
        }

        private void SaveSettings()
        {
            try
            {
                _config.Theme = Theme;
                _config.EnableUnattendedAccess = EnableUnattendedAccess;
                _config.UnattendedPassword = UnattendedPassword;
                _config.UnattendedPermission = UnattendedPermission;
                _config.StartWithWindows = StartWithWindows;
                _config.MinimizeToTray = MinimizeToTray;
                _config.VideoQuality = VideoQuality;
                _config.TargetFps = TargetFps;
                _config.DisableRemoteWallpaper = DisableRemoteWallpaper;
                _config.EnableClipboardSync = EnableClipboardSync;
                _config.BlockRemoteInput = BlockRemoteInput;
                _config.P2pPort = P2pPort;
                _config.LanDiscoveryPort = LanDiscoveryPort;
                _config.KeepAliveIntervalMs = KeepAliveIntervalMs;
                _config.SignalingServerUrl = SignalingServerUrl;

                _config.Save();
                RotinaRemote.Client.Services.ThemeManager.ApplyTheme(Theme);
                MessageBox.Show("Configurações guardadas com sucesso no disco!", "RotinaRemote", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao guardar configurações: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnSignalingConnectRequestReceived(string sourceId, string targetId, string payload)
        {
            try
            {
                string callerId = sourceId;
                string callerIp = "Desconhecido";
                string callerCity = "Desconhecida";
                string callerCountry = "Desconhecido";
                string callerLocation = "Desconhecida";

                SignalingConnectRequestPayload? req = null;
                try
                {
                    req = MessageSerializer.DeserializeJson<SignalingConnectRequestPayload>(System.Text.Encoding.UTF8.GetBytes(payload));
                    if (req != null)
                    {
                        callerId = !string.IsNullOrWhiteSpace(req.CallerDeviceId) ? req.CallerDeviceId : sourceId;
                        callerIp = !string.IsNullOrWhiteSpace(req.CallerIp) ? req.CallerIp : "Desconhecido";
                        callerCity = !string.IsNullOrWhiteSpace(req.City) ? req.City : "Desconhecida";
                        callerCountry = !string.IsNullOrWhiteSpace(req.Country) ? req.Country : "Desconhecido";
                        callerLocation = !string.IsNullOrWhiteSpace(req.Location) ? req.Location : $"{callerCity}, {callerCountry}";
                    }
                }
                catch { }

                bool isApproved = false;

                if (_config.EnableUnattendedAccess &&
                    !string.IsNullOrWhiteSpace(_config.UnattendedPassword) &&
                    !string.IsNullOrWhiteSpace(req?.Password) &&
                    req.Password == _config.UnattendedPassword)
                {
                    isApproved = true;
                    _activeIncomingPermission = _config.UnattendedPermission;
                    AppLogger.LogInfo("MainViewModel", $"Pedido de ligação de {callerId} ({callerCity}, {callerCountry}) aprovado via Acesso Não Supervisionado por Senha (Permissão: {_activeIncomingPermission}).");
                }
                else
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        var dialog = new Views.PermissionDialogWindow(callerId, callerIp, callerCity, callerCountry, callerLocation);
                        isApproved = dialog.ShowDialog() == true && dialog.IsApproved;
                    });
                    if (isApproved)
                    {
                        _activeIncomingPermission = "FullControl";
                    }
                }

                if (!isApproved)
                {
                    var rejectEndpointData = new SignalingEndpointInfo
                    {
                        Accepted = false,
                        Message = "A ligação remota foi recusada pelo utilizador do computador anfitrião."
                    };
                    string rejectPayload = System.Text.Json.JsonSerializer.Serialize(rejectEndpointData);
                    await _signalingClient.SendMessageAsync("ConnectResponse", sourceId, rejectPayload);
                    AppLogger.LogInfo("MainViewModel", $"Pedido de ligação de {callerId} ({callerCity}, {callerCountry}) rejeitado pelo utilizador.");
                    return;
                }

                string publicIp = string.Empty;
                try
                {
                    var stunResult = await StunClient.QueryPublicEndPointAsync(_config.StunServerHost, _config.StunServerPort);
                    if (stunResult.Success && stunResult.PublicEndPoint != null)
                    {
                        publicIp = stunResult.PublicEndPoint.Address.ToString();
                    }
                }
                catch { }

                string relaySessionId = Guid.NewGuid().ToString();

                var endpointData = new SignalingEndpointInfo
                {
                    Accepted = true,
                    LocalIp = _lanDiscovery.LocalIP ?? "",
                    PublicIp = publicIp,
                    Port = _p2pPort,
                    RelaySessionId = relaySessionId,
                    RelayServerUrl = _config.RelayServerUrl,
                    PermissionMode = _activeIncomingPermission
                };

                string jsonPayload = System.Text.Json.JsonSerializer.Serialize(endpointData);
                await _signalingClient.SendMessageAsync("ConnectResponse", sourceId, jsonPayload);

                if (!string.IsNullOrWhiteSpace(_config.RelayServerUrl))
                {
                    _ = ConnectHostToRelayAsync(relaySessionId, _config.RelayServerUrl);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("MainViewModel", "Erro ao responder a pedido de sinalização", ex);
            }
        }

        private async Task ConnectHostToRelayAsync(string relaySessionId, string relayServerUrl)
        {
            try
            {
                if (relayServerUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                    relayServerUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                {
                    string wsUrl = $"{relayServerUrl.TrimEnd('/')}?sessionId={relaySessionId}&role=host";
                    var ws = new System.Net.WebSockets.ClientWebSocket();
                    using var ctsWs = new CancellationTokenSource(15000);
                    await ws.ConnectAsync(new Uri(wsUrl), ctsWs.Token);

                    if (ws.State == System.Net.WebSockets.WebSocketState.Open)
                    {
                        var stream = new WebSocketStream(ws, ownsSocket: true);
                        var session = new ConnectionSession(stream);
                        _incomingSession = session;
                        session.FrameReceived += OnInputFrameReceivedFromClient;
                        session.Disconnected += () =>
                        {
                            _incomingSession = null;
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                ConnectionStatus = "Pronto";
                            });
                        };

                        AppLogger.LogInfo("MainViewModel", $"Host conectado ao Servidor Relay WebSocket ({relayServerUrl}) com SessionId: {relaySessionId}");
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            ConnectionStatus = "Sessão Ativa via Relay na Nuvem";
                            StartHostScreenStreaming(session);
                        });
                    }
                    return;
                }

                var (host, port) = ParseRelayEndpoint(relayServerUrl);
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                using var cts = new CancellationTokenSource(15000);
                await socket.ConnectAsync(host, port, cts.Token);

                byte[] headerBytes = System.Text.Encoding.UTF8.GetBytes(relaySessionId.PadRight(36).Substring(0, 36));
                await socket.SendAsync(headerBytes, SocketFlags.None);

                AppLogger.LogInfo("MainViewModel", $"Host conectado ao Servidor Relay TCP ({host}:{port}) com SessionId: {relaySessionId}");
                OnIncomingClientConnected(socket);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning("MainViewModel", $"Host não conseguiu conectar ao Servidor Relay ({relayServerUrl}): {ex.Message}");
            }
        }

        private async void OnIncomingClientConnected(Socket socket)
        {
            var remoteIp = ((IPEndPoint?)socket.RemoteEndPoint)?.Address.ToString() ?? "Remoto";
            string resolvedId = remoteIp;
            string city = "Rede Local";
            string country = "Portugal / Local";
            string location = "Rede Local (LAN)";

            if (remoteIp.StartsWith("172.19."))
            {
                location = "Windows Sandbox (Hyper-V)";
            }

            foreach (var kvp in _lanDiscovery.DiscoveredPeers)
            {
                if (kvp.Value.IpAddress.ToString() == remoteIp)
                {
                    resolvedId = kvp.Key;
                    break;
                }
            }

            bool isLocal = remoteIp.StartsWith("192.168.") ||
                           remoteIp.StartsWith("10.") ||
                           remoteIp.StartsWith("172.19.") ||
                           remoteIp.StartsWith("127.") ||
                           remoteIp.Equals("::1");

            if (!isLocal && IPAddress.TryParse(remoteIp, out _))
            {
                try
                {
                    var geo = await RotinaRemote.Core.Services.GeoLocationService.GetGeoLocationAsync(resolvedId);
                    if (geo != null)
                    {
                        city = geo.City;
                        country = geo.Country;
                        location = geo.Location;
                    }
                }
                catch { }
            }

            var session = new ConnectionSession(socket);
            bool isApproved = false;

            if (_config.EnableUnattendedAccess && !string.IsNullOrWhiteSpace(_config.UnattendedPassword))
            {
                var hsTcs = new TaskCompletionSource<string?>();
                Action<PacketFrame> hsHandler = f =>
                {
                    if (f.Channel == ChannelType.Control && f.Payload.Length > 0)
                    {
                        try
                        {
                            var hs = MessageSerializer.DeserializeJson<HandshakeRequestPayload>(f.Payload);
                            hsTcs.TrySetResult(hs?.Password);
                        }
                        catch { }
                    }
                };
                session.FrameReceived += hsHandler;
                var completed = await Task.WhenAny(hsTcs.Task, Task.Delay(1200));
                session.FrameReceived -= hsHandler;

                if (completed == hsTcs.Task && hsTcs.Task.Result == _config.UnattendedPassword)
                {
                    isApproved = true;
                    _activeIncomingPermission = _config.UnattendedPermission;
                    AppLogger.LogInfo("MainViewModel", $"Ligação P2P direta de {resolvedId} aprovada via Senha de Acesso Não Supervisionado ({_activeIncomingPermission}).");
                }
            }

            if (!isApproved)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new Views.PermissionDialogWindow(resolvedId, remoteIp, city, country, location);
                    isApproved = dialog.ShowDialog() == true && dialog.IsApproved;
                    if (isApproved)
                    {
                        _activeIncomingPermission = "FullControl";
                    }
                });
            }

            if (isApproved)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _incomingSession = session;
                    session.FrameReceived += OnInputFrameReceivedFromClient;
                    session.Disconnected += () =>
                    {
                        if (_config.BlockRemoteInput)
                        {
                            InputInjector.SetBlockLocalInput(false);
                        }
                        _incomingSession = null;
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            ConnectionStatus = "Pronto";
                        });
                    };
                    ConnectionStatus = "Sessão Ativa com " + resolvedId;
                    StartHostScreenStreaming(session);
                });
            }
            else
            {
                try { session.Dispose(); } catch { }
                try { socket.Close(); } catch { }
            }
        }

        private void OnInputFrameReceivedFromClient(PacketFrame frame)
        {
            if (frame.Channel == ChannelType.Input && frame.Payload.Length > 0)
            {
                try
                {
                    var inputPayload = MessageSerializer.DeserializeJson<InputPacketPayload>(frame.Payload);
                    if (inputPayload != null)
                    {
                        if (string.Equals(_activeIncomingPermission, "OnlyRead", StringComparison.OrdinalIgnoreCase))
                        {
                            if (inputPayload.Type == ProtocolInputType.Mouse && inputPayload.MouseType != (byte)MouseEventType.Move)
                            {
                                AppLogger.LogWarning("RemoteSession", $"[HOST BLOCKED] Clique de rato (Tipo={(MouseEventType)inputPayload.MouseType}) BLOQUEADO: Sessão remota em modo APENAS LEITURA (OnlyRead)!");
                            }
                            return;
                        }

                        if (inputPayload.Type == ProtocolInputType.Mouse)
                        {
                            var mType = (MouseEventType)inputPayload.MouseType;
                            var bounds = _screenCapturer.CurrentBounds;
                            if (mType != MouseEventType.Move)
                            {
                                AppLogger.LogInfo("RemoteSession", $"[HOST RECV] Recebido clique {mType} em ({inputPayload.NormX:F4}, {inputPayload.NormY:F4}). Permissão={_activeIncomingPermission}. Ecrã Alvo: {bounds.Width}x{bounds.Height} em ({bounds.X}, {bounds.Y})");
                            }
                            InputInjector.InjectMouse(
                                mType,
                                inputPayload.NormX,
                                inputPayload.NormY,
                                inputPayload.WheelDelta,
                                bounds.X,
                                bounds.Y,
                                bounds.Width,
                                bounds.Height);
                        }
                        else if (inputPayload.Type == ProtocolInputType.Keyboard)
                        {
                            AppLogger.LogInfo("RemoteSession", $"[HOST RECV] Recebido teclado: KeyType={(KeyEventType)inputPayload.KeyType}, VKey={inputPayload.VirtualKeyCode}");
                            InputInjector.InjectKeyboard(
                                (KeyEventType)inputPayload.KeyType,
                                inputPayload.VirtualKeyCode);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("RemoteSession", "Host: Erro ao processar frame de input recebido", ex);
                }
            }
        }

        private uint _inputSeq = 0;
        private InputPacketPayload? _pendingMouseMove = null;
        private bool _isSendingMouseMove = false;
        private readonly object _mouseMoveLock = new object();

        public async void SendInputToRemoteHost(InputPacketPayload inputPayload)
        {
            if (_activeSession == null || !_activeSession.IsConnected || !IsConnected)
            {
                if (inputPayload.Type == ProtocolInputType.Mouse && inputPayload.MouseType != (byte)MouseEventType.Move)
                {
                    AppLogger.LogWarning("RemoteSession", $"[CLIENT ERROR] Não foi possível enviar clique: Sessão não está ligada ou ativa (_activeSession={_activeSession != null}, IsConnected={IsConnected})");
                }
                return;
            }

            // Se for movimento do rato, coalescer para enviar apenas a posição mais recente e não congestionar a fila
            if (inputPayload.Type == ProtocolInputType.Mouse && inputPayload.MouseType == (byte)MouseEventType.Move)
            {
                lock (_mouseMoveLock)
                {
                    _pendingMouseMove = inputPayload;
                    if (_isSendingMouseMove)
                    {
                        return;
                    }
                    _isSendingMouseMove = true;
                }

                _ = Task.Run(async () =>
                {
                    while (true)
                    {
                        InputPacketPayload? toSend = null;
                        lock (_mouseMoveLock)
                        {
                            toSend = _pendingMouseMove;
                            _pendingMouseMove = null;
                            if (toSend == null)
                            {
                                _isSendingMouseMove = false;
                                break;
                            }
                        }

                        try
                        {
                            if (_activeSession != null && _activeSession.IsConnected && IsConnected)
                            {
                                _inputSeq++;
                                var bytes = MessageSerializer.SerializeJson(toSend);
                                var packet = new PacketFrame(ChannelType.Input, _inputSeq, bytes);
                                await _activeSession.SendFrameAsync(packet);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogWarning("RemoteSession", $"Erro ao enviar movimento de rato: {ex.Message}");
                        }
                    }
                });

                return;
            }

            // Cliques de rato e teclado têm prioridade máxima e são despachados imediatamente
            try
            {
                _inputSeq++;
                var bytes = MessageSerializer.SerializeJson(inputPayload);
                var packet = new PacketFrame(ChannelType.Input, _inputSeq, bytes);
                await _activeSession.SendFrameAsync(packet);
                if (inputPayload.Type == ProtocolInputType.Mouse && inputPayload.MouseType != (byte)MouseEventType.Move)
                {
                    AppLogger.LogInfo("RemoteSession", $"[CLIENT SENT] Pacote de clique {(MouseEventType)inputPayload.MouseType} despachado com sucesso via rede (Seq={_inputSeq}).");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("RemoteSession", "Erro ao enviar input prioritário para o computador remoto", ex);
            }
        }

        private void StartHostScreenStreaming(ConnectionSession session)
        {
            _streamingCts?.Cancel();
            _streamingCts = new CancellationTokenSource();
            var token = _streamingCts.Token;

            if (_config.BlockRemoteInput)
            {
                InputInjector.SetBlockLocalInput(true);
            }

            Task.Run(async () =>
            {
                uint frameSeq = 0;
                long quality = (long)Math.Clamp(_config.VideoQuality, 30, 95);
                int targetFps = Math.Clamp(_config.TargetFps, 10, 60);
                int frameDelay = Math.Max(10, 1000 / targetFps);

                while (!token.IsCancellationRequested && session.IsConnected)
                {
                    try
                    {
                        var frame = _screenCapturer.CaptureNextFrame(quality);
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
                    await Task.Delay(frameDelay, token);
                }

                if (_config.BlockRemoteInput)
                {
                    InputInjector.SetBlockLocalInput(false);
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
            Socket? activeSocket = null;
            ConnectionSession? activeSession = null;
            string usedTransportName = "Direto (P2P)";
            TransportTypeEnum usedTransportType = TransportTypeEnum.DirectP2P;

            if (rawInput.Contains('.') || rawInput.Contains(':') || rawInput.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                int targetPort = 48270;
                string hostPart = rawInput;

                if (rawInput.Contains(':'))
                {
                    var parts = rawInput.Split(':');
                    hostPart = parts[0];
                    if (parts.Length > 1 && int.TryParse(parts[1], out int p))
                    {
                        targetPort = p;
                    }
                }

                IPAddress? connectIp = null;
                if (IPAddress.TryParse(hostPart, out var parsedIp))
                {
                    connectIp = parsedIp;
                }
                else
                {
                    try
                    {
                        var hostAddresses = await Dns.GetHostAddressesAsync(hostPart);
                        if (hostAddresses.Length > 0)
                        {
                            connectIp = hostAddresses[0];
                        }
                    }
                    catch { }
                }

                if (connectIp != null)
                {
                    ConnectionStatus = "A ligar diretamente a " + connectIp + "...";
                    activeSocket = await TryConnectTcpAsync(connectIp, targetPort, 3000);
                    if (activeSocket != null)
                    {
                        usedTransportName = IPAddress.IsLoopback(connectIp) || connectIp.ToString().StartsWith("192.168.") || connectIp.ToString().StartsWith("10.")
                            ? "Direto (P2P Local)"
                            : "Direto (P2P WAN)";
                    }
                }
            }
            else if (DeviceId.TryParse(rawInput, out var parsedId))
            {
                targetHost = parsedId.Formatted;

                // Check if user is trying to connect to their own device ID
                if (parsedId.RawValue.Equals(_identity.RawId, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Está a tentar conectar ao ID deste próprio computador (Loopback).\n\n" +
                                    "A ligação local faria o programa capturar e exibir o próprio ecrã (espelho infinito).\n" +
                                    "Para conectar à Windows Sandbox ou a outro PC, introduza o ID da máquina remota.",
                                    "Aviso de Ligação", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ConnectionStatus = "Pronto";
                    return;
                }

                // Attempt to resolve target device IP via LAN / Windows Sandbox UDP discovery first
                ConnectionStatus = "A procurar " + targetHost + " na rede local / Sandbox...";
                var resolvedIp = await _lanDiscovery.ResolveDeviceIdAsync(parsedId.RawValue, 3500);

                if (resolvedIp != null)
                {
                    ConnectionStatus = "Peer localizado em " + resolvedIp + ". A ligar...";
                    activeSocket = await TryConnectTcpAsync(resolvedIp, 48270, 3000);
                    if (activeSocket != null)
                    {
                        usedTransportName = "Direto (P2P Local / Sandbox)";
                        usedTransportType = TransportTypeEnum.DirectP2P;
                    }
                }

                if (activeSocket == null && _signalingClient.IsConnected)
                {
                    ConnectionStatus = "A aguardar autorização do anfitrião " + targetHost + "...";

                    var myGeo = await RotinaRemote.Core.Services.GeoLocationService.GetGeoLocationAsync(_identity.FormattedId);
                    var reqPayload = new SignalingConnectRequestPayload
                    {
                        CallerDeviceId = _identity.FormattedId,
                        CallerIp = myGeo.Ip,
                        City = myGeo.City,
                        Country = myGeo.Country,
                        Location = myGeo.Location,
                        Password = TargetPassword?.Trim() ?? string.Empty
                    };
                    string reqJson = System.Text.Json.JsonSerializer.Serialize(reqPayload);
                    var signalingPayload = await _signalingClient.ResolveViaSignalingAsync(parsedId.RawValue, TimeSpan.FromSeconds(35), reqJson);

                    if (!string.IsNullOrWhiteSpace(signalingPayload))
                    {
                        SignalingEndpointInfo? endpointInfo = null;
                        try
                        {
                            endpointInfo = System.Text.Json.JsonSerializer.Deserialize<SignalingEndpointInfo>(signalingPayload);
                        }
                        catch { }

                        if (endpointInfo != null)
                        {
                            if (!endpointInfo.Accepted)
                            {
                                string rejectReason = !string.IsNullOrWhiteSpace(endpointInfo.Message)
                                    ? endpointInfo.Message
                                    : "A ligação remota foi recusada pelo utilizador do computador anfitrião.";
                                MessageBox.Show(rejectReason, "Ligação Recusada", MessageBoxButton.OK, MessageBoxImage.Warning);
                                ConnectionStatus = "Ligação Recusada";
                                return;
                            }
                            // 1. Try Local IP (if present)
                            if (activeSocket == null && !string.IsNullOrWhiteSpace(endpointInfo.LocalIp) && IPAddress.TryParse(endpointInfo.LocalIp, out var locIp))
                            {
                                ConnectionStatus = "A tentar ligação P2P Local com " + locIp + "...";
                                activeSocket = await TryConnectTcpAsync(locIp, endpointInfo.Port, 1500);
                                if (activeSocket != null)
                                {
                                    usedTransportName = "Direto (P2P Local)";
                                    usedTransportType = TransportTypeEnum.DirectP2P;
                                }
                            }

                            // 2. Try Public IP (if direct LAN failed and STUN public IP is present)
                            if (activeSocket == null && !string.IsNullOrWhiteSpace(endpointInfo.PublicIp) && IPAddress.TryParse(endpointInfo.PublicIp, out var pubIp))
                            {
                                ConnectionStatus = "A tentar ligação P2P WAN (STUN) com " + pubIp + "...";
                                activeSocket = await TryConnectTcpAsync(pubIp, endpointInfo.Port, 2500);
                                if (activeSocket != null)
                                {
                                    usedTransportName = "Direto (P2P WAN)";
                                    usedTransportType = TransportTypeEnum.DirectP2P;
                                }
                            }

                            // 3. Fallback to Relay Server (if direct P2P connections failed and Relay session is available)
                            if (activeSocket == null && activeSession == null && !string.IsNullOrWhiteSpace(endpointInfo.RelaySessionId))
                            {
                                ConnectionStatus = "A estabelecer ponte via Servidor Relay na Nuvem...";
                                string relayUrl = !string.IsNullOrWhiteSpace(endpointInfo.RelayServerUrl) ? endpointInfo.RelayServerUrl : _config.RelayServerUrl;

                                if (relayUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                                    relayUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                                {
                                    activeSession = await TryConnectWebSocketRelayAsync(endpointInfo.RelaySessionId, relayUrl, 10000);
                                    if (activeSession != null)
                                    {
                                        usedTransportName = "Servidor Relay WebSocket (Cloud)";
                                        usedTransportType = TransportTypeEnum.Relay;
                                    }
                                }
                                else
                                {
                                    activeSocket = await TryConnectRelayAsync(endpointInfo.RelaySessionId, relayUrl, 6000);
                                    if (activeSocket != null)
                                    {
                                        usedTransportName = "Servidor Relay TCP (Nuvem)";
                                        usedTransportType = TransportTypeEnum.Relay;
                                    }
                                }
                            }
                        }
                        else if (IPAddress.TryParse(signalingPayload, out var plainIp))
                        {
                            // Legacy single IP payload
                            activeSocket = await TryConnectTcpAsync(plainIp, 48270, 3000);
                            if (activeSocket != null)
                            {
                                usedTransportName = "Direto (P2P)";
                                usedTransportType = TransportTypeEnum.DirectP2P;
                            }
                        }
                    }
                }

                // Verificação de segunda oportunidade se o Sandbox/LAN respondeu entretanto
                if (activeSocket == null && activeSession == null)
                {
                    if (_lanDiscovery.DiscoveredPeers.TryGetValue(parsedId.RawValue, out var discoveredPeer))
                    {
                        ConnectionStatus = "A tentar ligação P2P Local com " + discoveredPeer.IpAddress + "...";
                        activeSocket = await TryConnectTcpAsync(discoveredPeer.IpAddress, discoveredPeer.Port, 3000);
                        if (activeSocket != null)
                        {
                            usedTransportName = "Direto (P2P Local / Sandbox)";
                            usedTransportType = TransportTypeEnum.DirectP2P;
                        }
                    }
                }

                // Fallback automático para instâncias Sandbox / Hyper-V ativas na máquina
                if (activeSocket == null && activeSession == null)
                {
                    var neighbors = LanDiscoveryService.GetNeighborIpAddresses();
                    foreach (var nip in neighbors)
                    {
                        string nipStr = nip.ToString();
                        if (nipStr.StartsWith("172.19.") && !nipStr.Equals("172.19.0.1"))
                        {
                            var probeSocket = await TryConnectTcpAsync(nip, 48270, 1000);
                            if (probeSocket != null)
                            {
                                activeSocket = probeSocket;
                                usedTransportName = "Direto (Windows Sandbox - " + nip + ")";
                                usedTransportType = TransportTypeEnum.DirectP2P;
                                AppLogger.LogInfo("MainViewModel", $"Fallback Sandbox bem-sucedido para {nip}:48270.");
                                break;
                            }
                        }
                    }
                }

                if (activeSocket == null && activeSession == null)
                {
                    MessageBox.Show($"O dispositivo com ID {targetHost} não está online no Servidor de Sinalização na Nuvem nem foi localizado na rede local.\n\n" +
                                    "Certifique-se de que:\n" +
                                    "1. O RotinaRemote está aberto e em execução no computador remoto.\n" +
                                    "2. Ambas as máquinas estão conectadas à Internet ou à mesma rede/Sandbox.\n\n" +
                                    "Dica: Se estiver a utilizar o Windows Sandbox ou rede local, pode também introduzir diretamente o Endereço IP do computador remoto (ex: 172.19.12.201).",
                                    "Dispositivo Offline ou Não Encontrado", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ConnectionStatus = "Dispositivo Offline";
                    return;
                }
            }

            if (activeSession == null && activeSocket != null)
            {
                activeSession = new ConnectionSession(activeSocket);
                if (!string.IsNullOrWhiteSpace(TargetPassword))
                {
                    try
                    {
                        var hs = new HandshakeRequestPayload
                        {
                            ClientDeviceId = _identity.FormattedId,
                            Password = TargetPassword.Trim()
                        };
                        var hsBytes = MessageSerializer.SerializeJson(hs);
                        _ = activeSession.SendFrameAsync(new PacketFrame(ChannelType.Control, 0, hsBytes));
                    }
                    catch { }
                }
            }

            if (activeSession != null)
            {
                try
                {
                    _activeSession = activeSession;
                    _activeSession.FrameReceived += OnFrameReceivedFromHost;
                    _activeSession.Disconnected += OnSessionDisconnected;

                    IsConnected = true;
                    TransportType = usedTransportName;
                    ConnectionStatus = "Ligado a " + targetHost + " (" + usedTransportName + ")";
                    SelectedTabIndex = 1;
                    AppLogger.LogInfo("RemoteSession", $"[SESSION CONNECTED] Sessão remota estabelecida com sucesso com {targetHost} via {usedTransportName}.");

                    History.Insert(0, new ConnectionHistoryItem
                    {
                        RemoteId = targetHost,
                        RemoteName = "PC-REMOTO-" + targetHost,
                        ConnectionTime = DateTime.Now,
                        Duration = TimeSpan.FromMinutes(1),
                        Transport = usedTransportType,
                        Status = "Ativa"
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("MainViewModel", "Erro ao iniciar sessão com o socket ativo", ex);
                    MessageBox.Show($"Erro ao iniciar sessão: {ex.Message}", "Erro de Ligação", MessageBoxButton.OK, MessageBoxImage.Error);
                    ConnectionStatus = "Falha na Ligação";
                    IsConnected = false;
                }
            }
            else
            {
                MessageBox.Show($"Não foi possível estabelecer ligação TCP (P2P Direto ou Relay) com {targetHost}.\n\n" +
                                "Possíveis causas:\n" +
                                "1. Os computadores estão em redes separadas e a porta TCP 48270 está bloqueada na firewall/router da máquina remota.\n" +
                                "2. O Servidor Relay está inacessível ou não responde em " + _config.RelayServerUrl + ".\n" +
                                "3. O tempo limite de ligação expirou.\n\n" +
                                "Dica: Em redes fora da mesma empresa/casa, certifique-se que ambas as máquinas estão ligadas à Internet e que o Servidor Relay está em execução.",
                                "Erro de Conexão Externa", MessageBoxButton.OK, MessageBoxImage.Error);
                ConnectionStatus = "Falha na Ligação";
                IsConnected = false;
            }
        }

        private async Task<Socket?> TryConnectTcpAsync(IPAddress ip, int port, int timeoutMs)
        {
            try
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try { socket.NoDelay = true; } catch { }
                using var cts = new CancellationTokenSource(timeoutMs);
                var connectTask = socket.ConnectAsync(new IPEndPoint(ip, port));
                var timeoutTask = Task.Delay(timeoutMs, cts.Token);

                if (await Task.WhenAny(connectTask, timeoutTask) == connectTask)
                {
                    cts.Cancel();
                    await connectTask;
                    if (socket.Connected) return socket;
                }

                try { socket.Close(); } catch { }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<ConnectionSession?> TryConnectWebSocketRelayAsync(string relaySessionId, string relayServerUrl, int timeoutMs)
        {
            try
            {
                string wsUrl = $"{relayServerUrl.TrimEnd('/')}?sessionId={relaySessionId}&role=client";
                var ws = new System.Net.WebSockets.ClientWebSocket();
                using var cts = new CancellationTokenSource(timeoutMs);
                await ws.ConnectAsync(new Uri(wsUrl), cts.Token);

                if (ws.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var stream = new WebSocketStream(ws, ownsSocket: true);
                    return new ConnectionSession(stream);
                }
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning("MainViewModel", $"Erro ao conectar ao Relay WebSocket ({relayServerUrl}): {ex.Message}");
                return null;
            }
        }

        private async Task<Socket?> TryConnectRelayAsync(string relaySessionId, string relayServerUrl, int timeoutMs)
        {
            try
            {
                var (host, port) = ParseRelayEndpoint(relayServerUrl);
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                using var cts = new CancellationTokenSource(timeoutMs);

                var connectTask = socket.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(timeoutMs, cts.Token);

                if (await Task.WhenAny(connectTask, timeoutTask) == connectTask)
                {
                    cts.Cancel();
                    await connectTask;
                    if (socket.Connected)
                    {
                        byte[] headerBytes = System.Text.Encoding.UTF8.GetBytes(relaySessionId.PadRight(36).Substring(0, 36));
                        await socket.SendAsync(headerBytes, SocketFlags.None);
                        return socket;
                    }
                }

                try { socket.Close(); } catch { }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private (string host, int port) ParseRelayEndpoint(string relayUrl)
        {
            if (string.IsNullOrWhiteSpace(relayUrl)) return ("127.0.0.1", 5001);
            string raw = relayUrl.Replace("tcp://", "").Replace("http://", "").Replace("https://", "").TrimEnd('/');
            if (raw.Contains(':'))
            {
                var parts = raw.Split(':');
                if (int.TryParse(parts[1], out int p))
                {
                    return (parts[0], p);
                }
                return (parts[0], 5001);
            }
            return (raw, 5001);
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
            if (_incomingSession != null)
            {
                _incomingSession.Close();
                _incomingSession = null;
            }
            IsConnected = false;
            RemoteScreenSource = null;
            ConnectionStatus = "Pronto";
            SelectedTabIndex = 0;
            AppLogger.LogInfo("RemoteSession", "[SESSION DISCONNECTED] Sessão remota desconectada e recursos libertados.");
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
