using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Windows.Foundation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Core; // for dispatcher

namespace MicroWinUICore
{
    /// <summary>
    /// Shows a provided UWP MenuFlyout at the current cursor by creating a temporary IslandWindow.
    /// Dismisses on host window deactivation (simpler & reliable) instead of global mouse hook.
    /// </summary>
    internal sealed class TrayFlyoutManager : IDisposable
    {
        private readonly IslandWindow _mainWindow;
        private readonly NotifyIcon _notifyIcon;
        private bool _disposed;

        private IslandWindow _flyoutHost;
        private Page _anchorPage;
        private MenuFlyout _activeFlyout;

        public TrayFlyoutManager(IslandWindow mainWindow, NotifyIcon notifyIcon)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _notifyIcon = notifyIcon ?? throw new ArgumentNullException(nameof(notifyIcon));
        }

        /// <summary>
        /// Show the specified MenuFlyout at current cursor screen position. Replaces any existing flyout.
        /// </summary>
        public void ShowFlyoutAtCursor(MenuFlyout flyout)
        {
            if (_disposed || flyout == null) return;
            CleanupFlyoutResources();

            var cursor = Cursor.Position; // screen coordinates

            _flyoutHost = new IslandWindow
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = cursor,
                ClientSize = new System.Drawing.Size(300, 200), // give room for flyout hit-test
                TopMost = true,
                Text = string.Empty
            };
            _flyoutHost.Backdrop = IslandWindow.SystemBackdrop.None;
            _flyoutHost.Deactivate += FlyoutHost_Deactivate; // outside click / focus change

            _anchorPage = new Page();
            _flyoutHost.Content = _anchorPage;
            _flyoutHost.Show();
            _flyoutHost.Activate(); // ensure we receive Deactivate when user clicks elsewhere

            _activeFlyout = flyout;
            _activeFlyout.Closed += FlyoutClosed;

            var options = new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.Bottom,
                Position = new Point(0, 0)
            };
            _activeFlyout.ShowAt(_anchorPage, options);
        }

        private void FlyoutHost_Deactivate(object sender, EventArgs e)
        {
            // Host lost focus => dismiss
            TryHideActiveFlyout("Deactivate");
        }

        private void FlyoutClosed(object sender, object e)
        {
            CleanupFlyoutResources();
        }

        private void TryHideActiveFlyout(string reason)
        {
            try { if (_activeFlyout != null) { _activeFlyout.Hide(); Debug.WriteLine($"Flyout hidden ({reason})."); } } catch { }
        }

        private void CleanupFlyoutResources()
        {
            if (_activeFlyout != null)
            {
                try { _activeFlyout.Closed -= FlyoutClosed; } catch { }
            }
            _activeFlyout = null;

            if (_flyoutHost != null)
            {
                try { _flyoutHost.Deactivate -= FlyoutHost_Deactivate; } catch { }
                try { _flyoutHost.Close(); _flyoutHost.Dispose(); } catch { }
                _flyoutHost = null;
            }
            _anchorPage = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CleanupFlyoutResources();
        }
    }
}
