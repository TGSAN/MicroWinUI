using MicroWinUI;
using System;
using System.Windows.Forms;
using Windows.UI.Xaml.Controls;
using System.Diagnostics;

namespace MicroWinUICore
{
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            App app = new();

            var window = new IslandWindow();
            var page = new CodePage(window);
            window.Content = page;
            window.ClientSize = new System.Drawing.Size(1280, 720);
            window.MinimumSize = new System.Drawing.Size(480, 480);
            window.Text = "DisplayInfo";

            var notifyIcon = new NotifyIcon
            {
                Icon = window.Icon,
                Text = "DisplayInfo",
                Visible = true
            };

            using var trayManager = new TrayFlyoutManager(window, notifyIcon);

            notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
                var flyout = new MenuFlyout();
                var hdrItem = new MenuFlyoutItem { Text = "HDR 设置" };
                hdrItem.Click += (cs, ce) => { try { Process.Start("ms-settings:display-hdr"); } catch { } };
                flyout.Items.Add(hdrItem);
                var exitItem = new MenuFlyoutItem { Text = "退出" };
                exitItem.Click += (cs, ce) =>
                {
                    try { notifyIcon.Visible = false; notifyIcon.Dispose(); } catch { }
                    try { window.Close(); } catch { }
                    try { Application.Exit(); } catch { }
                };
                flyout.Items.Add(exitItem);
                trayManager.ShowFlyoutAtCursor(flyout);
            };

            Application.Run(window);

            app.Close();
        }
    }
}
