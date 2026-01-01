using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class FastbootDevice
    {
        public string Serial { get; set; } = "";
        public string Product { get; set; } = "";
        public string Variant { get; set; } = "";
        public string SecureBoot { get; set; } = "";
        public string Serialno { get; set; } = "";
        public bool IsUnlocked { get; set; }
        public string CurrentSlot { get; set; } = "";
        public List<string> Partitions { get; set; } = new();
    }

    public class FastbootResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
        public TimeSpan Duration { get; set; }
    }

    public class FastbootPartitionInfo
    {
        public string Name { get; set; } = "";
        public string Slot { get; set; } = "";
        public long Size { get; set; }
        public string Type { get; set; } = "";
        public bool IsActive { get; set; }
    }

    public class FastbootService
    {
        private readonly string _fastbootPath;
        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        // Common partitions for different devices
        public static readonly Dictionary<string, List<string>> CommonPartitions = new()
        {
            ["Generic"] = new() { "boot", "recovery", "system", "vendor", "vbmeta", "dtbo", "cache", "userdata" },
            ["A/B Devices"] = new() { "boot_a", "boot_b", "system_a", "system_b", "vendor_a", "vendor_b", "vbmeta_a", "vbmeta_b" },
            ["Samsung"] = new() { "boot", "recovery", "system", "vendor", "vbmeta", "dtbo", "super", "metadata" },
            ["Xiaomi"] = new() { "boot", "recovery", "system", "vendor", "vbmeta", "dtbo", "persist", "cust" },
            ["OnePlus"] = new() { "boot", "recovery", "system", "vendor", "vbmeta", "dtbo", "LOGO", "aop" },
            ["Pixel"] = new() { "boot", "vendor_boot", "system", "vendor", "vbmeta", "dtbo", "product", "system_ext" }
        };

        // Common fastboot commands
        public static readonly Dictionary<string, string> CommonCommands = new()
        {
            ["Reboot to System"] = "reboot",
            ["Reboot to Bootloader"] = "reboot bootloader",
            ["Reboot to Recovery"] = "reboot recovery",
            ["Reboot to Fastbootd"] = "reboot fastboot",
            ["Reboot to EDL"] = "reboot edl",
            ["Get All Variables"] = "getvar all",
            ["Get Product"] = "getvar product",
            ["Get Serial"] = "getvar serialno",
            ["Get Bootloader Version"] = "getvar version-bootloader",
            ["Get Baseband Version"] = "getvar version-baseband",
            ["Get Secure Boot Status"] = "getvar secure",
            ["Get Unlocked Status"] = "getvar unlocked",
            ["Get Current Slot"] = "getvar current-slot",
            ["Get Slot Count"] = "getvar slot-count",
            ["Get Battery Level"] = "getvar battery-level",
            ["Get Battery Voltage"] = "getvar battery-voltage",
            ["Erase Cache"] = "erase cache",
            ["Erase Userdata"] = "erase userdata",
            ["Format Cache"] = "format cache",
            ["Format Userdata"] = "format userdata",
            ["Set Active Slot A"] = "set_active a",
            ["Set Active Slot B"] = "set_active b",
            ["OEM Unlock"] = "oem unlock",
            ["OEM Lock"] = "oem lock",
            ["Flashing Unlock"] = "flashing unlock",
            ["Flashing Lock"] = "flashing lock",
            ["Flashing Unlock Critical"] = "flashing unlock_critical",
            ["Disable Verity"] = "oem disable-verity",
            ["Enable Verity"] = "oem enable-verity"
        };

        public FastbootService(string toolsPath)
        {
            _fastbootPath = Path.Combine(toolsPath, "platform-tools", "fastboot.exe");
        }

        public async Task<List<FastbootDevice>> DetectDevicesAsync()
        {
            var devices = new List<FastbootDevice>();

            try
            {
                var result = await RunCommandAsync("devices -l");
                var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if (line.Contains("fastboot") || line.Contains("device"))
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            var device = new FastbootDevice { Serial = parts[0] };

                            // Get device info
                            var info = await GetDeviceInfoAsync(device.Serial);
                            device.Product = info.GetValueOrDefault("product", "Unknown");
                            device.Variant = info.GetValueOrDefault("variant", "Unknown");
                            device.SecureBoot = info.GetValueOrDefault("secure", "Unknown");
                            device.Serialno = info.GetValueOrDefault("serialno", device.Serial);
                            device.IsUnlocked = info.GetValueOrDefault("unlocked", "no") == "yes";
                            device.CurrentSlot = info.GetValueOrDefault("current-slot", "");

                            devices.Add(device);
                        }
                    }
                }

                LogMessage?.Invoke(this, $"Found {devices.Count} fastboot device(s)");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Device detection error: {ex.Message}");
            }

            return devices;
        }

        public async Task<Dictionary<string, string>> GetDeviceInfoAsync(string serial = "")
        {
            var info = new Dictionary<string, string>();

            try
            {
                var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";
                var result = await RunCommandAsync($"{serialArg} getvar all");
                var lines = (result.Output + result.Error).Split('\n');

                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"(.+?):\s*(.+)");
                    if (match.Success)
                    {
                        var key = match.Groups[1].Value.Trim().ToLower();
                        var value = match.Groups[2].Value.Trim();
                        if (!info.ContainsKey(key))
                            info[key] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Get info error: {ex.Message}");
            }

            return info;
        }

        public async Task<List<FastbootPartitionInfo>> GetPartitionListAsync(string serial = "")
        {
            var partitions = new List<FastbootPartitionInfo>();

            try
            {
                var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";
                var result = await RunCommandAsync($"{serialArg} getvar all");
                var lines = (result.Output + result.Error).Split('\n');

                foreach (var line in lines)
                {
                    // Look for partition-size entries
                    var match = Regex.Match(line, @"partition-size:(\w+):\s*0x([0-9a-fA-F]+)");
                    if (match.Success)
                    {
                        var partition = new FastbootPartitionInfo
                        {
                            Name = match.Groups[1].Value,
                            Size = Convert.ToInt64(match.Groups[2].Value, 16)
                        };

                        // Check if A/B partition
                        if (partition.Name.EndsWith("_a"))
                            partition.Slot = "a";
                        else if (partition.Name.EndsWith("_b"))
                            partition.Slot = "b";

                        partitions.Add(partition);
                    }

                    // Check for partition type
                    var typeMatch = Regex.Match(line, @"partition-type:(\w+):\s*(\w+)");
                    if (typeMatch.Success)
                    {
                        var name = typeMatch.Groups[1].Value;
                        var type = typeMatch.Groups[2].Value;
                        var existing = partitions.Find(p => p.Name == name);
                        if (existing != null)
                            existing.Type = type;
                    }
                }

                LogMessage?.Invoke(this, $"Found {partitions.Count} partitions");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Partition list error: {ex.Message}");
            }

            return partitions;
        }

        public async Task<FastbootResult> FlashPartitionAsync(string partition, string imagePath,
            string serial = "", IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(imagePath))
            {
                return new FastbootResult
                {
                    Success = false,
                    Error = $"Image file not found: {imagePath}"
                };
            }

            LogMessage?.Invoke(this, $"Flashing {partition} with {Path.GetFileName(imagePath)}...");
            progress?.Report(10);

            var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";
            var result = await RunCommandAsync($"{serialArg} flash {partition} \"{imagePath}\"",
                TimeSpan.FromMinutes(10), cancellationToken);

            progress?.Report(100);

            if (result.Success)
                LogMessage?.Invoke(this, $"Flash {partition} completed");
            else
                LogMessage?.Invoke(this, $"Flash {partition} failed: {result.Error}");

            return result;
        }

        public async Task<FastbootResult> FlashMultipleAsync(Dictionary<string, string> partitionImages,
            string serial = "", IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            var totalPartitions = partitionImages.Count;
            var currentPartition = 0;
            var errors = new List<string>();

            foreach (var kvp in partitionImages)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var partition = kvp.Key;
                var imagePath = kvp.Value;

                var progressValue = (int)((currentPartition * 100.0) / totalPartitions);
                progress?.Report(progressValue);

                var result = await FlashPartitionAsync(partition, imagePath, serial, null, cancellationToken);
                if (!result.Success)
                {
                    errors.Add($"{partition}: {result.Error}");
                }

                currentPartition++;
            }

            progress?.Report(100);

            return new FastbootResult
            {
                Success = errors.Count == 0,
                Output = $"Flashed {currentPartition - errors.Count}/{totalPartitions} partitions",
                Error = string.Join("\n", errors)
            };
        }

        public async Task<FastbootResult> ErasePartitionAsync(string partition, string serial = "")
        {
            LogMessage?.Invoke(this, $"Erasing {partition}...");

            var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";
            var result = await RunCommandAsync($"{serialArg} erase {partition}");

            if (result.Success)
                LogMessage?.Invoke(this, $"Erased {partition}");
            else
                LogMessage?.Invoke(this, $"Erase {partition} failed: {result.Error}");

            return result;
        }

        public async Task<FastbootResult> FormatPartitionAsync(string partition, string serial = "")
        {
            LogMessage?.Invoke(this, $"Formatting {partition}...");

            var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";
            var result = await RunCommandAsync($"{serialArg} format {partition}");

            if (result.Success)
                LogMessage?.Invoke(this, $"Formatted {partition}");
            else
                LogMessage?.Invoke(this, $"Format {partition} failed: {result.Error}");

            return result;
        }

        public async Task<FastbootResult> BootImageAsync(string imagePath, string serial = "")
        {
            if (!File.Exists(imagePath))
            {
                return new FastbootResult
                {
                    Success = false,
                    Error = $"Image file not found: {imagePath}"
                };
            }

            LogMessage?.Invoke(this, $"Booting from {Path.GetFileName(imagePath)}...");

            var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";
            return await RunCommandAsync($"{serialArg} boot \"{imagePath}\"");
        }

        public async Task<FastbootResult> RebootAsync(string mode = "", string serial = "")
        {
            var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";
            var cmd = string.IsNullOrEmpty(mode) ? "reboot" : $"reboot {mode}";

            LogMessage?.Invoke(this, $"Rebooting to {(string.IsNullOrEmpty(mode) ? "system" : mode)}...");
            return await RunCommandAsync($"{serialArg} {cmd}");
        }

        public async Task<FastbootResult> UnlockBootloaderAsync(string serial = "")
        {
            LogMessage?.Invoke(this, "Attempting to unlock bootloader...");

            var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";

            // Try different unlock commands
            var result = await RunCommandAsync($"{serialArg} flashing unlock");
            if (!result.Success)
            {
                result = await RunCommandAsync($"{serialArg} oem unlock");
            }

            if (result.Success)
                LogMessage?.Invoke(this, "Bootloader unlock command sent. Check device for confirmation.");
            else
                LogMessage?.Invoke(this, $"Unlock failed: {result.Error}");

            return result;
        }

        public async Task<FastbootResult> LockBootloaderAsync(string serial = "")
        {
            LogMessage?.Invoke(this, "Attempting to lock bootloader...");

            var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";

            var result = await RunCommandAsync($"{serialArg} flashing lock");
            if (!result.Success)
            {
                result = await RunCommandAsync($"{serialArg} oem lock");
            }

            return result;
        }

        public async Task<FastbootResult> SetActiveSlotAsync(string slot, string serial = "")
        {
            if (slot != "a" && slot != "b")
            {
                return new FastbootResult
                {
                    Success = false,
                    Error = "Invalid slot. Must be 'a' or 'b'"
                };
            }

            LogMessage?.Invoke(this, $"Setting active slot to {slot}...");

            var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";
            return await RunCommandAsync($"{serialArg} set_active {slot}");
        }

        public async Task<string> GetVariableAsync(string variable, string serial = "")
        {
            var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";
            var result = await RunCommandAsync($"{serialArg} getvar {variable}");

            var match = Regex.Match(result.Output + result.Error, $@"{variable}:\s*(.+)");
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        public async Task<FastbootResult> RunCustomCommandAsync(string command, string serial = "")
        {
            LogMessage?.Invoke(this, $"Running: fastboot {command}");

            var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";
            return await RunCommandAsync($"{serialArg} {command}");
        }

        public async Task<FastbootResult> FlashUpdatePackageAsync(string zipPath, string serial = "",
            bool wipeData = false, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(zipPath))
            {
                return new FastbootResult
                {
                    Success = false,
                    Error = $"Update package not found: {zipPath}"
                };
            }

            LogMessage?.Invoke(this, $"Flashing update package: {Path.GetFileName(zipPath)}");
            progress?.Report(10);

            var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";
            var wipeArg = wipeData ? "-w" : "";

            var result = await RunCommandAsync($"{serialArg} update {wipeArg} \"{zipPath}\"",
                TimeSpan.FromMinutes(30), cancellationToken);

            progress?.Report(100);
            return result;
        }

        public async Task<FastbootResult> OemCommandAsync(string oemCommand, string serial = "")
        {
            LogMessage?.Invoke(this, $"Running OEM command: {oemCommand}");

            var serialArg = string.IsNullOrEmpty(serial) ? "" : $"-s {serial}";
            return await RunCommandAsync($"{serialArg} oem {oemCommand}");
        }

        private async Task<FastbootResult> RunCommandAsync(string arguments,
            TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var result = new FastbootResult();
            var sw = Stopwatch.StartNew();

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _fastbootPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                var timeoutMs = (int)(timeout?.TotalMilliseconds ?? 120000);
                var completed = await Task.Run(() => process.WaitForExit(timeoutMs), cancellationToken);

                if (!completed)
                {
                    process.Kill();
                    result.Error = "Command timed out";
                    return result;
                }

                result.Output = await outputTask;
                result.Error = await errorTask;
                result.Success = process.ExitCode == 0 ||
                                 result.Output.Contains("OKAY") ||
                                 result.Error.Contains("OKAY");

                // Some commands output to stderr but are successful
                if (!result.Success && string.IsNullOrEmpty(result.Error) &&
                    !result.Output.Contains("FAILED") && !result.Output.Contains("error"))
                {
                    result.Success = true;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            sw.Stop();
            result.Duration = sw.Elapsed;

            return result;
        }
    }
}
