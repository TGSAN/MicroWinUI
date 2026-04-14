using Microsoft.Win32;
using Mile.Xaml;
using Mile.Xaml.Interop;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms.Integration;
using System.Windows.Interop;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace MicroWinUICore
{
    public partial class IslandWindow : System.Windows.Window
    {
        public enum SystemBackdrop
        {
            None,
            Acrylic,
            Mica,
            Tabbed
        }

        UISettings uiSettings = new UISettings();

        WindowsXamlHost xamlHost;
        WindowsFormsHost formsHost;
        IntPtr _hwnd;
        UIElement _pendingContent;
        bool _islandInitialized;

        SystemBackdrop _backdrop = SystemBackdrop.None;
        public SystemBackdrop Backdrop
        {
            get
            {
                return _backdrop;
            }
            set
            {
                _backdrop = value;
                UpdateBackdrop(); // Update backdrop
            }
        }

        ElementTheme _actualTheme = ElementTheme.Light;
        public ElementTheme ActualTheme
        {
            get
            {
                return _actualTheme;
            }
        }

        public UIElement XamlIslandContent
        {
            get
            {
                return xamlHost.Child;
            }
            set
            {
                if (!_islandInitialized)
                {
                    _pendingContent = value;
                    return;
                }
                xamlHost.Child = value;
                if (value != null)
                {
                    CoreWindow coreWindow = CoreWindow.GetForCurrentThread();
                    _coreWindowWHND = coreWindow.GetInterop().GetWindowHandle();
                    UpdateCoreWindowPos();
                }
                else
                {
                    _coreWindowWHND = IntPtr.Zero;
                }
            }
        }

        private IntPtr _coreWindowWHND = IntPtr.Zero;
        public IntPtr coreWindowHWND
        {
            get
            {
                return _coreWindowWHND;
            }
        }

        public IslandWindow()
        {
            uiSettings.ColorValuesChanged += (s, e) =>
            {
                Dispatcher.Invoke(UpdateTheme);
                Dispatcher.Invoke(UpdateBackdrop);
            };
            this.SourceInitialized += IslandWindow_SourceInitialized;
            this.Loaded += IslandWindow_Loaded;
            this.Activated += IslandWindow_Activated;

            this.SizeChanged += IslandWindow_SizeChanged;
            this.LocationChanged += IslandWindow_LocationChanged;
        }

        private void IslandWindow_SourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            // Hook WndProc
            var hwndSource = HwndSource.FromHwnd(_hwnd);
            hwndSource.AddHook(WndProc);

            // DWM setup (only needs HWND, no XAML Island yet)
            ExtendFrameIntoClientArea(_hwnd);
            UpdateTheme();
        }

        private void IslandWindow_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            InitializeIsland();
        }

        private void IslandWindow_Activated(object sender, EventArgs e)
        {
            if (_hwnd == IntPtr.Zero) return;
            ExtendFrameIntoClientArea(_hwnd);
            UpdateTheme();
            UpdateBackdrop();
            ExtendFrameIntoClientArea(_hwnd); // DWM 重启需要两次设置 ExtendFrameIntoClientArea 才能生效
        }

        private void XamlHost_SizeChanged(object sender, EventArgs e)
        {
            UpdateCoreWindowPos();
        }

        private void XamlHost_HandleCreated(object sender, EventArgs e)
        {
            UpdateCoreWindowPos();
        }

        private void IslandWindow_LocationChanged(object sender, EventArgs e)
        {
            UpdateCoreWindowPos();
        }

        private void IslandWindow_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            UpdateCoreWindowPos();
        }

        private void UpdateCoreWindowPos()
        {
            if (coreWindowHWND != IntPtr.Zero && xamlHost != null && xamlHost.IsHandleCreated)
            {
                Rectangle rect = xamlHost.RectangleToScreen(xamlHost.ClientRectangle);
                Win32API.SetWindowPos(
                    coreWindowHWND,
                    Win32API.HWND_TOP,
                    rect.Left,
                    rect.Top,
                    rect.Width,
                    rect.Height,
                    Win32API.SWP_NOZORDER | Win32API.SWP_NOACTIVATE
                );
            }
        }

        private bool IsColorLight(Windows.UI.Color clr)
        {
            return (((5 * clr.G) + (2 * clr.R) + clr.B) > (8 * 128));
        }

        public void InitializeIsland()
        {
            // Use a WinForms Panel as intermediary to prevent WPF layout
            // from passing infinite size to WindowsXamlHost.Measure
            var panel = new System.Windows.Forms.Panel();
            panel.Dock = System.Windows.Forms.DockStyle.Fill;

            formsHost = new WindowsFormsHost();
            formsHost.Child = panel;
            this.Content = formsHost;

            // Defer XAML Island creation until layout is complete
            Dispatcher.BeginInvoke(new Action(() =>
            {
                xamlHost = new WindowsXamlHost();
                xamlHost.HandleCreated += XamlHost_HandleCreated;
                xamlHost.SizeChanged += XamlHost_SizeChanged;
                xamlHost.Dock = System.Windows.Forms.DockStyle.Fill;
                panel.Controls.Add(xamlHost);

                ExtendFrameIntoClientArea(_hwnd);
                UpdateBackdrop();

                _islandInitialized = true;

                if (_pendingContent != null)
                {
                    XamlIslandContent = _pendingContent;
                    _pendingContent = null;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void UpdateTheme()
        {
            if (_hwnd == IntPtr.Zero) return;
            bool isDarkMode = IsAppDarkMode();
            _actualTheme = isDarkMode ? ElementTheme.Dark : ElementTheme.Light;
            var attribute = Win32API.DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;
            if (IsWindows10OrGreater(18985))
            {
                attribute = Win32API.DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE;
            }
            SetWindowAttribute(_hwnd, attribute, isDarkMode ? 1 : 0, sizeof(int));
        }

        private void UpdateBackdrop()
        {
            if (_hwnd == IntPtr.Zero || xamlHost == null) return;
            // DWMWA_COLOR_NONE 忽略标题上色
            SetWindowAttribute(_hwnd, Win32API.DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, 0xFFFFFFFE, sizeof(int));
            bool isDarkMode = IsAppDarkMode();
            bool colorPrevalence = IsColorPrevalence();
            if (colorPrevalence && (Backdrop == SystemBackdrop.None || !IsWindows10OrGreater(22000)))
            {
                // 覆盖颜色
                (this.xamlHost.Child as Page)?.Background = isDarkMode ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32)) : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 243, 243, 243));
            }
            else
            {
                (this.xamlHost.Child as Page)?.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
            }
            if (Backdrop == SystemBackdrop.Mica)
            {
                // Enable Mica
                SetWindowAttribute(_hwnd, Win32API.DWMWINDOWATTRIBUTE.DWMWA_MICA, 1, sizeof(int));
                // Set the backdrop type to Main Window
                var type = Win32API.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW;
                SetWindowAttribute(_hwnd, Win32API.DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, (uint)type, sizeof(uint));
            }
            else
            {
                // Disable Mica
                SetWindowAttribute(_hwnd, Win32API.DWMWINDOWATTRIBUTE.DWMWA_MICA, 0, sizeof(int));
                if (Backdrop == SystemBackdrop.Acrylic)
                {
                    var type = Win32API.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW;
                    SetWindowAttribute(_hwnd, Win32API.DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, (uint)type, sizeof(uint));
                }
                else if (Backdrop == SystemBackdrop.Tabbed)
                {
                    var type = Win32API.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TABBEDWINDOW;
                    SetWindowAttribute(_hwnd, Win32API.DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, (uint)type, sizeof(uint));
                }
                else
                {
                    var type = Win32API.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_NONE;
                    SetWindowAttribute(_hwnd, Win32API.DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, (uint)type, sizeof(uint));
                }
            }
        }

        private static int ExtendFrameIntoClientArea(IntPtr hWnd)
        {
            var margins = new Win32API.MARGINS(-1);
            var result = Win32API.DwmExtendFrameIntoClientArea(hWnd, margins);
            return result;
        }

        private static int SetWindowAttribute<T>(IntPtr hWnd, Win32API.DWMWINDOWATTRIBUTE attribute, T value, int sizeOf)
        {
            var pinnedValue = GCHandle.Alloc(value, GCHandleType.Pinned);
            var valueAddr = pinnedValue.AddrOfPinnedObject();
            var result = Win32API.DwmSetWindowAttribute(hWnd, (uint)attribute, valueAddr, sizeOf);
            pinnedValue.Free();
            return result;
        }

        public bool IsAppDarkMode()
        {
            var foreground = uiSettings.GetColorValue(UIColorType.Foreground);
            bool isDark = IsColorLight(foreground);
            return isDark;
        }

        public bool IsColorPrevalence()
        {
            string registData = "0"; // 默认0，不上色
            try
            {
                RegistryKey reg_HKCU = Registry.CurrentUser;
                RegistryKey reg_ThemesPersonalize = reg_HKCU.OpenSubKey(@"Software\Microsoft\Windows\DWM", false); // false为只读，true为可写入
                registData = reg_ThemesPersonalize.GetValue("ColorPrevalence").ToString();
            }
            catch { }
            return registData == "1";
        }

        private void CloseAllXamlPopups()
        {
            if (xamlHost.Child == null) return;
            xamlHost.Invoke(() =>
            {
                var xamlRoot = xamlHost.Child.XamlRoot;
                if (xamlRoot != null)
                {
                    var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot);
                    foreach (var popup in popups)
                    {
                        // 忽略非 LightDismiss 的弹出层（如 ContentDialog 本身、及其半透明黑色遮罩 SmokeLayer）
                        // 只有像 ComboBox 的下拉菜单这种默认开启 LightDismiss 的 Popup 才需要我们在这里强制关闭。
                        if (!popup.IsLightDismissEnabled)
                        {
                            continue;
                        }
                        popup.IsOpen = false;
                    }
                }
            });
        }

        private bool IsPointInXamlIsland(System.Drawing.Point screenPoint)
        {
            var clientPoint = xamlHost.PointToClient(screenPoint);
            return xamlHost.ClientRectangle.Contains(clientPoint);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCLBUTTONDOWN = 0x00A1; // 非客户区（标题栏等）鼠标按下
            const int WM_LBUTTONDOWN = 0x0201;   // 客户区鼠标按下
            const int WM_ACTIVATE = 0x0006;

            switch (msg)
            {
                case WM_NCLBUTTONDOWN:
                case WM_LBUTTONDOWN:
                    // 检查点击是否在 XAML Island 控件外部
                    if (!IsPointInXamlIsland(System.Windows.Forms.Cursor.Position))
                    {
                        CloseAllXamlPopups();
                    }
                    break;

                case WM_ACTIVATE:
                    // 窗口激活状态改变时也可能需要关闭
                    if ((int)wParam == 0) // WA_INACTIVE
                    {
                        CloseAllXamlPopups();
                    }
                    break;
            }

            return IntPtr.Zero;
        }

        private static bool IsWindows10OrGreater(int build = -1)
        {
            return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= build;
        }
    }
}
