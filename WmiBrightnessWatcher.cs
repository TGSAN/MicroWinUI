using System;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace MicroWinUI
{
    /// <summary>
    /// WMI-based brightness provider and watcher (no DDC/CI), using WmiMonitorBrightness and WmiMonitorBrightnessEvent.
    /// </summary>
    internal sealed class WmiBrightnessWatcher : IDisposable
    {
        private readonly string targetWmiInstanceName; // e.g., DISPLAY\\DEL4098\\5&10a58962&0&UID4353
        private ManagementEventWatcher eventWatcher;
        public event EventHandler<byte> BrightnessChanged; // percentage 0..100

        public WmiBrightnessWatcher(string wmiInstanceName)
        {
            targetWmiInstanceName = wmiInstanceName;
        }

        public void Start()
        {
            try
            {
                Stop();
                var scope = new ManagementScope(@"\\.\root\WMI");
                scope.Connect();

                var wql = new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent");
                eventWatcher = new ManagementEventWatcher(scope, wql);
                eventWatcher.EventArrived += (s, e) =>
                {
                    try
                    {
                        var inst = e.NewEvent;
                        if (inst == null) return;
                        var instanceName = (inst["InstanceName"] as string) ?? string.Empty;
                        if (!IsSameInstance(instanceName, targetWmiInstanceName)) return;
                        var b = Convert.ToByte(inst["Brightness"]);
                        BrightnessChanged?.Invoke(this, b);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"WMI Brightness Event processing failed: {ex}");
                    }
                };
                eventWatcher.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WMI Brightness watcher start failed: {ex}");
            }
        }

        private static bool IsSameInstance(string eventInstance, string targetInstance)
        {
            if (string.Equals(eventInstance, targetInstance, StringComparison.OrdinalIgnoreCase)) return true;
            // Some InstanceName values append _0 / _1 etc. Remove trailing _digits for comparison.
            string evNorm = StripIndexSuffix(eventInstance);
            string tgtNorm = StripIndexSuffix(targetInstance);
            return string.Equals(evNorm, tgtNorm, StringComparison.OrdinalIgnoreCase);
        }

        private static string StripIndexSuffix(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int underscore = s.LastIndexOf('_');
            if (underscore > 0 && underscore < s.Length - 1)
            {
                bool allDigits = true;
                for (int i = underscore + 1; i < s.Length; i++)
                {
                    if (!char.IsDigit(s[i])) { allDigits = false; break; }
                }
                if (allDigits)
                {
                    return s.Substring(0, underscore);
                }
            }
            return s;
        }

        public void Stop()
        {
            try
            {
                if (eventWatcher != null)
                {
                    eventWatcher.Stop();
                    eventWatcher.Dispose();
                    eventWatcher = null;
                }
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
