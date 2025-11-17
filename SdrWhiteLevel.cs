using System;
using System.Diagnostics;
using MicroWinUICore;

namespace MicroWinUI
{
    /// <summary>
    /// Facade for setting SDR white level (nits) for the display that hosts a given window/Island.
    /// </summary>
    internal static class SdrWhiteLevel
    {
        /// <summary>
        /// Clamp and round nits to Windows slider steps (80..480, step 4), then apply to the monitor hosting the hwnd.
        /// </summary>
        public static bool TrySetForWindow(IntPtr hwnd, double nits)
        {
            try
            {
                int v = (int)Math.Round(nits);
                if (v < 80) v = 80;
                if (v > 480) v = 480;
                if ((v % 4) != 0) v += 4 - (v % 4);
                return Win32API.TrySetSdrWhiteForWindowMonitor(hwnd, v);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TrySetForWindow failed: {ex}");
                return false;
            }
        }
    }
}
