using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using RotinaRemote.Core.Configuration;
using RotinaRemote.Core.Logging;
using RotinaRemote.Network;
using RotinaRemote.Security;

namespace RotinaRemote.Client.Services
{
    public class RotinaRemoteWindowsService : ServiceBase
    {
        private P2PTransportListener? _listener;
        private LanDiscoveryService? _lanDiscovery;
        private SignalingClient? _signalingClient;
        private CancellationTokenSource? _cts;

        public RotinaRemoteWindowsService()
        {
            ServiceName = WindowsServiceManager.ServiceName;
            CanStop = true;
            CanShutdown = true;
        }

        protected override void OnStart(string[] args)
        {
            AppLogger.LogInfo("WindowsService", "RotinaRemote Background Service a iniciar via SCM...");
            _cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    var config = AppConfig.Load();
                    var identity = DeviceIdentity.LoadOrCreate();

                    _listener = new P2PTransportListener();
                    _listener.Start(config.P2pPort);

                    _lanDiscovery = new LanDiscoveryService();
                    _lanDiscovery.Start(identity.RawId, config.LanDiscoveryPort);

                    _signalingClient = new SignalingClient();
                    await _signalingClient.StartAsync(config.SignalingServerUrl, identity.RawId);

                    AppLogger.LogInfo("WindowsService", $"Serviço em execução ativa no ID {identity.FormattedId}.");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("WindowsService", "Erro ao inicializar serviço de fundo", ex);
                }
            });
        }

        protected override void OnStop()
        {
            AppLogger.LogInfo("WindowsService", "RotinaRemote Background Service a parar...");
            try
            {
                _cts?.Cancel();
                _listener?.Dispose();
                _lanDiscovery?.Dispose();
                _signalingClient?.Dispose();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("WindowsService", "Erro ao terminar serviço de fundo", ex);
            }
        }
    }
}
