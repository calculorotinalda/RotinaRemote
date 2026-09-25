using System;
using System.Windows;
using System.Windows.Threading;
using RotinaRemote.Core.Models;

namespace RotinaRemote.Client.Views
{
    public partial class PermissionDialogWindow : Window
    {
        public bool IsApproved { get; private set; }
        public SessionPermission GrantedPermissions { get; private set; } = SessionPermission.None;

        private DispatcherTimer? _countdownTimer;
        private int _secondsRemaining = 30;

        public PermissionDialogWindow(string remoteId, string ip = "", string city = "", string country = "", string location = "")
        {
            InitializeComponent();

            // Formatação do ID para leitura fácil (ex: 729 710 658)
            if (DeviceId.TryParse(remoteId, out var parsedId))
            {
                TxtRemoteId.Text = parsedId.Formatted;
            }
            else
            {
                TxtRemoteId.Text = !string.IsNullOrWhiteSpace(remoteId) ? remoteId : "Desconhecido";
            }

            TxtRemoteIp.Text = !string.IsNullOrWhiteSpace(ip) ? ip : "Desconhecido";
            TxtRemoteCity.Text = !string.IsNullOrWhiteSpace(city) ? city : "Desconhecida";
            TxtRemoteCountry.Text = !string.IsNullOrWhiteSpace(country) ? country : "Desconhecido";
            TxtRemoteLocation.Text = !string.IsNullOrWhiteSpace(location) ? location : (!string.IsNullOrWhiteSpace(city) ? $"{city}, {country}" : "Desconhecida");

            // Temporizador de 30 segundos com auto-rejeição
            _countdownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _countdownTimer.Tick += (s, e) =>
            {
                _secondsRemaining--;
                if (_secondsRemaining > 0)
                {
                    TxtCountdown.Text = $"Aguardando a sua resposta (rejeição automática em {_secondsRemaining}s)...";
                }
                else
                {
                    _countdownTimer.Stop();
                    IsApproved = false;
                    DialogResult = false;
                    Close();
                }
            };
            _countdownTimer.Start();
        }

        private void OnAcceptClicked(object sender, RoutedEventArgs e)
        {
            _countdownTimer?.Stop();
            IsApproved = true;
            GrantedPermissions = SessionPermission.None;

            if (ChkViewScreen.IsChecked == true) GrantedPermissions |= SessionPermission.ViewScreen;
            if (ChkControlMouse.IsChecked == true) GrantedPermissions |= SessionPermission.ControlMouse;
            if (ChkControlKeyboard.IsChecked == true) GrantedPermissions |= SessionPermission.ControlKeyboard;
            if (ChkFileTransfer.IsChecked == true) GrantedPermissions |= SessionPermission.FileTransfer;
            if (ChkClipboard.IsChecked == true) GrantedPermissions |= SessionPermission.ClipboardSync;

            DialogResult = true;
            Close();
        }

        private void OnRejectClicked(object sender, RoutedEventArgs e)
        {
            _countdownTimer?.Stop();
            IsApproved = false;
            GrantedPermissions = SessionPermission.None;
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _countdownTimer?.Stop();
            base.OnClosed(e);
        }
    }
}
