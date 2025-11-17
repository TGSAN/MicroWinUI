using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace MicroWinUI
{
    internal static class Brightness
    {
        // Public entry point: level in [0..1]
        public static void TryPersistBrightness(double level)
        {
            try
            {
                if (double.IsNaN(level)) level = 0.5;
                level = Math.Max(0.0, Math.Min(1.0, level));
                byte percent = (byte)Math.Max(0, Math.Min(100, (int)Math.Round(level * 100)));
                TryPersistViaWmi(percent);
            }
            catch { }
        }

        private static void TryPersistViaWmi(byte percent)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\WMI");
                scope.Connect();

                // Probe capability
                var q = new ObjectQuery("SELECT * FROM WmiMonitorBrightness");
                using (var searcher = new ManagementObjectSearcher(scope, q))
                using (var results = searcher.Get())
                {
                    bool any = false;
                    foreach (ManagementObject _ in results)
                    {
                        any = true; break;
                    }
                    if (!any)
                    {
                        return; // No brightness support via WMI
                    }
                }

                var q2 = new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods");
                using (var searcher2 = new ManagementObjectSearcher(scope, q2))
                using (var methods = searcher2.Get())
                {
                    foreach (ManagementObject m in methods)
                    {
                        try
                        {
                            // Parameters: Timeout (uint32), Brightness (uint8)
                            m.InvokeMethod("WmiSetBrightness", new object[] { 1u, percent });
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        public static double TryGetCurrentBrightnessLevel()
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\WMI");
                scope.Connect();
                var q = new ObjectQuery($"SELECT * FROM WmiMonitorBrightness");
                using (var searcher = new ManagementObjectSearcher(scope, q))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        var val = mo["CurrentBrightness"];
                        if (val != null)
                        {
                            var level = Convert.ToByte(val) * 0.01;
                            return level;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WMI brightness query failed: {ex.Message}");
            }
            return 0.5;
        }
    }
}
