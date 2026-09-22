using System;
using System.IO;
using System.Text.Json;

namespace RotinaRemote.Core.Configuration
{
    public class AppConfig
    {
        public string SignalingServerUrl { get; set; } = "wss://rotinaremote.onrender.com/ws";
        public string RelayServerUrl { get; set; } = "tcp://127.0.0.1:5001";
        public string StunServerHost { get; set; } = "stun.l.google.com";
        public int StunServerPort { get; set; } = 19302;
        public int KeepAliveIntervalMs { get; set; } = 3000;
        public int TargetFps { get; set; } = 60;
        public string DefaultSavePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        public string Theme { get; set; } = "Dark"; // Dark, Light, System
        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool EnableClipboardSync { get; set; } = true;

        private static readonly string ConfigFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var json = File.ReadAllText(ConfigFilePath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null) return config;
                }
            }
            catch
            {
                // Fallback para padrão em caso de erro
            }

            var defaultConfig = new AppConfig();
            defaultConfig.Save();
            return defaultConfig;
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch
            {
                // Ignorar erro ao guardar configuração
            }
        }
    }
}
