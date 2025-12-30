using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MicroWinUICore
{
    internal class Win32API
    {
        [DllImport("user32.dll")]
        public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll", SetLastError = false, ExactSpelling = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, [In] IntPtr pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll", SetLastError = false, ExactSpelling = true)]
        public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, in MARGINS pMarInset);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOREDRAW = 0x0008;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_HIDEWINDOW = 0x0080;
        public const uint SWP_NOCOPYBITS = 0x0100;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint SWP_NOSENDCHANGING = 0x0400;

        public const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

        public enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        public enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MARGINS
        {
            /// <summary>Width of the left border that retains its size.</summary>
            public int cxLeftWidth;

            /// <summary>Width of the right border that retains its size.</summary>
            public int cxRightWidth;

            /// <summary>Height of the top border that retains its size.</summary>
            public int cyTopHeight;

            /// <summary>Height of the bottom border that retains its size.</summary>
            public int cyBottomHeight;

            public MARGINS(int allMargins) => cxLeftWidth = cxRightWidth = cyTopHeight = cyBottomHeight = allMargins;
        }

        /// <summary>
        /// Type of system backdrop to be rendered by DWM
        /// </summary>
        public enum DWM_SYSTEMBACKDROP_TYPE : uint
        {
            DWMSBT_AUTO = 0,

            /// <summary>
            /// no backdrop
            /// </summary>
            DWMSBT_NONE = 1,

            /// <summary>
            /// Use tinted blurred wallpaper backdrop (Mica)
            /// </summary>
            DWMSBT_MAINWINDOW = 2,

            /// <summary>
            /// Use Acrylic backdrop
            /// </summary>
            DWMSBT_TRANSIENTWINDOW = 3,

            /// <summary>
            /// Use blurred wallpaper backdrop
            /// </summary>
            DWMSBT_TABBEDWINDOW = 4
        }

        internal enum DWMWINDOWATTRIBUTE : uint
        {
            DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19,
            DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
            DWMWA_WINDOW_CORNER_PREFERENCE = 33,
            DWMWA_BORDER_COLOR = 34,
            DWMWA_CAPTION_COLOR = 35,
            DWMWA_TITLE_COLOR = 36,
            DWMWA_TEXT_COLOR = 36,
            DWMWA_SYSTEMBACKDROP_TYPE = 38,
            DWMWA_MICA = 1029,
        }
    }
}
