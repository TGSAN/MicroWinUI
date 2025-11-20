using MicroWinUI;
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Reflection.Emit;
using System.Text;
using System.Windows.Forms;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using System.Threading;

namespace MicroWinUICore
{
    public static class Program
    {
        static bool SettingHide = false;
        static bool SettingEnableColorAccurate = false;
        static bool SettingEnableKeepHDR = false;
        static bool SettingDisableNotify = false;
        public static Action ShowWindow;
        public static Action HideWindow;
        private static Mutex _singleInstanceMutex; // Ensure single process instance

        [STAThread]
        static void Main(string[] args)
        {
            DpiAware.ConfigureDpi();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            {
                // Single instance guard
                _singleInstanceMutex = new Mutex(initiallyOwned: true, name: "Global\\MicroWinUI.DisplayInfo", createdNew: out var createdNew);
                if (!createdNew)
                {
                    // Another instance is already running, exit silently
                    MessageBox.Show("DisplayInfo 已经在运行中。", "信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            foreach (string arg in args)
            {
                if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
                {
                    StringBuilder stringBuilder = new StringBuilder();
                    stringBuilder.AppendLine("命令行参数");
                    stringBuilder.AppendLine();
                    stringBuilder.AppendLine("--help, -h\t\t\t显示此帮助信息");
                    stringBuilder.AppendLine("--hide\t\t\t启动时隐藏主界面");
                    stringBuilder.AppendLine("--enable-color-accurate\t启动时默认打开\"自动保持色准\"功能");
                    stringBuilder.AppendLine("--enable-keep-hdr\t\t启动时默认打开\"自动保持 HDR 亮度内容\"功能");
                    stringBuilder.AppendLine("--disable-notify\t\t不要显示任何通知");
                    MessageBox.Show(stringBuilder.ToString().Trim());
                    return;
                }
                else if (arg.Equals("--hide", StringComparison.OrdinalIgnoreCase))
                {
                    SettingHide = true;
                }
                else if (arg.Equals("--enable-color-accurate", StringComparison.OrdinalIgnoreCase) || arg.Equals("-k", StringComparison.OrdinalIgnoreCase))
                {
                    SettingEnableColorAccurate = true;
                }
                else if (arg.Equals("--enable-keep-hdr", StringComparison.OrdinalIgnoreCase) || arg.Equals("-k", StringComparison.OrdinalIgnoreCase))
                {
                    SettingEnableKeepHDR = true;
                }
                else if (arg.Equals("--disable-notify", StringComparison.OrdinalIgnoreCase))
                {
                    SettingDisableNotify = true;
                }
            }

            App app = new();

            IslandWindow window = new();
            CodePage page = new(window);
            window.Content = page;
            window.ClientSize = new(1280, 720);
            window.MinimumSize = new(480, 480);
            window.Text = "DisplayInfo";

            var notifyIcon = new NotifyIcon
            {
                Icon = window.Icon,
                Text = "DisplayInfo",
                Visible = true
            };

            ShowWindow = () =>
            {
                page.VideoStart();
                page.Visibility = Windows.UI.Xaml.Visibility.Visible; // 恢复渲染
                window.Content = page;
                window.Show();
                Win32API.ShowWindow(window.Handle, Win32API.SW_RESTORE);
                window.Activate();
            };

            HideWindow = () =>
            {
                Win32API.ShowWindow(window.Handle, Win32API.SW_RESTORE); // 防止恢复的时候处于最小化状态找不到
                window.Hide();
                page.Visibility = Windows.UI.Xaml.Visibility.Collapsed; // 停止渲染
                window.Content = null;
                page.VideoStop();
                if (!SettingDisableNotify)
                {
                    notifyIcon.BalloonTipClicked += (s, e) =>
                    {
                        ShowWindow();
                    };
                    notifyIcon.ShowBalloonTip(2500, "正在后台继续运行", "已最小化至系统托盘，可通过点击托盘图标或通知显示主界面", ToolTipIcon.None);
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            };

            if (SettingHide)
            {
                window.Load += (object sender, EventArgs e) =>
                {
                    window.BeginInvoke(new Action(() =>
                    {
                        HideWindow();
                        window.Enabled = true;
                        window.Opacity = 1;
                    }));
                };
                window.Enabled = false;
                window.Opacity = 0;
            }

            if (SettingEnableKeepHDR)
            {
                page.laptopKeepHDRBrightnessModeToggleSwitch.IsOn = true;
            }

            if (SettingEnableColorAccurate)
            {
                page.DisplayColorOverrideScenarioAccurate = true;
            }

            using TrayFlyoutManager trayManager = new(window, notifyIcon);

            notifyIcon.MouseClick += async (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var flyout = trayManager.CreateFlyout();

                    if (window.Visible)
                    {
                        var hideItem = trayManager.CreateMenuFlyoutItem("隐藏主窗口", () =>
                        {
                            HideWindow();
                        });
                        hideItem.Icon = new FontIcon
                        {
                            Glyph = "\uE73F"
                        };
                        flyout.Items.Add(hideItem);
                    }
                    else
                    {
                        var showItem = trayManager.CreateMenuFlyoutItem("显示主窗口", () =>
                        {
                            ShowWindow();
                        });
                        showItem.Icon = new FontIcon
                        {
                            Glyph = "\uE8A7"
                        };
                        flyout.Items.Add(showItem);
                    }
                    flyout.Items.Add(trayManager.CreateMenuFlyoutSeparator());
                    var displayColorOverrideScenarioAccurate = trayManager.CreateMenuFlyoutItem("自动保持色准", () =>
                    {
                        var isOn = page.DisplayColorOverrideScenarioAccurate;
                        page.DisplayColorOverrideScenarioAccurate = isOn;
                    });
                    displayColorOverrideScenarioAccurate.Icon = new FontIcon
                    {
                        Glyph = page.DisplayColorOverrideScenarioAccurate ? "\uE73D" : "\uE739"
                    };
                    flyout.Items.Add(displayColorOverrideScenarioAccurate);
                    var laptopKeepHDRBrightnessModeToggleItem = trayManager.CreateMenuFlyoutItem("自动保持 HDR 亮度内容", () =>
                    {
                        page.laptopKeepHDRBrightnessModeToggleSwitch.IsOn = !page.laptopKeepHDRBrightnessModeToggleSwitch.IsOn;
                    });
                    laptopKeepHDRBrightnessModeToggleItem.Icon = new FontIcon
                    {
                        Glyph = page.laptopKeepHDRBrightnessModeToggleSwitch.IsOn ? "\uE73D" : "\uE739"
                    };
                    if (!page.laptopKeepHDRBrightnessModeToggleSwitch.IsEnabled)
                    {
                        laptopKeepHDRBrightnessModeToggleItem.IsEnabled = false;
                        laptopKeepHDRBrightnessModeToggleItem.Text += " (不支持)";
                    }
                    flyout.Items.Add(laptopKeepHDRBrightnessModeToggleItem);
                    var preciseBrightnessPairSubItem = trayManager.CreateMenuFlyoutSubItem("精准 SDR 内容亮度");
                    preciseBrightnessPairSubItem.Icon = new FontIcon
                    {
                        Glyph = "\uE706"
                    };
                    if (!page.IsBrightnessNitsControlSupportedForCurrentMonitor)
                    {
                        preciseBrightnessPairSubItem.IsEnabled = false;
                        preciseBrightnessPairSubItem.Text += " (不支持)";
                    }
                    else
                    {
                        var pairs = await page.GetLaptopPreciseKeepHDRBrightnessLevelNitsPair();
                        var currrntBrightnessLevelPercent = Math.Round(Brightness.TryGetCurrentBrightnessLevel() * 100);
                        if (pairs.Length > 0)
                        {
                            foreach (var pair in pairs)
                            {
                                var levelPercent = Math.Round(pair.Key * 100);
                                var brightness = pair.Value;
                                var item = trayManager.CreateMenuFlyoutItem($"亮度 {levelPercent} ({brightness} 尼特)", () =>
                                {
                                    page.SetNitsSync(brightness);
                                });
                                if (currrntBrightnessLevelPercent == levelPercent)
                                {
                                    item.Icon = new FontIcon
                                    {
                                        Glyph = "\uE73E"
                                    };
                                }
                                preciseBrightnessPairSubItem.Items.Add(item);
                            }
                        }
                        else
                        {
                            var item = trayManager.CreateMenuFlyoutItem($"没有可用的亮度", () => {});
                            item.IsEnabled = false;
                            preciseBrightnessPairSubItem.Items.Add(item);
                        }
                    }
                    flyout.Items.Add(preciseBrightnessPairSubItem);
                    flyout.Items.Add(trayManager.CreateMenuFlyoutSeparator());
                    var reloadAllCalibItem = trayManager.CreateMenuFlyoutItem("重新加载显示器校色", () =>
                    {
                        bool success = Win32API.TryReloadColorCalibration();
                        if (!success) 
                        {
                            if (!SettingDisableNotify)
                            {
                                notifyIcon.ShowBalloonTip(1500, "失败", "无法重新加载显示器校色", ToolTipIcon.Info);
                            }
                        }
                    });
                    reloadAllCalibItem.Icon = new FontIcon
                    {
                        Glyph = "\uE895"
                    }; flyout.Items.Add(reloadAllCalibItem);
                    flyout.Items.Add(trayManager.CreateMenuFlyoutSeparator());
                    var hdrSettingsItem = trayManager.CreateMenuFlyoutItem("HDR 设置", () => Process.Start("ms-settings:display-hdr"));
                    hdrSettingsItem.Icon = new FontIcon
                    {
                        Glyph = "\uE713"
                    };
                    flyout.Items.Add(hdrSettingsItem);
                    var hdrCalibItem = trayManager.CreateMenuFlyoutItem("HDR 显示器校准", () => Process.Start("ms-windows-store://pdp?productId=9N7F2SM5D1LR&mode=mini"));
                    hdrCalibItem.Icon = new FontIcon
                    {
                        Glyph = "\uE82F"
                    };
                    flyout.Items.Add(hdrCalibItem);
                    flyout.Items.Add(trayManager.CreateMenuFlyoutSeparator());
                    var exitItem = trayManager.CreateMenuFlyoutItem("退出", () =>
                    {
                        try { notifyIcon.Visible = false; notifyIcon.Dispose(); } catch { }
                        try { window.Close(); } catch { }
                        try { Application.Exit(); } catch { }
                    });
                    exitItem.Icon = new FontIcon
                    {
                        Glyph = "\uF3B1"
                    };
                    flyout.Items.Add(exitItem);

                    trayManager.ShowFlyoutAtCursor(flyout);
                }
                else if (e.Button == MouseButtons.Left)
                {
                    ShowWindow();
                }
            };

            Application.Run(window);

            app.Close();

            try
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
            catch { }
        }
    }
}
