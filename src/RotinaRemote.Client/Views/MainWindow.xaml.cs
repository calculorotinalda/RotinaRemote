using System.Windows;
using System.Windows.Input;
using RotinaRemote.Input;

namespace RotinaRemote.Client.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnRemoteScreenMouseMove(object sender, MouseEventArgs e)
        {
            if (RemoteScreenImage.ActualWidth > 0 && RemoteScreenImage.ActualHeight > 0)
            {
                var pos = e.GetPosition(RemoteScreenImage);
                double normX = pos.X / RemoteScreenImage.ActualWidth;
                double normY = pos.Y / RemoteScreenImage.ActualHeight;

                if (normX >= 0 && normX <= 1.0 && normY >= 0 && normY <= 1.0)
                {
                    InputInjector.InjectMouse(MouseEventType.Move, normX, normY);
                }
            }
        }

        private void OnRemoteScreenMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (RemoteScreenImage.ActualWidth > 0 && RemoteScreenImage.ActualHeight > 0)
            {
                var pos = e.GetPosition(RemoteScreenImage);
                double normX = pos.X / RemoteScreenImage.ActualWidth;
                double normY = pos.Y / RemoteScreenImage.ActualHeight;

                if (normX >= 0 && normX <= 1.0 && normY >= 0 && normY <= 1.0)
                {
                    InputInjector.InjectMouse(MouseEventType.LeftDown, normX, normY);
                    InputInjector.InjectMouse(MouseEventType.LeftUp, normX, normY);
                }
            }
        }

        private void OnRemoteScreenRightMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (RemoteScreenImage.ActualWidth > 0 && RemoteScreenImage.ActualHeight > 0)
            {
                var pos = e.GetPosition(RemoteScreenImage);
                double normX = pos.X / RemoteScreenImage.ActualWidth;
                double normY = pos.Y / RemoteScreenImage.ActualHeight;

                if (normX >= 0 && normX <= 1.0 && normY >= 0 && normY <= 1.0)
                {
                    InputInjector.InjectMouse(MouseEventType.RightDown, normX, normY);
                    InputInjector.InjectMouse(MouseEventType.RightUp, normX, normY);
                }
            }
        }
    }
}
