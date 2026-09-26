using System;
using System.IO;
using System.Text.Json;

namespace RotinaRemote.Core.Configuration
{
    public class AppConfig
    {
        // 1. Tema Visual (Dark / Light)
        public string Theme { get; set; } = "Dark";

        // 2. Ativação de Acesso Não Supervisionado por Senha
        public bool EnableUnattendedAccess { get; set; } = false;

        // 3. Senha de Acesso Remoto Imediato
        public string UnattendedPassword { get; set; } = string.Empty;

        // 4. Nível de Permissão (FullControl / OnlyRead)
        public string UnattendedPermission { get; set; } = "FullControl";

        // 5. Iniciar Automaticamente com o Windows
        public bool StartWithWindows { get; set; } = false;

        // 6. Minimizar para a Área de Notificação (System Tray)
        public bool MinimizeToTray { get; set; } = true;

        // 7. Qualidade de Imagem / Compressão (30 a 95%)
        public int VideoQuality { get; set; } = 60;

        // 8. Taxa de Fotogramas por Segundo Alvo (15, 30, 60 FPS)
        public int TargetFps { get; set; } = 60;

        // 9. Ocultar Papel de Parede do Ambiente de Trabalho Remoto
        public bool DisableRemoteWallpaper { get; set; } = false;

        // 10. Sincronização Bidirecional da Área de Transferência
        public bool EnableClipboardSync { get; set; } = true;

        // 11. Bloqueio de Entrada Local (Suprimir entrada local durante controlo remoto)
        public bool BlockRemoteInput { get; set; } = false;

        // 12. Porta TCP do Listener P2P Direto
        public int P2pPort { get; set; } = 48270;

        // 13. Porta UDP do Serviço de Descoberta LAN
        public int LanDiscoveryPort { get; set; } = 48271;

        // 14. Intervalo de KeepAlive / Heartbeat (ms)
        public int KeepAliveIntervalMs { get; set; } = 3000;

        // 15. Servidor de Sinalização na Nuvem
        public string SignalingServerUrl { get; set; } = "wss://rotinaremote-signaling-49575983278.europe-west1.run.app/ws";

        // Parâmetros Adicionais de Infraestrutura
        public string RelayServerUrl { get; set; } = "wss://rotinaremote-signaling-49575983278.europe-west1.run.app/relay";
        public string StunServerHost { get; set; } = "stun.l.google.com";
        public int StunServerPort { get; set; } = 19302;
        public string DefaultSavePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

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
