using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class QualcommDevice
    {
        public string Port { get; set; } = "";
        public string Mode { get; set; } = ""; // EDL, Diag, Sahara
        public string Chipset { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public bool IsSecure { get; set; }
        public string HwId { get; set; } = "";
        public string SwId { get; set; } = "";
    }

    public class PartitionEntry
    {
        public string Name { get; set; } = "";
        public long StartSector { get; set; }
        public long NumSectors { get; set; }
        public long SizeBytes => NumSectors * 512;
        public string SizeFormatted => FormatSize(SizeBytes);
        public string Type { get; set; } = "";
        public bool IsActive { get; set; }
        public string Guid { get; set; } = "";

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }

    public class QualcommService
    {
        private readonly string _toolsPath;
        private readonly string _edlPath;
        private readonly string _loadersPath;
        private readonly HttpClient _httpClient;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        // EDL Tool download URL (bkerler/edl)
        private const string EDL_REPO_URL = "https://github.com/bkerler/edl/archive/refs/heads/main.zip";

        // Common Qualcomm chipsets and their loaders
        public static readonly Dictionary<string, string> ChipsetLoaders = new()
        {
            { "MSM8917", "prog_emmc_firehose_8917.mbn" },
            { "MSM8937", "prog_emmc_firehose_8937.mbn" },
            { "MSM8953", "prog_emmc_firehose_8953.mbn" },
            { "MSM8996", "prog_ufs_firehose_8996.elf" },
            { "MSM8998", "prog_ufs_firehose_8998.elf" },
            { "SDM660", "prog_firehose_ddr.elf" },
            { "SDM670", "prog_firehose_ddr.elf" },
            { "SDM710", "prog_firehose_ddr.elf" },
            { "SDM845", "prog_firehose_ddr.elf" },
            { "SM6115", "prog_firehose_ddr.elf" }, // Snapdragon 662
            { "SM6125", "prog_firehose_ddr.elf" }, // Snapdragon 665
            { "SM6150", "prog_firehose_ddr.elf" }, // Snapdragon 675
            { "SM7125", "prog_firehose_ddr.elf" }, // Snapdragon 720G
            { "SM7150", "prog_firehose_ddr.elf" }, // Snapdragon 730
            { "SM7225", "prog_firehose_ddr.elf" }, // Snapdragon 750G
            { "SM7325", "prog_firehose_ddr.elf" }, // Snapdragon 778G
            { "SM8150", "prog_firehose_ddr.elf" }, // Snapdragon 855
            { "SM8250", "prog_firehose_ddr.elf" }, // Snapdragon 865
            { "SM8350", "prog_firehose_ddr.elf" }, // Snapdragon 888
            { "SM8450", "prog_firehose_ddr.elf" }, // Snapdragon 8 Gen 1
            { "SM8550", "prog_firehose_ddr.elf" }, // Snapdragon 8 Gen 2
            { "SM8650", "prog_firehose_ddr.elf" }, // Snapdragon 8 Gen 3
        };

        public QualcommService(string toolsPath)
        {
            _toolsPath = toolsPath;
            _edlPath = Path.Combine(toolsPath, "edl");
            _loadersPath = Path.Combine(toolsPath, "loaders");
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhoneRomFlashTool/1.0");

            Directory.CreateDirectory(_loadersPath);
        }

        public bool IsEdlToolInstalled()
        {
            return File.Exists(Path.Combine(_edlPath, "edl.py")) ||
                   File.Exists(Path.Combine(_edlPath, "edl-main", "edl.py"));
        }

        public async Task<bool> DownloadEdlToolAsync(IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Downloading EDL tool (bkerler/edl)...");
                var zipPath = Path.Combine(_toolsPath, "edl.zip");

                using var response = await _httpClient.GetAsync(EDL_REPO_URL, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;

                using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[8192];
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        downloadedBytes += bytesRead;
                        if (totalBytes > 0)
                            progress?.Report((int)((downloadedBytes * 100) / totalBytes));
                    }
                }

                LogMessage?.Invoke(this, "Extracting EDL tool...");
                if (Directory.Exists(_edlPath))
                    Directory.Delete(_edlPath, true);

                ZipFile.ExtractToDirectory(zipPath, _edlPath);
                File.Delete(zipPath);

                LogMessage?.Invoke(this, "EDL tool installed successfully");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Download error: {ex.Message}");
                return false;
            }
        }

        public async Task<List<QualcommDevice>> DetectEdlDevicesAsync()
        {
            var devices = new List<QualcommDevice>();

            try
            {
                // Search for Qualcomm EDL devices (VID: 05C6)
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_05C6%'");

                foreach (var obj in searcher.Get())
                {
                    var deviceId = obj["DeviceID"]?.ToString() ?? "";
                    var name = obj["Name"]?.ToString() ?? "";

                    var device = new QualcommDevice();

                    if (deviceId.Contains("PID_9008"))
                    {
                        device.Mode = "EDL (Emergency Download)";
                    }
                    else if (deviceId.Contains("PID_9006") || deviceId.Contains("PID_9007"))
                    {
                        device.Mode = "Diag Mode";
                    }
                    else if (deviceId.Contains("PID_900E"))
                    {
                        device.Mode = "Sahara Mode";
                    }
                    else continue;

                    // Get COM port
                    if (name.Contains("COM"))
                    {
                        var match = Regex.Match(name, @"COM(\d+)");
                        if (match.Success)
                            device.Port = $"COM{match.Groups[1].Value}";
                    }

                    // Try to get device info using EDL
                    if (IsEdlToolInstalled() && device.Mode.Contains("EDL"))
                    {
                        var info = await GetDeviceInfoViaEdlAsync(device.Port);
                        device.Chipset = info.GetValueOrDefault("chipset", "");
                        device.HwId = info.GetValueOrDefault("hwid", "");
                        device.SerialNumber = info.GetValueOrDefault("serial", "");
                        device.IsSecure = info.GetValueOrDefault("secure", "") == "True";
                    }

                    devices.Add(device);
                    LogMessage?.Invoke(this, $"Found: {device.Mode} on {device.Port} ({device.Chipset})");
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Detection error: {ex.Message}");
            }

            return devices;
        }

        private async Task<Dictionary<string, string>> GetDeviceInfoViaEdlAsync(string port)
        {
            var info = new Dictionary<string, string>();
            try
            {
                var result = await RunEdlCommandAsync($"--port={port} printgpt", TimeSpan.FromSeconds(10));

                // Parse chipset
                var chipMatch = Regex.Match(result, @"Chipset:\s*(\w+)");
                if (chipMatch.Success)
                    info["chipset"] = chipMatch.Groups[1].Value;

                var serialMatch = Regex.Match(result, @"Serial:\s*(\w+)");
                if (serialMatch.Success)
                    info["serial"] = serialMatch.Groups[1].Value;

                info["secure"] = result.Contains("Secure boot: True") ? "True" : "False";
            }
            catch { }
            return info;
        }

        public async Task<List<PartitionEntry>> ReadGptAsync(string port = "", string loaderPath = "")
        {
            var partitions = new List<PartitionEntry>();

            try
            {
                if (!IsEdlToolInstalled())
                {
                    LogMessage?.Invoke(this, "EDL tool not installed. Downloading...");
                    await DownloadEdlToolAsync();
                }

                LogMessage?.Invoke(this, "Reading GPT partition table...");

                var args = "printgpt";
                if (!string.IsNullOrEmpty(port))
                    args = $"--port={port} " + args;
                if (!string.IsNullOrEmpty(loaderPath) && File.Exists(loaderPath))
                    args = $"--loader=\"{loaderPath}\" " + args;

                var result = await RunEdlCommandAsync(args, TimeSpan.FromSeconds(30));

                // Parse GPT output
                // Format: partition_name  start_sector  num_sectors  size
                var lines = result.Split('\n');
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"(\w+)\s+(\d+)\s+(\d+)\s+([\d.]+\s*\w+)");
                    if (match.Success)
                    {
                        partitions.Add(new PartitionEntry
                        {
                            Name = match.Groups[1].Value,
                            StartSector = long.Parse(match.Groups[2].Value),
                            NumSectors = long.Parse(match.Groups[3].Value)
                        });
                    }
                }

                LogMessage?.Invoke(this, $"Found {partitions.Count} partitions");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"GPT read error: {ex.Message}");
            }

            return partitions;
        }

        public async Task<bool> FlashPartitionAsync(string port, string loaderPath,
            string partitionName, string imagePath, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    LogMessage?.Invoke(this, $"Image not found: {imagePath}");
                    return false;
                }

                if (!IsEdlToolInstalled())
                {
                    await DownloadEdlToolAsync(progress, ct);
                }

                LogMessage?.Invoke(this, $"Flashing {partitionName}...");
                progress?.Report(10);

                var args = $"w {partitionName} \"{imagePath}\"";
                if (!string.IsNullOrEmpty(port))
                    args = $"--port={port} " + args;
                if (!string.IsNullOrEmpty(loaderPath) && File.Exists(loaderPath))
                    args = $"--loader=\"{loaderPath}\" " + args;

                var result = await RunEdlCommandAsync(args, TimeSpan.FromMinutes(10), ct, p => progress?.Report(10 + (int)(p * 0.9)));

                var success = result.Contains("Done") || result.Contains("successfully") || result.Contains("ok");
                progress?.Report(100);

                LogMessage?.Invoke(this, success ? $"Flash {partitionName} completed" : $"Flash failed: {result}");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Flash error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> BackupPartitionAsync(string port, string loaderPath,
            string partitionName, string outputPath, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                if (!IsEdlToolInstalled())
                {
                    await DownloadEdlToolAsync(progress, ct);
                }

                LogMessage?.Invoke(this, $"Backing up {partitionName}...");
                progress?.Report(10);

                var args = $"r {partitionName} \"{outputPath}\"";
                if (!string.IsNullOrEmpty(port))
                    args = $"--port={port} " + args;
                if (!string.IsNullOrEmpty(loaderPath) && File.Exists(loaderPath))
                    args = $"--loader=\"{loaderPath}\" " + args;

                var result = await RunEdlCommandAsync(args, TimeSpan.FromMinutes(10), ct, p => progress?.Report(10 + (int)(p * 0.9)));

                var success = File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
                progress?.Report(100);

                LogMessage?.Invoke(this, success ? $"Backup saved: {outputPath}" : "Backup failed");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Backup error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ErasePartitionAsync(string port, string loaderPath, string partitionName)
        {
            try
            {
                LogMessage?.Invoke(this, $"Erasing {partitionName}...");

                var args = $"e {partitionName}";
                if (!string.IsNullOrEmpty(port))
                    args = $"--port={port} " + args;
                if (!string.IsNullOrEmpty(loaderPath) && File.Exists(loaderPath))
                    args = $"--loader=\"{loaderPath}\" " + args;

                var result = await RunEdlCommandAsync(args, TimeSpan.FromMinutes(2));
                var success = result.Contains("Done") || result.Contains("ok");

                LogMessage?.Invoke(this, success ? $"Erased {partitionName}" : $"Erase failed");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Erase error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RebootDeviceAsync(string port = "", string mode = "")
        {
            try
            {
                var rebootMode = mode switch
                {
                    "edl" => "edl",
                    "recovery" => "recovery",
                    "bootloader" => "bootloader",
                    _ => ""
                };

                var args = $"reset {rebootMode}".Trim();
                if (!string.IsNullOrEmpty(port))
                    args = $"--port={port} " + args;

                await RunEdlCommandAsync(args, TimeSpan.FromSeconds(10));
                LogMessage?.Invoke(this, $"Reboot to {(string.IsNullOrEmpty(mode) ? "system" : mode)}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Reboot error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UnlockCriticalAsync(string port = "", string loaderPath = "")
        {
            try
            {
                LogMessage?.Invoke(this, "Attempting to unlock critical partitions...");

                var args = "setprojmodel 1";
                if (!string.IsNullOrEmpty(port))
                    args = $"--port={port} " + args;
                if (!string.IsNullOrEmpty(loaderPath) && File.Exists(loaderPath))
                    args = $"--loader=\"{loaderPath}\" " + args;

                var result = await RunEdlCommandAsync(args, TimeSpan.FromSeconds(30));
                var success = !result.Contains("error");

                LogMessage?.Invoke(this, success ? "Critical unlock attempted" : "Unlock failed");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Unlock error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> WriteRawAsync(string port, string loaderPath, long startSector, string imagePath)
        {
            try
            {
                LogMessage?.Invoke(this, $"Writing raw data at sector {startSector}...");

                var args = $"ws {startSector} \"{imagePath}\"";
                if (!string.IsNullOrEmpty(port))
                    args = $"--port={port} " + args;
                if (!string.IsNullOrEmpty(loaderPath) && File.Exists(loaderPath))
                    args = $"--loader=\"{loaderPath}\" " + args;

                var result = await RunEdlCommandAsync(args, TimeSpan.FromMinutes(10));
                return result.Contains("Done") || result.Contains("ok");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Raw write error: {ex.Message}");
                return false;
            }
        }

        public async Task<string> ReadQfuseAsync(string port = "")
        {
            try
            {
                var args = "qfuses";
                if (!string.IsNullOrEmpty(port))
                    args = $"--port={port} " + args;

                return await RunEdlCommandAsync(args, TimeSpan.FromSeconds(30));
            }
            catch
            {
                return "";
            }
        }

        private async Task<string> RunEdlCommandAsync(string arguments, TimeSpan timeout,
            CancellationToken ct = default, Action<int>? progressCallback = null)
        {
            var edlScript = Path.Combine(_edlPath, "edl-main", "edl.py");
            if (!File.Exists(edlScript))
                edlScript = Path.Combine(_edlPath, "edl.py");

            var pythonPath = await FindPythonAsync();
            if (string.IsNullOrEmpty(pythonPath))
            {
                throw new Exception("Python not found. Please install Python 3.8+");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{edlScript}\" {arguments}",
                WorkingDirectory = Path.GetDirectoryName(edlScript),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            var output = new System.Text.StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    output.AppendLine(e.Data);
                    // Parse progress from output
                    var match = Regex.Match(e.Data, @"(\d+)%");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int pct))
                        progressCallback?.Invoke(pct);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    output.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds), ct);
            if (!completed)
            {
                process.Kill();
                throw new TimeoutException("Command timed out");
            }

            return output.ToString();
        }

        private async Task<string> FindPythonAsync()
        {
            var pythonPaths = new[] { "python", "python3", "py" };

            foreach (var python in pythonPaths)
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = python,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        if (process.ExitCode == 0)
                            return python;
                    }
                }
                catch { }
            }

            // Check common install paths
            var commonPaths = new[]
            {
                @"C:\Python312\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Python310\python.exe",
                @"C:\Python39\python.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\Python\Python312\python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\Python\Python311\python.exe")
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return "";
        }

        public string GetRecommendedLoader(string chipset)
        {
            if (ChipsetLoaders.TryGetValue(chipset.ToUpper(), out var loader))
            {
                var loaderPath = Path.Combine(_loadersPath, loader);
                if (File.Exists(loaderPath))
                    return loaderPath;
            }
            return "";
        }

        public List<string> GetAvailableLoaders()
        {
            if (!Directory.Exists(_loadersPath))
                return new List<string>();

            return Directory.GetFiles(_loadersPath, "*.mbn")
                .Concat(Directory.GetFiles(_loadersPath, "*.elf"))
                .Select(Path.GetFileName)
                .ToList()!;
        }

        public List<string> GetSupportedChipsets() => ChipsetLoaders.Keys.ToList();
    }

    public class ImeiService
    {
        private readonly string _adbPath;
        public event EventHandler<string>? LogMessage;

        public ImeiService(string adbPath)
        {
            _adbPath = adbPath;
        }

        public async Task<Dictionary<string, string>> ReadImeiInfoAsync()
        {
            var info = new Dictionary<string, string>();

            try
            {
                var imei1 = await GetImeiSlotAsync(0);
                var imei2 = await GetImeiSlotAsync(1);

                if (!string.IsNullOrEmpty(imei1))
                    info["IMEI 1"] = imei1;
                if (!string.IsNullOrEmpty(imei2))
                    info["IMEI 2"] = imei2;

                var serial = await RunAdbCommandAsync("shell getprop ro.serialno");
                if (!string.IsNullOrEmpty(serial))
                    info["Serial"] = serial.Trim();

                var wifiMac = await RunAdbCommandAsync("shell cat /sys/class/net/wlan0/address 2>/dev/null");
                if (!string.IsNullOrEmpty(wifiMac) && wifiMac.Contains(":"))
                    info["WiFi MAC"] = wifiMac.Trim();

                var btMac = await RunAdbCommandAsync("shell settings get secure bluetooth_address");
                if (!string.IsNullOrEmpty(btMac) && btMac.Contains(":"))
                    info["Bluetooth MAC"] = btMac.Trim();
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error: {ex.Message}");
            }

            return info;
        }

        private async Task<string> GetImeiSlotAsync(int slot)
        {
            try
            {
                // Method 1: service call (Android 5+)
                var serviceNum = slot == 0 ? 1 : 4;
                var result = await RunAdbCommandAsync($"shell service call iphonesubinfo {serviceNum}");
                var imei = ParseServiceCallOutput(result);

                if (!string.IsNullOrEmpty(imei) && imei.Length >= 14)
                    return imei;

                // Method 2: getprop
                result = await RunAdbCommandAsync($"shell getprop persist.radio.imei{(slot == 0 ? "" : "2")}");
                if (!string.IsNullOrEmpty(result.Trim()) && result.Trim().Length >= 14)
                    return result.Trim();

                // Method 3: dumpsys (requires root or special permissions)
                result = await RunAdbCommandAsync("shell dumpsys iphonesubinfo");
                var match = Regex.Match(result, @"Device ID\s*=\s*(\d{15})");
                if (match.Success)
                    return match.Groups[1].Value;

                return "";
            }
            catch
            {
                return "";
            }
        }

        private string ParseServiceCallOutput(string output)
        {
            try
            {
                // Parse Result: Parcel format with hex values
                var sb = new System.Text.StringBuilder();
                var matches = Regex.Matches(output, @"'([0-9.])'");
                foreach (Match m in matches)
                {
                    sb.Append(m.Groups[1].Value);
                }
                var result = sb.ToString().Replace(".", "");
                return result.Length >= 14 ? result : "";
            }
            catch
            {
                return "";
            }
        }

        public bool ValidateImei(string imei)
        {
            if (string.IsNullOrEmpty(imei) || imei.Length != 15 || !imei.All(char.IsDigit))
                return false;

            // Luhn algorithm
            int sum = 0;
            for (int i = 0; i < 14; i++)
            {
                int digit = imei[i] - '0';
                if (i % 2 == 1)
                {
                    digit *= 2;
                    if (digit > 9) digit -= 9;
                }
                sum += digit;
            }

            int checkDigit = (10 - (sum % 10)) % 10;
            return checkDigit == (imei[14] - '0');
        }

        public string GenerateCheckDigit(string imei14)
        {
            if (imei14.Length != 14 || !imei14.All(char.IsDigit)) return "";

            int sum = 0;
            for (int i = 0; i < 14; i++)
            {
                int digit = imei14[i] - '0';
                if (i % 2 == 1)
                {
                    digit *= 2;
                    if (digit > 9) digit -= 9;
                }
                sum += digit;
            }

            return ((10 - (sum % 10)) % 10).ToString();
        }

        private async Task<string> RunAdbCommandAsync(string command)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return "";

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                return output;
            }
            catch
            {
                return "";
            }
        }
    }
}
