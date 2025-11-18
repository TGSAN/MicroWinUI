using Mile.Xaml;
using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.UI.Xaml;
using Windows.UI.ViewManagement;
using Microsoft.Win32;

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

        IntPtr _coreWindowHWND = IntPtr.Zero;
        public IntPtr coreWindowHWND
        {
            get
            {
                return _coreWindowHWND;
            }
            set
            {
                _coreWindowHWND = value;
                UpdateCoreWindowPos();
            }
        }

        UISettings uiSettings = new UISettings();

        WindowsXamlHost xamlHost = new WindowsXamlHost();

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
                UpdateCoreWindowPos();
            }
        }

        public IslandWindow()
        {
            uiSettings.ColorValuesChanged += (s, e) =>
            {
                Invoke(UpdateTheme);
            };
            xamlHost.HandleCreated += XamlHost_HandleCreated;
            xamlHost.SizeChanged += XamlHost_SizeChanged;
            this.Load += IslandForm_Load;
            this.Activated += IslandWindow_Activated;
            this.Resize += IslandWindow_Resize;
            this.Move += IslandWindow_Move;

            Icon extractedIcon = Icon.ExtractAssociatedIcon(this.GetType().Assembly.Location);
            if (extractedIcon != null)
            {
                this.Icon = extractedIcon; // 设置窗体图标
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
            if (coreWindowHWND != IntPtr.Zero && xamlHost.IsHandleCreated)
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

        private void IslandWindow_Activated(object sender, EventArgs e)
        {
            bool isDarkMode = IsAppDarkMode();
            bool colorPrevalence = IsColorPrevalence();
            if (colorPrevalence)
            {
                // 覆盖颜色
                SetWindowAttribute(Handle, Win32API.DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, isDarkMode ? 0x00202020 : 0x00F3F3F3, sizeof(int));
            }
            else
            {
                SetWindowAttribute(Handle, Win32API.DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR, 0xFFFFFFFF, sizeof(int));
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

            // Add XAML Island
            this.Controls.Add(xamlHost);
            xamlHost.Dock = DockStyle.Fill;
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

        private static int SetWindowCompositionAttribute(IntPtr hWnd)
        {
            //int _blurOpacity = 0; /* 0-255 如果为0，颜色不能设置纯黑000000 */
            int _blurOpacity = 32; /* 0-255 如果为0，颜色不能设置纯黑000000 */
            int _blurBackgroundColor = 0xFFFFFF; /* Drak BGR color format */
            // int _blurBackgroundColor = 0xE6E6E6; /* Drak BGR color format */

            var accent = new Win32API.AccentPolicy
            {
                AccentState = Win32API.AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT,
                GradientColor = (_blurOpacity << 24) | (_blurBackgroundColor & 0xFFFFFF)
            };

            var accentStructSize = Marshal.SizeOf(accent);

            var accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new Win32API.WindowCompositionAttributeData
            {
                Attribute = Win32API.WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = accentStructSize,
                Data = accentPtr
            };

            var result = Win32API.SetWindowCompositionAttribute(hWnd, ref data);

            Marshal.FreeHGlobal(accentPtr);

            return result;
        }

        private static int CheckHResult(int result)
        {
            if (Marshal.GetExceptionForHR(result) is { } ex) throw ex;
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

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Transparent
        }

        private static bool IsWindows10OrGreater(int build = -1)
        {
            return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= build;
        }
    }
}
