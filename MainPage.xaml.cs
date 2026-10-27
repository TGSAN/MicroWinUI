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

        /// <summary>
        /// 加载嵌入资源中的界面 XAML。
        /// 字段回填与事件挂接都由 XamlPageLoader 按 x:Name / 事件属性自动完成，
        /// 页面代码与标准 XAML 工程保持一致。
        /// </summary>
        private void InitializeComponent()
        {
            XamlPageLoader.Initialize(this, "MainPage.xaml");
        }

        // 由 XamlPageLoader 按 x:Name 自动回填，与 XAML 编译器生成的字段等价。
        // 编译器看不到反射赋值，故抑制「未赋值/未使用」警告。
#pragma warning disable 0169, 0649
        private Slider slider;
        private ProgressBar ProgressBarPause;
        private ProgressBar ProgressBarError;
        private InkToolbar inkToolbar;
        private InkCanvas inkCanvasDemo;
        private MediaPlayerElement MediaPlayer;
#pragma warning restore 0169, 0649

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
