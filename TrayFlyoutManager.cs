using System;
using System.Diagnostics;
using System.Windows.Forms;
using Windows.Foundation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml; // Style, CornerRadius, Thickness
using Windows.UI.Xaml.Media; // Brush, AcrylicBrush, SolidColorBrush
using Windows.UI; // Color
using Windows.UI.ViewManagement;

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

        // Lazily created styles to keep Program.cs clean
        private Style _presenterStyle;
        private Style _menuFlyoutItemStyle;
        private Style _toggleMenuFlyoutItemStyle;
        private Style _menuFlyoutSubItemStyle;
        private Style _menuFlyoutSeparatorStyle;

        public TrayFlyoutManager(IslandWindow mainWindow, NotifyIcon notifyIcon)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _notifyIcon = notifyIcon ?? throw new ArgumentNullException(nameof(notifyIcon));
        }

        /// <summary>
        /// Provide a pre-styled MenuFlyout that matches the app's theme with acrylic/fallback background.
        /// </summary>
        public MenuFlyout CreateFlyout()
        {
            EnsureStyles();
            var flyout = new MenuFlyout();
            try { flyout.MenuFlyoutPresenterStyle = _presenterStyle; } catch (Exception ex) { Debug.WriteLine("Presenter style apply failed: " + ex.Message); }
            return flyout;
        }

        /// <summary>
        /// Provide a pre-styled MenuFlyoutItem.
        /// </summary>
        /// 
        public MenuFlyoutItem CreateMenuFlyoutItem(string text, Action onClick)
        {
            EnsureStyles();
            var item = new MenuFlyoutItem { Text = text ?? string.Empty };
            try { if (_menuFlyoutItemStyle != null) item.Style = _menuFlyoutItemStyle; } catch (Exception ex) { Debug.WriteLine("MenuFlyoutItem style apply failed: " + ex.Message); }
            if (onClick != null)
            {
                item.Click += (s, e) => { try { onClick(); } catch { } };
            }
            return item;
        }

        /// <summary>
        /// Provide a pre-styled ToggleMenuFlyoutItem.
        /// </summary>
        /// 
        public ToggleMenuFlyoutItem CreateToggleMenuFlyoutItem(string text, Action onClick)
        {
            EnsureStyles();
            var item = new ToggleMenuFlyoutItem { Text = text ?? string.Empty };
            try { if (_toggleMenuFlyoutItemStyle != null) item.Style = _toggleMenuFlyoutItemStyle; } catch (Exception ex) { Debug.WriteLine("ToggleMenuFlyoutItem style apply failed: " + ex.Message); }
            if (onClick != null)
            {
                item.Click += (s, e) => { try { onClick(); } catch { } };
            }
            return item;
        }

        /// <summary>
        /// Provide a pre-styled MenuFlyoutSubItem.
        /// </summary>
        /// 
        public MenuFlyoutSubItem CreateMenuFlyoutSubItem(string text)
        {
            EnsureStyles();
            var item = new MenuFlyoutSubItem { Text = text ?? string.Empty };
            try { if (_menuFlyoutSubItemStyle != null) item.Style = _menuFlyoutSubItemStyle; } catch (Exception ex) { Debug.WriteLine("MenuFlyoutSubItem style apply failed: " + ex.Message); }
            return item;
        }

        /// <summary>
        /// Provide a pre-styled MenuFlyoutSeparator.
        /// </summary>
        /// 
        public MenuFlyoutSeparator CreateMenuFlyoutSeparator()
        {
            EnsureStyles();
            var item = new MenuFlyoutSeparator();
            try { if (_menuFlyoutSeparatorStyle != null) item.Style = _menuFlyoutSeparatorStyle; } catch (Exception ex) { Debug.WriteLine("MenuFlyoutSeparator style apply failed: " + ex.Message); }
            return item;
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
                ClientSize = new System.Drawing.Size(0, 0), // give room for flyout hit-test
                Opacity = 0,
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
                Position = new Point(0, 0),
                ShowMode = FlyoutShowMode.Standard
            };
            _activeFlyout.ShowAt(_anchorPage, options);
        }

        private void EnsureStyles()
        {
            var ui = new UISettings();
            var isDark = _mainWindow.ActualTheme == ElementTheme.Dark;

            try
            {
                _presenterStyle = new Style(typeof(MenuFlyoutPresenter));
                _presenterStyle.Setters.Add(new Setter(MenuFlyoutPresenter.IsDefaultShadowEnabledProperty, false));
                _presenterStyle.Setters.Add(new Setter(MenuFlyoutPresenter.CornerRadiusProperty, new CornerRadius(8)));
                _presenterStyle.Setters.Add(new Setter(MenuFlyoutPresenter.BackgroundProperty, BuildFlyoutBackground()));
                _presenterStyle.Setters.Add(new Setter(MenuFlyoutPresenter.PaddingProperty, new Thickness(4, 0, 4, 0)));
                _presenterStyle.Setters.Add(new Setter(MenuFlyoutPresenter.BorderThicknessProperty, new Thickness(1)));
                _presenterStyle.Setters.Add(new Setter(MenuFlyoutPresenter.BorderBrushProperty, isDark ? new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00)) : new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0x00, 0x00))));
                _presenterStyle.Setters.Add(new Setter(MenuFlyoutPresenter.MarginProperty, new Thickness(0)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Presenter style creation failed: " + ex.Message);
            }

            if (_menuFlyoutItemStyle == null)
            {
                try
                {
                    _menuFlyoutItemStyle = new Style(typeof(MenuFlyoutItem));
                    _menuFlyoutItemStyle.Setters.Add(new Setter(MenuFlyoutItem.CornerRadiusProperty, new CornerRadius(6)));
                    _menuFlyoutItemStyle.Setters.Add(new Setter(MenuFlyoutItem.PaddingProperty, new Thickness(0)));
                    _menuFlyoutItemStyle.Setters.Add(new Setter(MenuFlyoutItem.MarginProperty, new Thickness(0)));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("MenuFlyoutItem style creation failed: " + ex.Message);
                }
            }

            if (_toggleMenuFlyoutItemStyle == null)
            {
                try
                {
                    _toggleMenuFlyoutItemStyle = new Style(typeof(ToggleMenuFlyoutItem));
                    _toggleMenuFlyoutItemStyle.Setters.Add(new Setter(ToggleMenuFlyoutItem.CornerRadiusProperty, new CornerRadius(6)));
                    _toggleMenuFlyoutItemStyle.Setters.Add(new Setter(ToggleMenuFlyoutItem.PaddingProperty, new Thickness(0)));
                    _toggleMenuFlyoutItemStyle.Setters.Add(new Setter(ToggleMenuFlyoutItem.MarginProperty, new Thickness(0)));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("ToggleMenuFlyoutItem style creation failed: " + ex.Message);
                }
            }

            if (_menuFlyoutSubItemStyle == null)
            {
                try
                {
                    _menuFlyoutSubItemStyle = new Style(typeof(MenuFlyoutSubItem));
                    _menuFlyoutSubItemStyle.Setters.Add(new Setter(MenuFlyoutSubItem.CornerRadiusProperty, new CornerRadius(6)));
                    _menuFlyoutSubItemStyle.Setters.Add(new Setter(MenuFlyoutSubItem.PaddingProperty, new Thickness(0)));
                    _menuFlyoutSubItemStyle.Setters.Add(new Setter(MenuFlyoutSubItem.MarginProperty, new Thickness(0)));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("MenuFlyoutSubItem style creation failed: " + ex.Message);
                }
            }

            if (_menuFlyoutSeparatorStyle == null)
            {
                try
                {
                    _menuFlyoutSeparatorStyle = new Style(typeof(MenuFlyoutSeparator));
                    _menuFlyoutSeparatorStyle.Setters.Add(new Setter(MenuFlyoutSeparator.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x10, 0x00, 0x00, 0x00))));
                    _menuFlyoutSeparatorStyle.Setters.Add(new Setter(MenuFlyoutSeparator.PaddingProperty, new Thickness(0, 4, 0, 4)));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("MenuFlyoutSeparator style creation failed: " + ex.Message);
                }
            }
        }

        // helper to build a reliable acrylic/fallback brush each time
        private Brush BuildFlyoutBackground()
        {
            try
            {
                var ui = new UISettings();
                var isDark = _mainWindow.ActualTheme == ElementTheme.Dark;
                var tint = isDark ? Color.FromArgb(0xFF, 0x2C, 0x2C, 0x2C) : Color.FromArgb(0xFF, 0xF9, 0xF9, 0xF9);
                var fallback = isDark ? Color.FromArgb(0xE6, 0x22, 0x22, 0x22) : Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF);

                bool effectsEnabled = true;
                try { effectsEnabled = ui.AdvancedEffectsEnabled; } catch { }
                if (!effectsEnabled)
                {
                    return new SolidColorBrush(fallback);
                }

                var acrylic = new AcrylicBrush
                {
                    BackgroundSource = AcrylicBackgroundSource.Backdrop,
                    TintColor = tint,
                    TintOpacity = 0,
                    TintLuminosityOpacity = 0.85,
                    FallbackColor = fallback
                };
                return acrylic;
            }
            catch
            {
                return new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF));
            }
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
