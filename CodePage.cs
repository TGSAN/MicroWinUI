using MicroWinUICore;
using Mile.Xaml.Interop;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading; // for ConcurrentDictionary
using System.Threading.Tasks;
using Windows.Devices.Display;
using Windows.Devices.Enumeration;
using Windows.Foundation; // for Size
using Windows.Foundation.Metadata;
using Windows.Graphics.Display;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI; // for Color fallback
using Windows.UI.Core;
using Windows.UI.ViewManagement; // for UISettings
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media; // for Brush

namespace MicroWinUI
{
    internal class CodePage : Page
    {
        CoreWindow coreWindow = CoreWindow.GetForCurrentThread();
        IslandWindow coreWindowHost;
        DisplayEnhancementOverride displayEnhancementOverride;
        DisplayInformation displayInfo;
        ScrollViewer scrollViewer; // scroll container
        StackPanel mainStackPanel;
        Grid displayInfoTable;
        Border leftCard;
        Border rightCard;
        StackPanel rightPanel; // holds buttons + sdr/hdr vertically
        Grid horizontalContainer; // Grid with adaptive spacers
        StackPanel verticalContainer; // vertical stack of left/right sections
        TextBlock sdrBoostSliderLabel;
        Slider sdrBoostSlider;
        bool sdrBoostSliderDraging = false;
        public ToggleSwitch laptopKeepHDRBrightnessModeToggleSwitch;
        string sdrDemoPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\SystemResources\Windows.UI.SettingsAppThreshold\SystemSettings\Assets\SDRSample.mkv";
        string hdrDemoPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\SystemResources\Windows.UI.SettingsAppThreshold\SystemSettings\Assets\HDRSample.mkv";
        MediaPlayerElement sdrDemoPlayer;
        MediaPlayerElement hdrDemoPlayer;
        List<DisplayMonitor> monitors = new List<DisplayMonitor>();
        UISettings uiSettings; // 监听系统颜色/主题变化

        public bool IsBrightnessNitsControlSupportedForCurrentMonitor
        {
            get
            {
                var capabilities = displayEnhancementOverride.GetCurrentDisplayEnhancementOverrideCapabilities();
                return capabilities.IsBrightnessNitsControlSupported;
            }
        }

        public bool IsBrightnessControlSupportedForCurrentMonitor
        {
            get
            {
                var capabilities = displayEnhancementOverride.GetCurrentDisplayEnhancementOverrideCapabilities();
                return capabilities.IsBrightnessControlSupported;
            }
        }

        private readonly ConcurrentDictionary<string, ConcurrentDictionary<float, float>> BrightnessNitsCache = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<float, float>> HDRBrightnessLevelCache = new();
        private WmiBrightnessWatcher wmiBrightnessWatcher; // WMI brightness watcher for current monitor

        public CodePage(IslandWindow coreWindowHost)
        {
            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Disabled;

            this.coreWindowHost = coreWindowHost;
            this.coreWindowHost.coreWindowHWND = this.coreWindow.GetInterop().GetWindowHandle();

            uiSettings = new UISettings();
            uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged; // 系统主题或强调色变化时触发

            //new Task(async () =>
            //{
            //    while (true)
            //    {
            //        _ = coreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
            //        () =>
            //        {
            //            var bounds = coreWindow.Bounds;
            //            Debug.WriteLine($"CodePage CoreWindow Bounds: {bounds.Width} x {bounds.Height}, X: {bounds.X}, Y: {bounds.Y}");
            //        });
            //        await Task.Delay(1000);
            //    }
            //}).Start();

            displayEnhancementOverride = DisplayEnhancementOverride.GetForCurrentView();
            //var colorSettings = ColorOverrideSettings.CreateFromDisplayColorOverrideScenario(DisplayColorOverrideScenario.Accurate);
            //displayEnhancementOverride.ColorOverrideSettings = colorSettings;
            //if (displayEnhancementOverride.CanOverride)
            //{
            //    displayEnhancementOverride.RequestOverride();
            //}

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

            displayInfoTable = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            // 4列布局：* | Key | Value | *，内容居中且允许分割线贯穿
            displayInfoTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            displayInfoTable.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            displayInfoTable.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            displayInfoTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            leftCard = new Border
            {
                Child = displayInfoTable,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0, 8, 0, 8),
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
                Margin = new Thickness(0, 0, 0, 16),
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var openHdrSettingsButton = new Button
            {
                Content = "HDR 设置",
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 16, 0)
            };
            openHdrSettingsButton.Click += OpenHdrSettingsButton_Click;
            buttonsStackPanel.Children.Add(openHdrSettingsButton);

            var openHdrCalibrationSettingsButton = new Button
            {
                Content = "HDR 显示器校准",
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 16, 0)
            };
            openHdrCalibrationSettingsButton.Click += OpenHdrCalibrationSettingsButton_Click;
            buttonsStackPanel.Children.Add(openHdrCalibrationSettingsButton);

            rightPanel.Children.Add(buttonsStackPanel);

            var sdrBoostSliderPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 400
            };

            sdrBoostSliderLabel = new TextBlock
            {
                Text = "SDR 内容亮度",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            sdrBoostSliderPanel.Children.Add(sdrBoostSliderLabel);

            var sdrBoostSliderDesc = new TextBlock
            {
                Text = "将此窗口移动到要调整的显示器上，然后拖动滑块，直到 SDR 内容的亮度符合您的需要。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.75,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            sdrBoostSliderPanel.Children.Add(sdrBoostSliderDesc);

            sdrBoostSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                StepFrequency = 1,
                ManipulationMode = Windows.UI.Xaml.Input.ManipulationModes.TranslateRailsX,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            sdrBoostSlider.ValueChanged += SdrBoostSlider_ValueChanged;
            sdrBoostSlider.ManipulationStarting += (s, e) =>
            {
                sdrBoostSliderDraging = true;
            };
            sdrBoostSlider.ManipulationCompleted += (s, e) =>
            {
                sdrBoostSliderDraging = false;
                UpdateDisplayInfo();
            };
            sdrBoostSliderPanel.Children.Add(sdrBoostSlider);

            rightPanel.Children.Add(sdrBoostSliderPanel);

            if (File.Exists(sdrDemoPath) && File.Exists(hdrDemoPath))
            {
                var sdrHdrStackPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var sdrStackPanel = new StackPanel { Orientation = Orientation.Vertical };
                sdrDemoPlayer = new MediaPlayerElement
                {
                    AutoPlay = true,
                    AreTransportControlsEnabled = false
                };
                sdrDemoPlayer.SetMediaPlayer(null);
                sdrDemoPlayer.Width = 192;
                sdrDemoPlayer.Height = 108;
                sdrDemoPlayer.Margin = new Thickness(0, 0, 16, 0);
                sdrStackPanel.Children.Add(sdrDemoPlayer);
                sdrStackPanel.Children.Add(new TextBlock
                {
                    Text = "SDR 内容",
                    FontSize = 12,
                    Opacity = 0.75,
                    Margin = new Thickness(0, 4, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                sdrHdrStackPanel.Children.Add(sdrStackPanel);

                var hdrStackPanel = new StackPanel { Orientation = Orientation.Vertical };
                hdrDemoPlayer = new MediaPlayerElement
                {
                    AutoPlay = true,
                    AreTransportControlsEnabled = false
                };
                hdrDemoPlayer.SetMediaPlayer(null);
                hdrDemoPlayer.Width = 192;
                hdrDemoPlayer.Height = 108;
                hdrStackPanel.Children.Add(hdrDemoPlayer);
                hdrStackPanel.Children.Add(new TextBlock
                {
                    Text = "HDR 内容",
                    FontSize = 12,
                    Opacity = 0.75,
                    Margin = new Thickness(0, 4, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                sdrHdrStackPanel.Children.Add(hdrStackPanel);

                VideoStart();

                rightPanel.Children.Add(sdrHdrStackPanel);

                laptopKeepHDRBrightnessModeToggleSwitch = new ToggleSwitch
                {
                    Header = "自动保持 HDR 亮度内容",
                    FontSize = 12,
                    IsOn = false,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                laptopKeepHDRBrightnessModeToggleSwitch.Toggled += (s, e) =>
                {
                    UpdateDisplayInfo();
                };
                rightPanel.Children.Add(laptopKeepHDRBrightnessModeToggleSwitch);
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
            horizontalContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            horizontalContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            horizontalContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            horizontalContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            horizontalContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
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
            this.SizeChanged += CodePage_SizeChanged;
            this.Loaded += CodePage_Loaded;
            this.Unloaded += CodePage_Unloaded;
            UpdateResponsiveLayout();
            // Init WMI brightness watcher for current window's monitor
            InitializeWmiBrightnessWatcher();
        }

        public void VideoStop()
        {
            sdrDemoPlayer.Source = null;
            sdrDemoPlayer.SetMediaPlayer(null);
            hdrDemoPlayer.Source = null;
            hdrDemoPlayer.SetMediaPlayer(null);
        }

        public void VideoStart()
        {
            sdrDemoPlayer.SetMediaPlayer(new MediaPlayer());
            sdrDemoPlayer.MediaPlayer.SystemMediaTransportControls.IsEnabled = false;
            sdrDemoPlayer.MediaPlayer.IsLoopingEnabled = true;
            sdrDemoPlayer.MediaPlayer.IsMuted = true;
            sdrDemoPlayer.Source = MediaSource.CreateFromUri(new Uri(sdrDemoPath));

            hdrDemoPlayer.SetMediaPlayer(new MediaPlayer());
            hdrDemoPlayer.MediaPlayer.SystemMediaTransportControls.IsEnabled = false;
            hdrDemoPlayer.MediaPlayer.IsLoopingEnabled = true;
            hdrDemoPlayer.MediaPlayer.IsMuted = true;
            hdrDemoPlayer.Source = MediaSource.CreateFromUri(new Uri(hdrDemoPath));

            // Start Play
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

        public void SetNitsSync(float nits)
        {
            var hwnd = coreWindowHost?.coreWindowHWND ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero)
            {
                Debug.WriteLine($"Set SdrWhiteLevel {nits} Nits");
                SdrWhiteLevel.TrySetForWindow(hwnd, nits);
            }
            var level = TryGetLevelFromHDRBrightnessNits(nits);
            Brightness.TryPersistBrightness(level);
        }

        private void HDRBrightnessSyncBySystemBrightness(float nits)
        {
            if (laptopKeepHDRBrightnessModeToggleSwitch != null && laptopKeepHDRBrightnessModeToggleSwitch.IsEnabled && laptopKeepHDRBrightnessModeToggleSwitch.IsOn)
            {
                var hwnd = coreWindowHost?.coreWindowHWND ?? IntPtr.Zero;
                if (hwnd != IntPtr.Zero)
                {
                    Debug.WriteLine($"Set SdrWhiteLevel {nits} Nits");
                    SdrWhiteLevel.TrySetForWindow(hwnd, nits);
                }
            }
        }

        private void SystemBrightnessSyncByHDRBrightness(float nits)
        {
            if (laptopKeepHDRBrightnessModeToggleSwitch != null && laptopKeepHDRBrightnessModeToggleSwitch.IsEnabled && laptopKeepHDRBrightnessModeToggleSwitch.IsOn)
            {
                var level = TryGetLevelFromHDRBrightnessNits(nits);
                Brightness.TryPersistBrightness(level);
            }
        }

        private void SdrBoostSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (sdrBoostSliderDraging)
            {
                var nits = (((float)sdrBoostSlider.Value) * 4.0f) + 80; // 0-100 映射到 80-480 Nits
                Debug.WriteLine($"Set {nits} Nits");
                if (laptopKeepHDRBrightnessModeToggleSwitch != null && laptopKeepHDRBrightnessModeToggleSwitch.IsOn)
                {
                    Debug.WriteLine($"SystemBrightnessSyncByHDRBrightness");
                    SystemBrightnessSyncByHDRBrightness(nits);
                }
                else
                {
                    Debug.WriteLine($"TrySetForWindow");
                    // Apply to the monitor that hosts this CoreWindow
                    var hwnd = coreWindowHost?.coreWindowHWND ?? IntPtr.Zero;
                    if (hwnd != IntPtr.Zero)
                    {
                        _ = SdrWhiteLevel.TrySetForWindow(hwnd, nits);
                    }
                }
            }
        }

        private void CodePage_Unloaded(object sender, RoutedEventArgs e)
        {
            try { wmiBrightnessWatcher?.Dispose(); } catch { }
        }

        private void InitializeWmiBrightnessWatcher()
        {
            try
            {
                var hwnd = coreWindowHost.coreWindowHWND;
                var interfacePath = Win32API.TryGetMonitorInterfaceIdFromWindow(hwnd); // \\?\DISPLAY#...
                var wmiInstance = Win32API.TryConvertMonitorDevicePathToWmiInstanceName(interfacePath);
                if (!string.IsNullOrEmpty(wmiInstance))
                {
                    wmiBrightnessWatcher?.Dispose();
                    wmiBrightnessWatcher = new WmiBrightnessWatcher(wmiInstance);
                    wmiBrightnessWatcher.BrightnessChanged += (s, b) =>
                    {
                        Debug.WriteLine($"WMI BrightnessChanged: {b}% for {wmiInstance}");
                        var brightnessLevel = b * 0.001f;
                        var nits = TryGetNitsFromBrightnessLevel(brightnessLevel);
                        Debug.WriteLine($"{brightnessLevel}, {nits} Nits");
                        //BrightnessPersistence.TryPersistBrightness(brightnessLevel);
                        UpdateDisplayInfo();
                    };
                    wmiBrightnessWatcher.Start();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeWmiBrightnessWatcher failed: {ex}");
            }
        }

        private void CodePage_Loaded(object sender, RoutedEventArgs e)
        {
            // await LoadMonitorsAsync();
            // ensure layout evaluates once content is loaded
            UpdateResponsiveLayout();
            ApplyCardBackgroundForTheme(); // 初始化主题背景
            // 并行预热亮度百分比到尼特值的映射缓存
            if (this.IsBrightnessNitsControlSupportedForCurrentMonitor)
            {
                new Thread(() =>
                {
                    _ = BuildBrightnessMappingAsync();
                    _ = BuildHDRBrightnessMappingAsync();
                }).Start();
            }
            //GetLaptopPreciseKeepHDRBrightnessLevel().ContinueWith(task =>
            //{
            //    var result = task.Result;
            //    foreach (var item in result)
            //    {
            //        Debug.WriteLine($"Laptop Precise Keep HDR Brightness Level: {item * 100}");
            //    }
            //});
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
                double requiredWidth = leftCard.DesiredSize.Width + rightCard.DesiredSize.Width + 192;
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

        private void OpenHdrCalibrationSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("ms-windows-store://pdp?productId=9N7F2SM5D1LR&mode=mini");
        }

        public double GetValueAtRatio(double start, double end, double ratio)
        {
            return start + (end - start) * ratio;
        }

        private async void UpdateDisplayInfo()
        {
            await this.coreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    async () =>
                    {
                        var currentDisplayMonitor = await GetCurrentDisplayMonitorForCoreWindow();
                        var capabilities = displayEnhancementOverride.GetCurrentDisplayEnhancementOverrideCapabilities();
                        var brightnessLevel = Brightness.TryGetCurrentBrightnessLevel();
                        var brightnessNits = TryGetNitsFromBrightnessLevel(brightnessLevel);
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

                        // 重建表格
                        displayInfoTable.Children.Clear();
                        displayInfoTable.RowDefinitions.Clear();

                        Action addSeparator = () =>
                        {
                            var rowIndexSep = displayInfoTable.RowDefinitions.Count;
                            displayInfoTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                            var separator = new Border
                            {
                                Height = 1,
                                Margin = new Thickness(0, 4, 0, 4),
                                Background = new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x00, 0x00)),
                                HorizontalAlignment = HorizontalAlignment.Stretch
                            };
                            Grid.SetRow(separator, rowIndexSep);
                            Grid.SetColumn(separator, 0);
                            Grid.SetColumnSpan(separator, 4);
                            displayInfoTable.Children.Add(separator);
                        };

                        Action<string, string> addRow = (key, value) =>
                        {
                            var rowIndex = displayInfoTable.RowDefinitions.Count;
                            displayInfoTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                            var keyBlock = new TextBlock
                            {
                                Text = key,
                                FontSize = 14,
                                Margin = new Thickness(0, 4, 12, 4),
                                TextWrapping = TextWrapping.NoWrap,
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            var valueBlock = new TextBlock
                            {
                                Text = value,
                                FontSize = 14,
                                Opacity = 0.75,
                                Margin = new Thickness(12, 4, 0, 4),
                                TextWrapping = TextWrapping.Wrap,
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                                VerticalAlignment = VerticalAlignment.Center
                            };

                            Grid.SetRow(keyBlock, rowIndex);
                            Grid.SetColumn(keyBlock, 1);
                            Grid.SetRow(valueBlock, rowIndex);
                            Grid.SetColumn(valueBlock, 2);
                            displayInfoTable.Children.Add(keyBlock);
                            displayInfoTable.Children.Add(valueBlock);
                        };

                        addRow("系统亮度调节", capabilities.IsBrightnessControlSupported ? "支持" : "不支持");
                        if (capabilities.IsBrightnessControlSupported)
                        {
                            addRow("精确式系统亮度调节", capabilities.IsBrightnessNitsControlSupported ? "支持" : "不支持");
                            if (nitsRanges.Count > 0)
                            {
                                addRow("精确式系统亮度调节精度", $"{nitsRanges[0].StepSizeNits} 尼特");
                                addRow("精确式系统亮度调节最高亮度", $"{nitsRanges[0].MaxNits} 尼特");
                                addRow("精确式系统亮度调节最低亮度", $"{nitsRanges[0].MinNits} 尼特");
                            }
                            var sdrBrightness = (brightnessLevel * 100.0).ToString("F0");
                            var sdrValue = capabilities.IsBrightnessNitsControlSupported
                                ? $"{sdrBrightness}% ({brightnessNits} 尼特)"
                                : $"{sdrBrightness}%";
                            addRow("系统 SDR 亮度", sdrValue);
                        }
                        addSeparator();

                        addRow("HDR10", colorInfo.IsHdrMetadataFormatCurrentlySupported(HdrMetadataFormat.Hdr10) ? "支持" : "不支持");
                        addRow("HDR10+", colorInfo.IsHdrMetadataFormatCurrentlySupported(HdrMetadataFormat.Hdr10Plus) ? "支持" : "不支持");
                        addRow("Dolby Vision", (currentDisplayMonitor?.IsDolbyVisionSupportedInHdrMode == true) ? "支持" : "不支持");
                        addSeparator();

                        addRow("最高 HDR 亮度（峰值）", $"{colorInfo.MaxLuminanceInNits} 尼特");
                        addRow("最高 HDR 亮度（全屏）", $"{colorInfo.MaxAverageFullFrameLuminanceInNits} 尼特");
                        addRow("最低亮度", $"{colorInfo.MinLuminanceInNits} 尼特");
                        addRow("SDR 亮度", $"{colorInfo.SdrWhiteLevelInNits} 尼特");
                        addSeparator();

                        addRow("色彩模式", advancedColorStr);
                        addSeparator();

                        addRow("红", colorInfo.RedPrimary.ToString());
                        addRow("绿", colorInfo.GreenPrimary.ToString());
                        addRow("蓝", colorInfo.BluePrimary.ToString());
                        addSeparator();

                        addRow("白点", colorInfo.WhitePoint.ToString());

                        if (!sdrBoostSliderDraging)
                        {
                            sdrBoostSlider.Value = ((colorInfo.SdrWhiteLevelInNits - 80) / 4); // 80-480 Nits 映射到 0-100 滑块
                        }
                        if (capabilities.IsBrightnessNitsControlSupported)
                        {
                            laptopKeepHDRBrightnessModeToggleSwitch.IsEnabled = true;
                            HDRBrightnessSyncBySystemBrightness(brightnessNits);
                        }
                        else
                        {
                            laptopKeepHDRBrightnessModeToggleSwitch.IsEnabled = false;
                        }
                        if (capabilities.IsBrightnessControlSupported
                            && (!laptopKeepHDRBrightnessModeToggleSwitch.IsEnabled || !laptopKeepHDRBrightnessModeToggleSwitch.IsOn))
                        {
                            sdrBoostSliderLabel.Text = "HDR 内容亮度";
                        }
                        else
                        {
                            sdrBoostSliderLabel.Text = "SDR 内容亮度";
                        }
                    });
        }

        private async Task BuildBrightnessMappingAsync()
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
            };

            var hwnd = coreWindowHost.coreWindowHWND;
            string interfaceId = Win32API.TryGetMonitorInterfaceIdFromWindow(hwnd);
            if (string.IsNullOrEmpty(interfaceId))
            {
                interfaceId = "default";
            }

            Parallel.For(0, 101, options, i =>
            {
                var level = i * 0.01f;
                try
                {
                    if (!BrightnessNitsCache.ContainsKey(interfaceId))
                    {
                        BrightnessNitsCache[interfaceId] = new();
                    }
                    var dict = BrightnessNitsCache[interfaceId];
                    if (!dict.ContainsKey(level))
                    {
                        var settings = BrightnessOverrideSettings.CreateFromLevel(level);
                        dict[level] = settings.DesiredNits;
                        Debug.WriteLine($"Brightness Level: {i}%, Nits: {settings.DesiredNits}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Brightness mapping failed at {i}%: {ex.Message}");
                }
            });
        }

        private async Task BuildHDRBrightnessMappingAsync()
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
            };

            var hwnd = coreWindowHost.coreWindowHWND;
            string interfaceId = Win32API.TryGetMonitorInterfaceIdFromWindow(hwnd);
            if (string.IsNullOrEmpty(interfaceId))
            {
                interfaceId = "default";
            }

            Parallel.For(0, 101, options, i =>
            {
                var nits = (i * 4.0f) + 80.0f; // 80-480 Nits
                try
                {
                    if (!HDRBrightnessLevelCache.ContainsKey(interfaceId))
                    {
                        HDRBrightnessLevelCache[interfaceId] = new();
                    }
                    var dict = HDRBrightnessLevelCache[interfaceId];
                    if (!dict.ContainsKey(nits))
                    {
                        var settings = BrightnessOverrideSettings.CreateFromNits(nits);
                        dict[nits] = (float)settings.DesiredLevel;
                        Debug.WriteLine($"HDR Brightness Nits: {nits}, Level: {(float)settings.DesiredLevel * 100}%");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"HDR Brightness mapping failed at Nits: {nits}: {ex.Message}");
                }
            });
        }

        public async Task<KeyValuePair<float, float>[]> GetLaptopPreciseKeepHDRBrightnessLevelNitsPair()
        {
            var hwnd = coreWindowHost.coreWindowHWND;
            string interfaceId = Win32API.TryGetMonitorInterfaceIdFromWindow(hwnd);
            if (string.IsNullOrEmpty(interfaceId))
            {
                interfaceId = "default";
            }
            if (!BrightnessNitsCache.ContainsKey(interfaceId))
            {
                await BuildBrightnessMappingAsync();
            }
            var result = new List<KeyValuePair<float, float>>();
            if (BrightnessNitsCache.TryGetValue(interfaceId, out var dict))
            {
                foreach (var pair in dict)
                {
                    var nits = pair.Value;
                    if (nits >= 80 && nits <= 480)
                    {
                        if (nits % 4 == 0)
                        {
                            result.Add(pair);
                        }
                    }
                }
            }
            result.Sort((a, b) => a.Key.CompareTo(b.Key));
            return result.ToArray();
        }

        private float TryGetNitsFromBrightnessLevel(float brightnessLevel)
        {
            var hwnd = coreWindowHost.coreWindowHWND;
            string interfaceId = Win32API.TryGetMonitorInterfaceIdFromWindow(hwnd);
            if (string.IsNullOrEmpty(interfaceId))
            {
                interfaceId = "default";
            }
            if (brightnessLevel < 0) brightnessLevel = 0;
            if (brightnessLevel > 1) brightnessLevel = 1;
            if (BrightnessNitsCache.TryGetValue(interfaceId, out var dict)
                && dict.TryGetValue(brightnessLevel, out var nits))
            {
                return nits;
            }
            else
            {
                var currentBrightnessSettings = BrightnessOverrideSettings.CreateFromLevel(brightnessLevel);
                return currentBrightnessSettings.DesiredNits;
            }
        }

        private float TryGetLevelFromHDRBrightnessNits(float brightnessNits)
        {
            var hwnd = coreWindowHost.coreWindowHWND;
            string interfaceId = Win32API.TryGetMonitorInterfaceIdFromWindow(hwnd);
            if (string.IsNullOrEmpty(interfaceId))
            {
                interfaceId = "default";
            }
            if (brightnessNits < 80) brightnessNits = 80;
            if (brightnessNits > 480) brightnessNits = 480;
            if (HDRBrightnessLevelCache.TryGetValue(interfaceId, out var dict)
                && dict.TryGetValue(brightnessNits, out var level))
            {
                return level;
            }
            else
            {
                var currentBrightnessSettings = BrightnessOverrideSettings.CreateFromNits(brightnessNits);
                return (float)currentBrightnessSettings.DesiredLevel;
            }
        }
    }
}
