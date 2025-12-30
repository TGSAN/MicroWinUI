using MicroWinUICore;
using Mile.Xaml.Interop;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MicroWinUI
{
    public sealed partial class MainPage : Page
    {
        IslandWindow coreWindowHost;
        
        public MainPage(IslandWindow coreWindowHost)
        {
            this.coreWindowHost = coreWindowHost;
            this.InitializeComponent();
            
            MediaPlayer.Source = MediaSource.CreateFromUri(new Uri("C:\\Windows\\SystemResources\\Windows.UI.SettingsAppThreshold\\SystemSettings\\Assets\\SDRSample.mkv"));
            inkToolbar.TargetInkCanvas = inkCanvasDemo;
        }

        private void NoneBackdropRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            coreWindowHost.Backdrop = IslandWindow.SystemBackdrop.None;
        }

        private void AcrylicBackdropRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            coreWindowHost.Backdrop = IslandWindow.SystemBackdrop.Acrylic;
        }

        private void MicaBackdropRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            coreWindowHost.Backdrop = IslandWindow.SystemBackdrop.Mica;
        }

        private void MicaAltBackdropRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            coreWindowHost.Backdrop = IslandWindow.SystemBackdrop.Tabbed;
        }
    }
}
