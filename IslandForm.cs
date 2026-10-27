using Microsoft.Win32;
using Mile.Xaml;
using Mile.Xaml.Interop;
using System;
using System.Drawing;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml.Linq;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace MicroWinUICore
{
    public partial class IslandWindow : Form
    {
        public enum SystemBackdrop
        {
            None,
            Acrylic,
            Mica,
            Tabbed
        }

        UISettings uiSettings = new UISettings();

        WindowsXamlHost xamlHost = new TransparentWindowsXamlHost();

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

        public UIElement Content
        {
            get
            {
                return xamlHost.Child;
            }
            set
            {
                xamlHost.Child = value;
                if (value != null)
                {
                    try
                    {
                        CoreWindow coreWindow = CoreWindow.GetForCurrentThread();
                        _coreWindowWHND = coreWindow?.GetInterop().GetWindowHandle() ?? IntPtr.Zero;
                    }
                    catch (Exception)
                    {
                        _coreWindowWHND = IntPtr.Zero;
                    }

                    UpdateCoreWindowPos();
                    if (IsHandleCreated)
                    {
                        UpdateBackdrop();
                    }
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

        private Size _logicalClientSize = Size.Empty;
        private uint _appliedDpi = 96;

        public IslandWindow()
        {
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            // 窗口尺寸由调用方以逻辑像素（96 DPI 基准）指定，经 ScaleToDeviceSize 换算，
            // 因此关闭 WinForms 自身的自动缩放，避免叠加缩放。
            this.AutoScaleMode = AutoScaleMode.None;
            this.BackColor = Color.Black;

            xamlHost.BackColor = Color.Black;
            xamlHost.Dock = DockStyle.Fill;
            xamlHost.Paint += (s, e) =>
            {
                e.Graphics.Clear(Color.Transparent);
            };
            this.Controls.Add(xamlHost);

            uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
            this.Load += IslandForm_Load;
            this.Activated += IslandWindow_Activated;

            this.Resize += IslandWindow_Resize;
            this.Move += IslandWindow_Move;
            xamlHost.HandleCreated += XamlHost_HandleCreated;
            xamlHost.SizeChanged += XamlHost_SizeChanged;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            uint windowDpi = Win32API.GetDpiForWindow(Handle);
            if (!_logicalClientSize.IsEmpty && windowDpi != 0 && windowDpi != _appliedDpi)
            {
                _appliedDpi = windowDpi;
                Size scaled = ScaleToDeviceSize(_logicalClientSize, windowDpi);
                base.SetClientSizeCore(scaled.Width, scaled.Height);
            }
            ExtendFrameIntoClientArea(Handle);
            UpdateTheme();
            UpdateBackdrop();
        }

        protected override void SetClientSizeCore(int x, int y)
        {
            _logicalClientSize = new Size(x, y);
            uint dpi = IsHandleCreated
                ? Win32API.GetDpiForWindow(Handle)
                : Win32API.GetDpiForSystem();
            if (dpi == 0)
            {
                dpi = 96;
            }
            _appliedDpi = dpi;
            Size scaled = ScaleToDeviceSize(_logicalClientSize, dpi);
            base.SetClientSizeCore(scaled.Width, scaled.Height);
        }

        /// <summary>
        /// 把以 96 DPI 为基准的逻辑尺寸换算成当前显示器的物理像素尺寸。
        /// </summary>
        public Size ScaleToDeviceSize(Size logicalSize)
        {
            uint dpi = IsHandleCreated
                ? Win32API.GetDpiForWindow(Handle)
                : Win32API.GetDpiForSystem();
            return ScaleToDeviceSize(logicalSize, dpi);
        }

        private static Size ScaleToDeviceSize(Size logicalSize, uint dpi)
        {
            if (dpi == 0)
            {
                dpi = 96;
            }

            float scale = dpi / 96f;
            return new Size(
                (int)Math.Round(logicalSize.Width * scale),
                (int)Math.Round(logicalSize.Height * scale));
        }

        private void IslandWindow_Activated(object sender, EventArgs e)
        {
            ExtendFrameIntoClientArea(Handle);
            UpdateTheme();
            UpdateBackdrop();
            ExtendFrameIntoClientArea(Handle); // DWM 重启需要两次设置 ExtendFrameIntoClientArea 才能生效
        }

        private void UiSettings_ColorValuesChanged(UISettings sender, object args)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed && !Disposing && IsHandleCreated)
                    {
                        UpdateTheme();
                        UpdateBackdrop();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // The callback can race with handle destruction during shutdown.
            }
        }

        private void XamlHost_SizeChanged(object sender, EventArgs e)
        {
            UpdateCoreWindowPos();
        }

        private void XamlHost_HandleCreated(object sender, EventArgs e)
        {
            UpdateCoreWindowPos();
        }

        private void IslandWindow_Move(object sender, EventArgs e)
        {
            UpdateCoreWindowPos();
        }

        private void IslandWindow_Resize(object sender, EventArgs e)
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

        private void IslandForm_Load(object sender, EventArgs e)
        {
            InitializeIsland();
        }

        public void InitializeIsland()
        {
            ExtendFrameIntoClientArea(Handle);
            UpdateTheme();
            UpdateBackdrop();
        }

        private void UpdateTheme()
        {
            bool isDarkMode = IsAppDarkMode();
            _actualTheme = isDarkMode ? ElementTheme.Dark : ElementTheme.Light;
            if (!IsHandleCreated)
            {
                return;
            }
            var attribute = Win32API.DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;
            if (IsWindows10OrGreater(18985))
            {
                attribute = Win32API.DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE;
            }
            SetWindowAttribute(Handle, attribute, isDarkMode ? 1 : 0, sizeof(int));
        }

        private void UpdateBackdrop()
        {
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
            if (!IsHandleCreated)
            {
                return;
            }
            // DWMWA_COLOR_NONE 忽略标题上色
            SetWindowAttribute(Handle, Win32API.DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, 0xFFFFFFFE, sizeof(int));
            if (Backdrop == SystemBackdrop.Mica)
            {
                // Enable Mica
                SetWindowAttribute(Handle, Win32API.DWMWINDOWATTRIBUTE.DWMWA_MICA, 1, sizeof(int));
                // Set the backdrop type to Main Window
                var type = Win32API.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW;
                SetWindowAttribute(Handle, Win32API.DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, (uint)type, sizeof(uint));
            }
            else
            {
                // Disable Mica
                SetWindowAttribute(Handle, Win32API.DWMWINDOWATTRIBUTE.DWMWA_MICA, 0, sizeof(int));
                if (Backdrop == SystemBackdrop.Acrylic)
                {
                    var type = Win32API.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW;
                    SetWindowAttribute(Handle, Win32API.DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, (uint)type, sizeof(uint));
                }
                else if (Backdrop == SystemBackdrop.Tabbed)
                {
                    var type = Win32API.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TABBEDWINDOW;
                    SetWindowAttribute(Handle, Win32API.DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, (uint)type, sizeof(uint));
                }
                else
                {
                    var type = Win32API.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_NONE;
                    SetWindowAttribute(Handle, Win32API.DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, (uint)type, sizeof(uint));
                }
            }
        }

        private static int ExtendFrameIntoClientArea(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return 0;
            }

            var margins = new Win32API.MARGINS(-1);
            return Win32API.DwmExtendFrameIntoClientArea(hWnd, margins);
        }

        private static int SetWindowAttribute<T>(IntPtr hWnd, Win32API.DWMWINDOWATTRIBUTE attribute, T value, int sizeOf)
        {
            GCHandle pinnedValue = default(GCHandle);
            try
            {
                pinnedValue = GCHandle.Alloc(value, GCHandleType.Pinned);
                return Win32API.DwmSetWindowAttribute(
                    hWnd,
                    (uint)attribute,
                    pinnedValue.AddrOfPinnedObject(),
                    sizeOf);
            }
            finally
            {
                if (pinnedValue.IsAllocated)
                {
                    pinnedValue.Free();
                }
            }
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
            if (IsDisposed || Disposing || xamlHost == null || xamlHost.IsDisposed
                || !xamlHost.IsHandleCreated || xamlHost.Child == null)
            {
                return;
            }

            Action closePopups = () =>
            {
                if (IsDisposed || Disposing || xamlHost.IsDisposed || xamlHost.Child == null)
                {
                    return;
                }

                var xamlRoot = xamlHost.Child.XamlRoot;
                if (xamlRoot == null)
                {
                    return;
                }

                var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot);
                foreach (var popup in popups)
                {
                    // Keep non-light-dismiss surfaces such as ContentDialog open.
                    if (popup.IsLightDismissEnabled)
                    {
                        popup.IsOpen = false;
                    }
                }
            };

            try
            {
                if (xamlHost.InvokeRequired)
                {
                    xamlHost.BeginInvoke(closePopups);
                }
                else
                {
                    closePopups();
                }
            }
            catch (InvalidOperationException)
            {
                // Handle destruction can race with an outside click during shutdown.
            }
        }

        private bool IsPointInXamlIsland(Point screenPoint)
        {
            var clientPoint = xamlHost.PointToClient(screenPoint);
            return xamlHost.ClientRectangle.Contains(clientPoint);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_ERASEBKGND = 0x0014;
            const int WM_NCLBUTTONDOWN = 0x00A1; // 非客户区（标题栏等）鼠标按下
            const int WM_LBUTTONDOWN = 0x0201;   // 客户区鼠标按下
            const int WM_ACTIVATE = 0x0006;
            const int WM_DPICHANGED = 0x02E0;

            switch (m.Msg)
            {
                case WM_ERASEBKGND:
                    if (Win32API.GetClientRect(m.HWnd, out Win32API.RECT clientRect))
                    {
                        Win32API.FillRect(m.WParam, in clientRect, Win32API.GetStockObject(Win32API.BLACK_BRUSH));
                    }
                    m.Result = new IntPtr(1);
                    return;

                case WM_DPICHANGED:
                    if (m.LParam != IntPtr.Zero && IsHandleCreated && !IsDisposed)
                    {
                        var suggestedRect = Marshal.PtrToStructure<Win32API.RECT>(m.LParam);
                        Win32API.SetWindowPos(
                            Handle,
                            Win32API.HWND_TOP,
                            suggestedRect.Left,
                            suggestedRect.Top,
                            suggestedRect.Right - suggestedRect.Left,
                            suggestedRect.Bottom - suggestedRect.Top,
                            Win32API.SWP_NOZORDER | Win32API.SWP_NOACTIVATE);
                        UpdateCoreWindowPos();
                    }
                    m.Result = IntPtr.Zero;
                    return;

                case WM_NCLBUTTONDOWN:
                case WM_LBUTTONDOWN:
                    // 检查点击是否在 XAML Island 控件外部
                    if (!IsPointInXamlIsland(Cursor.Position))
                    {
                        CloseAllXamlPopups();
                    }
                    break;

                case WM_ACTIVATE:
                    // 窗口激活状态改变时也可能需要关闭
                    if ((int)m.WParam == 0) // WA_INACTIVE
                    {
                        CloseAllXamlPopups();
                    }
                    break;
            }

            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Transparent);
        }

        private sealed class TransparentWindowsXamlHost : WindowsXamlHost
        {
            protected override void OnPaintBackground(PaintEventArgs pevent)
            {
                pevent.Graphics.Clear(Color.Transparent);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Color.Transparent);
                base.OnPaint(e);
            }

            protected override void WndProc(ref Message m)
            {
                const int WM_ERASEBKGND = 0x0014;
                if (m.Msg == WM_ERASEBKGND)
                {
                    if (Win32API.GetClientRect(m.HWnd, out Win32API.RECT clientRect))
                    {
                        Win32API.FillRect(m.WParam, in clientRect, Win32API.GetStockObject(Win32API.BLACK_BRUSH));
                    }
                    m.Result = new IntPtr(1);
                    return;
                }

                base.WndProc(ref m);
            }
        }

        private static bool IsWindows10OrGreater(int build = -1)
        {
            return Environment.OSVersion.Version.Major >= 10
                && (build < 0 || Environment.OSVersion.Version.Build >= build);
        }

    }
}
