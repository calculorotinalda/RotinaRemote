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

            // Ajusta limites máximos com base no ecrã ativo para garantir visibilidade total em qualquer resolução ou escala DPI
            try
            {
                MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height * 0.90);
                MaxWidth = Math.Max(460, SystemParameters.WorkArea.Width * 0.90);
            }
            catch { }

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

        private void OnSelectAllPermissionsClicked(object sender, RoutedEventArgs e)
        {
            ChkViewScreen.IsChecked = true;
            ChkControlMouse.IsChecked = true;
            ChkControlKeyboard.IsChecked = true;
            ChkFileTransfer.IsChecked = true;
            ChkClipboard.IsChecked = true;
        }

        private void OnViewOnlyPermissionsClicked(object sender, RoutedEventArgs e)
        {
            ChkViewScreen.IsChecked = true;
            ChkControlMouse.IsChecked = false;
            ChkControlKeyboard.IsChecked = false;
            ChkFileTransfer.IsChecked = false;
            ChkClipboard.IsChecked = false;
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
