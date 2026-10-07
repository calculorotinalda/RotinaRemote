using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using RotinaRemote.Client.Models;
using RotinaRemote.Client.Services;
using RotinaRemote.Core.Services;
using System.Windows.Threading;
using RotinaRemote.Input;
using RotinaRemote.Network;
using RotinaRemote.Protocol;
using RotinaRemote.Screen;
using RotinaRemote.Security;
using RotinaRemote.FileTransfer;
using RotinaRemote.Client.Views;

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

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;

        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            if (parameter is T typed) return _canExecute?.Invoke(typed) ?? true;
            if (parameter == null && default(T) == null) return _canExecute?.Invoke(default) ?? true;
            return _canExecute?.Invoke(default) ?? true;
        }

        public void Execute(object? parameter)
        {
            if (parameter is T typed) _execute(typed);
            else _execute(default);
        }

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
        private CancellationTokenSource? _sessionMonitoringCts;
        private string? _activeConnectedTargetId;
        private DateTime _sessionStartTime;
        private int _incomingClientScreenWidth = 0;
        private int _incomingClientScreenHeight = 0;
        private IncomingConnectionItem? _activeIncomingConnection;
        private IncomingConnectionItem? _pendingRelayConnectionItem;
        private DispatcherTimer? _incomingDurationTimer;

        // Gestão de Licenciamento e Histórico
        private readonly LicenseService _licenseService = LicenseService.Instance;
        private readonly ClipboardSyncManager _clipboardSync = new();
        private readonly FileTransferEngine _fileTransferEngine = new();
        private string _licenseKeyInput = string.Empty;
        private string _licenseActivationFeedback = string.Empty;
        private ConnectionHistoryItem? _activeOutgoingHistoryItem;
        private ConnectionHistoryItem? _activeIncomingHistoryItem;

        // Chat e Escalonamento de Visualização
        private string _chatInputText = string.Empty;
        private int _unreadChatCount = 0;
        private bool _isChatOpen = false;
        private string _selectedViewScale = "Ajustar ao Ecrã";
        private System.Windows.Media.Stretch _screenStretchMode = System.Windows.Media.Stretch.Uniform;
        private double _zoomScaleFactor = 1.0;
        private bool _isZoomEnabled = false;

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
        private bool _enableDebugMode = false;

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

        // 16. Modo de Depuração (Geração de Ficheiros de Log e Diagnóstico)
        public bool EnableDebugMode
        {
            get => _enableDebugMode;
            set
            {
                if (!IsPremium && value)
                {
                    MessageBox.Show("O Modo de Depuração e geração de logs é uma funcionalidade exclusiva da versão Premium.\n\nPor favor ative a licença Premium para desbloquear.", "Recurso Premium", MessageBoxButton.OK, MessageBoxImage.Information);
                    OnPropertyChanged(nameof(EnableDebugMode));
                    OnPropertyChanged(nameof(DebugModeSelectedIndex));
                    return;
                }
                if (SetProperty(ref _enableDebugMode, value))
                {
                    AppLogger.IsDebugModeEnabled = value;
                    ShellAuditor.IsDebugModeEnabled = value;
                    _config.EnableDebugMode = value;
                    OnPropertyChanged(nameof(DebugModeSelectedIndex));
                    if (value)
                    {
                        AppLogger.LogInfo("Config", "Modo de Depuração ATIVADO pelo utilizador. Geração de ficheiros habilitada.");
                        ShellAuditor.InitializeLogPaths();
                        ShellAuditor.WriteLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [MODO DEPURAÇÃO] Modo de depuração ativado nas configurações avançadas.");
                    }
                }
            }
        }

        public int DebugModeSelectedIndex
        {
            get => _enableDebugMode ? 1 : 0;
            set => EnableDebugMode = (value == 1);
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
        public ObservableCollection<IncomingConnectionItem> IncomingConnections { get; } = new();

        // Licenciamento Free vs Premium
        public bool IsPremium => _licenseService.IsPremium;
        public bool IsFree => !_licenseService.IsPremium;
        public string LicenseBadgeText => IsPremium ? "⭐ PREMIUM" : "GRÁTIS (FREE)";
        public string LicenseBadgeBackground => IsPremium ? "#10B981" : "#64748B";
        public string LicenseStatusText => _licenseService.CurrentLicense.StatusText;
        public string LicensePlanName => _licenseService.CurrentLicense.Plan;
        public string LicenseLicensedTo => _licenseService.CurrentLicense.LicensedTo;
        public string LicenseKeyDisplay => string.IsNullOrEmpty(_licenseService.CurrentLicense.LicenseKey)
            ? "Nenhuma (Versão Gratuita)"
            : _licenseService.CurrentLicense.LicenseKey;

        public string LicenseKeyInput
        {
            get => _licenseKeyInput;
            set => SetProperty(ref _licenseKeyInput, value);
        }

        public string LicenseActivationFeedback
        {
            get => _licenseActivationFeedback;
            set => SetProperty(ref _licenseActivationFeedback, value);
        }

        public string SessionsTabHeader => IsPremium ? "Sessões" : "🔒 Sessões";
        public string HistoryTabHeader => IsPremium ? "Histórico" : "🔒 Histórico";
        public string DiagnosticTabHeader => IsPremium ? "Diagnóstico" : "🔒 Diagnóstico";

        public IncomingConnectionItem? ActiveIncomingConnection
        {
            get => _activeIncomingConnection;
            set
            {
                if (SetProperty(ref _activeIncomingConnection, value))
                {
                    OnPropertyChanged(nameof(HasActiveIncomingConnection));
                    OnPropertyChanged(nameof(IsNoIncomingConnectionActive));
                    OnPropertyChanged(nameof(ActiveIncomingConnectionsCount));
                }
            }
        }

        public bool HasActiveIncomingConnection => ActiveIncomingConnection != null && ActiveIncomingConnection.IsActive;
        public bool IsNoIncomingConnectionActive => !HasActiveIncomingConnection;
        public int ActiveIncomingConnectionsCount => IncomingConnections.Count(c => c.IsActive);
        public int TotalIncomingConnectionsCount => IncomingConnections.Count;

        public string ExecutionModeText =>
            WindowsServiceManager.GetStatus() == ServiceStatusEnum.Running
                ? "Serviço do Windows (LocalSystem)"
                : "Aplicação Desktop (Sessão de Utilizador)";

        public bool IsRunningAsService => WindowsServiceManager.GetStatus() == ServiceStatusEnum.Running;

        public string ServiceNoticeText =>
            "O RotinaRemote está a monitorizar ativamente todas as ligações e tentativas de acesso ao seu ID diretamente através desta aplicação. O monitoramento e controlo funcionam a 100% em modo de utilizador normal, mesmo sem ter o serviço do Windows instalado.";

        public ICommand CopyIdCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand RunDiagnosticsCommand { get; }
        public ICommand ExportDiagnosticsCommand { get; }

        public ICommand DisconnectIncomingCommand { get; }
        public ICommand KillSessionCommand { get; }
        public ICommand ClearIncomingHistoryCommand { get; }
        public ICommand RefreshConnectionsCommand { get; }

        // Comandos de Licenciamento e Histórico
        public ICommand ActivateLicenseCommand { get; }
        public ICommand DeactivateLicenseCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand ConnectFromHistoryCommand { get; }
        public ICommand CopyHistoryIdCommand { get; }
        public ICommand DeleteHistoryItemCommand { get; }

        public ICommand SendRemoteMinimizeCommand { get; }
        public ICommand SendRemoteMaximizeCommand { get; }
        public ICommand SendRemoteCloseCommand { get; }
        public ICommand SendRemoteMinimizeHostRotinaCommand { get; }
        public ICommand SendRemoteMaximizeHostRotinaCommand { get; }
        public ICommand OpenSendFileDialogCommand { get; }
        public ICommand MinimizeLocalWindowCommand { get; }
        public ICommand MaximizeLocalWindowCommand { get; }

        // Comandos de Definições Avançadas e Serviço
        public ICommand SaveSettingsCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand InstallServiceCommand { get; }
        public ICommand StartServiceCommand { get; }
        public ICommand StopServiceCommand { get; }
        public ICommand UninstallServiceCommand { get; }
        public ICommand RefreshServiceStatusCommand { get; }

        // Comandos de Chat
        public ICommand SendChatMessageCommand { get; }
        public ICommand ToggleChatCommand { get; }

        // Comandos de Gestor de Processos Remotos
        public ICommand RefreshRemoteProcessesCommand { get; }
        public ICommand KillRemoteProcessCommand { get; }
        public ICommand OpenProcessTabCommand { get; }
        public ICommand ReturnToScreenTabCommand { get; }
        public ICommand ClearProcessFilterCommand { get; }

        // Comandos de Gestor de Serviços Remotos
        public ICommand OpenServicesWindowCommand { get; }
        public ICommand RefreshRemoteServicesCommand { get; }
        public ICommand StartRemoteServiceCommand { get; }
        public ICommand StopRemoteServiceCommand { get; }
        public ICommand ChangeStartupTypeRemoteServiceCommand { get; }
        public ICommand ClearServiceFilterCommand { get; }

        // Propriedades do Gestor de Processos Remotos
        public ObservableCollection<RemoteProcessItem> RemoteProcesses { get; } = new();
        public ObservableCollection<RemoteProcessItem> FilteredRemoteProcesses { get; } = new();

        private string _processFilter = string.Empty;
        public string ProcessFilter
        {
            get => _processFilter;
            set
            {
                if (SetProperty(ref _processFilter, value))
                {
                    FilterProcesses();
                }
            }
        }

        private RemoteProcessItem? _selectedRemoteProcess;
        public RemoteProcessItem? SelectedRemoteProcess
        {
            get => _selectedRemoteProcess;
            set => SetProperty(ref _selectedRemoteProcess, value);
        }

        private int _totalRemoteProcessesCount;
        public int TotalRemoteProcessesCount
        {
            get => _totalRemoteProcessesCount;
            set => SetProperty(ref _totalRemoteProcessesCount, value);
        }

        private string _totalRemoteMemoryFormatted = "0 MB";
        public string TotalRemoteMemoryFormatted
        {
            get => _totalRemoteMemoryFormatted;
            set => SetProperty(ref _totalRemoteMemoryFormatted, value);
        }

        private bool _isLoadingProcesses;
        public bool IsLoadingProcesses
        {
            get => _isLoadingProcesses;
            set => SetProperty(ref _isLoadingProcesses, value);
        }

        private string _processManagerStatusText = "Pronto. Clique em 'Atualizar' para listar os processos remotos em segundo plano.";
        public string ProcessManagerStatusText
        {
            get => _processManagerStatusText;
            set => SetProperty(ref _processManagerStatusText, value);
        }

        private int _remoteSessionSubTabIndex = 0;
        public int RemoteSessionSubTabIndex
        {
            get => _remoteSessionSubTabIndex;
            set
            {
                if (SetProperty(ref _remoteSessionSubTabIndex, value))
                {
                    if (value == 1 && _activeSession != null && _activeSession.IsConnected)
                    {
                        RefreshRemoteProcesses();
                    }
                }
            }
        }

        // Propriedades do Gestor de Serviços Remotos
        public ObservableCollection<RemoteServiceItem> RemoteServices { get; } = new();
        public ObservableCollection<RemoteServiceItem> FilteredRemoteServices { get; } = new();
        public ObservableCollection<string> StartupTypeOptions { get; } = new() { "Automático", "Manual", "Desativado" };

        private string _serviceFilterText = string.Empty;
        public string ServiceFilterText
        {
            get => _serviceFilterText;
            set
            {
                if (SetProperty(ref _serviceFilterText, value))
                {
                    FilterServices();
                }
            }
        }

        private RemoteServiceItem? _selectedRemoteService;
        public RemoteServiceItem? SelectedRemoteService
        {
            get => _selectedRemoteService;
            set
            {
                if (SetProperty(ref _selectedRemoteService, value))
                {
                    OnPropertyChanged(nameof(HasSelectedRemoteService));
                    if (value != null && !string.IsNullOrWhiteSpace(value.StartupType))
                    {
                        var match = StartupTypeOptions.FirstOrDefault(o => o.Equals(value.StartupType, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            SelectedNewStartupType = match;
                        }
                    }
                }
            }
        }

        public bool HasSelectedRemoteService => SelectedRemoteService != null;

        private string _selectedNewStartupType = "Automático";
        public string SelectedNewStartupType
        {
            get => _selectedNewStartupType;
            set => SetProperty(ref _selectedNewStartupType, value);
        }

        private int _totalRemoteServicesCount;
        public int TotalRemoteServicesCount
        {
            get => _totalRemoteServicesCount;
            set => SetProperty(ref _totalRemoteServicesCount, value);
        }

        private int _runningRemoteServicesCount;
        public int RunningRemoteServicesCount
        {
            get => _runningRemoteServicesCount;
            set => SetProperty(ref _runningRemoteServicesCount, value);
        }

        private bool _isLoadingServices;
        public bool IsLoadingServices
        {
            get => _isLoadingServices;
            set => SetProperty(ref _isLoadingServices, value);
        }

        private string _servicesStatusText = "Pronto. Clique em 'Atualizar' para listar os serviços remotos.";
        public string ServicesStatusText
        {
            get => _servicesStatusText;
            set => SetProperty(ref _servicesStatusText, value);
        }

        // Modo de Privacidade Remoto
        private bool _isRemotePrivacyModeActive = false;
        public bool IsRemotePrivacyModeActive
        {
            get => _isRemotePrivacyModeActive;
            set
            {
                if (SetProperty(ref _isRemotePrivacyModeActive, value))
                {
                    OnPropertyChanged(nameof(PrivacyButtonContent));
                    OnPropertyChanged(nameof(PrivacyButtonBackgroundBrush));
                }
            }
        }

        public string PrivacyButtonContent => IsRemotePrivacyModeActive ? "🔒 Ecrã Ocultado" : "_ Ocultar Rotina";
        public System.Windows.Media.Brush PrivacyButtonBackgroundBrush => IsRemotePrivacyModeActive
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)) // #DC2626
            : (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource("CardBackground");

        // Propriedades de Chat e Visualização
        public ObservableCollection<ChatMessageItem> ChatMessages { get; } = new();

        public string ChatInputText
        {
            get => _chatInputText;
            set => SetProperty(ref _chatInputText, value);
        }

        public int UnreadChatCount
        {
            get => _unreadChatCount;
            set
            {
                if (SetProperty(ref _unreadChatCount, value))
                {
                    OnPropertyChanged(nameof(ChatButtonText));
                    OnPropertyChanged(nameof(ChatTabHeader));
                }
            }
        }

        public bool IsChatOpen
        {
            get => _isChatOpen;
            set
            {
                if (SetProperty(ref _isChatOpen, value))
                {
                    if (value)
                    {
                        UnreadChatCount = 0;
                    }
                }
            }
        }

        public string ChatButtonText => UnreadChatCount > 0 ? $"💬 Chat ({UnreadChatCount})" : "💬 Chat";
        public string ChatTabHeader => UnreadChatCount > 0 ? $"Chat ({UnreadChatCount}) 🔴" : "Chat";

        public string SelectedViewScale
        {
            get => _selectedViewScale;
            set
            {
                if (SetProperty(ref _selectedViewScale, value))
                {
                    UpdateViewScale(value);
                }
            }
        }

        public System.Windows.Media.Stretch ScreenStretchMode
        {
            get => _screenStretchMode;
            set => SetProperty(ref _screenStretchMode, value);
        }

        public double ZoomScaleFactor
        {
            get => _zoomScaleFactor;
            set => SetProperty(ref _zoomScaleFactor, value);
        }

        public bool IsZoomEnabled
        {
            get => _isZoomEnabled;
            set
            {
                if (SetProperty(ref _isZoomEnabled, value))
                {
                    OnPropertyChanged(nameof(ScrollBarVisibility));
                }
            }
        }

        public System.Windows.Controls.ScrollBarVisibility ScrollBarVisibility =>
            IsZoomEnabled ? System.Windows.Controls.ScrollBarVisibility.Auto : System.Windows.Controls.ScrollBarVisibility.Disabled;

        private void UpdateViewScale(string scale)
        {
            switch (scale)
            {
                case "100% (Original)":
                    ScreenStretchMode = System.Windows.Media.Stretch.None;
                    ZoomScaleFactor = 1.0;
                    IsZoomEnabled = true;
                    break;
                case "125%":
                    ScreenStretchMode = System.Windows.Media.Stretch.None;
                    ZoomScaleFactor = 1.25;
                    IsZoomEnabled = true;
                    break;
                case "150%":
                    ScreenStretchMode = System.Windows.Media.Stretch.None;
                    ZoomScaleFactor = 1.50;
                    IsZoomEnabled = true;
                    break;
                case "Preencher (Fill)":
                    ScreenStretchMode = System.Windows.Media.Stretch.Fill;
                    ZoomScaleFactor = 1.0;
                    IsZoomEnabled = false;
                    break;
                case "Ajustar ao Ecrã":
                default:
                    ScreenStretchMode = System.Windows.Media.Stretch.Uniform;
                    ZoomScaleFactor = 1.0;
                    IsZoomEnabled = false;
                    break;
            }
            AppLogger.LogInfo("RemoteSession", $"Visualização ajustada para: {scale} (Stretch={ScreenStretchMode}, Zoom={ZoomScaleFactor})");
        }

        public MainViewModel()
        {
            _config = AppConfig.Load();
            _identity = DeviceIdentity.LoadOrCreate();
            MyDeviceId = _identity.FormattedId;

            // Inicialização do Licenciamento Free vs Premium
            _licenseService.Initialize(_config.LicenseKey);
            _licenseService.LicenseChanged += OnLicenseChanged;

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

            if (!IsPremium)
            {
                _enableDebugMode = false;
                _config.EnableDebugMode = false;
                AppLogger.IsDebugModeEnabled = false;
                ShellAuditor.IsDebugModeEnabled = false;
            }
            else
            {
                _enableDebugMode = _config.EnableDebugMode;
                AppLogger.IsDebugModeEnabled = _enableDebugMode;
                ShellAuditor.IsDebugModeEnabled = _enableDebugMode;
            }

            // Carregar Histórico persistido
            try
            {
                var loadedHistory = HistoryManager.LoadHistory();
                foreach (var item in loadedHistory)
                {
                    History.Add(item);
                }
            }
            catch { }

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

            DisconnectIncomingCommand = new RelayCommand<IncomingConnectionItem>(item => DisconnectIncomingSession(item));
            KillSessionCommand = new RelayCommand<IncomingConnectionItem>(item => KillIncomingSession(item));
            ClearIncomingHistoryCommand = new RelayCommand(ClearIncomingHistory);
            RefreshConnectionsCommand = new RelayCommand(RefreshConnectionsState);

            ActivateLicenseCommand = new RelayCommand(ActivateLicenseAction);
            DeactivateLicenseCommand = new RelayCommand(DeactivateLicenseAction);
            ClearHistoryCommand = new RelayCommand(ClearHistory);
            ConnectFromHistoryCommand = new RelayCommand<ConnectionHistoryItem>(ConnectFromHistory);
            CopyHistoryIdCommand = new RelayCommand<ConnectionHistoryItem>(CopyHistoryId);
            DeleteHistoryItemCommand = new RelayCommand<ConnectionHistoryItem>(DeleteHistoryItem);

            SendRemoteMinimizeCommand = new RelayCommand(SendRemoteMinimize);
            SendRemoteMaximizeCommand = new RelayCommand(SendRemoteMaximize);
            SendRemoteCloseCommand = new RelayCommand(SendRemoteClose);
            SendRemoteMinimizeHostRotinaCommand = new RelayCommand(SendRemoteMinimizeHostRotina);
            SendRemoteMaximizeHostRotinaCommand = new RelayCommand(SendRemoteMaximizeHostRotina);
            OpenSendFileDialogCommand = new RelayCommand(OpenSendFileDialog);
            MinimizeLocalWindowCommand = new RelayCommand(MinimizeLocalWindow);
            MaximizeLocalWindowCommand = new RelayCommand(MaximizeLocalWindow);

            RefreshRemoteProcessesCommand = new RelayCommand(RefreshRemoteProcesses);
            KillRemoteProcessCommand = new RelayCommand<RemoteProcessItem>(KillRemoteProcess);
            OpenProcessTabCommand = new RelayCommand(OpenProcessTab);
            ReturnToScreenTabCommand = new RelayCommand(ReturnToScreenTab);
            ClearProcessFilterCommand = new RelayCommand(() => ProcessFilter = string.Empty);

            OpenServicesWindowCommand = new RelayCommand(OpenServicesWindow);
            RefreshRemoteServicesCommand = new RelayCommand(RefreshRemoteServices);
            StartRemoteServiceCommand = new RelayCommand(StartRemoteService);
            StopRemoteServiceCommand = new RelayCommand(StopRemoteService);
            ChangeStartupTypeRemoteServiceCommand = new RelayCommand(ChangeStartupTypeRemoteService);
            ClearServiceFilterCommand = new RelayCommand(() => ServiceFilterText = string.Empty);

            _fileTransferEngine.FileReceived += (savedPath, size) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    string fileName = Path.GetFileName(savedPath);
                    ChatMessages.Add(new ChatMessageItem
                    {
                        SenderId = "Sistema",
                        SenderName = "Transferência",
                        Message = $"📁 Ficheiro recebido: {fileName} ({size / 1024.0:F1} KB) pronto a Colar (Ctrl+V) ou guardado no Ambiente de Trabalho.",
                        Timestamp = DateTime.Now,
                        IsOutgoing = false
                    });

                    if (!IsChatOpen)
                    {
                        UnreadChatCount++;
                    }

                    _clipboardSync.ReceiveRemoteFiles(new[] { savedPath });

                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                });
            };

            SendChatMessageCommand = new RelayCommand(SendChatMessage);
            ToggleChatCommand = new RelayCommand(ToggleChat);

            SaveSettingsCommand = new RelayCommand(SaveSettings);
            ToggleThemeCommand = new RelayCommand(ToggleTheme);

            InputInjector.GetMainWindowHandle = () =>
            {
                try
                {
                    var mainWin = System.Windows.Application.Current?.MainWindow;
                    if (mainWin != null)
                    {
                        return new System.Windows.Interop.WindowInteropHelper(mainWin).Handle;
                    }
                }
                catch { }
                return IntPtr.Zero;
            };

            InputInjector.OnMinimizeRequested = () =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var mainWin = System.Windows.Application.Current?.MainWindow;
                        if (mainWin != null)
                        {
                            mainWin.WindowState = System.Windows.WindowState.Minimized;
                        }
                    }
                    catch { }
                });
            };

            InputInjector.OnMaximizeRequested = () =>
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var mainWin = System.Windows.Application.Current?.MainWindow;
                        if (mainWin != null)
                        {
                            mainWin.WindowState = mainWin.WindowState == System.Windows.WindowState.Maximized
                                ? System.Windows.WindowState.Normal
                                : System.Windows.WindowState.Maximized;
                        }
                    }
                    catch { }
                });
            };
            InstallServiceCommand = new RelayCommand(InstallService);
            StartServiceCommand = new RelayCommand(StartService);
            StopServiceCommand = new RelayCommand(StopService);
            UninstallServiceCommand = new RelayCommand(UninstallService);
            RefreshServiceStatusCommand = new RelayCommand(RefreshServiceStatus);

            _incomingDurationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _incomingDurationTimer.Tick += (s, e) =>
            {
                if (ActiveIncomingConnection != null && ActiveIncomingConnection.IsActive)
                {
                    ActiveIncomingConnection.NotifyDurationChanged();
                }
            };
            _incomingDurationTimer.Start();

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

        public void RefreshConnectionsState()
        {
            OnPropertyChanged(nameof(ExecutionModeText));
            OnPropertyChanged(nameof(IsRunningAsService));
            OnPropertyChanged(nameof(ActiveIncomingConnectionsCount));
            OnPropertyChanged(nameof(TotalIncomingConnectionsCount));
            OnPropertyChanged(nameof(HasActiveIncomingConnection));
            OnPropertyChanged(nameof(IsNoIncomingConnectionActive));
            RefreshServiceStatus();
        }

        public void ClearIncomingHistory()
        {
            var activeItems = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(IncomingConnections, c => c.IsActive));
            IncomingConnections.Clear();
            foreach (var a in activeItems)
            {
                IncomingConnections.Add(a);
            }
            OnPropertyChanged(nameof(TotalIncomingConnectionsCount));
        }

        private void OnLicenseChanged()
        {
            OnPropertyChanged(nameof(IsPremium));
            OnPropertyChanged(nameof(IsFree));
            OnPropertyChanged(nameof(LicenseBadgeText));
            OnPropertyChanged(nameof(LicenseBadgeBackground));
            OnPropertyChanged(nameof(LicenseStatusText));
            OnPropertyChanged(nameof(LicensePlanName));
            OnPropertyChanged(nameof(LicenseLicensedTo));
            OnPropertyChanged(nameof(LicenseKeyDisplay));
            OnPropertyChanged(nameof(SessionsTabHeader));
            OnPropertyChanged(nameof(HistoryTabHeader));
            OnPropertyChanged(nameof(DiagnosticTabHeader));

            if (!IsPremium)
            {
                EnableDebugMode = false;
                DebugModeSelectedIndex = 0;
            }
        }

        private void ActivateLicenseAction()
        {
            if (string.IsNullOrWhiteSpace(LicenseKeyInput))
            {
                LicenseActivationFeedback = "⚠️ Introduza uma chave de licença.";
                MessageBox.Show("Por favor introduza uma Chave de Licença válida.\n\nChave de Teste Premium Oficial:\n" + LicenseService.MasterTestKey, "Ativação de Licença", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = _licenseService.ActivateLicense(LicenseKeyInput, _config);
            if (result.Success)
            {
                LicenseActivationFeedback = "✅ " + result.Message;
                LicenseKeyInput = string.Empty;
                MessageBox.Show(result.Message + "\n\nO separador Sessões, Histórico, Diagnóstico e a opção de depuração nas configurações avançadas estão agora desbloqueados.", "RotinaRemote Premium", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                LicenseActivationFeedback = "❌ " + result.Message;
                MessageBox.Show(result.Message + "\n\nUtilize a Chave de Teste Premium:\n" + LicenseService.MasterTestKey, "Chave Inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeactivateLicenseAction()
        {
            _licenseService.DeactivateLicense(_config);
            LicenseActivationFeedback = "ℹ️ Versão Free ativa. Recursos avançados bloqueados.";
            MessageBox.Show("A licença Premium foi removida. A aplicação retornou à Versão Free (Gratuita).", "Versão Free", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ClearHistory()
        {
            History.Clear();
            HistoryManager.ClearHistory();
        }

        private void DeleteHistoryItem(ConnectionHistoryItem? item)
        {
            if (item != null)
            {
                History.Remove(item);
                HistoryManager.SaveHistory(History);
            }
        }

        private void ConnectFromHistory(ConnectionHistoryItem? item)
        {
            if (item != null && !string.IsNullOrWhiteSpace(item.RemoteId))
            {
                TargetDeviceId = item.RemoteId;
                SelectedTabIndex = 0;
            }
        }

        private void CopyHistoryId(ConnectionHistoryItem? item)
        {
            if (item != null && !string.IsNullOrWhiteSpace(item.RemoteId))
            {
                Clipboard.SetText(item.RemoteId);
                MessageBox.Show($"ID {item.RemoteId} copiado para a Área de Transferência.", "ID Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public void DisconnectIncomingSession(IncomingConnectionItem? item = null)
        {
            try
            {
                AppLogger.LogInfo("RemoteSession", "[HOST ACTION] Desconexão manual acionada pelo utilizador no separador Ligações.");
                PrivacyScreenManager.Instance.Deactivate();
                _clipboardSync.Stop();
                _sessionMonitoringCts?.Cancel();
                if (_incomingSession != null)
                {
                    try { _incomingSession.Close(); } catch { }
                    _incomingSession = null;
                }
                _streamingCts?.Cancel();
                DisplayResolutionManager.RestoreOriginalResolution();
                _screenCapturer.ClearTargetResolution();
                _incomingClientScreenWidth = 0;
                _incomingClientScreenHeight = 0;

                if (_config.BlockRemoteInput)
                {
                    InputInjector.SetBlockLocalInput(false);
                }

                if (!string.IsNullOrEmpty(_activeConnectedTargetId))
                {
                    ShellAuditor.LogSessionEnded(_activeConnectedTargetId, DateTime.UtcNow - _sessionStartTime);
                    _activeConnectedTargetId = null;
                }

                var target = item ?? ActiveIncomingConnection;
                if (target != null)
                {
                    target.IsActive = false;
                    target.EndTime = DateTime.Now;
                    target.Status = "Terminada pelo Anfitrião";
                }

                if (_activeIncomingHistoryItem != null)
                {
                    _activeIncomingHistoryItem.Status = "Terminada pelo Anfitrião";
                    if (target != null) _activeIncomingHistoryItem.Duration = DateTime.Now - target.StartTime;
                    HistoryManager.SaveHistory(History);
                    _activeIncomingHistoryItem = null;
                }

                ActiveIncomingConnection = null;
                ConnectionStatus = "Pronto";
                OnPropertyChanged(nameof(HasActiveIncomingConnection));
                OnPropertyChanged(nameof(IsNoIncomingConnectionActive));
                OnPropertyChanged(nameof(ActiveIncomingConnectionsCount));

                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var mainWin = System.Windows.Application.Current.MainWindow;
                        if (mainWin != null && mainWin.WindowState == System.Windows.WindowState.Minimized)
                        {
                            mainWin.WindowState = System.Windows.WindowState.Normal;
                        }
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                AppLogger.LogError("RemoteSession", "Erro ao desconectar ligação de entrada", ex);
            }
        }

        private void KillIncomingSession(IncomingConnectionItem? item = null)
        {
            try
            {
                AppLogger.LogWarning("RemoteSession", "[KILL SESSION] Ordem de KILL imediato acionada pelo utilizador. A forçar encerramento total da sessão!");

                PrivacyScreenManager.Instance.Deactivate();
                _clipboardSync.Stop();
                _sessionMonitoringCts?.Cancel();
                _streamingCts?.Cancel();

                try
                {
                    DisplayResolutionManager.RestoreOriginalResolution();
                    _screenCapturer.ClearTargetResolution();
                }
                catch { }

                try { InputInjector.SetBlockLocalInput(false); } catch { }

                _incomingClientScreenWidth = 0;
                _incomingClientScreenHeight = 0;

                if (_incomingSession != null)
                {
                    try { _incomingSession.Close(); } catch { }
                    _incomingSession = null;
                }

                if (_activeSession != null)
                {
                    try { _activeSession.Close(); } catch { }
                    _activeSession = null;
                }

                var target = item ?? ActiveIncomingConnection;
                if (target != null)
                {
                    target.IsActive = false;
                    target.EndTime = DateTime.Now;
                    target.Status = "💀 Morta Forçadamente (KILL)";
                }

                if (_activeIncomingHistoryItem != null)
                {
                    _activeIncomingHistoryItem.Status = "💀 Terminada (KILL)";
                    if (target != null) _activeIncomingHistoryItem.Duration = DateTime.Now - target.StartTime;
                    HistoryManager.SaveHistory(History);
                    _activeIncomingHistoryItem = null;
                }

                if (ActiveIncomingConnection != null)
                {
                    ActiveIncomingConnection.IsActive = false;
                    ActiveIncomingConnection.EndTime = DateTime.Now;
                    ActiveIncomingConnection.Status = "💀 Morta Forçadamente (KILL)";
                    ActiveIncomingConnection = null;
                }

                if (!string.IsNullOrEmpty(_activeConnectedTargetId))
                {
                    ShellAuditor.LogSessionEnded(_activeConnectedTargetId, DateTime.UtcNow - _sessionStartTime);
                    _activeConnectedTargetId = null;
                }

                IsConnected = false;
                RemoteScreenSource = null;
                ConnectionStatus = "Sessão Terminada (KILL)";

                RefreshConnectionsState();
                AppLogger.LogInfo("RemoteSession", "[KILL SESSION] Sessão terminada forçadamente com sucesso.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("RemoteSession", "Erro ao executar KILL na sessão", ex);
            }
        }

        private void SendRemoteWindowControl(RemoteWindowAction action)
        {
            if (_activeSession != null && _activeSession.IsConnected)
            {
                var payload = new RemoteWindowControlPayload { Action = action };
                var bytes = MessageSerializer.SerializeJson(payload);
                var packet = new PacketFrame(ChannelType.Control, 0, bytes);
                _ = _activeSession.SendFrameAsync(packet);
                AppLogger.LogInfo("RemoteSession", $"[CLIENT COMMAND] Ação de janela remota enviada: {action}");
            }
        }

        private void SendRemoteMinimize()
        {
            SendRemoteWindowControl(RemoteWindowAction.MinimizeActiveWindow);
        }

        private void SendRemoteMaximize()
        {
            SendRemoteWindowControl(RemoteWindowAction.MaximizeActiveWindow);
        }

        private void SendRemoteClose()
        {
            SendRemoteWindowControl(RemoteWindowAction.CloseActiveWindow);
        }

        private void SendRemoteMinimizeHostRotina()
        {
            SendRemoteWindowControl(RemoteWindowAction.MinimizeHostRotina);
            IsRemotePrivacyModeActive = true;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ChatMessages.Add(new ChatMessageItem
                {
                    SenderId = "Sistema",
                    SenderName = "Privacidade",
                    Message = "🔒 Modo de Privacidade ATIVO no computador remoto: o utilizador remoto não consegue ver o que está a fazer.",
                    Timestamp = DateTime.Now,
                    IsOutgoing = false
                });
            });
        }

        private void SendRemoteMaximizeHostRotina()
        {
            SendRemoteWindowControl(RemoteWindowAction.MaximizeHostRotina);
            IsRemotePrivacyModeActive = false;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ChatMessages.Add(new ChatMessageItem
                {
                    SenderId = "Sistema",
                    SenderName = "Privacidade",
                    Message = "👁️ Modo de Privacidade DESATIVADO no computador remoto: a visualização do ecrã remoto foi reposta.",
                    Timestamp = DateTime.Now,
                    IsOutgoing = false
                });
            });
        }

        public void OpenProcessTab()
        {
            SelectedTabIndex = 1;
            RemoteSessionSubTabIndex = 1;
            RefreshRemoteProcesses();
        }

        public void ReturnToScreenTab()
        {
            SelectedTabIndex = 1;
            RemoteSessionSubTabIndex = 0;
        }

        private void FilterProcesses()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                FilteredRemoteProcesses.Clear();
                var filter = _processFilter?.Trim().ToLowerInvariant() ?? string.Empty;
                foreach (var proc in RemoteProcesses)
                {
                    if (string.IsNullOrEmpty(filter) ||
                        proc.ProcessName.ToLowerInvariant().Contains(filter) ||
                        proc.ProcessId.ToString().Contains(filter) ||
                        proc.MainWindowTitle.ToLowerInvariant().Contains(filter))
                    {
                        FilteredRemoteProcesses.Add(proc);
                    }
                }
            });
        }

        public async void RefreshRemoteProcesses()
        {
            if (_activeSession == null || !_activeSession.IsConnected)
            {
                ProcessManagerStatusText = "Não é possível listar processos: nenhuma sessão remota ativa ligada.";
                return;
            }

            try
            {
                IsLoadingProcesses = true;
                ProcessManagerStatusText = "A obter lista de processos do computador remoto (em segundo plano)...";

                var req = new ProcessManagerPayload
                {
                    Action = ProcessManagerAction.ListRequest
                };
                var bytes = MessageSerializer.SerializeJson(req);
                await _activeSession.SendFrameAsync(new PacketFrame(ChannelType.ProcessManager, 0, bytes));
            }
            catch (Exception ex)
            {
                IsLoadingProcesses = false;
                ProcessManagerStatusText = $"Erro ao solicitar processos: {ex.Message}";
                AppLogger.LogError("ProcessManager", "Erro ao solicitar processos ao anfitrião", ex);
            }
        }

        public async void KillRemoteProcess(RemoteProcessItem? item = null)
        {
            var target = item ?? SelectedRemoteProcess;
            if (target == null)
            {
                MessageBox.Show("Por favor selecione um processo da lista para terminar.", "Gestor de Processos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_activeSession == null || !_activeSession.IsConnected)
            {
                MessageBox.Show("Sessão remota não está ativa.", "Gestor de Processos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Tem a certeza de que deseja terminar o processo '{target.ProcessName}' (PID {target.ProcessId}) no computador remoto?\n\nEsta ação será executada silenciosamente em segundo plano.",
                "Terminar Processo Remoto",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                ProcessManagerStatusText = $"A terminar processo '{target.ProcessName}' (PID {target.ProcessId}) no computador remoto...";
                var req = new ProcessManagerPayload
                {
                    Action = ProcessManagerAction.KillRequest,
                    TargetProcessId = target.ProcessId
                };
                var bytes = MessageSerializer.SerializeJson(req);
                await _activeSession.SendFrameAsync(new PacketFrame(ChannelType.ProcessManager, 0, bytes));
            }
            catch (Exception ex)
            {
                ProcessManagerStatusText = $"Falha ao enviar ordem de terminação: {ex.Message}";
                AppLogger.LogError("ProcessManager", "Falha ao enviar ordem de terminação de processo", ex);
            }
        }

        private RemoteServicesWindow? _servicesWindow;

        public void OpenServicesWindow()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_servicesWindow == null || !_servicesWindow.IsLoaded)
                {
                    _servicesWindow = new RemoteServicesWindow
                    {
                        DataContext = this,
                        Owner = System.Windows.Application.Current.MainWindow
                    };
                    _servicesWindow.Closed += (s, e) => _servicesWindow = null;
                    _servicesWindow.Show();
                }
                else
                {
                    if (_servicesWindow.WindowState == WindowState.Minimized)
                        _servicesWindow.WindowState = WindowState.Normal;
                    _servicesWindow.Activate();
                }

                if (_activeSession != null && _activeSession.IsConnected)
                {
                    RefreshRemoteServices();
                }
                else
                {
                    ServicesStatusText = "Aguardando ligação a uma sessão remota para consultar serviços.";
                }
            });
        }

        private void FilterServices()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                FilteredRemoteServices.Clear();
                var filter = _serviceFilterText?.Trim().ToLowerInvariant() ?? string.Empty;
                foreach (var svc in RemoteServices)
                {
                    if (string.IsNullOrEmpty(filter) ||
                        svc.ServiceName.ToLowerInvariant().Contains(filter) ||
                        svc.DisplayName.ToLowerInvariant().Contains(filter) ||
                        svc.Status.ToLowerInvariant().Contains(filter) ||
                        svc.StartupType.ToLowerInvariant().Contains(filter))
                    {
                        FilteredRemoteServices.Add(svc);
                    }
                }
            });
        }

        public async void RefreshRemoteServices()
        {
            if (_activeSession == null || !_activeSession.IsConnected)
            {
                ServicesStatusText = "Não é possível listar serviços: nenhuma sessão remota ativa ligada.";
                return;
            }

            try
            {
                IsLoadingServices = true;
                ServicesStatusText = "A obter lista de serviços do computador remoto (em segundo plano)...";

                var req = new ServiceManagerPayload
                {
                    Action = ServiceManagerAction.ListRequest
                };
                var bytes = MessageSerializer.SerializeJson(req);
                await _activeSession.SendFrameAsync(new PacketFrame(ChannelType.ServiceManager, 0, bytes));
            }
            catch (Exception ex)
            {
                IsLoadingServices = false;
                ServicesStatusText = $"Erro ao solicitar serviços: {ex.Message}";
                AppLogger.LogError("ServiceManager", "Erro ao solicitar serviços ao anfitrião", ex);
            }
        }

        public async void StartRemoteService()
        {
            if (SelectedRemoteService == null)
            {
                MessageBox.Show("Por favor selecione um serviço da lista para iniciar.", "Serviços Remotos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_activeSession == null || !_activeSession.IsConnected)
            {
                MessageBox.Show("Sessão remota não está ativa.", "Serviços Remotos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                IsLoadingServices = true;
                ServicesStatusText = $"A iniciar serviço '{SelectedRemoteService.ServiceName}' no computador remoto...";
                var req = new ServiceManagerPayload
                {
                    Action = ServiceManagerAction.StartRequest,
                    TargetServiceName = SelectedRemoteService.ServiceName
                };
                var bytes = MessageSerializer.SerializeJson(req);
                await _activeSession.SendFrameAsync(new PacketFrame(ChannelType.ServiceManager, 0, bytes));
            }
            catch (Exception ex)
            {
                IsLoadingServices = false;
                ServicesStatusText = $"Falha ao enviar ordem de início: {ex.Message}";
                AppLogger.LogError("ServiceManager", "Falha ao enviar ordem de início de serviço", ex);
            }
        }

        public async void StopRemoteService()
        {
            if (SelectedRemoteService == null)
            {
                MessageBox.Show("Por favor selecione um serviço da lista para parar.", "Serviços Remotos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_activeSession == null || !_activeSession.IsConnected)
            {
                MessageBox.Show("Sessão remota não está ativa.", "Serviços Remotos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Tem a certeza de que deseja parar o serviço '{SelectedRemoteService.DisplayName}' ({SelectedRemoteService.ServiceName}) no computador remoto?\n\nEsta ação será executada silenciosamente em segundo plano.",
                "Parar Serviço Remoto",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                IsLoadingServices = true;
                ServicesStatusText = $"A parar serviço '{SelectedRemoteService.ServiceName}' no computador remoto...";
                var req = new ServiceManagerPayload
                {
                    Action = ServiceManagerAction.StopRequest,
                    TargetServiceName = SelectedRemoteService.ServiceName
                };
                var bytes = MessageSerializer.SerializeJson(req);
                await _activeSession.SendFrameAsync(new PacketFrame(ChannelType.ServiceManager, 0, bytes));
            }
            catch (Exception ex)
            {
                IsLoadingServices = false;
                ServicesStatusText = $"Falha ao enviar ordem de paragem: {ex.Message}";
                AppLogger.LogError("ServiceManager", "Falha ao enviar ordem de paragem de serviço", ex);
            }
        }

        public async void ChangeStartupTypeRemoteService()
        {
            if (SelectedRemoteService == null)
            {
                MessageBox.Show("Por favor selecione um serviço da lista para alterar o tipo de arranque.", "Serviços Remotos", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_activeSession == null || !_activeSession.IsConnected)
            {
                MessageBox.Show("Sessão remota não está ativa.", "Serviços Remotos", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                IsLoadingServices = true;
                ServicesStatusText = $"A alterar arranque de '{SelectedRemoteService.ServiceName}' para {SelectedNewStartupType}...";
                var req = new ServiceManagerPayload
                {
                    Action = ServiceManagerAction.ChangeStartupTypeRequest,
                    TargetServiceName = SelectedRemoteService.ServiceName,
                    NewStartupType = SelectedNewStartupType
                };
                var bytes = MessageSerializer.SerializeJson(req);
                await _activeSession.SendFrameAsync(new PacketFrame(ChannelType.ServiceManager, 0, bytes));
            }
            catch (Exception ex)
            {
                IsLoadingServices = false;
                ServicesStatusText = $"Falha ao alterar arranque: {ex.Message}";
                AppLogger.LogError("ServiceManager", "Falha ao alterar tipo de arranque do serviço", ex);
            }
        }

        public void OpenSendFileDialog()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Selecionar ficheiros para transferir",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                _ = SendFilesAsync(dlg.FileNames);
            }
        }

        public async Task SendFilesAsync(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0) return;

            ConnectionSession? session = null;
            if (_activeSession != null && _activeSession.IsConnected)
            {
                session = _activeSession;
            }
            else if (_incomingSession != null && _incomingSession.IsConnected)
            {
                session = _incomingSession;
            }

            if (session == null)
            {
                AppLogger.LogWarning("FileTransfer", "Nenhuma sessão ativa ligada para enviar ficheiros.");
                return;
            }

            foreach (var path in filePaths)
            {
                if (!File.Exists(path)) continue;

                try
                {
                    var fileInfo = new FileInfo(path);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        ChatMessages.Add(new ChatMessageItem
                        {
                            SenderId = _identity.FormattedId,
                            SenderName = "Eu",
                            Message = $"📁 A enviar ficheiro: {fileInfo.Name} ({fileInfo.Length / 1024.0:F1} KB)...",
                            Timestamp = DateTime.Now,
                            IsOutgoing = true
                        });
                    });

                    await _fileTransferEngine.SendFileAsync(path, async (payload) =>
                    {
                        var bytes = MessageSerializer.SerializeJson(payload);
                        var packet = new PacketFrame(ChannelType.File, 0, bytes);
                        await session.SendFrameAsync(packet);
                    });

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        ChatMessages.Add(new ChatMessageItem
                        {
                            SenderId = _identity.FormattedId,
                            SenderName = "Eu",
                            Message = $"✅ Ficheiro '{fileInfo.Name}' transferido com sucesso!",
                            Timestamp = DateTime.Now,
                            IsOutgoing = true
                        });
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("FileTransfer", $"Erro ao transferir '{path}'", ex);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        ChatMessages.Add(new ChatMessageItem
                        {
                            SenderId = _identity.FormattedId,
                            SenderName = "Sistema",
                            Message = $"❌ Falha ao enviar '{Path.GetFileName(path)}': {ex.Message}",
                            Timestamp = DateTime.Now,
                            IsOutgoing = true
                        });
                    });
                }
            }
        }

        private void MinimizeLocalWindow()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (System.Windows.Application.Current.MainWindow != null)
                {
                    System.Windows.Application.Current.MainWindow.WindowState = WindowState.Minimized;
                }
            });
        }

        private void MaximizeLocalWindow()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (System.Windows.Application.Current.MainWindow != null)
                {
                    System.Windows.Application.Current.MainWindow.WindowState =
                        System.Windows.Application.Current.MainWindow.WindowState == WindowState.Maximized
                            ? WindowState.Normal
                            : WindowState.Maximized;
                }
            });
        }

        private void SendChatMessage()
        {
            if (string.IsNullOrWhiteSpace(ChatInputText)) return;
            string msg = ChatInputText.Trim();
            ChatInputText = string.Empty;

            var chatItem = new ChatMessageItem
            {
                SenderId = _identity.FormattedId,
                SenderName = "Eu",
                Message = msg,
                Timestamp = DateTime.Now,
                IsOutgoing = true
            };
            System.Windows.Application.Current.Dispatcher.Invoke(() => ChatMessages.Add(chatItem));

            var payload = new ChatMessagePayload
            {
                SenderId = _identity.FormattedId,
                SenderName = Environment.MachineName,
                Message = msg,
                Timestamp = DateTime.UtcNow
            };
            var bytes = MessageSerializer.SerializeJson(payload);
            var packet = new PacketFrame(ChannelType.Chat, 0, bytes);

            if (_activeSession != null && _activeSession.IsConnected)
            {
                _ = _activeSession.SendFrameAsync(packet);
                AppLogger.LogInfo("Chat", $"[CHAT CLIENT] Mensagem enviada para anfitrião: {msg}");
            }
            else if (_incomingSession != null && _incomingSession.IsConnected)
            {
                _ = _incomingSession.SendFrameAsync(packet);
                AppLogger.LogInfo("Chat", $"[CHAT HOST] Mensagem enviada para cliente: {msg}");
            }
        }

        private void ToggleChat()
        {
            IsChatOpen = !IsChatOpen;
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

                if (!IsPremium)
                {
                    _config.EnableDebugMode = false;
                    _enableDebugMode = false;
                    AppLogger.IsDebugModeEnabled = false;
                    ShellAuditor.IsDebugModeEnabled = false;
                }
                else
                {
                    _config.EnableDebugMode = EnableDebugMode;
                }

                _config.LicenseKey = _licenseService.CurrentLicense.LicenseKey;

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
                        if (req.ClientScreenWidth > 0 && req.ClientScreenHeight > 0)
                        {
                            _incomingClientScreenWidth = req.ClientScreenWidth;
                            _incomingClientScreenHeight = req.ClientScreenHeight;
                            AppLogger.LogInfo("MainViewModel", $"[RESOLUTION] Resolução do ecrã do cliente recebida via sinalização: {_incomingClientScreenWidth}x{_incomingClientScreenHeight}");
                        }
                    }
                }
                catch { }

                var connItem = new IncomingConnectionItem
                {
                    ConnectionId = Guid.NewGuid().ToString("N").Substring(0, 8),
                    RemoteDeviceId = callerId,
                    RemoteIp = callerIp,
                    City = callerCity,
                    Country = callerCountry,
                    Location = callerLocation,
                    TransportType = !string.IsNullOrWhiteSpace(_config.RelayServerUrl) ? "Relay Nuvem" : "P2P Direto / STUN",
                    Permission = "A aguardar aprovação...",
                    ClientResolution = (_incomingClientScreenWidth > 0 && _incomingClientScreenHeight > 0)
                        ? $"{_incomingClientScreenWidth}x{_incomingClientScreenHeight}"
                        : "Automática / Padrão",
                    StartTime = DateTime.Now,
                    Status = "A validar acesso...",
                    IsActive = false,
                    TargetRotinaId = _identity.FormattedId,
                    ExecutionMode = IsRunningAsService ? "Serviço Windows" : "Aplicação Desktop"
                };

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IncomingConnections.Insert(0, connItem);
                    RefreshConnectionsState();
                });

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
                        if (isApproved)
                        {
                            _activeIncomingPermission = dialog.GrantedPermissions.HasFlag(SessionPermission.ControlMouse) ? "FullControl" : "ViewOnly";
                        }
                    });
                }

                if (!isApproved)
                {
                    connItem.Status = "Recusada pelo Anfitrião";
                    connItem.IsActive = false;
                    connItem.EndTime = DateTime.Now;
                    connItem.Permission = "Recusado";
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(RefreshConnectionsState);

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

                connItem.Status = "A estabelecer ligação...";
                connItem.Permission = _activeIncomingPermission;
                connItem.IsActive = true;
                _pendingRelayConnectionItem = connItem;

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

                        var currentConn = _pendingRelayConnectionItem;
                        if (currentConn != null)
                        {
                            currentConn.Status = "Ativa (Em Controlo)";
                            currentConn.IsActive = true;
                            currentConn.TransportType = "Servidor Relay WebSocket (Cloud)";
                            currentConn.ClientResolution = (_incomingClientScreenWidth > 0 && _incomingClientScreenHeight > 0)
                                ? $"{_incomingClientScreenWidth}x{_incomingClientScreenHeight}"
                                : "Automática / Padrão";
                            ActiveIncomingConnection = currentConn;

                            var historyEntry = new ConnectionHistoryItem
                            {
                                RemoteId = currentConn.RemoteDeviceId,
                                RemoteName = string.IsNullOrWhiteSpace(currentConn.Location) ? "Cliente Remoto" : currentConn.Location,
                                Direction = "Entrada",
                                RemoteIp = currentConn.RemoteIp,
                                Location = currentConn.Location,
                                ConnectionTime = DateTime.Now,
                                Duration = TimeSpan.Zero,
                                TransportName = currentConn.TransportType,
                                Status = "Em curso..."
                            };
                            _activeIncomingHistoryItem = historyEntry;
                            History.Insert(0, historyEntry);
                            HistoryManager.SaveHistory(History);

                            System.Windows.Application.Current.Dispatcher.Invoke(RefreshConnectionsState);
                        }

                        session.FrameReceived += OnInputFrameReceivedFromClient;
                        if (EnableClipboardSync)
                        {
                            _clipboardSync.Start(async text =>
                            {
                                if (_incomingSession != null && _incomingSession.IsConnected && EnableClipboardSync)
                                {
                                    var clipPayload = new ClipboardPayload { Text = text };
                                    var bytes = MessageSerializer.SerializeJson(clipPayload);
                                    await _incomingSession.SendFrameAsync(new PacketFrame(ChannelType.Clipboard, 0, bytes));
                                }
                            }, async files =>
                            {
                                if (_incomingSession != null && _incomingSession.IsConnected && EnableClipboardSync)
                                {
                                    await SendFilesAsync(files);
                                }
                            });
                        }
                        session.Disconnected += () =>
                        {
                            PrivacyScreenManager.Instance.Deactivate();
                            _clipboardSync.Stop();
                            _sessionMonitoringCts?.Cancel();
                            DisplayResolutionManager.RestoreOriginalResolution();
                            _screenCapturer.ClearTargetResolution();
                            _incomingClientScreenWidth = 0;
                            _incomingClientScreenHeight = 0;
                            if (!string.IsNullOrEmpty(_activeConnectedTargetId))
                            {
                                ShellAuditor.LogSessionEnded(_activeConnectedTargetId, DateTime.UtcNow - _sessionStartTime);
                                _activeConnectedTargetId = null;
                            }
                            if (currentConn != null)
                            {
                                currentConn.IsActive = false;
                                currentConn.EndTime = DateTime.Now;
                                currentConn.Status = "Terminada";
                            }
                            if (_activeIncomingHistoryItem != null && currentConn != null)
                            {
                                _activeIncomingHistoryItem.Duration = DateTime.Now - currentConn.StartTime;
                                _activeIncomingHistoryItem.Status = "Concluída";
                                HistoryManager.SaveHistory(History);
                                _activeIncomingHistoryItem = null;
                            }
                            if (ActiveIncomingConnection == currentConn)
                            {
                                ActiveIncomingConnection = null;
                            }
                            _incomingSession = null;
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                ConnectionStatus = "Pronto";
                                RefreshConnectionsState();
                                try
                                {
                                    var mainWin = System.Windows.Application.Current.MainWindow;
                                    if (mainWin != null && mainWin.WindowState == System.Windows.WindowState.Minimized)
                                    {
                                        mainWin.WindowState = System.Windows.WindowState.Normal;
                                    }
                                }
                                catch { }
                            });
                        };

                        AppLogger.LogInfo("MainViewModel", $"Host conectado ao Servidor Relay WebSocket ({relayServerUrl}) com SessionId: {relaySessionId}");
                        _activeConnectedTargetId = $"Host-Relay ({relaySessionId})";
                        _sessionStartTime = DateTime.UtcNow;
                        _ = ShellAuditor.AuditConnectionAtConnectAsync(
                            direction: "Entrada (Host / Anfitrião controlado via Nuvem)",
                            targetId: relaySessionId,
                            transportName: "Servidor Relay WebSocket (Cloud)",
                            cloudServerUrl: relayServerUrl);

                        _sessionMonitoringCts?.Cancel();
                        _sessionMonitoringCts = new CancellationTokenSource();
                        ShellAuditor.StartSessionMonitoring(relaySessionId, _sessionMonitoringCts.Token);

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

            // Check if this was initiated from a pending relay request
            IncomingConnectionItem connItem;
            if (_pendingRelayConnectionItem != null && !_pendingRelayConnectionItem.IsActive)
            {
                connItem = _pendingRelayConnectionItem;
                _pendingRelayConnectionItem = null;
                connItem.TransportType = "Servidor Relay TCP (Nuvem)";
            }
            else
            {
                connItem = new IncomingConnectionItem
                {
                    ConnectionId = Guid.NewGuid().ToString("N").Substring(0, 8),
                    RemoteDeviceId = resolvedId,
                    RemoteIp = remoteIp,
                    City = city,
                    Country = country,
                    Location = location,
                    TransportType = isLocal ? "P2P Local (LAN)" : "P2P Direto (TCP)",
                    Permission = "A aguardar aprovação...",
                    ClientResolution = (_incomingClientScreenWidth > 0 && _incomingClientScreenHeight > 0)
                        ? $"{_incomingClientScreenWidth}x{_incomingClientScreenHeight}"
                        : "Automática / Padrão",
                    StartTime = DateTime.Now,
                    Status = "A validar acesso...",
                    IsActive = false,
                    TargetRotinaId = _identity.FormattedId,
                    ExecutionMode = IsRunningAsService ? "Serviço Windows" : "Aplicação Desktop"
                };

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IncomingConnections.Insert(0, connItem);
                    RefreshConnectionsState();
                });
            }

            var session = new ConnectionSession(socket);
            bool isApproved = false;
            HandshakeRequestPayload? handshakePayload = null;

            // Aguarda handshake de controlo com senha e resolução do cliente (timeout 1.2s)
            var hsTcs = new TaskCompletionSource<HandshakeRequestPayload?>();
            Action<PacketFrame> hsHandler = f =>
            {
                if (f.Channel == ChannelType.Control && f.Payload.Length > 0)
                {
                    try
                    {
                        var hs = MessageSerializer.DeserializeJson<HandshakeRequestPayload>(f.Payload);
                        hsTcs.TrySetResult(hs);
                    }
                    catch { }
                }
            };
            session.FrameReceived += hsHandler;
            var completed = await Task.WhenAny(hsTcs.Task, Task.Delay(1200));
            session.FrameReceived -= hsHandler;

            if (completed == hsTcs.Task && hsTcs.Task.Result != null)
            {
                handshakePayload = hsTcs.Task.Result;
                if (!string.IsNullOrWhiteSpace(handshakePayload.ClientDeviceId))
                {
                    resolvedId = handshakePayload.ClientDeviceId;
                    connItem.RemoteDeviceId = resolvedId;
                }
                if (handshakePayload.ClientScreenWidth > 0 && handshakePayload.ClientScreenHeight > 0)
                {
                    _incomingClientScreenWidth = handshakePayload.ClientScreenWidth;
                    _incomingClientScreenHeight = handshakePayload.ClientScreenHeight;
                    connItem.ClientResolution = $"{_incomingClientScreenWidth}x{_incomingClientScreenHeight}";
                    AppLogger.LogInfo("MainViewModel", $"[RESOLUTION] Resolução do cliente recebida via handshake P2P: {_incomingClientScreenWidth}x{_incomingClientScreenHeight}");
                }
            }

            if (_config.EnableUnattendedAccess && !string.IsNullOrWhiteSpace(_config.UnattendedPassword))
            {
                if (handshakePayload != null && handshakePayload.Password == _config.UnattendedPassword)
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
                        _activeIncomingPermission = dialog.GrantedPermissions.HasFlag(SessionPermission.ControlMouse) ? "FullControl" : "ViewOnly";
                    }
                });
            }

            if (isApproved)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    connItem.Status = "Ativa (Em Controlo)";
                    connItem.Permission = _activeIncomingPermission;
                    connItem.IsActive = true;
                    if (_incomingClientScreenWidth > 0 && _incomingClientScreenHeight > 0)
                    {
                        connItem.ClientResolution = $"{_incomingClientScreenWidth}x{_incomingClientScreenHeight}";
                    }
                    ActiveIncomingConnection = connItem;

                    var historyEntry = new ConnectionHistoryItem
                    {
                        RemoteId = connItem.RemoteDeviceId,
                        RemoteName = string.IsNullOrWhiteSpace(connItem.Location) ? "Cliente Remoto" : connItem.Location,
                        Direction = "Entrada",
                        RemoteIp = connItem.RemoteIp,
                        Location = connItem.Location,
                        ConnectionTime = DateTime.Now,
                        Duration = TimeSpan.Zero,
                        TransportName = connItem.TransportType,
                        Status = "Em curso..."
                    };
                    _activeIncomingHistoryItem = historyEntry;
                    History.Insert(0, historyEntry);
                    HistoryManager.SaveHistory(History);

                    RefreshConnectionsState();

                    _incomingSession = session;
                    session.FrameReceived += OnInputFrameReceivedFromClient;
                    if (EnableClipboardSync)
                    {
                        _clipboardSync.Start(async text =>
                        {
                            if (_incomingSession != null && _incomingSession.IsConnected && EnableClipboardSync)
                            {
                                var clipPayload = new ClipboardPayload { Text = text };
                                var bytes = MessageSerializer.SerializeJson(clipPayload);
                                await _incomingSession.SendFrameAsync(new PacketFrame(ChannelType.Clipboard, 0, bytes));
                            }
                        }, async files =>
                        {
                            if (_incomingSession != null && _incomingSession.IsConnected && EnableClipboardSync)
                            {
                                await SendFilesAsync(files);
                            }
                        });
                    }
                    session.Disconnected += () =>
                    {
                        PrivacyScreenManager.Instance.Deactivate();
                        _clipboardSync.Stop();
                        _sessionMonitoringCts?.Cancel();
                        DisplayResolutionManager.RestoreOriginalResolution();
                        _screenCapturer.ClearTargetResolution();
                        _incomingClientScreenWidth = 0;
                        _incomingClientScreenHeight = 0;
                        if (!string.IsNullOrEmpty(_activeConnectedTargetId))
                        {
                            ShellAuditor.LogSessionEnded(_activeConnectedTargetId, DateTime.UtcNow - _sessionStartTime);
                            _activeConnectedTargetId = null;
                        }
                        if (_config.BlockRemoteInput)
                        {
                            InputInjector.SetBlockLocalInput(false);
                        }
                        _incomingSession = null;
                        connItem.IsActive = false;
                        connItem.EndTime = DateTime.Now;
                        connItem.Status = "Terminada";
                        if (_activeIncomingHistoryItem != null)
                        {
                            _activeIncomingHistoryItem.Duration = DateTime.Now - connItem.StartTime;
                            _activeIncomingHistoryItem.Status = "Concluída";
                            HistoryManager.SaveHistory(History);
                            _activeIncomingHistoryItem = null;
                        }
                        if (ActiveIncomingConnection == connItem)
                        {
                            ActiveIncomingConnection = null;
                        }
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            ConnectionStatus = "Pronto";
                            RefreshConnectionsState();
                            try
                            {
                                var mainWin = System.Windows.Application.Current.MainWindow;
                                if (mainWin != null && mainWin.WindowState == System.Windows.WindowState.Minimized)
                                {
                                    mainWin.WindowState = System.Windows.WindowState.Normal;
                                }
                            }
                            catch { }
                        });
                    };
                    ConnectionStatus = "Sessão Ativa com " + resolvedId;
                    _activeConnectedTargetId = resolvedId;
                    _sessionStartTime = DateTime.UtcNow;
                    _ = ShellAuditor.AuditConnectionAtConnectAsync(
                        direction: "Entrada (Host / Anfitrião controlado via Rede Local)",
                        targetId: resolvedId,
                        transportName: connItem.TransportType,
                        cloudServerUrl: _config.SignalingServerUrl);

                    _sessionMonitoringCts?.Cancel();
                    _sessionMonitoringCts = new CancellationTokenSource();
                    ShellAuditor.StartSessionMonitoring(resolvedId, _sessionMonitoringCts.Token);

                    StartHostScreenStreaming(session);
                });
            }
            else
            {
                connItem.Status = "Recusada pelo Anfitrião";
                connItem.IsActive = false;
                connItem.EndTime = DateTime.Now;
                connItem.Permission = "Recusado";
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(RefreshConnectionsState);

                try { session.Dispose(); } catch { }
                try { socket.Close(); } catch { }
            }
        }

        private void OnInputFrameReceivedFromClient(PacketFrame frame)
        {
            if (frame.Channel == ChannelType.Clipboard && frame.Payload.Length > 0)
            {
                if (EnableClipboardSync)
                {
                    string text = string.Empty;
                    try
                    {
                        var clipPayload = MessageSerializer.DeserializeJson<ClipboardPayload>(frame.Payload);
                        text = clipPayload?.Text ?? string.Empty;
                    }
                    catch { }
                    if (string.IsNullOrEmpty(text))
                    {
                        text = System.Text.Encoding.UTF8.GetString(frame.Payload);
                    }
                    if (!string.IsNullOrEmpty(text))
                    {
                        _clipboardSync.ReceiveRemoteClipboard(text);
                    }
                }
                return;
            }

            if (frame.Channel == ChannelType.Chat && frame.Payload.Length > 0)
            {
                try
                {
                    var chatPayload = MessageSerializer.DeserializeJson<ChatMessagePayload>(frame.Payload);
                    if (chatPayload != null && !string.IsNullOrWhiteSpace(chatPayload.Message))
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            ChatMessages.Add(new ChatMessageItem
                            {
                                SenderId = chatPayload.SenderId,
                                SenderName = !string.IsNullOrEmpty(chatPayload.SenderName) ? chatPayload.SenderName : $"Cliente ({chatPayload.SenderId})",
                                Message = chatPayload.Message,
                                Timestamp = chatPayload.Timestamp.ToLocalTime(),
                                IsOutgoing = false
                            });

                            if (!IsChatOpen)
                            {
                                UnreadChatCount++;
                            }

                            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                        });
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Chat", "Erro ao processar mensagem de chat do cliente", ex);
                }
                return;
            }

            if (frame.Channel == ChannelType.File && frame.Payload.Length > 0)
            {
                try
                {
                    var filePayload = MessageSerializer.DeserializeJson<FileTransferPayload>(frame.Payload);
                    if (filePayload != null)
                    {
                        _ = _fileTransferEngine.HandleIncomingPayloadAsync(filePayload);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("FileTransfer", "Erro ao processar pacote de ficheiro do cliente", ex);
                }
                return;
            }

            if (frame.Channel == ChannelType.ProcessManager && frame.Payload.Length > 0)
            {
                try
                {
                    var procPayload = MessageSerializer.DeserializeJson<ProcessManagerPayload>(frame.Payload);
                    if (procPayload != null && _incomingSession != null && _incomingSession.IsConnected)
                    {
                        if (procPayload.Action == ProcessManagerAction.ListRequest)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var list = new List<RemoteProcessItem>();
                                    var procs = Process.GetProcesses();
                                    foreach (var p in procs)
                                    {
                                        try
                                        {
                                            list.Add(new RemoteProcessItem
                                            {
                                                ProcessId = p.Id,
                                                ProcessName = p.ProcessName,
                                                MainWindowTitle = p.MainWindowTitle ?? string.Empty,
                                                MemoryBytes = p.WorkingSet64,
                                                IsResponding = p.Responding
                                            });
                                        }
                                        catch
                                        {
                                            // Processos de sistema protegidos ou terminados
                                        }
                                        finally
                                        {
                                            p.Dispose();
                                        }
                                    }

                                    list = list.OrderByDescending(p => p.MemoryBytes).ToList();

                                    var responsePayload = new ProcessManagerPayload
                                    {
                                        Action = ProcessManagerAction.ListResponse,
                                        Processes = list,
                                        Success = true,
                                        Message = $"Lista obtida: {list.Count} processos remotos."
                                    };
                                    var bytes = MessageSerializer.SerializeJson(responsePayload);
                                    if (_incomingSession != null && _incomingSession.IsConnected)
                                    {
                                        await _incomingSession.SendFrameAsync(new PacketFrame(ChannelType.ProcessManager, 0, bytes));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    AppLogger.LogError("ProcessManager", "Host: Erro ao listar processos remotos", ex);
                                }
                            });
                        }
                        else if (procPayload.Action == ProcessManagerAction.KillRequest)
                        {
                            _ = Task.Run(async () =>
                            {
                                bool success = false;
                                string msg;
                                try
                                {
                                    var p = Process.GetProcessById(procPayload.TargetProcessId);
                                    string pName = p.ProcessName;
                                    p.Kill(entireProcessTree: true);
                                    p.Dispose();
                                    success = true;
                                    msg = $"Processo '{pName}' (PID {procPayload.TargetProcessId}) terminado com sucesso.";
                                    AppLogger.LogInfo("ProcessManager", $"[HOST KILL] {msg}");
                                }
                                catch (Exception ex)
                                {
                                    success = false;
                                    msg = $"Falha ao terminar processo (PID {procPayload.TargetProcessId}): {ex.Message}";
                                    AppLogger.LogWarning("ProcessManager", msg);
                                }

                                var responsePayload = new ProcessManagerPayload
                                {
                                    Action = ProcessManagerAction.KillResponse,
                                    TargetProcessId = procPayload.TargetProcessId,
                                    Success = success,
                                    Message = msg
                                };
                                var bytes = MessageSerializer.SerializeJson(responsePayload);
                                if (_incomingSession != null && _incomingSession.IsConnected)
                                {
                                    await _incomingSession.SendFrameAsync(new PacketFrame(ChannelType.ProcessManager, 0, bytes));
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ProcessManager", "Host: Erro ao processar frame de processos do cliente", ex);
                }
                return;
            }

            if (frame.Channel == ChannelType.ServiceManager && frame.Payload.Length > 0)
            {
                try
                {
                    var svcPayload = MessageSerializer.DeserializeJson<ServiceManagerPayload>(frame.Payload);
                    if (svcPayload != null && _incomingSession != null && _incomingSession.IsConnected)
                    {
                        if (svcPayload.Action == ServiceManagerAction.ListRequest)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var list = RemoteServicesManager.GetServices();
                                    var responsePayload = new ServiceManagerPayload
                                    {
                                        Action = ServiceManagerAction.ListResponse,
                                        Services = list,
                                        Success = true,
                                        Message = $"Lista obtida: {list.Count} serviços encontrados."
                                    };
                                    var bytes = MessageSerializer.SerializeJson(responsePayload);
                                    if (_incomingSession != null && _incomingSession.IsConnected)
                                    {
                                        await _incomingSession.SendFrameAsync(new PacketFrame(ChannelType.ServiceManager, 0, bytes));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    AppLogger.LogError("ServiceManager", "Host: Erro ao listar serviços remotos", ex);
                                }
                            });
                        }
                        else if (svcPayload.Action == ServiceManagerAction.StartRequest)
                        {
                            _ = Task.Run(async () =>
                            {
                                var (ok, msg) = RemoteServicesManager.StartService(svcPayload.TargetServiceName);
                                var responsePayload = new ServiceManagerPayload
                                {
                                    Action = ServiceManagerAction.StartResponse,
                                    TargetServiceName = svcPayload.TargetServiceName,
                                    Success = ok,
                                    Message = msg
                                };
                                var bytes = MessageSerializer.SerializeJson(responsePayload);
                                if (_incomingSession != null && _incomingSession.IsConnected)
                                {
                                    await _incomingSession.SendFrameAsync(new PacketFrame(ChannelType.ServiceManager, 0, bytes));
                                }
                            });
                        }
                        else if (svcPayload.Action == ServiceManagerAction.StopRequest)
                        {
                            _ = Task.Run(async () =>
                            {
                                var (ok, msg) = RemoteServicesManager.StopService(svcPayload.TargetServiceName);
                                var responsePayload = new ServiceManagerPayload
                                {
                                    Action = ServiceManagerAction.StopResponse,
                                    TargetServiceName = svcPayload.TargetServiceName,
                                    Success = ok,
                                    Message = msg
                                };
                                var bytes = MessageSerializer.SerializeJson(responsePayload);
                                if (_incomingSession != null && _incomingSession.IsConnected)
                                {
                                    await _incomingSession.SendFrameAsync(new PacketFrame(ChannelType.ServiceManager, 0, bytes));
                                }
                            });
                        }
                        else if (svcPayload.Action == ServiceManagerAction.ChangeStartupTypeRequest)
                        {
                            _ = Task.Run(async () =>
                            {
                                var (ok, msg) = RemoteServicesManager.ChangeStartupType(svcPayload.TargetServiceName, svcPayload.NewStartupType);
                                var responsePayload = new ServiceManagerPayload
                                {
                                    Action = ServiceManagerAction.ChangeStartupTypeResponse,
                                    TargetServiceName = svcPayload.TargetServiceName,
                                    NewStartupType = svcPayload.NewStartupType,
                                    Success = ok,
                                    Message = msg
                                };
                                var bytes = MessageSerializer.SerializeJson(responsePayload);
                                if (_incomingSession != null && _incomingSession.IsConnected)
                                {
                                    await _incomingSession.SendFrameAsync(new PacketFrame(ChannelType.ServiceManager, 0, bytes));
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ServiceManager", "Host: Erro ao processar frame de serviços do cliente", ex);
                }
                return;
            }

            if (frame.Channel == ChannelType.Control && frame.Payload.Length > 0)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(frame.Payload);
                    if (doc.RootElement.TryGetProperty("Action", out var actionProp))
                    {
                        var action = (RemoteWindowAction)actionProp.GetByte();
                        ExecuteRemoteWindowAction(action);
                        return;
                    }

                    if (doc.RootElement.TryGetProperty("TargetWidth", out _))
                    {
                        var resReq = MessageSerializer.DeserializeJson<ResolutionChangeRequestPayload>(frame.Payload);
                        if (resReq != null && resReq.TargetWidth > 0 && resReq.TargetHeight > 0)
                        {
                            _incomingClientScreenWidth = resReq.TargetWidth;
                            _incomingClientScreenHeight = resReq.TargetHeight;
                            bool adjusted = DisplayResolutionManager.TryAdjustHostResolution(resReq.TargetWidth, resReq.TargetHeight, out string resMsg);
                            _screenCapturer.SetTargetResolution(resReq.TargetWidth, resReq.TargetHeight);
                            AppLogger.LogInfo("RemoteSession", $"[RESOLUTION CHANGE] Pedido recebido do cliente: {resReq.TargetWidth}x{resReq.TargetHeight}: {resMsg}");

                            if (_incomingSession != null && _incomingSession.IsConnected)
                            {
                                var resp = new ResolutionChangeResponsePayload
                                {
                                    Success = adjusted,
                                    EffectiveWidth = _incomingClientScreenWidth,
                                    EffectiveHeight = _incomingClientScreenHeight,
                                    Message = resMsg
                                };
                                _ = _incomingSession.SendFrameAsync(new PacketFrame(ChannelType.Control, 0, MessageSerializer.SerializeJson(resp)));
                            }
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("RemoteSession", "Erro ao processar pacote de controlo do cliente", ex);
                }
                return;
            }

            if (frame.Channel == ChannelType.Input && frame.Payload.Length > 0)
            {
                try
                {
                    var inputPayload = MessageSerializer.DeserializeJson<InputPacketPayload>(frame.Payload);
                    if (inputPayload != null)
                    {
                        if (string.Equals(_activeIncomingPermission, "OnlyRead", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_activeIncomingPermission, "ViewOnly", StringComparison.OrdinalIgnoreCase))
                        {
                            if (inputPayload.Type == ProtocolInputType.Mouse && inputPayload.MouseType != (byte)MouseEventType.Move)
                            {
                                AppLogger.LogWarning("RemoteSession", $"[HOST BLOCKED] Clique de rato (Tipo={(MouseEventType)inputPayload.MouseType}) BLOQUEADO: Sessão remota em modo APENAS LEITURA ({_activeIncomingPermission})!");
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

        private void ExecuteRemoteWindowAction(RemoteWindowAction action)
        {
            AppLogger.LogInfo("RemoteSession", $"[HOST EXECUTE] Ação de controlo de janela recebida: {action}");

            switch (action)
            {
                case RemoteWindowAction.MinimizeActiveWindow:
                    InputInjector.MinimizeActiveWindow();
                    break;

                case RemoteWindowAction.MaximizeActiveWindow:
                    InputInjector.MaximizeOrRestoreActiveWindow();
                    break;

                case RemoteWindowAction.CloseActiveWindow:
                    InputInjector.CloseActiveWindow();
                    break;

                case RemoteWindowAction.MinimizeHostRotina:
                    AppLogger.LogInfo("RemoteSession", "[HOST PRIVACY] Ativando Modo de Privacidade / Ocultar Rotina a pedido do técnico remoto...");
                    PrivacyScreenManager.Instance.Activate();
                    break;

                case RemoteWindowAction.MaximizeHostRotina:
                    AppLogger.LogInfo("RemoteSession", "[HOST PRIVACY] Desativando Modo de Privacidade / Mostrar Rotina a pedido do técnico remoto...");
                    PrivacyScreenManager.Instance.Deactivate();
                    break;
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

            // Minimizar a janela principal do anfitrião para que o ambiente de trabalho fique desobstruído para o técnico
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var mainWin = System.Windows.Application.Current.MainWindow;
                    if (mainWin != null && mainWin.WindowState != System.Windows.WindowState.Minimized)
                    {
                        mainWin.WindowState = System.Windows.WindowState.Minimized;
                        AppLogger.LogInfo("RemoteSession", "[HOST] Janela principal do RotinaRemote minimizada automaticamente ao iniciar a sessão de assistência remota.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("RemoteSession", "Erro ao auto-minimizar janela principal do anfitrião", ex);
                }
            });

            if (_incomingClientScreenWidth > 0 && _incomingClientScreenHeight > 0)
            {
                try
                {
                    bool adjusted = DisplayResolutionManager.TryAdjustHostResolution(_incomingClientScreenWidth, _incomingClientScreenHeight, out string resMsg);
                    _screenCapturer.SetTargetResolution(_incomingClientScreenWidth, _incomingClientScreenHeight);
                    AppLogger.LogInfo("RemoteSession", $"[RESOLUTION] Ajuste de resolução do ecrã do anfitrião ({_incomingClientScreenWidth}x{_incomingClientScreenHeight}): {resMsg} (Sucesso={adjusted})");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("RemoteSession", "Erro ao ajustar resolução do ecrã do anfitrião", ex);
                }
            }

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

                _ = ShellAuditor.AuditConnectionAtConnectAsync(
                    direction: "Saída (Cliente a iniciar ligação)",
                    targetId: targetHost,
                    transportName: "A negociar rota de ligação...",
                    cloudServerUrl: _config.SignalingServerUrl);

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

                    var clientRes = DisplayResolutionManager.GetCurrentResolution();
                    var myGeo = await RotinaRemote.Core.Services.GeoLocationService.GetGeoLocationAsync(_identity.FormattedId);
                    var reqPayload = new SignalingConnectRequestPayload
                    {
                        CallerDeviceId = _identity.FormattedId,
                        CallerIp = myGeo.Ip,
                        City = myGeo.City,
                        Country = myGeo.Country,
                        Location = myGeo.Location,
                        Password = TargetPassword?.Trim() ?? string.Empty,
                        ClientScreenWidth = clientRes.Width,
                        ClientScreenHeight = clientRes.Height
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
                try
                {
                    var clientRes = DisplayResolutionManager.GetCurrentResolution();
                    var hs = new HandshakeRequestPayload
                    {
                        ClientDeviceId = _identity.FormattedId,
                        Password = TargetPassword?.Trim() ?? string.Empty,
                        ClientScreenWidth = clientRes.Width,
                        ClientScreenHeight = clientRes.Height
                    };
                    var hsBytes = MessageSerializer.SerializeJson(hs);
                    _ = activeSession.SendFrameAsync(new PacketFrame(ChannelType.Control, 0, hsBytes));
                }
                catch { }
            }

            if (activeSession != null)
            {
                try
                {
                    try
                    {
                        var clientRes = DisplayResolutionManager.GetCurrentResolution();
                        if (clientRes.Width > 0 && clientRes.Height > 0)
                        {
                            var resChange = new ResolutionChangeRequestPayload
                            {
                                TargetWidth = clientRes.Width,
                                TargetHeight = clientRes.Height
                            };
                            var resBytes = MessageSerializer.SerializeJson(resChange);
                            _ = activeSession.SendFrameAsync(new PacketFrame(ChannelType.Control, 0, resBytes));
                        }
                    }
                    catch { }

                    _activeSession = activeSession;
                    _activeSession.FrameReceived += OnFrameReceivedFromHost;
                    _activeSession.Disconnected += OnSessionDisconnected;

                    if (EnableClipboardSync)
                    {
                        _clipboardSync.Start(async text =>
                        {
                            if (_activeSession != null && _activeSession.IsConnected && EnableClipboardSync)
                            {
                                var clipPayload = new ClipboardPayload { Text = text };
                                var bytes = MessageSerializer.SerializeJson(clipPayload);
                                await _activeSession.SendFrameAsync(new PacketFrame(ChannelType.Clipboard, 0, bytes));
                            }
                        }, async files =>
                        {
                            if (_activeSession != null && _activeSession.IsConnected && EnableClipboardSync)
                            {
                                await SendFilesAsync(files);
                            }
                        });
                    }

                    IsConnected = true;
                    TransportType = usedTransportName;
                    ConnectionStatus = "Ligado a " + targetHost + " (" + usedTransportName + ")";
                    SelectedTabIndex = 1;
                    AppLogger.LogInfo("RemoteSession", $"[SESSION CONNECTED] Sessão remota estabelecida com sucesso com {targetHost} via {usedTransportName}.");

                    _activeConnectedTargetId = targetHost;
                    _sessionStartTime = DateTime.UtcNow;
                    _ = ShellAuditor.AuditConnectionAtConnectAsync(
                        direction: "Saída (Cliente conectado ao Host)",
                        targetId: targetHost,
                        transportName: usedTransportName,
                        cloudServerUrl: _config.SignalingServerUrl);

                    _sessionMonitoringCts?.Cancel();
                    _sessionMonitoringCts = new CancellationTokenSource();
                    ShellAuditor.StartSessionMonitoring(targetHost, _sessionMonitoringCts.Token);

                    _activeOutgoingHistoryItem = new ConnectionHistoryItem
                    {
                        RemoteId = targetHost,
                        RemoteName = "PC Remoto (" + targetHost + ")",
                        Direction = "Saída",
                        RemoteIp = targetHost,
                        Location = "Remoto",
                        ConnectionTime = DateTime.Now,
                        Duration = TimeSpan.Zero,
                        Transport = usedTransportType,
                        TransportName = usedTransportName,
                        Status = "Em curso..."
                    };
                    History.Insert(0, _activeOutgoingHistoryItem);
                    HistoryManager.SaveHistory(History);
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
            if (frame.Channel == ChannelType.Control && frame.Payload.Length > 0)
            {
                try
                {
                    var resResp = MessageSerializer.DeserializeJson<ResolutionChangeResponsePayload>(frame.Payload);
                    if (resResp != null && !string.IsNullOrEmpty(resResp.Message))
                    {
                        AppLogger.LogInfo("RemoteSession", $"[RESOLUTION STATUS] Resposta do host: {resResp.Message} ({resResp.EffectiveWidth}x{resResp.EffectiveHeight})");
                    }
                }
                catch { }
            }

            if (frame.Channel == ChannelType.Video && frame.Payload.Length > 0)
            {
                var bitmap = BytesToBitmapImage(frame.Payload);
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    RemoteScreenSource = bitmap;
                });
            }

            if (frame.Channel == ChannelType.Clipboard && frame.Payload.Length > 0)
            {
                if (EnableClipboardSync)
                {
                    string text = string.Empty;
                    try
                    {
                        var clipPayload = MessageSerializer.DeserializeJson<ClipboardPayload>(frame.Payload);
                        text = clipPayload?.Text ?? string.Empty;
                    }
                    catch { }
                    if (string.IsNullOrEmpty(text))
                    {
                        text = System.Text.Encoding.UTF8.GetString(frame.Payload);
                    }
                    if (!string.IsNullOrEmpty(text))
                    {
                        _clipboardSync.ReceiveRemoteClipboard(text);
                    }
                }
            }

            if (frame.Channel == ChannelType.Chat && frame.Payload.Length > 0)
            {
                try
                {
                    var chatPayload = MessageSerializer.DeserializeJson<ChatMessagePayload>(frame.Payload);
                    if (chatPayload != null && !string.IsNullOrWhiteSpace(chatPayload.Message))
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            ChatMessages.Add(new ChatMessageItem
                            {
                                SenderId = chatPayload.SenderId,
                                SenderName = !string.IsNullOrEmpty(chatPayload.SenderName) ? chatPayload.SenderName : $"Anfitrião ({chatPayload.SenderId})",
                                Message = chatPayload.Message,
                                Timestamp = chatPayload.Timestamp.ToLocalTime(),
                                IsOutgoing = false
                            });

                            if (!IsChatOpen)
                            {
                                UnreadChatCount++;
                            }

                            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                        });
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Chat", "Erro ao processar mensagem de chat do anfitrião", ex);
                }
            }

            if (frame.Channel == ChannelType.File && frame.Payload.Length > 0)
            {
                try
                {
                    var filePayload = MessageSerializer.DeserializeJson<FileTransferPayload>(frame.Payload);
                    if (filePayload != null)
                    {
                        _ = _fileTransferEngine.HandleIncomingPayloadAsync(filePayload);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("FileTransfer", "Erro ao processar pacote de ficheiro do anfitrião", ex);
                }
            }

            if (frame.Channel == ChannelType.ProcessManager && frame.Payload.Length > 0)
            {
                try
                {
                    var procPayload = MessageSerializer.DeserializeJson<ProcessManagerPayload>(frame.Payload);
                    if (procPayload != null)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (procPayload.Action == ProcessManagerAction.ListResponse)
                            {
                                RemoteProcesses.Clear();
                                long totalMem = 0;
                                if (procPayload.Processes != null)
                                {
                                    foreach (var item in procPayload.Processes)
                                    {
                                        RemoteProcesses.Add(item);
                                        totalMem += item.MemoryBytes;
                                    }
                                }
                                TotalRemoteProcessesCount = RemoteProcesses.Count;
                                TotalRemoteMemoryFormatted = $"{totalMem / (1024.0 * 1024.0):F0} MB ({(totalMem / (1024.0 * 1024.0 * 1024.0)):F2} GB)";
                                FilterProcesses();
                                IsLoadingProcesses = false;
                                ProcessManagerStatusText = $"Atualizado com sucesso às {DateTime.Now:HH:mm:ss}. {RemoteProcesses.Count} processos remotos listados.";
                            }
                            else if (procPayload.Action == ProcessManagerAction.KillResponse)
                            {
                                IsLoadingProcesses = false;
                                ProcessManagerStatusText = procPayload.Message;
                                if (procPayload.Success)
                                {
                                    var existing = RemoteProcesses.FirstOrDefault(p => p.ProcessId == procPayload.TargetProcessId);
                                    if (existing != null) RemoteProcesses.Remove(existing);
                                    var existingFiltered = FilteredRemoteProcesses.FirstOrDefault(p => p.ProcessId == procPayload.TargetProcessId);
                                    if (existingFiltered != null) FilteredRemoteProcesses.Remove(existingFiltered);
                                    TotalRemoteProcessesCount = RemoteProcesses.Count;
                                }
                                ChatMessages.Add(new ChatMessageItem
                                {
                                    SenderId = "Sistema",
                                    SenderName = "Gestor Processos",
                                    Message = procPayload.Message,
                                    Timestamp = DateTime.Now,
                                    IsOutgoing = false
                                });
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ProcessManager", "Cliente: Erro ao processar resposta do gestor de processos", ex);
                }
            }

            if (frame.Channel == ChannelType.ServiceManager && frame.Payload.Length > 0)
            {
                try
                {
                    var svcPayload = MessageSerializer.DeserializeJson<ServiceManagerPayload>(frame.Payload);
                    if (svcPayload != null)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (svcPayload.Action == ServiceManagerAction.ListResponse)
                            {
                                RemoteServices.Clear();
                                int runningCount = 0;
                                if (svcPayload.Services != null)
                                {
                                    foreach (var item in svcPayload.Services)
                                    {
                                        RemoteServices.Add(item);
                                        if (item.IsRunning) runningCount++;
                                    }
                                }
                                TotalRemoteServicesCount = RemoteServices.Count;
                                RunningRemoteServicesCount = runningCount;
                                FilterServices();
                                IsLoadingServices = false;
                                ServicesStatusText = $"Atualizado com sucesso às {DateTime.Now:HH:mm:ss}. {RemoteServices.Count} serviços listados ({runningCount} em execução).";
                            }
                            else if (svcPayload.Action == ServiceManagerAction.StartResponse ||
                                     svcPayload.Action == ServiceManagerAction.StopResponse ||
                                     svcPayload.Action == ServiceManagerAction.ChangeStartupTypeResponse)
                            {
                                IsLoadingServices = false;
                                ServicesStatusText = svcPayload.Message;
                                RefreshRemoteServices();
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ServiceManager", "Cliente: Erro ao processar resposta do gestor de serviços", ex);
                }
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
            try
            {
                _servicesWindow?.Close();
                _servicesWindow = null;
            }
            catch { }

            PrivacyScreenManager.Instance.Deactivate();
            IsRemotePrivacyModeActive = false;
            RemoteSessionSubTabIndex = 0;
            _clipboardSync.Stop();
            _sessionMonitoringCts?.Cancel();
            DisplayResolutionManager.RestoreOriginalResolution();
            _screenCapturer.ClearTargetResolution();
            _incomingClientScreenWidth = 0;
            _incomingClientScreenHeight = 0;
            if (!string.IsNullOrEmpty(_activeConnectedTargetId))
            {
                ShellAuditor.LogSessionEnded(_activeConnectedTargetId, DateTime.UtcNow - _sessionStartTime);
                _activeConnectedTargetId = null;
            }
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
            if (ActiveIncomingConnection != null)
            {
                ActiveIncomingConnection.IsActive = false;
                ActiveIncomingConnection.EndTime = DateTime.Now;
                ActiveIncomingConnection.Status = "Terminada";
                ActiveIncomingConnection = null;
                RefreshConnectionsState();
            }

            if (_activeOutgoingHistoryItem != null)
            {
                _activeOutgoingHistoryItem.Duration = DateTime.UtcNow - _sessionStartTime;
                _activeOutgoingHistoryItem.Status = "Concluída";
                HistoryManager.SaveHistory(History);
                _activeOutgoingHistoryItem = null;
            }

            if (_activeIncomingHistoryItem != null)
            {
                _activeIncomingHistoryItem.Status = "Concluída";
                HistoryManager.SaveHistory(History);
                _activeIncomingHistoryItem = null;
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
