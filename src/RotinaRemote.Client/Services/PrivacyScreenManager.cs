using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using RotinaRemote.Core.Logging;

namespace RotinaRemote.Client.Services
{
    public class PrivacyScreenManager
    {
        private static readonly Lazy<PrivacyScreenManager> _instance = new(() => new PrivacyScreenManager());
        public static PrivacyScreenManager Instance => _instance.Value;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // 17 decimal

        private Window? _curtainWindow;
        private bool _isActive = false;

        public bool IsActive => _isActive;

        public void Activate()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_isActive && _curtainWindow != null) return;

                try
                {
                    // 1. Minimizar a janela principal do anfitrião
                    if (Application.Current.MainWindow != null)
                    {
                        Application.Current.MainWindow.WindowState = WindowState.Minimized;
                    }

                    // 2. Criar a janela de cortina de privacidade cobrindo todos os ecrãs
                    _curtainWindow = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize,
                        ShowInTaskbar = false,
                        Topmost = true,
                        Left = SystemParameters.VirtualScreenLeft,
                        Top = SystemParameters.VirtualScreenTop,
                        Width = SystemParameters.VirtualScreenWidth,
                        Height = SystemParameters.VirtualScreenHeight,
                        Background = new SolidColorBrush(Color.FromRgb(11, 19, 43)), // #0B132B
                        Title = "RotinaRemote - Modo de Privacidade Ativo"
                    };

                    // Absorve interações locais do utilizador
                    _curtainWindow.PreviewMouseDown += (s, e) => e.Handled = true;
                    _curtainWindow.PreviewKeyDown += (s, e) => e.Handled = true;

                    // Conteúdo visual elegante e profissional
                    var border = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), // #1E293B
                        BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)), // #334155
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(16),
                        Padding = new Thickness(40),
                        MaxWidth = 650,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    var sp = new StackPanel();

                    var iconText = new TextBlock
                    {
                        Text = "🔒",
                        FontSize = 48,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 16)
                    };

                    var titleText = new TextBlock
                    {
                        Text = "Sessão de Assistência Remota em Curso",
                        FontSize = 22,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 12)
                    };

                    var descText = new TextBlock
                    {
                        Text = "Por motivos de confidencialidade e segurança, o ecrã deste computador foi temporariamente ocultado pelo técnico responsável.\n\nPor favor, não desligue o computador nem utilize o rato/teclado. O controlo visual do seu ecrã será restaurado assim que a sessão terminar.",
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)), // #CBD5E1
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        LineHeight = 22,
                        Margin = new Thickness(0, 0, 0, 24)
                    };

                    var footerText = new TextBlock
                    {
                        Text = "RotinaRemote — Calculorotina Unip. lda",
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)), // #38BDF8
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    sp.Children.Add(iconText);
                    sp.Children.Add(titleText);
                    sp.Children.Add(descText);
                    sp.Children.Add(footerText);
                    border.Child = sp;
                    _curtainWindow.Content = border;

                    _curtainWindow.SourceInitialized += (s, e) =>
                    {
                        try
                        {
                            var hwnd = new WindowInteropHelper(_curtainWindow).Handle;
                            bool setAffinity = SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
                            AppLogger.LogInfo("PrivacyScreen", $"[PRIVACY ACTIVATED] Cortina de privacidade ativada (WDA_EXCLUDEFROMCAPTURE={setAffinity}). O utilizador remoto não vê o ecrã.");
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogWarning("PrivacyScreen", $"SetWindowDisplayAffinity falhou: {ex.Message}");
                        }
                    };

                    _curtainWindow.Show();
                    _isActive = true;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("PrivacyScreen", "Falha ao ativar cortina de privacidade", ex);
                }
            });
        }

        public void Deactivate()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!_isActive && _curtainWindow == null) return;

                try
                {
                    if (_curtainWindow != null)
                    {
                        _curtainWindow.Close();
                        _curtainWindow = null;
                    }
                    _isActive = false;

                    if (Application.Current.MainWindow != null && Application.Current.MainWindow.WindowState == WindowState.Minimized)
                    {
                        Application.Current.MainWindow.WindowState = WindowState.Normal;
                    }

                    AppLogger.LogInfo("PrivacyScreen", "[PRIVACY DEACTIVATED] Cortina de privacidade removida. Visualização do ecrã reposta para o utilizador remoto.");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("PrivacyScreen", "Falha ao desativar cortina de privacidade", ex);
                }
            });
        }
    }
}
