using System;
using RotinaRemote.Client.ViewModels;

namespace RotinaRemote.Client.Models
{
    public class IncomingConnectionItem : ViewModelBase
    {
        private string _status = "Ativa";
        private bool _isActive = true;
        private DateTime? _endTime;
        private string _transportType = "Direto (P2P)";
        private string _permission = "Controlo Total";
        private string _clientResolution = "Automática";
        private string _remoteDeviceId = string.Empty;
        private string _remoteIp = string.Empty;
        private string _city = string.Empty;
        private string _country = string.Empty;
        private string _location = string.Empty;

        public string ConnectionId { get; set; } = Guid.NewGuid().ToString("N");

        public string RemoteDeviceId
        {
            get => _remoteDeviceId;
            set => SetProperty(ref _remoteDeviceId, value);
        }

        public string RemoteIp
        {
            get => _remoteIp;
            set => SetProperty(ref _remoteIp, value);
        }

        public string City
        {
            get => _city;
            set => SetProperty(ref _city, value);
        }

        public string Country
        {
            get => _country;
            set => SetProperty(ref _country, value);
        }

        public string Location
        {
            get => _location;
            set => SetProperty(ref _location, value);
        }

        public string TargetRotinaId { get; set; } = string.Empty;
        public string ExecutionMode { get; set; } = "Aplicação Desktop";
        public DateTime StartTime { get; set; } = DateTime.Now;

        public DateTime? EndTime
        {
            get => _endTime;
            set
            {
                if (SetProperty(ref _endTime, value))
                {
                    OnPropertyChanged(nameof(Duration));
                    OnPropertyChanged(nameof(DurationFormatted));
                }
            }
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (SetProperty(ref _isActive, value))
                {
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public string TransportType
        {
            get => _transportType;
            set => SetProperty(ref _transportType, value);
        }

        public string Permission
        {
            get => _permission;
            set => SetProperty(ref _permission, value);
        }

        public string ClientResolution
        {
            get => _clientResolution;
            set => SetProperty(ref _clientResolution, value);
        }

        public TimeSpan Duration => (EndTime ?? DateTime.Now) - StartTime;

        public string DurationFormatted
        {
            get
            {
                var d = Duration;
                if (d.TotalHours >= 1)
                    return $"{(int)d.TotalHours:D2}:{d.Minutes:D2}:{d.Seconds:D2}";
                return $"{d.Minutes:D2}:{d.Seconds:D2}";
            }
        }

        public string StartTimeFormatted => StartTime.ToString("HH:mm:ss");

        public string StatusColor => IsActive ? "#10B981" : "#94A3B8";

        public void NotifyDurationChanged()
        {
            OnPropertyChanged(nameof(Duration));
            OnPropertyChanged(nameof(DurationFormatted));
        }
    }
}
