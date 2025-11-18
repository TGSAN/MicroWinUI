using System;
using System.Runtime.InteropServices;

public static class DpiAware
{
    public static void ConfigureDpi()
    {
        // ---------------------------------------------------------
        // 1. 尝试 Windows 10 (1703+) / Windows 11 的方法 (PerMonitorV2)
        // ---------------------------------------------------------
        try
        {
            // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
            // 使用 IntPtr 转换 -4，防止溢出问题
            int result = SetProcessDpiAwarenessContext((IntPtr)(-4));

            // 如果返回非零值（S_OK 或 类似句柄），说明设置成功，直接返回
            if (result == 0 || result == 1) return;
        }
        catch (EntryPointNotFoundException)
        {
            // 说明系统是 Win10 1703 以前的版本，或者 User32.dll 中没有这个 API
            // 继续尝试下一种方法
        }

        // ---------------------------------------------------------
        // 2. 尝试 Windows 8.1 / Windows 10 (早期) 的方法 (PerMonitor)
        // ---------------------------------------------------------
        try
        {
            // Process_Per_Monitor_DPI_Aware = 2
            int result = SetProcessDpiAwareness(2);
            if (result == 0) return; // S_OK
        }
        catch (DllNotFoundException)
        {
            // Shcore.dll 不存在（说明是 Windows 7 或更早）
        }
        catch (EntryPointNotFoundException)
        {
            // Shcore.dll 存在但没有这个 API（极少见）
        }

        // ---------------------------------------------------------
        // 3. 尝试 Windows 7 / Vista 的方法 (System Aware)
        // ---------------------------------------------------------
        try
        {
            // Win7 只支持 System Aware (全局缩放)，不支持 Per-Monitor
            SetProcessDPIAware();
        }
        catch (EntryPointNotFoundException)
        {
            // 极旧的系统（XP），什么都不做
        }
    }

    // --- P/Invoke Definitions ---

    // Windows 10 1703+ (User32.dll)
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetProcessDpiAwarenessContext(IntPtr dpiContext);

    // Windows 8.1+ (Shcore.dll)
    [DllImport("shcore.dll", SetLastError = true)]
    private static extern int SetProcessDpiAwareness(int awareness);

    // Windows Vista/7 (User32.dll)
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDPIAware();
}