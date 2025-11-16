using MicroWinUICore;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Display;
using Windows.Devices.Enumeration;
using Windows.Foundation.Metadata;
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
        DisplayEnhancementOverride displayEnhancementOverride;
        BrightnessOverride brightnessOverride;
        DisplayInformation displayInfo;
        StackPanel mainStackPanel;
        TextBlock displayInfoTextBlock;
        string sdrDemoPath = @"C:\Windows\SystemResources\Windows.UI.SettingsAppThreshold\SystemSettings\Assets\SDRSample.mkv";
        string hdrDemoPath = @"C:\Windows\SystemResources\Windows.UI.SettingsAppThreshold\SystemSettings\Assets\HDRSample.mkv";
        MediaPlayerElement sdrDemoPlayer;
        MediaPlayerElement hdrDemoPlayer;
        List<DisplayMonitor> monitors = new List<DisplayMonitor>();

        public CodePage(IslandWindow coreWindow)
        {
            this.rtCoreWindow = CoreWindow.GetForCurrentThread();
            this.coreWindow = coreWindow;
            brightnessOverride = BrightnessOverride.GetForCurrentView();
            displayEnhancementOverride = DisplayEnhancementOverride.GetForCurrentView();
            var colorSettings = ColorOverrideSettings.CreateFromDisplayColorOverrideScenario(DisplayColorOverrideScenario.Accurate);
            displayEnhancementOverride.ColorOverrideSettings = colorSettings;
            if (displayEnhancementOverride.CanOverride) 
            {
                displayEnhancementOverride.RequestOverride();
            }
            brightnessOverride.IsOverrideActiveChanged += (s, e) =>
            {
                Debug.WriteLine($"IsOverrideActiveChanged: {s.IsOverrideActive}");
                Debug.WriteLine($"BrightnessLevelChanged: {s.BrightnessLevel}");
            };
            var brightnessSettings = BrightnessOverrideSettings.CreateFromNits(172);
            
            Debug.WriteLine(brightnessSettings.DesiredLevel);
            brightnessOverride.StartOverride();
            brightnessOverride.SetBrightnessLevel(brightnessSettings.DesiredLevel, DisplayBrightnessOverrideOptions.None);
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

        private async Task LoadMonitorsAsync()
        {
            try
            {
                var selector = DisplayMonitor.GetDeviceSelector();
                var devices = await DeviceInformation.FindAllAsync(selector);
                if (devices.Count == 0)
                {
                    Debug.WriteLine("No DisplayMonitor devices found.");
                    return;
                }

                monitors.Clear();
                foreach (var di in devices)
                {
                    try
                    {
                        DisplayMonitor monitor = null;
                        // Prefer FromInterfaceIdAsync when available; the Id returned by this selector is typically a device interface id
                        if (ApiInformation.IsMethodPresent("Windows.Devices.Display.DisplayMonitor", "FromInterfaceIdAsync"))
                        {
                            monitor = await DisplayMonitor.FromInterfaceIdAsync(di.Id);
                        }
                        else
                        {
                            monitor = await DisplayMonitor.FromIdAsync(di.Id);
                        }

                        if (monitor != null)
                        {
                            monitors.Add(monitor);
                            Debug.WriteLine($"Display device: {di.Name} | Kind: {di.Kind} | Id: {di.Id} -> DisplayMonitor obtained: {monitor.DisplayName} ({monitor.ConnectionKind}, {monitor.UsageKind})");
                            Debug.WriteLine($"Dolby Vision: {monitor.IsDolbyVisionSupportedInHdrMode}");
                            Debug.WriteLine($"Dolby Vision: {monitor.IsDolbyVisionSupportedInHdrMode}");
                        }
                        else
                        {
                            Debug.WriteLine($"Display device: {di.Name} | Id: {di.Id} -> DisplayMonitor is null.");
                        }
                    }
                    catch (FileNotFoundException fnf)
                    {
                        Debug.WriteLine($"DisplayMonitor not found for {di.Name} | Id: {di.Id}. FileNotFoundException: {fnf.Message}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to create DisplayMonitor for device {di.Name} | Id: {di.Id}. Exception: {ex}");
                    }
                }

                // dump properties of first device for diagnostics
                try
                {
                    foreach (var key in devices[0].Properties.Keys)
                    {
                        Debug.WriteLine($"Property Key: {key} | Value: {devices[0].Properties[key]}");
                    }
                    Debug.WriteLine($"Display device: {devices[0].Name} | Id: {devices[0].Id}");
                }
                catch { }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Display enumeration failed: {ex}");
            }
        }

        private void OpenHdrSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("ms-settings:display-hdr");
        }

        private async void MainStackPanel_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure monitors are resolved asynchronously on UI thread
            await LoadMonitorsAsync();

            if (sdrDemoPlayer != null && hdrDemoPlayer != null)
            {
                sdrDemoPlayer.MediaPlayer.Play();
                hdrDemoPlayer.MediaPlayer.Play();
                hdrDemoPlayer.MediaPlayer.PlaybackSession.SeekCompleted += (s, ev) =>
                {
                    _ = this.rtCoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () =>
                    {
                        Debug.WriteLine("SeekCompleted sync position");
                        hdrDemoPlayer.MediaPlayer.Pause();
                        sdrDemoPlayer.MediaPlayer.Pause();
                        sdrDemoPlayer.MediaPlayer.PlaybackSession.Position = hdrDemoPlayer.MediaPlayer.PlaybackSession.Position;
                        hdrDemoPlayer.MediaPlayer.Play();
                        sdrDemoPlayer.MediaPlayer.Play();
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
            var capabilities = displayEnhancementOverride.GetCurrentDisplayEnhancementOverrideCapabilities();
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
            var nitsRanges = capabilities.GetSupportedNitRanges();
            var displayInfoStringBuilder = new StringBuilder();
            displayInfoStringBuilder.AppendLine($"系统亮度调节：{(capabilities.IsBrightnessControlSupported ? "支持" : "不支持")}");
            displayInfoStringBuilder.AppendLine($"精确式系统亮度调节：{(capabilities.IsBrightnessNitsControlSupported ? "支持" : "不支持")}");
            if (nitsRanges.Count > 0) 
            {
                displayInfoStringBuilder.AppendLine($"精确式系统亮度调节精度：{nitsRanges[0].StepSizeNits} 尼特");
                displayInfoStringBuilder.AppendLine($"精确式系统亮度调节最高亮度：{nitsRanges[0].MaxNits} 尼特");
                displayInfoStringBuilder.AppendLine($"精确式系统亮度调节最低亮度：{nitsRanges[0].MinNits} 尼特");
            }
            displayInfoStringBuilder.AppendLine($"");
            displayInfoStringBuilder.AppendLine($"HDR10：{(colorInfo.IsHdrMetadataFormatCurrentlySupported(HdrMetadataFormat.Hdr10) ? "支持" : "不支持")}");
            displayInfoStringBuilder.AppendLine($"HDR10+：{(colorInfo.IsHdrMetadataFormatCurrentlySupported(HdrMetadataFormat.Hdr10Plus) ? "支持" : "不支持")}");
            displayInfoStringBuilder.AppendLine($"");
            displayInfoStringBuilder.AppendLine($"最高 HDR 亮度（峰值）：{colorInfo.MaxLuminanceInNits} 尼特");
            displayInfoStringBuilder.AppendLine($"最高 HDR 亮度（全屏）：{colorInfo.MaxAverageFullFrameLuminanceInNits} 尼特");
            displayInfoStringBuilder.AppendLine($"最低亮度：{colorInfo.MinLuminanceInNits} 尼特");
            displayInfoStringBuilder.AppendLine($"SDR 亮度：{colorInfo.SdrWhiteLevelInNits} 尼特");
            displayInfoStringBuilder.AppendLine($"");
            displayInfoStringBuilder.AppendLine($"色彩模式：{advancedColorStr}");
            displayInfoStringBuilder.AppendLine($"");
            displayInfoStringBuilder.AppendLine($"红：{colorInfo.RedPrimary}");
            displayInfoStringBuilder.AppendLine($"绿：{colorInfo.GreenPrimary}");
            displayInfoStringBuilder.AppendLine($"蓝：{colorInfo.BluePrimary}");
            displayInfoStringBuilder.AppendLine($"");
            displayInfoStringBuilder.AppendLine($"白点：{colorInfo.WhitePoint}");
            displayInfoTextBlock.Text = displayInfoStringBuilder.ToString();
        }

        private void DisplayInfo_AdvancedColorInfoChanged(DisplayInformation sender, object args)
        {
            UpdateDisplayInfo();
        }
    }
}
