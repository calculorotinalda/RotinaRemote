using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private Func<string, Task>? _sendTextFunc;
        private Func<string[], Task>? _sendFilesFunc;
        private string _lastLocalText = string.Empty;
        private string _lastReceivedText = string.Empty;
        private List<string> _lastLocalFiles = new();
        private List<string> _lastReceivedFiles = new();
        private bool _isRunning = false;

        public void Start(Func<string, Task> sendTextFunc, Func<string[], Task>? sendFilesFunc = null)
        {
            Stop();

            _sendTextFunc = sendTextFunc;
            _sendFilesFunc = sendFilesFunc;
            _isRunning = true;

            // Inicializa com o conteúdo atual para não reenviar imediatamente o que já estava na área de transferência
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files != null)
                    {
                        _lastLocalFiles = files.Cast<string>().Where(File.Exists).ToList();
                    }
                }
                else if (Clipboard.ContainsText())
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

            AppLogger.LogInfo("Clipboard", "Sincronizador bidirecional de área de transferência (Texto e Ficheiros) iniciado.");
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
            _sendTextFunc = null;
            _sendFilesFunc = null;
            _lastLocalText = string.Empty;
            _lastReceivedText = string.Empty;
            _lastLocalFiles.Clear();
            _lastReceivedFiles.Clear();
        }

        private async void OnTimerTick(object? sender, EventArgs e)
        {
            if (!_isRunning) return;

            try
            {
                // 1. Verificar se há ficheiros copiados (todas as extensões)
                if (Clipboard.ContainsFileDropList())
                {
                    var dropList = Clipboard.GetFileDropList();
                    if (dropList != null && dropList.Count > 0)
                    {
                        var currentFiles = dropList.Cast<string>().Where(File.Exists).ToList();
                        if (currentFiles.Count > 0 &&
                            !AreFileListsEqual(currentFiles, _lastLocalFiles) &&
                            !AreFileListsEqual(currentFiles, _lastReceivedFiles))
                        {
                            _lastLocalFiles = currentFiles;
                            AppLogger.LogInfo("Clipboard", $"[CLIPBOARD FILE DETECTED] {currentFiles.Count} ficheiro(s) detetados no clipboard: {string.Join(", ", currentFiles.Select(Path.GetFileName))}. A sincronizar com o computador remoto...");

                            if (_sendFilesFunc != null)
                            {
                                await _sendFilesFunc(currentFiles.ToArray());
                            }
                        }
                    }
                }
                // 2. Caso contrário, verificar se há texto copiado
                else if (Clipboard.ContainsText() && _sendTextFunc != null)
                {
                    string current = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(current) &&
                        current != _lastLocalText &&
                        current != _lastReceivedText)
                    {
                        _lastLocalText = current;
                        AppLogger.LogInfo("Clipboard", $"[CLIPBOARD SEND] Detetada nova cópia de texto local ({current.Length} chars). Enviando para computador remoto...");
                        await _sendTextFunc(current);
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
                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    try
                    {
                        Clipboard.SetText(text);
                        AppLogger.LogInfo("Clipboard", $"[CLIPBOARD RECV] Conteúdo remoto copiado para área de transferência local ({text.Length} chars).");
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == 5)
                        {
                            AppLogger.LogWarning("Clipboard", $"Falha ao colar no clipboard local após 5 tentativas: {ex.Message}");
                        }
                        else
                        {
                            System.Threading.Thread.Sleep(40);
                        }
                    }
                }
            });
        }

        private readonly List<string> _recentlyReceivedBatch = new();
        private DateTime _lastBatchTime = DateTime.MinValue;
        private readonly object _batchLock = new();

        public void ReceiveRemoteFiles(IEnumerable<string> filePaths)
        {
            var files = filePaths.Where(File.Exists).ToList();
            if (files.Count == 0) return;

            List<string> currentBatch;
            lock (_batchLock)
            {
                if (DateTime.UtcNow - _lastBatchTime < TimeSpan.FromSeconds(2.5))
                {
                    foreach (var f in files)
                    {
                        if (!_recentlyReceivedBatch.Contains(f, StringComparer.OrdinalIgnoreCase))
                            _recentlyReceivedBatch.Add(f);
                    }
                }
                else
                {
                    _recentlyReceivedBatch.Clear();
                    _recentlyReceivedBatch.AddRange(files);
                }
                _lastBatchTime = DateTime.UtcNow;

                _lastReceivedFiles = new List<string>(_recentlyReceivedBatch);
                _lastLocalFiles = new List<string>(_recentlyReceivedBatch);
                currentBatch = new List<string>(_recentlyReceivedBatch);
            }

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                for (int attempt = 1; attempt <= 8; attempt++)
                {
                    try
                    {
                        var sc = new System.Collections.Specialized.StringCollection();
                        foreach (var f in currentBatch) sc.Add(f);
                        Clipboard.SetFileDropList(sc);
                        AppLogger.LogInfo("Clipboard", $"[CLIPBOARD FILES RECV] {currentBatch.Count} ficheiro(s) colocados com sucesso na área de transferência do Windows (prontos a Colar com Ctrl+V).");
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == 8)
                        {
                            AppLogger.LogWarning("Clipboard", $"Falha ao colocar ficheiros no clipboard local após 8 tentativas: {ex.Message}");
                        }
                        else
                        {
                            System.Threading.Thread.Sleep(50);
                        }
                    }
                }
            });
        }

        private static bool AreFileListsEqual(List<string> list1, List<string> list2)
        {
            if (list1.Count != list2.Count) return false;
            for (int i = 0; i < list1.Count; i++)
            {
                if (!string.Equals(list1[i], list2[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
    }
}
