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

        WindowsXamlHost xamlHost = new WindowsXamlHost();
        private CustomTitleBar _titleBar;

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
            _titleBar = new CustomTitleBar(this);
            _titleBar.TitleBarHeight = 48;
            _titleBar.ExtendsContentIntoTitleBar = true;
            _titleBar.DragRegionInvalidated += OnDragRegionInvalidated;
            _titleBar.ThemeChanged += OnThemeChanged;

            uiSettings.ColorValuesChanged += (s, e) =>
            {
                Invoke(UpdateTheme);
                Invoke(UpdateBackdrop);
            };
            this.Load += IslandForm_Load;
            this.Activated += IslandWindow_Activated;

            this.Resize += IslandWindow_Resize;
            this.Move += IslandWindow_Move;
            xamlHost.HandleCreated += XamlHost_HandleCreated;
            xamlHost.SizeChanged += XamlHost_SizeChanged;
        }

        private void IslandWindow_Activated(object sender, EventArgs e)
        {
            ExtendFrameIntoClientArea(Handle);
            UpdateTheme();
            UpdateBackdrop();
            ExtendFrameIntoClientArea(Handle); // DWM 重启需要两次设置 ExtendFrameIntoClientArea 才能生效
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
            UpdateXamlHostLayout();
            UpdateCoreWindowPos();
        }

        private void OnDragRegionInvalidated(object sender, EventArgs e)
        {
            UpdateCoreWindowPos();
        }

        private void OnThemeChanged(object sender, EventArgs e)
        {
            UpdateTheme();
            UpdateBackdrop();
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

            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.BackColor = Color.Transparent;

            // Add XAML Island — 手动布局以适配标题栏高度
            this.Controls.Add(xamlHost);
            UpdateXamlHostLayout();
            _titleBar.Initialize();
        }

        /// <summary>
        /// 根据标题栏高度和调整大小边框重新计算 xamlHost 的位置和大小。
        /// 非最大化时在左、右、下三边留出调整大小边框的空间，使 WinForms 能接收边缘的 WM_NCHITTEST。
        /// </summary>
        private void UpdateXamlHostLayout()
        {
            if (xamlHost == null) return;
            int titleBarHeight = _titleBar?.ScaledTitleBarHeight ?? 0;

            bool maximized = (WindowState == FormWindowState.Maximized);
            if (maximized)
            {
                // 最大化时不需要调整大小边框，xamlHost 填满标题栏以下区域
                xamlHost.Top = titleBarHeight;
                xamlHost.Left = 0;
                xamlHost.Width = ClientSize.Width;
                xamlHost.Height = Math.Max(0, ClientSize.Height - titleBarHeight);
            }
            else
            {
                // 非最大化时在左、右、下留出边框空间以支持窗口拖拽调整大小
                int resizeBorder = CustomTitleBar.ScaleDimension(4, _titleBar?.CurrentDpi ?? 96);
                xamlHost.Top = titleBarHeight;
                xamlHost.Left = resizeBorder;
                xamlHost.Width = Math.Max(0, ClientSize.Width - resizeBorder * 2);
                xamlHost.Height = Math.Max(0, ClientSize.Height - titleBarHeight - resizeBorder);
            }
        }

        private void UpdateTheme()
        {
            bool isDarkMode = IsAppDarkMode();
            _actualTheme = isDarkMode ? ElementTheme.Dark : ElementTheme.Light;
            var attribute = Win32API.DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;
            if (IsWindows10OrGreater(18985))
            {
                attribute = Win32API.DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE;
            }
            SetWindowAttribute(Handle, attribute, isDarkMode ? 1 : 0, sizeof(int));
        }

        private void UpdateBackdrop()
        {
            // DWMWA_COLOR_NONE 忽略标题上色
            SetWindowAttribute(Handle, Win32API.DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, 0xFFFFFFFE, sizeof(int));
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

        private bool IsPointInXamlIsland(Point screenPoint)
        {
            var clientPoint = xamlHost.PointToClient(screenPoint);
            return xamlHost.ClientRectangle.Contains(clientPoint);
        }

        protected override void WndProc(ref Message m)
        {
            // 先委托给 CustomTitleBar 处理标题栏相关消息
            if (_titleBar != null && _titleBar.ProcessMessage(ref m))
                return;

            const int WM_NCLBUTTONDOWN = 0x00A1; // 非客户区（标题栏等）鼠标按下
            const int WM_LBUTTONDOWN = 0x0201;   // 客户区鼠标按下
            const int WM_ACTIVATE = 0x0006;
            const int WM_ACTIVATEAPP = 0x001C;

            switch (m.Msg)
            {
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Transparent — DWM 扩展帧不需要背景绘制
        }

        private static bool IsWindows10OrGreater(int build = -1)
        {
            return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= build;
        }
    }
}
