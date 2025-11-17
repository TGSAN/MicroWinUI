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
using Mile.Xaml.Interop;
using Windows.Foundation; // for Size
using Windows.UI.Xaml.Media; // for Brush
using Windows.UI; // for Color fallback
using Windows.UI.ViewManagement; // for UISettings

namespace MicroWinUI
{
    internal class CodePage : Page
    {
        CoreWindow coreWindow = CoreWindow.GetForCurrentThread();
        IslandWindow coreWindowHost;
        DisplayEnhancementOverride displayEnhancementOverride;
        BrightnessOverride brightnessOverride;
        DisplayInformation displayInfo;
        ScrollViewer scrollViewer; // scroll container
        StackPanel mainStackPanel;
        TextBlock displayInfoTextBlock;
        Border leftCard;
        Border rightCard;
        StackPanel rightPanel; // holds buttons + sdr/hdr vertically
        Grid horizontalContainer; // Grid with adaptive spacers
        StackPanel verticalContainer; // vertical stack of left/right sections
        string sdrDemoPath = @"C:\Windows\SystemResources\Windows.UI.SettingsAppThreshold\SystemSettings\Assets\SDRSample.mkv";
        string hdrDemoPath = @"C:\Windows\SystemResources\Windows.UI.SettingsAppThreshold\SystemSettings\Assets\HDRSample.mkv";
        MediaPlayerElement sdrDemoPlayer;
        MediaPlayerElement hdrDemoPlayer;
        List<DisplayMonitor> monitors = new List<DisplayMonitor>();
        UISettings uiSettings; // 监听系统颜色/主题变化

        public CodePage(IslandWindow coreWindowHost)
        {
            this.coreWindowHost = coreWindowHost;
            this.coreWindowHost.coreWindowHWND = this.coreWindow.GetInterop().GetWindowHandle();

            uiSettings = new UISettings();
            uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged; // 系统主题或强调色变化时触发

            new Task(async () =>
            {
                while (true)
                {
                    _ = coreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () =>
                    {
                        var bounds = coreWindow.Bounds;
                        Debug.WriteLine($"CodePage CoreWindow Bounds: {bounds.Width} x {bounds.Height}, X: {bounds.X}, Y: {bounds.Y}");
                    });
                    await Task.Delay(1000);
                }
            }).Start();
            brightnessOverride = BrightnessOverride.GetForCurrentView();
            brightnessOverride.IsOverrideActiveChanged += (s, e) =>
            {
                var brightnessSettings = BrightnessOverrideSettings.CreateFromLevel(s.BrightnessLevel);
                Debug.WriteLine($"IsOverrideActiveChanged: {s.IsOverrideActive}");
                Debug.WriteLine($"BrightnessLevelChanged: {s.BrightnessLevel}, {brightnessSettings.DesiredNits} Nits");
                //BrightnessPersistence.TryPersistBrightness(s.BrightnessLevel);
                _ = this.coreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () =>
                    {
                        UpdateDisplayInfo();
                    });
            };
            brightnessOverride.StartOverride();
            displayEnhancementOverride = DisplayEnhancementOverride.GetForCurrentView();
            var colorSettings = ColorOverrideSettings.CreateFromDisplayColorOverrideScenario(DisplayColorOverrideScenario.Accurate);
            displayEnhancementOverride.ColorOverrideSettings = colorSettings;
            if (displayEnhancementOverride.CanOverride)
            {
                displayEnhancementOverride.RequestOverride();
            }
            //var brightnessSettings = BrightnessOverrideSettings.CreateFromNits(80);
            //brightnessOverride.SetBrightnessLevel(brightnessSettings.DesiredLevel, DisplayBrightnessOverrideOptions.None);
            brightnessOverride.SetBrightnessScenario(DisplayBrightnessScenario.DefaultBrightness, DisplayBrightnessOverrideOptions.None);
            displayInfo = DisplayInformation.GetForCurrentView();
            displayInfo.AdvancedColorInfoChanged += DisplayInfo_AdvancedColorInfoChanged;

            // Root stack panel that will host either verticalContainer or horizontalContainer (within ScrollViewer)
            mainStackPanel = new StackPanel
            {
                Padding = new Thickness(0, 32, 0, 32),
                Orientation = Orientation.Vertical,
                Spacing = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center // vertical center when content smaller than viewport
            };

            displayInfoTextBlock = new TextBlock
            {
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480
            };

            leftCard = new Border
            {
                Child = displayInfoTextBlock,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(8, 0, 8, 0)
            };

            rightPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            rightCard = new Border
            {
                Child = rightPanel,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(8, 0, 8, 0)
            };

            var buttonsStackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var openHdrSettingsButton = new Button
            {
                Content = "HDR 设置",
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 16, 0)
            };
            openHdrSettingsButton.Click += OpenHdrSettingsButton_Click;
            var restartButton = new Button
            {
                Content = "重启程序",
                CornerRadius = new CornerRadius(4)
            };
            restartButton.Click += RestartButton_Click;
            buttonsStackPanel.Children.Add(openHdrSettingsButton);
            buttonsStackPanel.Children.Add(restartButton);
            rightPanel.Children.Add(buttonsStackPanel);

            if (File.Exists(sdrDemoPath) && File.Exists(hdrDemoPath))
            {
                var sdrHdrStackPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                restartButton.BorderThickness = new Thickness(2);
                var sdrStackPanel = new StackPanel { Orientation = Orientation.Vertical };
                sdrDemoPlayer = new MediaPlayerElement
                {
                    AutoPlay = true,
                    AreTransportControlsEnabled = false,
                    Source = MediaSource.CreateFromUri(new Uri(sdrDemoPath))
                };
                sdrDemoPlayer.MediaPlayer.SystemMediaTransportControls.IsEnabled = false;
                sdrDemoPlayer.MediaPlayer.IsLoopingEnabled = true;
                sdrDemoPlayer.MediaPlayer.IsMuted = true;
                sdrDemoPlayer.Width = 160;
                sdrDemoPlayer.Height = 90;
                sdrDemoPlayer.Margin = new Thickness(0, 0, 16, 0);
                sdrStackPanel.Children.Add(sdrDemoPlayer);
                sdrStackPanel.Children.Add(new TextBlock
                {
                    Text = "SDR 内容",
                    FontSize = 12,
                    Opacity = 0.5,
                    Margin = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                sdrHdrStackPanel.Children.Add(sdrStackPanel);

                var hdrStackPanel = new StackPanel { Orientation = Orientation.Vertical };
                hdrDemoPlayer = new MediaPlayerElement
                {
                    AutoPlay = true,
                    AreTransportControlsEnabled = false,
                    Source = MediaSource.CreateFromUri(new Uri(hdrDemoPath))
                };
                hdrDemoPlayer.MediaPlayer.SystemMediaTransportControls.IsEnabled = false;
                hdrDemoPlayer.MediaPlayer.IsLoopingEnabled = true;
                hdrDemoPlayer.MediaPlayer.IsMuted = true;
                hdrDemoPlayer.Width = 160;
                hdrDemoPlayer.Height = 90;
                hdrStackPanel.Children.Add(hdrDemoPlayer);
                hdrStackPanel.Children.Add(new TextBlock
                {
                    Text = "HDR 内容",
                    FontSize = 12,
                    Opacity = 0.5,
                    Margin = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                sdrHdrStackPanel.Children.Add(hdrStackPanel);
                rightPanel.Children.Add(sdrHdrStackPanel);
            }

            verticalContainer = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            verticalContainer.Children.Add(leftCard);
            verticalContainer.Children.Add(rightCard);

            horizontalContainer = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            horizontalContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            horizontalContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            horizontalContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            horizontalContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            horizontalContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(leftCard, 1);
            Grid.SetColumn(rightCard, 3);

            // start vertical
            mainStackPanel.Children.Add(verticalContainer);

            scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = mainStackPanel
            };

            Content = scrollViewer;
            InvalidateArrange();
            coreWindowHost.Backdrop = IslandWindow.SystemBackdrop.Tabbed;
            UpdateDisplayInfo();
            mainStackPanel.Loaded += MainStackPanel_Loaded;
            this.SizeChanged += CodePage_SizeChanged;
            this.Loaded += CodePage_Loaded;
            UpdateResponsiveLayout();
        }

        private void CodePage_Loaded(object sender, RoutedEventArgs e)
        {
            // await LoadMonitorsAsync();
            // ensure layout evaluates once content is loaded
            UpdateResponsiveLayout();
            ApplyCardBackgroundForTheme(); // 初始化主题背景
        }

        private void UiSettings_ColorValuesChanged(UISettings sender, object args)
        {
            // 回到 UI 线程应用新背景
            _ = coreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ApplyCardBackgroundForTheme();
            });
        }

        private void ApplyCardBackgroundForTheme()
        {
            // 使用宿主的 ActualTheme 判断深浅色；IslandWindow 会在系统暗/亮模式改变时刷新内部值
            var theme = coreWindowHost?.ActualTheme ?? ElementTheme.Light;
            if (theme == ElementTheme.Dark)
            {
                // 深色：稍暗半透明
                leftCard.Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
                leftCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0x00, 0x00));
                rightCard.Background = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
                rightCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0x00, 0x00));
            }
            else
            {
                // 浅色：白色半透明
                leftCard.Background = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
                leftCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0x00, 0x00));
                rightCard.Background = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
                rightCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0x00, 0x00));
            }
        }

        private void SwitchToHorizontal()
        {
            if (mainStackPanel.Children.Count == 0 || mainStackPanel.Children[0] != horizontalContainer)
            {
                verticalContainer.Children.Remove(leftCard);
                verticalContainer.Children.Remove(rightCard);
                horizontalContainer.Children.Clear();
                Grid.SetColumn(leftCard, 1);
                Grid.SetColumn(rightCard, 3);
                horizontalContainer.Children.Add(leftCard);
                horizontalContainer.Children.Add(rightCard);
                mainStackPanel.Children.Clear();
                mainStackPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
                mainStackPanel.Children.Add(horizontalContainer);
            }
        }

        private void SwitchToVertical()
        {
            if (mainStackPanel.Children.Count == 0 || mainStackPanel.Children[0] != verticalContainer)
            {
                horizontalContainer.Children.Clear();
                verticalContainer.Children.Clear();
                verticalContainer.Children.Add(leftCard);
                verticalContainer.Children.Add(rightCard);
                mainStackPanel.Children.Clear();
                mainStackPanel.HorizontalAlignment = HorizontalAlignment.Center;
                mainStackPanel.Children.Add(verticalContainer);
            }
        }

        private void CodePage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateResponsiveLayout(e.NewSize.Width);
        }

        private void UpdateResponsiveLayout(double availableWidth = -1)
        {
            try
            {
                if (availableWidth <= 0)
                {
                    availableWidth = coreWindow.Bounds.Width;
                }

                var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
                leftCard.Measure(infinite);
                rightCard.Measure(infinite);
                double requiredWidth = leftCard.DesiredSize.Width + rightCard.DesiredSize.Width + 32;
                bool shouldBeHorizontal = requiredWidth <= availableWidth;
                if (shouldBeHorizontal)
                {
                    SwitchToHorizontal();
                }
                else
                {
                    SwitchToVertical();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateResponsiveLayout failed: {ex}");
            }
        }

        private void DisplayInfo_AdvancedColorInfoChanged(DisplayInformation sender, object args)
        {
            UpdateDisplayInfo();
        }

        private async Task<DisplayMonitor> GetCurrentDisplayMonitorForCoreWindow()
        {
            try
            {
                var hwnd = coreWindowHost.coreWindowHWND;
                string interfaceId = Win32API.TryGetMonitorInterfaceIdFromWindow(hwnd);
                if (!string.IsNullOrEmpty(interfaceId))
                {
                    try
                    {
                        return await DisplayMonitor.FromInterfaceIdAsync(interfaceId);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"DisplayMonitor resolve failed: {ex.Message}");
                    }
                }
                else
                {
                    Debug.WriteLine("Could not resolve interface id for CoreWindow monitor.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error resolving CoreWindow monitor: {ex}");
            }
            return null;
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
            if (sdrDemoPlayer != null && hdrDemoPlayer != null)
            {
                sdrDemoPlayer.MediaPlayer.Play();
                hdrDemoPlayer.MediaPlayer.Play();
                hdrDemoPlayer.MediaPlayer.PlaybackSession.SeekCompleted += (s, ev) =>
                {
                    _ = this.coreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
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
            coreWindowHost.Content = new CodePage(coreWindowHost);
        }

        private async void UpdateDisplayInfo()
        {
            var currentDisplayMonitor = await GetCurrentDisplayMonitorForCoreWindow();
            var capabilities = displayEnhancementOverride.GetCurrentDisplayEnhancementOverrideCapabilities();
            var currentBrightnessSettings = BrightnessOverrideSettings.CreateFromLevel(brightnessOverride.BrightnessLevel);
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
            if (capabilities.IsBrightnessControlSupported)
            {
                displayInfoStringBuilder.AppendLine($"精确式系统亮度调节：{(capabilities.IsBrightnessNitsControlSupported ? "支持" : "不支持")}");
                if (nitsRanges.Count > 0)
                {
                    displayInfoStringBuilder.AppendLine($"精确式系统亮度调节精度：{nitsRanges[0].StepSizeNits} 尼特");
                    displayInfoStringBuilder.AppendLine($"精确式系统亮度调节最高亮度：{nitsRanges[0].MaxNits} 尼特");
                    displayInfoStringBuilder.AppendLine($"精确式系统亮度调节最低亮度：{nitsRanges[0].MinNits} 尼特");
                }
                displayInfoStringBuilder.AppendLine($"");
                displayInfoStringBuilder.AppendLine($"系统 SDR 亮度: {currentBrightnessSettings.DesiredLevel * 100}%{(capabilities.IsBrightnessNitsControlSupported ? $" ({currentBrightnessSettings.DesiredNits} 尼特)" : "")}");
            }
            displayInfoStringBuilder.AppendLine($"");
            displayInfoStringBuilder.AppendLine($"HDR10：{(colorInfo.IsHdrMetadataFormatCurrentlySupported(HdrMetadataFormat.Hdr10) ? "支持" : "不支持")}");
            displayInfoStringBuilder.AppendLine($"HDR10+：{(colorInfo.IsHdrMetadataFormatCurrentlySupported(HdrMetadataFormat.Hdr10Plus) ? "支持" : "不支持")}");
            displayInfoStringBuilder.AppendLine($"Dolby Vision：{(currentDisplayMonitor?.IsDolbyVisionSupportedInHdrMode == true ? "支持" : "不支持")}");
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
            displayInfoTextBlock.Text = displayInfoStringBuilder.ToString().Trim();
        }
    }
}
