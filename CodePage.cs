using MicroWinUICore;
using System;
using System.Diagnostics;
using System.IO;
using Windows.Graphics.Display;
using Windows.Media.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MicroWinUI
{
    internal class CodePage : Page
    {
        CoreWindow rtCoreWindow;
        IslandWindow coreWindow;
        DisplayInformation displayInfo;
        StackPanel mainStackPanel;
        TextBlock displayInfoTextBlock;
        string sdrDemoPath = @"C:\Windows\SystemResources\Windows.UI.SettingsAppThreshold\SystemSettings\Assets\SDRSample.mkv";
        string hdrDemoPath = @"C:\Windows\SystemResources\Windows.UI.SettingsAppThreshold\SystemSettings\Assets\HDRSample.mkv";
        MediaPlayerElement sdrDemoPlayer;
        MediaPlayerElement hdrDemoPlayer;

        public CodePage(IslandWindow coreWindow)
        {
            this.rtCoreWindow = CoreWindow.GetForCurrentThread();
            this.coreWindow = coreWindow;
            displayInfo = DisplayInformation.GetForCurrentView();
            displayInfo.AdvancedColorInfoChanged += DisplayInfo_AdvancedColorInfoChanged;
            mainStackPanel = new StackPanel();
            mainStackPanel.Orientation = Orientation.Vertical;
            mainStackPanel.Spacing = 32;
            mainStackPanel.HorizontalAlignment = HorizontalAlignment.Center;
            mainStackPanel.VerticalAlignment = VerticalAlignment.Center;
            displayInfoTextBlock = new TextBlock();
            mainStackPanel.Children.Add(displayInfoTextBlock);
            var buttonsStackPanel = new StackPanel();
            buttonsStackPanel.Orientation = Orientation.Horizontal;
            buttonsStackPanel.HorizontalAlignment = HorizontalAlignment.Center;
            buttonsStackPanel.VerticalAlignment = VerticalAlignment.Center;
            var openHdrSettingsButton = new Button();
            openHdrSettingsButton.Content = "HDR 设置";
            openHdrSettingsButton.CornerRadius = new CornerRadius(4);
            openHdrSettingsButton.Click += OpenHdrSettingsButton_Click;
            openHdrSettingsButton.Margin = new Thickness(0, 0, 16, 0);
            buttonsStackPanel.Children.Add(openHdrSettingsButton);
            var restartButton = new Button();
            restartButton.Content = "重启程序";
            restartButton.CornerRadius = new CornerRadius(4);
            restartButton.Click += RestartButton_Click;
            buttonsStackPanel.Children.Add(restartButton);
            mainStackPanel.Children.Add(buttonsStackPanel);
            if (File.Exists(sdrDemoPath) && File.Exists(hdrDemoPath))
            {
                var sdrHdrStackPanel = new StackPanel();
                restartButton.CornerRadius = new CornerRadius(4);
                restartButton.BorderThickness = new Thickness(2);
                sdrHdrStackPanel.Orientation = Orientation.Horizontal;
                sdrHdrStackPanel.HorizontalAlignment = HorizontalAlignment.Center;
                sdrHdrStackPanel.VerticalAlignment = VerticalAlignment.Center;
                var sdrStackPanel = new StackPanel();
                sdrStackPanel.Orientation = Orientation.Vertical;
                sdrDemoPlayer = new MediaPlayerElement();
                sdrDemoPlayer.AutoPlay = true;
                sdrDemoPlayer.AreTransportControlsEnabled = false;
                sdrDemoPlayer.Source = MediaSource.CreateFromUri(new Uri(sdrDemoPath));
                sdrDemoPlayer.MediaPlayer.SystemMediaTransportControls.IsEnabled = false;
                sdrDemoPlayer.MediaPlayer.IsLoopingEnabled = true;
                sdrDemoPlayer.MediaPlayer.IsMuted = true;
                sdrDemoPlayer.Width = 160;
                sdrDemoPlayer.Height = 90;
                sdrDemoPlayer.Margin = new Thickness(0, 0, 16, 0);
                sdrStackPanel.Children.Add(sdrDemoPlayer);
                var sdrTextBlock = new TextBlock();
                sdrTextBlock.Text = "SDR";
                sdrTextBlock.FontSize = 9;
                sdrTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
                sdrTextBlock.VerticalAlignment = VerticalAlignment.Center;
                sdrStackPanel.Children.Add(sdrTextBlock);
                sdrHdrStackPanel.Children.Add(sdrStackPanel);
                var hdrStackPanel = new StackPanel();
                hdrStackPanel.Orientation = Orientation.Vertical;
                hdrDemoPlayer = new MediaPlayerElement();
                hdrDemoPlayer.AutoPlay = true;
                hdrDemoPlayer.AreTransportControlsEnabled = false;
                hdrDemoPlayer.Source = MediaSource.CreateFromUri(new Uri(hdrDemoPath));
                hdrDemoPlayer.MediaPlayer.SystemMediaTransportControls.IsEnabled = false;
                hdrDemoPlayer.MediaPlayer.IsLoopingEnabled = true;
                hdrDemoPlayer.MediaPlayer.IsMuted = true;
                hdrDemoPlayer.Width = 160;
                hdrDemoPlayer.Height = 90;
                hdrStackPanel.Children.Add(hdrDemoPlayer);
                var hdrTextBlock = new TextBlock();
                hdrTextBlock.Text = "HDR";
                hdrTextBlock.FontSize = 9;
                hdrTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
                hdrTextBlock.VerticalAlignment = VerticalAlignment.Center;
                hdrStackPanel.Children.Add(hdrTextBlock);
                sdrHdrStackPanel.Children.Add(hdrStackPanel);
                mainStackPanel.Children.Add(sdrHdrStackPanel);
            }
            Content = mainStackPanel;
            InvalidateArrange();
            coreWindow.Backdrop = IslandWindow.SystemBackdrop.Tabbed;
            UpdateDisplayInfo();
            mainStackPanel.Loaded += MainStackPanel_Loaded;
        }

        private void OpenHdrSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("ms-settings:display-hdr");
        }

        private void MainStackPanel_Loaded(object sender, RoutedEventArgs e)
        {
            if (sdrDemoPlayer != null && hdrDemoPlayer != null)
            {
                sdrDemoPlayer.MediaPlayer.Play();
                hdrDemoPlayer.MediaPlayer.Play();
                hdrDemoPlayer.MediaPlayer.PlaybackSession.PositionChanged += (s2, ev2) =>
                {
                    _ = this.rtCoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () =>
                    {
                        var timeDiffMs = Math.Abs((hdrDemoPlayer.MediaPlayer.PlaybackSession.Position - sdrDemoPlayer.MediaPlayer.PlaybackSession.Position).TotalMilliseconds);
                        Debug.WriteLine($"{sdrDemoPlayer.MediaPlayer.PlaybackSession.Position}, {hdrDemoPlayer.MediaPlayer.PlaybackSession.Position}");
                        Debug.WriteLine($"时间差：{timeDiffMs}");
                        if (timeDiffMs > 1)
                        {
                            Debug.WriteLine("同步时间轴");
                            sdrDemoPlayer.MediaPlayer.Pause();
                            hdrDemoPlayer.MediaPlayer.Pause();
                            sdrDemoPlayer.MediaPlayer.PlaybackSession.Position = TimeSpan.Zero;
                            sdrDemoPlayer.MediaPlayer.PlaybackSession.Position = hdrDemoPlayer.MediaPlayer.PlaybackSession.Position;
                            hdrDemoPlayer.MediaPlayer.PlaybackSession.Position = TimeSpan.Zero;
                            hdrDemoPlayer.MediaPlayer.PlaybackSession.Position = sdrDemoPlayer.MediaPlayer.PlaybackSession.Position;
                            sdrDemoPlayer.MediaPlayer.Play();
                            hdrDemoPlayer.MediaPlayer.Play();
                        }
                    });
                };
            }
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            coreWindow.Content = new CodePage(coreWindow);
        }

        private void UpdateDisplayInfo()
        {
            var colorInfo = displayInfo.GetAdvancedColorInfo();
            var advancedColor = colorInfo.CurrentAdvancedColorKind;
            var advancedColorStr = "SDR";
            switch (advancedColor)
            {
                case AdvancedColorKind.StandardDynamicRange:
                    advancedColorStr = "Standard Dynamic Range";
                    break;
                case AdvancedColorKind.WideColorGamut:
                    advancedColorStr = "Wide Color Gamut";
                    break;
                case AdvancedColorKind.HighDynamicRange:
                    advancedColorStr = "High Dynamic Range";
                    break;
                default:
                    advancedColorStr = advancedColor.ToString();
                    break;
            }
            displayInfoTextBlock.Text = $"最高亮度（峰值）：{colorInfo.MaxLuminanceInNits} 尼特";
            displayInfoTextBlock.Text += $"\r\n最高亮度（全屏）：{colorInfo.MaxAverageFullFrameLuminanceInNits} 尼特";
            displayInfoTextBlock.Text += $"\r\n最低亮度：{colorInfo.MinLuminanceInNits} 尼特";
            displayInfoTextBlock.Text += $"\r\nSDR亮度：{colorInfo.SdrWhiteLevelInNits} 尼特";
            displayInfoTextBlock.Text += $"\r\n";
            displayInfoTextBlock.Text += $"\r\n色彩模式：{advancedColorStr}";
            displayInfoTextBlock.Text += $"\r\n";
            displayInfoTextBlock.Text += $"\r\n红：{colorInfo.RedPrimary}";
            displayInfoTextBlock.Text += $"\r\n绿：{colorInfo.GreenPrimary}";
            displayInfoTextBlock.Text += $"\r\n蓝：{colorInfo.BluePrimary}";
            displayInfoTextBlock.Text += $"\r\n";
            displayInfoTextBlock.Text += $"\r\n白点：{colorInfo.WhitePoint}";
        }

        private void DisplayInfo_AdvancedColorInfoChanged(DisplayInformation sender, object args)
        {
            UpdateDisplayInfo();
        }
    }
}
