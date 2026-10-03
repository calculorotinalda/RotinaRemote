using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using RotinaRemote.Core.Logging;

namespace RotinaRemote.Client.Services
{
    public class ClipboardSyncManager
    {
        private DispatcherTimer? _timer;
        private Func<string, Task>? _sendFunc;
        private string _lastLocalText = string.Empty;
        private string _lastReceivedText = string.Empty;
        private bool _isRunning = false;

        public void Start(Func<string, Task> sendFunc)
        {
            Stop();

            _sendFunc = sendFunc;
            _isRunning = true;

            // Inicializa com o conteúdo atual para não reenviar imediatamente o que já estava na área de transferência
            try
            {
                if (Clipboard.ContainsText())
                {
                    _lastLocalText = Clipboard.GetText();
                }
            }
            catch { }

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(450)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();

            AppLogger.LogInfo("Clipboard", "Sincronizador de área de transferência (Clipboard) iniciado.");
        }

        public void Stop()
        {
            _isRunning = false;
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
                _timer = null;
            }
            _sendFunc = null;
            _lastLocalText = string.Empty;
            _lastReceivedText = string.Empty;
        }

        private async void OnTimerTick(object? sender, EventArgs e)
        {
            if (!_isRunning || _sendFunc == null) return;

            try
            {
                if (Clipboard.ContainsText())
                {
                    string current = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(current) &&
                        current != _lastLocalText &&
                        current != _lastReceivedText)
                    {
                        _lastLocalText = current;
                        AppLogger.LogInfo("Clipboard", $"[CLIPBOARD SEND] Detetada nova cópia local ({current.Length} chars). Enviando para computador remoto...");
                        await _sendFunc(current);
                    }
                }
            }
            catch
            {
                // Clipboard pode estar temporariamente bloqueado por outra aplicação do Windows
            }
        }

        public void ReceiveRemoteClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            _lastReceivedText = text;
            _lastLocalText = text;

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Clipboard.SetText(text);
                    AppLogger.LogInfo("Clipboard", $"[CLIPBOARD RECV] Conteúdo remoto copiado para área de transferência local ({text.Length} chars).");
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning("Clipboard", $"Falha ao colar no clipboard local: {ex.Message}");
                }
            });
        }
    }
}
