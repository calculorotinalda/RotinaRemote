using System;
using System.Windows;
using RotinaRemote.Core.Logging;

using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace RotinaRemote.Client
{
    public partial class App : Application
    {
        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);

            AppLogger.LogInfo("App", "RotinaRemote Client iniciando...");

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
            };
        }
    }
}
