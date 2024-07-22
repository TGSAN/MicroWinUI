using MicroWinUICore;
using System;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MicroWinUI
{
    public sealed partial class MainPage : Page
    {
        IslandWindow coreWindow;
        
        public MainPage(IslandWindow coreWindow)
        {
            this.coreWindow = coreWindow;
            this.InitializeComponent();
            
            MediaPlayer.Source = MediaSource.CreateFromUri(new Uri("C:\\Windows\\SystemResources\\Windows.UI.SettingsAppThreshold\\SystemSettings\\Assets\\SDRSample.mkv"));
            inkToolbar.TargetInkCanvas = inkCanvasDemo;
        }

        private void Button_Click(object sender, RoutedEventArgs _)
            => (sender as Button).Content = "Clicked";

        private void NoneBackdropRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            coreWindow.Backdrop = IslandWindow.SystemBackdrop.None;
        }

        private void AcrylicBackdropRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            coreWindow.Backdrop = IslandWindow.SystemBackdrop.Acrylic;
        }

        private void MicaBackdropRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            coreWindow.Backdrop = IslandWindow.SystemBackdrop.Mica;
        }

        private void MicaAltBackdropRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            coreWindow.Backdrop = IslandWindow.SystemBackdrop.Tabbed;
        }
    }
}
