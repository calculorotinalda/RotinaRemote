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
        private static string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");

        public static string LogFilePath
        {
            get => _logFilePath;
            set => _logFilePath = value;
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
                    File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
                }
            }
            catch
            {
                // Ignorar exceções no próprio logger para não quebrar a aplicação
            }
        }
    }
}
