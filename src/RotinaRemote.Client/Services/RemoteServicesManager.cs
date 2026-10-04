using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using RotinaRemote.Core.Logging;
using RotinaRemote.Protocol;

namespace RotinaRemote.Client.Services
{
    public static class RemoteServicesManager
    {
        [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfig(
            IntPtr hService,
            uint dwServiceType,
            uint dwStartType,
            uint dwErrorControl,
            string? lpBinaryPathName,
            string? lpLoadOrderGroup,
            IntPtr lpdwTagId,
            string? lpDependencies,
            string? lpServiceStartName,
            string? lpPassword,
            string? lpDisplayName);

        [DllImport("advapi32.dll", EntryPoint = "CloseServiceHandle")]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
        private const uint SC_MANAGER_CONNECT = 0x0001;
        private const uint SERVICE_CHANGE_CONFIG = 0x0002;
        private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
        private const uint SERVICE_AUTO_START = 2;
        private const uint SERVICE_DEMAND_START = 3;
        private const uint SERVICE_DISABLED = 4;

        /// <summary>
        /// Obtém a lista completa de serviços do Windows na máquina local (executado pelo Host remotamente).
        /// </summary>
        public static List<RemoteServiceItem> GetServices()
        {
            var list = new List<RemoteServiceItem>();
            try
            {
                var services = ServiceController.GetServices();
                foreach (var sc in services)
                {
                    try
                    {
                        string status = sc.Status switch
                        {
                            ServiceControllerStatus.Running => "Em Execução",
                            ServiceControllerStatus.Stopped => "Parado",
                            ServiceControllerStatus.Paused => "Em Pausa",
                            ServiceControllerStatus.StartPending => "A Iniciar...",
                            ServiceControllerStatus.StopPending => "A Parar...",
                            ServiceControllerStatus.PausePending => "A Pausar...",
                            ServiceControllerStatus.ContinuePending => "A Retomar...",
                            _ => sc.Status.ToString()
                        };

                        string startup = "Desconhecido";
                        try
                        {
                            startup = sc.StartType switch
                            {
                                ServiceStartMode.Automatic => "Automático",
                                ServiceStartMode.Manual => "Manual",
                                ServiceStartMode.Disabled => "Desativado",
                                _ => sc.StartType.ToString()
                            };
                        }
                        catch
                        {
                            startup = "Desconhecido";
                        }

                        bool canStop = false;
                        try { canStop = sc.CanStop; } catch { }

                        bool canStart = false;
                        try { canStart = sc.Status != ServiceControllerStatus.Running; } catch { }

                        string displayName = string.IsNullOrWhiteSpace(sc.DisplayName) ? sc.ServiceName : sc.DisplayName;

                        list.Add(new RemoteServiceItem
                        {
                            ServiceName = sc.ServiceName ?? string.Empty,
                            DisplayName = displayName,
                            Status = status,
                            StartupType = startup,
                            CanStop = canStop,
                            CanStart = canStart
                        });
                    }
                    catch
                    {
                        // Ignora serviços individuais que possam estar protegidos
                    }
                    finally
                    {
                        sc.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("RemoteServicesManager", "Erro ao obter lista de serviços do Windows", ex);
            }

            return list.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>
        /// Inicia um serviço do Windows pelo seu ServiceName.
        /// </summary>
        public static (bool Success, string Message) StartService(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                return (false, "Nome do serviço não especificado.");

            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    return (true, $"O serviço '{sc.DisplayName}' já está em execução.");
                }

                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(6));
                return (true, $"Serviço '{sc.DisplayName}' iniciado com sucesso.");
            }
            catch (Exception ex)
            {
                // Tentativa de recurso via net start
                try
                {
                    using var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = "net.exe",
                        Arguments = $"start \"{serviceName}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });
                    p?.WaitForExit(5000);
                    if (p?.ExitCode == 0)
                    {
                        return (true, $"Serviço '{serviceName}' iniciado com sucesso.");
                    }
                }
                catch { }

                AppLogger.LogWarning("RemoteServicesManager", $"Falha ao iniciar serviço '{serviceName}': {ex.Message}");
                return (false, $"Falha ao iniciar serviço '{serviceName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Para um serviço do Windows pelo seu ServiceName.
        /// </summary>
        public static (bool Success, string Message) StopService(string serviceName)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                return (false, "Nome do serviço não especificado.");

            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    return (true, $"O serviço '{sc.DisplayName}' já está parado.");
                }

                if (sc.CanStop)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(6));
                    return (true, $"Serviço '{sc.DisplayName}' parado com sucesso.");
                }
                else
                {
                    // Forçar paragem via net stop
                    using var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = "net.exe",
                        Arguments = $"stop \"{serviceName}\" /y",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });
                    p?.WaitForExit(5000);
                    if (p?.ExitCode == 0)
                    {
                        return (true, $"Serviço '{serviceName}' parado com sucesso.");
                    }
                    return (false, $"O serviço '{sc.DisplayName}' não pode ser parado (CanStop = false).");
                }
            }
            catch (Exception ex)
            {
                // Fallback via net stop
                try
                {
                    using var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = "net.exe",
                        Arguments = $"stop \"{serviceName}\" /y",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });
                    p?.WaitForExit(5000);
                    if (p?.ExitCode == 0)
                    {
                        return (true, $"Serviço '{serviceName}' parado com sucesso.");
                    }
                }
                catch { }

                AppLogger.LogWarning("RemoteServicesManager", $"Falha ao parar serviço '{serviceName}': {ex.Message}");
                return (false, $"Falha ao parar serviço '{serviceName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Altera o tipo de arranque (Startup Type) de um serviço Windows.
        /// </summary>
        public static (bool Success, string Message) ChangeStartupType(string serviceName, string startupType)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                return (false, "Nome do serviço não especificado.");

            uint win32StartType;
            string scStartType;
            string label;

            switch (startupType.Trim().ToLowerInvariant())
            {
                case "automático":
                case "automatico":
                case "automatic":
                case "auto":
                    win32StartType = SERVICE_AUTO_START;
                    scStartType = "auto";
                    label = "Automático";
                    break;
                case "manual":
                case "demand":
                    win32StartType = SERVICE_DEMAND_START;
                    scStartType = "demand";
                    label = "Manual";
                    break;
                case "desativado":
                case "disabled":
                    win32StartType = SERVICE_DISABLED;
                    scStartType = "disabled";
                    label = "Desativado";
                    break;
                default:
                    return (false, $"Tipo de arranque '{startupType}' não suportado.");
            }

            // Tentativa 1: Win32 ChangeServiceConfigW
            try
            {
                IntPtr scm = OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
                if (scm == IntPtr.Zero)
                {
                    scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
                }

                if (scm != IntPtr.Zero)
                {
                    IntPtr svc = OpenService(scm, serviceName, SERVICE_CHANGE_CONFIG);
                    if (svc != IntPtr.Zero)
                    {
                        bool ok = ChangeServiceConfig(
                            svc,
                            SERVICE_NO_CHANGE,
                            win32StartType,
                            SERVICE_NO_CHANGE,
                            null, null, IntPtr.Zero, null, null, null, null);
                        CloseServiceHandle(svc);
                        CloseServiceHandle(scm);

                        if (ok)
                        {
                            AppLogger.LogInfo("RemoteServicesManager", $"Tipo de arranque de '{serviceName}' alterado para {label} via Win32.");
                            return (true, $"Tipo de arranque do serviço '{serviceName}' alterado para {label}.");
                        }
                    }
                    else
                    {
                        CloseServiceHandle(scm);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning("RemoteServicesManager", $"Tentativa Win32 ChangeServiceConfig falhou: {ex.Message}");
            }

            // Tentativa 2: sc.exe config "<serviceName>" start= <auto|demand|disabled>
            try
            {
                using var p = new Process();
                p.StartInfo.FileName = "sc.exe";
                p.StartInfo.Arguments = $"config \"{serviceName}\" start= {scStartType}";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.Start();

                string err = p.StandardError.ReadToEnd();
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit(4000);

                if (p.ExitCode == 0 || stdout.Contains("SUCCESS"))
                {
                    AppLogger.LogInfo("RemoteServicesManager", $"Tipo de arranque de '{serviceName}' alterado para {label} via sc.exe.");
                    return (true, $"Tipo de arranque do serviço '{serviceName}' alterado para {label}.");
                }

                string msg = !string.IsNullOrWhiteSpace(err) ? err.Trim() : stdout.Trim();
                return (false, $"Falha ao alterar arranque de '{serviceName}': {msg}");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("RemoteServicesManager", $"Erro ao alterar arranque de '{serviceName}'", ex);
                return (false, $"Falha ao alterar arranque de '{serviceName}': {ex.Message}");
            }
        }
    }
}
