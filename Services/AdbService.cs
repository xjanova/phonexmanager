using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PhoneRomFlashTool.Models;

namespace PhoneRomFlashTool.Services
{
    public class AdbService : IDeviceService, IDisposable
    {
        private readonly string _adbPath;
        private readonly string _fastbootPath;
        private Timer? _deviceMonitorTimer;
        private List<DeviceInfo> _lastKnownDevices = new();
        private bool _disposed;

        public event EventHandler<DeviceInfo>? DeviceConnected;
        public event EventHandler<DeviceInfo>? DeviceDisconnected;
        public event EventHandler<string>? LogMessage;

        public AdbService()
        {
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            _adbPath = Path.Combine(toolsPath, "adb.exe");
            _fastbootPath = Path.Combine(toolsPath, "fastboot.exe");

            // ถ้าไม่มี adb.exe ในโฟลเดอร์ Tools ให้ใช้จาก PATH
            if (!File.Exists(_adbPath))
            {
                _adbPath = "adb";
            }
            if (!File.Exists(_fastbootPath))
            {
                _fastbootPath = "fastboot";
            }
        }

        public void StartDeviceMonitoring()
        {
            _deviceMonitorTimer = new Timer(async _ => await CheckDevicesAsync(), null, 0, 2000);
            Log("Started device monitoring");
        }

        public void StopDeviceMonitoring()
        {
            _deviceMonitorTimer?.Dispose();
            _deviceMonitorTimer = null;
            Log("Stopped device monitoring");
        }

        private async Task CheckDevicesAsync()
        {
            try
            {
                var currentDevices = await GetConnectedDevicesAsync();

                // ตรวจสอบอุปกรณ์ที่เชื่อมต่อใหม่
                foreach (var device in currentDevices)
                {
                    if (!_lastKnownDevices.Any(d => d.SerialNumber == device.SerialNumber))
                    {
                        DeviceConnected?.Invoke(this, device);
                        Log($"Device connected: {device.DisplayName}");
                    }
                }

                // ตรวจสอบอุปกรณ์ที่ถูกถอดออก
                foreach (var device in _lastKnownDevices)
                {
                    if (!currentDevices.Any(d => d.SerialNumber == device.SerialNumber))
                    {
                        DeviceDisconnected?.Invoke(this, device);
                        Log($"Device disconnected: {device.DisplayName}");
                    }
                }

                _lastKnownDevices = currentDevices;
            }
            catch (Exception ex)
            {
                Log($"Error checking devices: {ex.Message}");
            }
        }

        public async Task<List<DeviceInfo>> GetConnectedDevicesAsync()
        {
            var devices = new List<DeviceInfo>();

            // ดึงอุปกรณ์จาก ADB
            var adbDevices = await GetAdbDevicesAsync();
            devices.AddRange(adbDevices);

            // ดึงอุปกรณ์จาก Fastboot
            var fastbootDevices = await GetFastbootDevicesAsync();
            devices.AddRange(fastbootDevices);

            // ดึงอุปกรณ์ USB โดยตรง (สำหรับ EDL, MTK mode)
            var usbDevices = GetUsbDevices();
            foreach (var usb in usbDevices)
            {
                if (!devices.Any(d => d.SerialNumber == usb.SerialNumber))
                {
                    devices.Add(usb);
                }
            }

            return devices;
        }

        private async Task<List<DeviceInfo>> GetAdbDevicesAsync()
        {
            var devices = new List<DeviceInfo>();

            try
            {
                var result = await ExecuteCommandAsync(_adbPath, "devices -l");
                var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines.Skip(1)) // Skip header
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("*"))
                        continue;

                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var serialNumber = parts[0];
                        var status = parts[1];

                        if (status == "device" || status == "recovery")
                        {
                            var device = new DeviceInfo
                            {
                                SerialNumber = serialNumber,
                                DeviceId = serialNumber,
                                IsConnected = true,
                                ConnectedTime = DateTime.Now,
                                ConnectionType = DeviceConnectionType.ADB,
                                Mode = status == "recovery" ? DeviceMode.Recovery : DeviceMode.Normal
                            };

                            // ดึงข้อมูลเพิ่มเติม
                            await PopulateDeviceInfoAsync(device);
                            devices.Add(device);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error getting ADB devices: {ex.Message}");
            }

            return devices;
        }

        private async Task<List<DeviceInfo>> GetFastbootDevicesAsync()
        {
            var devices = new List<DeviceInfo>();

            try
            {
                var result = await ExecuteCommandAsync(_fastbootPath, "devices");
                var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && parts[1] == "fastboot")
                    {
                        var device = new DeviceInfo
                        {
                            SerialNumber = parts[0],
                            DeviceId = parts[0],
                            IsConnected = true,
                            ConnectedTime = DateTime.Now,
                            ConnectionType = DeviceConnectionType.Fastboot,
                            Mode = DeviceMode.Fastboot
                        };

                        // ดึงข้อมูลจาก fastboot
                        await PopulateFastbootInfoAsync(device);
                        devices.Add(device);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error getting Fastboot devices: {ex.Message}");
            }

            return devices;
        }

        private List<DeviceInfo> GetUsbDevices()
        {
            var devices = new List<DeviceInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB%'");

                foreach (ManagementObject device in searcher.Get())
                {
                    var deviceId = device["DeviceID"]?.ToString() ?? "";
                    var name = device["Name"]?.ToString() ?? "";
                    var description = device["Description"]?.ToString() ?? "";

                    // ตรวจสอบอุปกรณ์ที่เป็น Qualcomm EDL mode
                    if (name.Contains("Qualcomm HS-USB QDLoader") ||
                        description.Contains("Qualcomm HS-USB QDLoader"))
                    {
                        devices.Add(new DeviceInfo
                        {
                            DeviceId = deviceId,
                            SerialNumber = ExtractSerialFromDeviceId(deviceId),
                            Brand = "Qualcomm",
                            Model = "EDL Device",
                            ConnectionType = DeviceConnectionType.EDL,
                            Mode = DeviceMode.EDL,
                            IsConnected = true,
                            ConnectedTime = DateTime.Now
                        });
                    }
                    // MTK Preloader
                    else if (name.Contains("MediaTek PreLoader") ||
                             name.Contains("MediaTek USB Port"))
                    {
                        devices.Add(new DeviceInfo
                        {
                            DeviceId = deviceId,
                            SerialNumber = ExtractSerialFromDeviceId(deviceId),
                            Brand = "MediaTek",
                            Model = "MTK Device",
                            ConnectionType = DeviceConnectionType.MTK,
                            Mode = DeviceMode.Download,
                            IsConnected = true,
                            ConnectedTime = DateTime.Now
                        });
                    }
                    // Samsung Download Mode
                    else if (name.Contains("SAMSUNG Mobile USB") ||
                             description.Contains("Samsung Mobile USB"))
                    {
                        devices.Add(new DeviceInfo
                        {
                            DeviceId = deviceId,
                            SerialNumber = ExtractSerialFromDeviceId(deviceId),
                            Brand = "Samsung",
                            Model = "Samsung Device",
                            ConnectionType = DeviceConnectionType.Samsung,
                            Mode = DeviceMode.Download,
                            IsConnected = true,
                            ConnectedTime = DateTime.Now
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error scanning USB devices: {ex.Message}");
            }

            return devices;
        }

        private static string ExtractSerialFromDeviceId(string deviceId)
        {
            var parts = deviceId.Split('\\');
            return parts.Length > 2 ? parts[2] : deviceId;
        }

        private async Task PopulateDeviceInfoAsync(DeviceInfo device)
        {
            try
            {
                device.Brand = await GetPropertyAsync(device.SerialNumber, "ro.product.brand");
                device.Model = await GetPropertyAsync(device.SerialNumber, "ro.product.model");
                device.AndroidVersion = await GetPropertyAsync(device.SerialNumber, "ro.build.version.release");
                device.Chipset = await GetPropertyAsync(device.SerialNumber, "ro.hardware");
            }
            catch (Exception ex)
            {
                Log($"Error getting device properties: {ex.Message}");
            }
        }

        private async Task PopulateFastbootInfoAsync(DeviceInfo device)
        {
            try
            {
                var result = await ExecuteCommandAsync(_fastbootPath, $"-s {device.SerialNumber} getvar all");
                var lines = result.Split('\n');

                foreach (var line in lines)
                {
                    if (line.StartsWith("(bootloader) product:"))
                        device.Model = line.Replace("(bootloader) product:", "").Trim();
                    else if (line.StartsWith("(bootloader) unlocked:"))
                        device.BootloaderStatus = line.Contains("yes") ? "Unlocked" : "Locked";
                }
            }
            catch (Exception ex)
            {
                Log($"Error getting fastboot info: {ex.Message}");
            }
        }

        private async Task<string> GetPropertyAsync(string serialNumber, string property)
        {
            try
            {
                var result = await ExecuteCommandAsync(_adbPath, $"-s {serialNumber} shell getprop {property}");
                return result.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        public async Task<DeviceInfo?> GetDeviceInfoAsync(string deviceId)
        {
            var devices = await GetConnectedDevicesAsync();
            return devices.FirstOrDefault(d => d.DeviceId == deviceId || d.SerialNumber == deviceId);
        }

        public async Task<bool> RebootToModeAsync(string deviceId, DeviceMode mode)
        {
            try
            {
                var command = mode switch
                {
                    DeviceMode.Recovery => "reboot recovery",
                    DeviceMode.Fastboot => "reboot bootloader",
                    DeviceMode.Download => "reboot download", // Samsung
                    DeviceMode.EDL => "reboot edl",
                    _ => "reboot"
                };

                var result = await ExecuteCommandAsync(_adbPath, $"-s {deviceId} {command}");
                Log($"Rebooting device {deviceId} to {mode}: {result}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error rebooting device: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> IsDeviceConnectedAsync(string deviceId)
        {
            var devices = await GetConnectedDevicesAsync();
            return devices.Any(d => d.DeviceId == deviceId || d.SerialNumber == deviceId);
        }

        public async Task<string> ExecuteAdbCommandAsync(string deviceId, string command)
        {
            return await ExecuteCommandAsync(_adbPath, $"-s {deviceId} {command}");
        }

        public async Task<string> ExecuteFastbootCommandAsync(string deviceId, string command)
        {
            return await ExecuteCommandAsync(_fastbootPath, $"-s {deviceId} {command}");
        }

        private async Task<string> ExecuteCommandAsync(string executable, string arguments)
        {
            // Check if executable exists or is in PATH
            if (!IsExecutableAvailable(executable))
            {
                return ""; // Return empty if tool not available
            }

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };

                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        error.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                var result = output.ToString();
                if (!string.IsNullOrEmpty(error.ToString()))
                {
                    result += "\n" + error.ToString();
                }

                return result;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Executable not found
                return "";
            }
        }

        private bool IsExecutableAvailable(string executable)
        {
            // If it's a full path, check if file exists
            if (Path.IsPathRooted(executable))
            {
                return File.Exists(executable);
            }

            // Check if it's just "adb" or "fastboot" (in PATH)
            // Cache the result to avoid repeated checks
            if (_executableCache.TryGetValue(executable, out bool available))
            {
                return available;
            }

            // Try to find in PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            var paths = pathEnv.Split(Path.PathSeparator);

            foreach (var path in paths)
            {
                var fullPath = Path.Combine(path, executable);
                if (File.Exists(fullPath))
                {
                    _executableCache[executable] = true;
                    return true;
                }
                // Also try with .exe extension
                if (File.Exists(fullPath + ".exe"))
                {
                    _executableCache[executable] = true;
                    return true;
                }
            }

            _executableCache[executable] = false;
            return false;
        }

        private static readonly Dictionary<string, bool> _executableCache = new();

        private void Log(string message)
        {
            LogMessage?.Invoke(this, $"[ADB] {message}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _deviceMonitorTimer?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
