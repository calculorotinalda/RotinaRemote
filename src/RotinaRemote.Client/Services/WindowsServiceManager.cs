using System;
using System.Diagnostics;
using System.IO;
using RotinaRemote.Core.Logging;

namespace RotinaRemote.Client.Services
{
    public enum ServiceStatusEnum
    {
        NotInstalled,
        Stopped,
        Running,
        Unknown
    }

    public static class WindowsServiceManager
    {
        public const string ServiceName = "RotinaRemoteService";
        public const string DisplayName = "RotinaRemote Background Service";

        public static ServiceStatusEnum GetStatus()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"query {ServiceName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return ServiceStatusEnum.Unknown;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

                if (output.Contains("1060") || output.Contains("FAILED 1060") || output.Contains("não existe"))
                {
                    return ServiceStatusEnum.NotInstalled;
                }

                if (output.Contains("STATE") && output.Contains("RUNNING"))
                {
                    return ServiceStatusEnum.Running;
                }

                if (output.Contains("STATE") && output.Contains("STOPPED"))
                {
                    return ServiceStatusEnum.Stopped;
                }

                if (proc.ExitCode == 0)
                {
                    return ServiceStatusEnum.Stopped;
                }

                return ServiceStatusEnum.NotInstalled;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("WindowsServiceManager", "Erro ao consultar estado do serviço", ex);
                return ServiceStatusEnum.Unknown;
            }
        }

        public static (bool Success, string Message) InstallService()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RotinaRemote.exe");
                string binPath = $"\\\"{exePath}\\\" --service";

                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"create {ServiceName} binPath= \"{binPath}\" start= auto DisplayName= \"{DisplayName}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);

                // Configura descrição
                try
                {
                    var descPsi = new ProcessStartInfo
                    {
                        FileName = "sc.exe",
                        Arguments = $"description {ServiceName} \"Serviço contínuo de assistência e suporte remoto RotinaRemote.\"",
                        Verb = "runas",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    };
                    using var descProc = Process.Start(descPsi);
                    descProc?.WaitForExit(3000);
                }
                catch { }

                AppLogger.LogInfo("WindowsServiceManager", "Comando de instalação de serviço executado.");
                return (true, "Serviço instalado com sucesso! Pode agora iniciá-lo.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("WindowsServiceManager", "Erro ao instalar serviço", ex);
                return (false, $"Erro ao instalar serviço: {ex.Message}");
            }
        }

        public static (bool Success, string Message) StartService()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"start {ServiceName}",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);

                AppLogger.LogInfo("WindowsServiceManager", "Comando de arranque de serviço executado.");
                return (true, "Comando de arranque do serviço enviado.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("WindowsServiceManager", "Erro ao iniciar serviço", ex);
                return (false, $"Erro ao iniciar serviço: {ex.Message}");
            }
        }

        public static (bool Success, string Message) StopService()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"stop {ServiceName}",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);

                AppLogger.LogInfo("WindowsServiceManager", "Comando de paragem de serviço executado.");
                return (true, "Comando de paragem do serviço enviado.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("WindowsServiceManager", "Erro ao parar serviço", ex);
                return (false, $"Erro ao parar serviço: {ex.Message}");
            }
        }

        public static (bool Success, string Message) UninstallService()
        {
            try
            {
                // 1. Para o serviço Windows
                StopService();

                // 2. Termina eventuais processos de serviço bloqueados em segundo plano
                try
                {
                    var currentPid = Process.GetCurrentProcess().Id;
                    var procs = Process.GetProcessesByName("RotinaRemote");
                    foreach (var p in procs)
                    {
                        if (p.Id != currentPid)
                        {
                            try { p.Kill(); p.WaitForExit(1000); } catch { }
                        }
                    }
                }
                catch { }

                // 3. Executa a remoção definitiva no SCM
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"delete {ServiceName}",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);

                AppLogger.LogInfo("WindowsServiceManager", "Comando de desinstalação de serviço executado com sucesso.");
                return (true, "Serviço desinstalado e removido do sistema com sucesso.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("WindowsServiceManager", "Erro ao desinstalar serviço", ex);
                return (false, $"Erro ao desinstalar serviço: {ex.Message}");
            }
        }
    }
}
