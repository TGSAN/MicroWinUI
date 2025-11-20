using MicroWinUICore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MicroWinUI
{
    public class HdrSwitch
    {
        public struct HdrStatusInfo
        {
            public string DevicePath;   // 设备路径 (\\?\DISPLAY#...)
            public string FriendlyName; // 友好名称
            public uint DisplayId;      // 显示器内部 ID
            public bool IsSupported;    // 硬件是否支持 HDR
            public bool IsEnabled;      // 当前是否已开启 HDR
        }

        private static string GetMonitorDevicePath(Win32API.LUID adapterId, uint targetId)
        {
            var requestPacket = new Win32API.DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = new Win32API.DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = Win32API.DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_TARGET_NAME,
                    size = Marshal.SizeOf(typeof(Win32API.DISPLAYCONFIG_TARGET_DEVICE_NAME)),
                    adapterId = adapterId,
                    id = targetId
                }
            };

            int result = Win32API.DisplayConfigGetDeviceInfo(ref requestPacket);
            if (result == Win32API.ERROR_SUCCESS)
            {
                return requestPacket.monitorDevicePath;
            }
            return null;
        }

        public static List<HdrStatusInfo> GetHdrStatus()
        {
            var statusList = new List<HdrStatusInfo>();

            uint pathCount, modeCount;
            Win32API.GetDisplayConfigBufferSizes(Win32API.QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);

            var paths = new Win32API.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new Win32API.DISPLAYCONFIG_MODE_INFO[modeCount];

            Win32API.QueryDisplayConfig(Win32API.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);

            foreach (var mode in modes)
            {
                if (mode.infoType != Win32API.DISPLAYCONFIG_MODE_INFO_TYPE.TARGET)
                    continue;

                // 1. 获取 HDR 状态
                var colorPacket = new Win32API.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    header = new Win32API.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = Win32API.DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_ADVANCED_COLOR_INFO,
                        size = Marshal.SizeOf(typeof(Win32API.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO)),
                        adapterId = mode.adapterId,
                        id = mode.id
                    }
                };

                // 2. 获取名称和路径 (映射 ID -> Path)
                var namePacket = new Win32API.DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new Win32API.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = Win32API.DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_TARGET_NAME,
                        size = Marshal.SizeOf(typeof(Win32API.DISPLAYCONFIG_TARGET_DEVICE_NAME)),
                        adapterId = mode.adapterId,
                        id = mode.id
                    }
                };

                bool isHdrInfoSuccess = Win32API.DisplayConfigGetDeviceInfo(ref colorPacket) == Win32API.ERROR_SUCCESS;
                bool isNameInfoSuccess = Win32API.DisplayConfigGetDeviceInfo(ref namePacket) == Win32API.ERROR_SUCCESS;

                if (isHdrInfoSuccess)
                {
                    statusList.Add(new HdrStatusInfo
                    {
                        DisplayId = mode.id,
                        IsSupported = colorPacket.AdvancedColorSupported,
                        IsEnabled = colorPacket.AdvancedColorEnabled,
                        DevicePath = isNameInfoSuccess ? namePacket.monitorDevicePath : null,
                        FriendlyName = isNameInfoSuccess ? namePacket.monitorFriendlyDeviceName : null
                    });
                }
            }

            return statusList;
        }

        public static void SetGlobalHdr(bool enable)
        {
            uint pathCount, modeCount;
            int result = Win32API.GetDisplayConfigBufferSizes(Win32API.QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);

            if (result != Win32API.ERROR_SUCCESS)
                throw new Win32Exception(result);

            var paths = new Win32API.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new Win32API.DISPLAYCONFIG_MODE_INFO[modeCount];

            result = Win32API.QueryDisplayConfig(Win32API.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result != Win32API.ERROR_SUCCESS)
                throw new Win32Exception(result);

            // 遍历所有 Mode
            foreach (var mode in modes)
            {
                // 只关心 TARGET 类型 (显示器端)
                if (mode.infoType != Win32API.DISPLAYCONFIG_MODE_INFO_TYPE.TARGET)
                    continue;

                // 获取当前状态 (对应 hdr_support_get)
                var getPacket = new Win32API.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    header = new Win32API.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = Win32API.DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_ADVANCED_COLOR_INFO,
                        size = Marshal.SizeOf(typeof(Win32API.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO)),
                        adapterId = mode.adapterId,
                        id = mode.id
                    }
                };

                if (Win32API.DisplayConfigGetDeviceInfo(ref getPacket) == Win32API.ERROR_SUCCESS)
                {
                    // 如果显示器支持 HDR
                    if (getPacket.AdvancedColorSupported)
                    {
                        // 如果状态已经符合，跳过
                        if (getPacket.AdvancedColorEnabled == enable)
                            continue;

                        // 设置 HDR 状态
                        var setPacket = new Win32API.DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
                        {
                            header = new Win32API.DISPLAYCONFIG_DEVICE_INFO_HEADER
                            {
                                type = Win32API.DISPLAYCONFIG_DEVICE_INFO_TYPE.SET_ADVANCED_COLOR_STATE,
                                size = Marshal.SizeOf(typeof(Win32API.DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE)),
                                adapterId = mode.adapterId,
                                id = mode.id
                            },
                            value = enable ? 1u : 0u // 设置 Bit 0
                        };

                        int setResult = Win32API.DisplayConfigSetDeviceInfo(ref setPacket);
                        if (setResult == Win32API.ERROR_SUCCESS)
                        {
                            Console.WriteLine($"显示器 (ID: {mode.id}) HDR 已切换为: {enable}");
                        }
                        else
                        {
                            Console.WriteLine($"显示器 (ID: {mode.id}) 切换失败，错误码: {setResult}");
                        }
                    }
                }
            }
        }
    }
}