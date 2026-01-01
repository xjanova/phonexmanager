using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PhoneRomFlashTool.Models;

namespace PhoneRomFlashTool.Services
{
    public class FlashService
    {
        private readonly AdbService _adbService;

        public event EventHandler<ProgressEventArgs>? ProgressChanged;
        public event EventHandler<string>? LogMessage;

        public FlashService(AdbService adbService)
        {
            _adbService = adbService;
        }

        public async Task<bool> FlashPartitionAsync(
            string deviceId,
            string partitionName,
            string imagePath,
            IProgress<ProgressEventArgs>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    Log($"Image file not found: {imagePath}");
                    return false;
                }

                var device = await _adbService.GetDeviceInfoAsync(deviceId);
                if (device == null)
                {
                    Log("Device not found");
                    return false;
                }

                Log($"Starting flash of {partitionName} with {imagePath}");
                progress?.Report(new ProgressEventArgs(0, $"Preparing to flash {partitionName}..."));

                // ตรวจสอบ mode ของอุปกรณ์
                if (device.Mode == DeviceMode.Fastboot)
                {
                    return await FlashViaFastbootAsync(deviceId, partitionName, imagePath, progress, cancellationToken);
                }
                else if (device.Mode == DeviceMode.Normal || device.Mode == DeviceMode.Recovery)
                {
                    // ลอง flash ผ่าน ADB (ต้อง root)
                    return await FlashViaAdbAsync(deviceId, partitionName, imagePath, progress, cancellationToken);
                }
                else
                {
                    Log($"Unsupported device mode for flashing: {device.Mode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Flash error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> FlashViaFastbootAsync(
            string deviceId,
            string partitionName,
            string imagePath,
            IProgress<ProgressEventArgs>? progress,
            CancellationToken cancellationToken)
        {
            try
            {
                progress?.Report(new ProgressEventArgs(10, $"Flashing {partitionName} via Fastboot..."));

                var result = await _adbService.ExecuteFastbootCommandAsync(deviceId,
                    $"flash {partitionName} \"{imagePath}\"");

                Log($"Fastboot result: {result}");

                if (result.Contains("OKAY") || result.Contains("Finished"))
                {
                    progress?.Report(new ProgressEventArgs(100, "Flash completed successfully!"));
                    return true;
                }

                if (result.Contains("FAILED") || result.Contains("error"))
                {
                    Log($"Flash failed: {result}");
                    return false;
                }

                progress?.Report(new ProgressEventArgs(100, "Flash completed"));
                return true;
            }
            catch (Exception ex)
            {
                Log($"Fastboot flash error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> FlashViaAdbAsync(
            string deviceId,
            string partitionName,
            string imagePath,
            IProgress<ProgressEventArgs>? progress,
            CancellationToken cancellationToken)
        {
            try
            {
                progress?.Report(new ProgressEventArgs(10, "Pushing image to device..."));

                var fileName = Path.GetFileName(imagePath);
                var remotePath = $"/sdcard/{fileName}";

                // Push image to device
                var pushResult = await _adbService.ExecuteAdbCommandAsync(deviceId,
                    $"push \"{imagePath}\" {remotePath}");

                if (pushResult.Contains("error"))
                {
                    Log($"Push failed: {pushResult}");
                    return false;
                }

                progress?.Report(new ProgressEventArgs(50, $"Flashing {partitionName}..."));

                // Find partition path
                var partitionPath = await FindPartitionPathAsync(deviceId, partitionName);
                if (string.IsNullOrEmpty(partitionPath))
                {
                    Log($"Partition {partitionName} not found");
                    return false;
                }

                // Flash using dd
                var ddResult = await _adbService.ExecuteAdbCommandAsync(deviceId,
                    $"shell \"su -c 'dd if={remotePath} of={partitionPath} bs=4096'\"");

                // Clean up
                await _adbService.ExecuteAdbCommandAsync(deviceId, $"shell rm {remotePath}");

                progress?.Report(new ProgressEventArgs(100, "Flash completed!"));
                Log($"Flash completed: {ddResult}");

                return !ddResult.Contains("error");
            }
            catch (Exception ex)
            {
                Log($"ADB flash error: {ex.Message}");
                return false;
            }
        }

        private async Task<string> FindPartitionPathAsync(string deviceId, string partitionName)
        {
            var paths = new[]
            {
                $"/dev/block/bootdevice/by-name/{partitionName}",
                $"/dev/block/by-name/{partitionName}",
                $"/dev/block/platform/*/by-name/{partitionName}"
            };

            foreach (var path in paths)
            {
                var result = await _adbService.ExecuteAdbCommandAsync(deviceId,
                    $"shell \"su -c 'ls {path} 2>/dev/null'\"");

                if (!string.IsNullOrWhiteSpace(result) && !result.Contains("No such"))
                {
                    return result.Trim();
                }
            }

            return string.Empty;
        }

        public async Task<bool> RebootDeviceAsync(string deviceId, DeviceMode mode)
        {
            return await _adbService.RebootToModeAsync(deviceId, mode);
        }

        public async Task<bool> WipePartitionAsync(string deviceId, string partitionName)
        {
            try
            {
                var device = await _adbService.GetDeviceInfoAsync(deviceId);
                if (device?.Mode != DeviceMode.Fastboot)
                {
                    Log("Device must be in Fastboot mode to wipe partitions");
                    return false;
                }

                var result = await _adbService.ExecuteFastbootCommandAsync(deviceId,
                    $"erase {partitionName}");

                Log($"Wipe result: {result}");
                return result.Contains("OKAY");
            }
            catch (Exception ex)
            {
                Log($"Wipe error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UnlockBootloaderAsync(string deviceId)
        {
            try
            {
                var device = await _adbService.GetDeviceInfoAsync(deviceId);
                if (device?.Mode != DeviceMode.Fastboot)
                {
                    Log("Device must be in Fastboot mode to unlock bootloader");
                    return false;
                }

                var result = await _adbService.ExecuteFastbootCommandAsync(deviceId,
                    "flashing unlock");

                Log($"Unlock result: {result}");
                return result.Contains("OKAY");
            }
            catch (Exception ex)
            {
                Log($"Unlock error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> LockBootloaderAsync(string deviceId)
        {
            try
            {
                var device = await _adbService.GetDeviceInfoAsync(deviceId);
                if (device?.Mode != DeviceMode.Fastboot)
                {
                    Log("Device must be in Fastboot mode to lock bootloader");
                    return false;
                }

                var result = await _adbService.ExecuteFastbootCommandAsync(deviceId,
                    "flashing lock");

                Log($"Lock result: {result}");
                return result.Contains("OKAY");
            }
            catch (Exception ex)
            {
                Log($"Lock error: {ex.Message}");
                return false;
            }
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(this, $"[Flash] {message}");
        }
    }
}
