using MicroWinUI;
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Reflection.Emit;
using System.Text;
using System.Windows.Forms;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace MicroWinUICore
{
    public static class Program
    {
        static bool SettingHide = false;
        static bool SettingEnableKeepHDR = false;
        static bool SettingDisableNotify = false;

        [STAThread]
        static void Main(string[] args)
        {
            DpiAware.ConfigureDpi();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            foreach (string arg in args)
            {
                if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
                {
                    StringBuilder stringBuilder = new StringBuilder();
                    stringBuilder.AppendLine("命令行参数");
                    stringBuilder.AppendLine();
                    stringBuilder.AppendLine("--help, -h\t\t\t显示此帮助信息");
                    stringBuilder.AppendLine("--hide\t\t\t启动时隐藏主界面");
                    stringBuilder.AppendLine("--enable-keep-hdr, -k\t启动时默认打开\"保持 HDR 亮度模式\"");
                    stringBuilder.AppendLine("--disable-notify\t\t不要显示任何通知");
                    MessageBox.Show(stringBuilder.ToString().Trim());
                    return;
                }
                else if (arg.Equals("--hide", StringComparison.OrdinalIgnoreCase))
                {
                    SettingHide = true;
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

            var showWindow = () =>
            {
                page.VideoStart();
                page.Visibility = Windows.UI.Xaml.Visibility.Visible; // 恢复渲染
                window.Content = page;
                window.Show();
                Win32API.ShowWindow(window.Handle, Win32API.SW_RESTORE);
                window.Activate();
            };

            var hideWindow = () =>
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
                        showWindow();
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
                        hideWindow();
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
                            hideWindow();
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
                            showWindow();
                        });
                        showItem.Icon = new FontIcon
                        {
                            Glyph = "\uE8A7"
                        };
                        flyout.Items.Add(showItem);
                    }
                    flyout.Items.Add(trayManager.CreateMenuFlyoutSeparator());
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
                    if (!page.IsBrightnessNitsControlSupportedForCurrentMonitor)
                    {
                        preciseBrightnessPairSubItem.IsEnabled = false;
                        preciseBrightnessPairSubItem.Text += " (不支持)";
                    }
                    else
                    { 
                        var pairs = await page.GetLaptopPreciseKeepHDRBrightnessLevelNitsPair();
                        if (pairs.Length > 0)
                        {
                            foreach (var pair in pairs)
                            {
                                var level = pair.Key;
                                var brightness = pair.Value;
                                var item = trayManager.CreateMenuFlyoutItem($"亮度 {Math.Round(level * 100)} ({brightness} 尼特)", () =>
                                {
                                    page.SetNitsSync(brightness);
                                });
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
                        bool okAll = Win32API.TryReloadAllCalibrationViaClsid();
                        //if (!SettingDisableNotify)
                        //{
                        //    notifyIcon.ShowBalloonTip(1500, okAll ? "已重新加载" : "重新加载失败", okAll ? "显示器校色重新加载成功" : "无法重新加载显示器校色", okAll ? ToolTipIcon.None : ToolTipIcon.Error);
                        //}
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
                    showWindow();
                }
            };

            Application.Run(window);

            app.Close();
        }
    }
}
