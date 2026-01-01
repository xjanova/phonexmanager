using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PhoneRomFlashTool.Models;

namespace PhoneRomFlashTool.Services
{
    public interface IDeviceService
    {
        event EventHandler<DeviceInfo>? DeviceConnected;
        event EventHandler<DeviceInfo>? DeviceDisconnected;
        event EventHandler<string>? LogMessage;

        Task<List<DeviceInfo>> GetConnectedDevicesAsync();
        Task<DeviceInfo?> GetDeviceInfoAsync(string deviceId);
        Task<bool> RebootToModeAsync(string deviceId, DeviceMode mode);
        Task<bool> IsDeviceConnectedAsync(string deviceId);
        void StartDeviceMonitoring();
        void StopDeviceMonitoring();
    }
}
