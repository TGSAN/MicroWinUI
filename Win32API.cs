﻿﻿﻿using System;
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

        /// <summary>
        /// DWM 颜色值：表示不设置颜色（透明/系统默认）。
        /// </summary>
        public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

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

        // WM 消息常量
        public const int WM_SIZE = 0x0005;
        public const int WM_GETMINMAXINFO = 0x0024;
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_NCCALCSIZE = 0x0083;
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_DPICHANGED = 0x02E0;

        // NCHITTEST 返回值常量
        public const int HTCLIENT = 1;
        public const int HTCAPTION = 2;
        public const int HTMINBUTTON = 8;
        public const int HTMAXBUTTON = 9;
        public const int HTLEFT = 10;
        public const int HTRIGHT = 11;
        public const int HTTOP = 12;
        public const int HTTOPLEFT = 13;
        public const int HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15;
        public const int HTBOTTOMLEFT = 16;
        public const int HTBOTTOMRIGHT = 17;
        public const int HTCLOSE = 20;

        // GetSystemMetrics 常量
        public const int SM_CXFRAME = 32;
        public const int SM_CYFRAME = 33;
        public const int SM_CXSIZE = 30;
        public const int SM_CXPADDEDBORDER = 92;

        // P/Invoke 声明
        [DllImport("user32.dll")]
        public static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("user32.dll")]
        public static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern int GetDpiForWindow(IntPtr hwnd);
        /// <summary>
        /// 将消息传递给 DWM 进行默认处理（标题栏按钮 hover 等效果）。
        /// </summary>
        [DllImport("dwmapi.dll", SetLastError = false, ExactSpelling = true)]
        public static extern int DwmDefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);

        // 结构体定义
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NCCALCSIZE_PARAMS
        {
            public RECT rgrc0;
            public RECT rgrc1;
            public RECT rgrc2;
            public IntPtr lppos;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

    }
}
