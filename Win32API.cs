using System;
using System.Runtime.InteropServices;

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

        // ================= Monitor mapping helper =================
        public const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice; // "\\\\.\\DISPLAY1" etc
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left; public int top; public int right; public int bottom; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags,
            ref uint numPathArrayElements,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray,
            ref uint numModeInfoArrayElements,
            [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

        [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
        [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }
        private enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : int { UNINITIALIZED = -1 }
        private enum DISPLAYCONFIG_ROTATION : int { IDENTITY = 1 }
        private enum DISPLAYCONFIG_SCALING : int { IDENTITY = 1 }
        private enum DISPLAYCONFIG_SCANLINE_ORDERING : int { UNSPECIFIED = 0 }
        [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_PATH_TARGET_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology; public DISPLAYCONFIG_ROTATION rotation; public DISPLAYCONFIG_SCALING scaling; public DISPLAYCONFIG_RATIONAL refreshRate; public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering; [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable; public uint statusFlags; }
        [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }
        private enum DISPLAYCONFIG_DEVICE_INFO_TYPE : int { GET_SOURCE_NAME = 1, GET_TARGET_NAME = 2 }
        [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public DISPLAYCONFIG_DEVICE_INFO_TYPE type; public int size; public LUID adapterId; public uint id; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME { public DISPLAYCONFIG_DEVICE_INFO_HEADER header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DISPLAYCONFIG_TARGET_DEVICE_NAME { public DISPLAYCONFIG_DEVICE_INFO_HEADER header; public DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS flags; public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath; }
        [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS { public uint value; }

        // Mode info union required by QueryDisplayConfig (even if we don't consume contents)
        [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_MODE_INFO { public DISPLAYCONFIG_MODE_INFO_TYPE infoType; public uint id; public LUID adapterId; public DISPLAYCONFIG_MODE_INFO_UNION mode; }
        private enum DISPLAYCONFIG_MODE_INFO_TYPE : uint { SOURCE = 1, TARGET = 2, DESKTOP_IMAGE = 3 }
        [StructLayout(LayoutKind.Explicit)] private struct DISPLAYCONFIG_MODE_INFO_UNION { [FieldOffset(0)] public DISPLAYCONFIG_TARGET_MODE targetMode; [FieldOffset(0)] public DISPLAYCONFIG_SOURCE_MODE sourceMode; [FieldOffset(0)] public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_TARGET_MODE { public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo; }
        [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_SOURCE_MODE { public uint width; public uint height; public uint pixelFormat; public POINTL position; }
        [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO { public POINTL PathSourceSize; public RECT desktopImageRegion; public RECT desktopImageClip; }
        [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO { public ulong pixelRate; public DISPLAYCONFIG_RATIONAL hSyncFreq; public DISPLAYCONFIG_RATIONAL vSyncFreq; public DISPLAYCONFIG_2DREGION activeSize; public DISPLAYCONFIG_2DREGION totalSize; public uint videoStandard; public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering; }
        [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_2DREGION { public uint cx; public uint cy; }
        [StructLayout(LayoutKind.Sequential)] private struct POINTL { public int x; public int y; }

        /// <summary>
        /// Return monitor interface path (\\\\?\\DISPLAY#...) for the monitor containing given HWND, or null.
        /// </summary>
        public static string TryGetMonitorInterfaceIdFromWindow(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return null;
                IntPtr hmon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (hmon == IntPtr.Zero) return null;

                var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                if (!GetMonitorInfo(hmon, ref mi)) return null;
                string gdiDeviceName = mi.szDevice;
                if (string.IsNullOrEmpty(gdiDeviceName)) return null;

                uint numPath = 0, numMode = 0;
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out numPath, out numMode) != 0 || numPath == 0) return null;
                var paths = new DISPLAYCONFIG_PATH_INFO[numPath];
                var modes = new DISPLAYCONFIG_MODE_INFO[numMode];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPath, paths, ref numMode, modes, IntPtr.Zero) != 0) return null;

                for (int i = 0; i < paths.Length; i++)
                {
                    var sourceReq = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                    {
                        header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                        {
                            type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_SOURCE_NAME,
                            size = Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME)),
                            adapterId = paths[i].sourceInfo.adapterId,
                            id = paths[i].sourceInfo.id
                        }
                    };
                    if (DisplayConfigGetDeviceInfo(ref sourceReq) != 0) continue;
                    if (!string.Equals(sourceReq.viewGdiDeviceName, gdiDeviceName, StringComparison.OrdinalIgnoreCase)) continue;

                    var targetReq = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                    {
                        header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                        {
                            type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_TARGET_NAME,
                            size = Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME)),
                            adapterId = paths[i].targetInfo.adapterId,
                            id = paths[i].targetInfo.id
                        }
                    };
                    if (DisplayConfigGetDeviceInfo(ref targetReq) != 0) return null;
                    return targetReq.monitorDevicePath; // \\?\\DISPLAY#...
                }
            }
            catch { }
            return null;
        }
        // ===============================================================
    }
}
