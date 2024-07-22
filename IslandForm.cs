using Mile.Xaml;
using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.UI.Xaml;
using Windows.UI.ViewManagement;

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
            }
        }

        public IslandWindow()
        {
            uiSettings.ColorValuesChanged += (s, e) =>
            {
                Invoke(UpdateTheme);
            };
            this.Load += IslandForm_Load;
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
            SetWindowAttribute(Handle, Win32API.DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, isDarkMode ? 1 : 0, sizeof(int));
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

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Transparent
        }
    }
}
