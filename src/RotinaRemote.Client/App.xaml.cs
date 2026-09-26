using System;
using System.ServiceProcess;
using System.Windows;
using RotinaRemote.Client.Services;
using RotinaRemote.Core.Configuration;
using RotinaRemote.Core.Logging;

using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace RotinaRemote.Client
{
    public partial class App : Application
    {
        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            try
            {
                System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            }
            catch { }

            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    AppLogger.LogCritical("App", "UnhandledException capturada", ex);
                }
            };

            DispatcherUnhandledException += (s, args) =>
            {
                AppLogger.LogCritical("App", "DispatcherUnhandledException capturada", args.Exception);
                args.Handled = true;
                MessageBox.Show($"Ocorreu um erro inesperado:\n{args.Exception.Message}\n\nDetalhes guardados em log.txt.", "RotinaRemote Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                if (Current.MainWindow == null || !Current.MainWindow.IsLoaded)
                {
                    Current.Shutdown();
                }
            };

            // Se iniciado em modo Serviço Windows via SCM
            if (Array.Exists(e.Args, a => a.Equals("--service", StringComparison.OrdinalIgnoreCase)))
            {
                AppLogger.LogInfo("App", "RotinaRemote iniciado em modo Serviço Windows.");
                ServiceBase.Run(new RotinaRemoteWindowsService());
                Shutdown();
                return;
            }

            AppLogger.LogInfo("App", "RotinaRemote Client iniciando...");
            ShellAuditor.InitializeLogPaths();
            ShellAuditor.WriteLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INICIALIZAÇÃO] RotinaRemote Client iniciado. Sistema de auditoria de shell e conectividade à Internet ativo.");

            var config = AppConfig.Load();
            ThemeManager.ApplyTheme(config.Theme);

            var mainWindow = new Views.MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
    }
}
