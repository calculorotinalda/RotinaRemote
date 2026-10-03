using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RotinaRemote.Core.Models;

namespace RotinaRemote.Core.Services
{
    public static class HistoryManager
    {
        private static readonly object _lock = new object();

        public static string GetHistoryFilePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localFile = Path.Combine(baseDir, "history.json");
            try
            {
                // Verifica se temos permissão de escrita no diretório base
                string testFile = Path.Combine(baseDir, ".history_test");
                File.WriteAllText(testFile, "ok");
                File.Delete(testFile);
                return localFile;
            }
            catch
            {
                // Fallback seguro para AppData
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RotinaRemote");
                if (!Directory.Exists(appData))
                {
                    Directory.CreateDirectory(appData);
                }
                return Path.Combine(appData, "history.json");
            }
        }

        public static List<ConnectionHistoryItem> LoadHistory()
        {
            lock (_lock)
            {
                try
                {
                    string path = GetHistoryFilePath();
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        var list = JsonSerializer.Deserialize<List<ConnectionHistoryItem>>(json);
                        if (list != null)
                        {
                            return list.OrderByDescending(x => x.ConnectionTime).ToList();
                        }
                    }
                }
                catch
                {
                    // Fallback para lista vazia em caso de leitura com erro
                }
                return new List<ConnectionHistoryItem>();
            }
        }

        public static void SaveHistory(IEnumerable<ConnectionHistoryItem> items)
        {
            lock (_lock)
            {
                try
                {
                    string path = GetHistoryFilePath();
                    var list = items.Take(500).ToList();
                    string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, json);
                }
                catch
                {
                }
            }
        }

        public static void ClearHistory()
        {
            lock (_lock)
            {
                try
                {
                    string path = GetHistoryFilePath();
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
