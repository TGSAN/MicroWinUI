using Microsoft.Win32;
using Mile.Xaml.Interop;
using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media;

namespace MicroWinUICore
{
    internal class XamlIslandHost : HwndHost
    {
        private DesktopWindowXamlSource _xamlSource;
        private IntPtr _xamlIslandHwnd;

        public DesktopWindowXamlSource XamlSource => _xamlSource;

        public event EventHandler XamlSourceReady;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _xamlSource = new DesktopWindowXamlSource();
            var native = _xamlSource.GetInterop();
            native.AttachToWindow(hwndParent.Handle);
            _xamlIslandHwnd = native.GetWindowHandle();

            XamlSourceReady?.Invoke(this, EventArgs.Empty);

            return new HandleRef(this, _xamlIslandHwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (_xamlSource != null)
            {
                _xamlSource = null;
            }
        }
    }

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

        XamlIslandHost _islandHost;
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
                UpdateBackdrop();
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
                if (_islandHost?.XamlSource == null) return _pendingContent;
                return _islandHost.XamlSource.Content;
            }
            set
            {
                if (!_islandInitialized)
                {
                    _pendingContent = value;
                    return;
                }
                _islandHost.XamlSource.Content = value;
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
            this.Activated += IslandWindow_Activated;
            this.SizeChanged += IslandWindow_SizeChanged;
            this.LocationChanged += IslandWindow_LocationChanged;
        }

        private void IslandWindow_SourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            var hwndSource = HwndSource.FromHwnd(_hwnd);
            hwndSource.AddHook(WndProc);

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

        private void IslandWindow_LocationChanged(object sender, EventArgs e)
        {
            UpdateCoreWindowPos();
        }

        private void IslandWindow_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            UpdateCoreWindowPos();
        }

        public void InitializeIsland()
        {
            _islandHost = new XamlIslandHost();
            _islandHost.XamlSourceReady += (s, e) =>
            {
                ExtendFrameIntoClientArea(_hwnd);
                UpdateTheme();
                UpdateBackdrop();

                _islandInitialized = true;

                if (_pendingContent != null)
                {
                    XamlIslandContent = _pendingContent;
                    _pendingContent = null;
                }
            };
            this.Content = _islandHost;
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
            if (_hwnd == IntPtr.Zero || _islandHost?.XamlSource == null) return;
            // DWMWA_COLOR_NONE 忽略标题上色
            SetWindowAttribute(_hwnd, Win32API.DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, 0xFFFFFFFE, sizeof(int));
            bool isDarkMode = IsAppDarkMode();
            bool colorPrevalence = IsColorPrevalence();
            if (colorPrevalence && (Backdrop == SystemBackdrop.None || !IsWindows10OrGreater(22000)))
            {
                // 覆盖颜色
                var content = _islandHost.XamlSource.Content as Page;
                if (content != null)
                    content.Background = isDarkMode ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32)) : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 243, 243, 243));
            }
            else
            {
                var content = _islandHost.XamlSource.Content as Page;
                if (content != null)
                    content.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
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

        private bool IsAppDarkMode()
        {
            var foreground = uiSettings.GetColorValue(UIColorType.Foreground);
            return IsColorLight(foreground);
        }

        private bool IsColorPrevalence()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("ColorPrevalence");
                        if (value is int intValue)
                        {
                            return intValue == 1;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private bool IsColorLight(Windows.UI.Color clr)
        {
            return (((5 * clr.G) + (2 * clr.R) + clr.B) > (8 * 128));
        }

        private void UpdateCoreWindowPos()
        {
            if (_coreWindowWHND == IntPtr.Zero || _hwnd == IntPtr.Zero) return;
            Win32API.GetWindowRect(_hwnd, out Win32API.RECT rect);
            Win32API.SetWindowPos(_coreWindowWHND, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top, 0x0040);
        }

        private static void ExtendFrameIntoClientArea(IntPtr hwnd)
        {
            Win32API.MARGINS margins = new Win32API.MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            Win32API.DwmExtendFrameIntoClientArea(hwnd, margins);
        }

        private static void SetWindowAttribute(IntPtr hwnd, Win32API.DWMWINDOWATTRIBUTE attribute, uint parameter, int parameterSize)
        {
            IntPtr ptr = Marshal.AllocHGlobal(parameterSize);
            try
            {
                Marshal.WriteInt32(ptr, unchecked((int)parameter));
                Win32API.DwmSetWindowAttribute(hwnd, (uint)attribute, ptr, parameterSize);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        private static void SetWindowAttribute(IntPtr hwnd, Win32API.DWMWINDOWATTRIBUTE attribute, int parameter, int parameterSize)
        {
            IntPtr ptr = Marshal.AllocHGlobal(parameterSize);
            try
            {
                Marshal.WriteInt32(ptr, parameter);
                Win32API.DwmSetWindowAttribute(hwnd, (uint)attribute, ptr, parameterSize);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        private void CloseAllXamlPopups()
        {
            if (_islandHost?.XamlSource?.Content == null) return;
            var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(_islandHost.XamlSource.Content.XamlRoot);
            foreach (var popup in popups)
            {
                if (popup.IsOpen)
                {
                    popup.IsOpen = false;
                }
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCLBUTTONDOWN = 0x00A1;
            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_ACTIVATE = 0x0006;

            switch (msg)
            {
                case WM_NCLBUTTONDOWN:
                case WM_LBUTTONDOWN:
                    CloseAllXamlPopups();
                    break;

                case WM_ACTIVATE:
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
