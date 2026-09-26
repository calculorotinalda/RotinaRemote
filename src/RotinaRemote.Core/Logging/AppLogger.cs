using System;
using System.IO;
using System.Text;

namespace RotinaRemote.Core.Logging
{
    public enum LogSeverity
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    public static class AppLogger
    {
        private static readonly object _lock = new object();
        private static readonly System.Collections.Generic.HashSet<string> _logFilePaths = new(StringComparer.OrdinalIgnoreCase);

        static AppLogger()
        {
            InitializeLogPaths();
        }

        public static void InitializeLogPaths()
        {
            lock (_lock)
            {
                _logFilePaths.Clear();

                // 1. Diretoria base da aplicação (onde está o exe em execução)
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string mainLog = Path.Combine(baseDir, "log.txt");
                _logFilePaths.Add(mainLog);

                // 2. Diretoria do processo executável se diferente da base
                try
                {
                    string? procPath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(procPath))
                    {
                        string? procDir = Path.GetDirectoryName(procPath);
                        if (!string.IsNullOrEmpty(procDir) && !procDir.Equals(baseDir, StringComparison.OrdinalIgnoreCase))
                        {
                            _logFilePaths.Add(Path.Combine(procDir, "log.txt"));
                        }
                    }
                }
                catch { }

                // 3. Diretoria "Releases" onde estão os ficheiros executáveis gerados
                try
                {
                    string current = baseDir;
                    for (int i = 0; i < 5 && !string.IsNullOrEmpty(current); i++)
                    {
                        string candidate = Path.Combine(current, "Releases");
                        if (Directory.Exists(candidate))
                        {
                            _logFilePaths.Add(Path.Combine(candidate, "log.txt"));
                            break;
                        }
                        var parent = Directory.GetParent(current);
                        if (parent == null) break;
                        current = parent.FullName;
                    }

                    // Verifica caminho absoluto padrão de releases no ambiente de trabalho e projeto
                    string workspaceReleases = @"C:\Users\alll\Documents\Rotinaremote\Releases";
                    if (Directory.Exists(workspaceReleases))
                    {
                        _logFilePaths.Add(Path.Combine(workspaceReleases, "log.txt"));
                    }
                }
                catch { }
            }
        }

        public static string LogFilePath
        {
            get => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
            set
            {
                lock (_lock)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        _logFilePaths.Add(value);
                    }
                }
            }
        }

        public static void LogDebug(string component, string message)
        {
            WriteLog(LogSeverity.Debug, component, message, null);
        }

        public static void LogInfo(string component, string message)
        {
            WriteLog(LogSeverity.Info, component, message, null);
        }

        public static void LogWarning(string component, string message)
        {
            WriteLog(LogSeverity.Warning, component, message, null);
        }

        public static void LogError(string component, string message, Exception? ex = null)
        {
            WriteLog(LogSeverity.Error, component, message, ex);
        }

        public static void LogCritical(string component, string message, Exception? ex = null)
        {
            WriteLog(LogSeverity.Critical, component, message, ex);
        }

        public static void LogRemoteSession(string component, string message, Exception? ex = null)
        {
            WriteLog(LogSeverity.Info, $"[SESSÃO REMOTA] {component}", message, ex);
        }

        private static void WriteLog(LogSeverity severity, string component, string message, Exception? ex)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var sb = new StringBuilder();
                sb.Append($"[{timestamp}] [{severity.ToString().ToUpper()}] [{component}] {message}");

                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append($"  Exception: {ex.GetType().Name} - {ex.Message}");
                    if (!string.IsNullOrEmpty(ex.StackTrace))
                    {
                        sb.AppendLine();
                        sb.Append($"  StackTrace: {ex.StackTrace}");
                    }
                }

                var logLine = sb.ToString();

                lock (_lock)
                {
                    Console.WriteLine(logLine);
                    foreach (var path in _logFilePaths)
                    {
                        try
                        {
                            var dir = Path.GetDirectoryName(path);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            File.AppendAllText(path, logLine + Environment.NewLine);
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                // Ignorar exceções no próprio logger para não quebrar a aplicação
            }
        }
    }
}
