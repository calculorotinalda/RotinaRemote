using System.Windows;
using RotinaRemote.Core.Models;

namespace RotinaRemote.Client.Views
{
    public partial class PermissionDialogWindow : Window
    {
        public bool IsApproved { get; private set; }
        public SessionPermission GrantedPermissions { get; private set; }

        public PermissionDialogWindow(string remoteId, string remoteName)
        {
            InitializeComponent();
            TxtRemoteId.Text = remoteId;
            TxtRemoteName.Text = remoteName;
        }

        private void OnAcceptClicked(object sender, RoutedEventArgs e)
        {
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
            IsApproved = false;
            GrantedPermissions = SessionPermission.None;
            DialogResult = false;
            Close();
        }
    }
}
