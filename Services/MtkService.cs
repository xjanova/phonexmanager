using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class MtkDevice
    {
        public string Port { get; set; } = "";
        public string Mode { get; set; } = ""; // Preloader, BROM, Meta
        public string Chipset { get; set; } = "";
        public string HwCode { get; set; } = "";
        public string SwVersion { get; set; } = "";
        public bool IsSecureBoot { get; set; }
        public bool IsDaEnabled { get; set; }
        public string SocId { get; set; } = "";
    }

    public class ScatterEntry
    {
        public string PartitionName { get; set; } = "";
        public string FileName { get; set; } = "";
        public long StartAddress { get; set; }
        public long Length { get; set; }
        public string Region { get; set; } = "";
        public string Storage { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsDownload { get; set; }
        public bool IsBoot { get; set; }
        public string SizeFormatted => FormatSize(Length);

        private static string FormatSize(long bytes)
        {
            if (bytes == 0) return "0 B";
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

    public class MtkService
    {
        private readonly string _toolsPath;
        private readonly string _mtkClientPath;
        private readonly HttpClient _httpClient;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        // MTKClient download URL (bkerler/mtkclient)
        private const string MTKCLIENT_URL = "https://github.com/bkerler/mtkclient/archive/refs/heads/main.zip";

        // Common MTK chipsets
        public static readonly Dictionary<string, string> ChipsetInfo = new()
        {
            { "MT6735", "Helio P10 (2015)" },
            { "MT6737", "Quad-core (2016)" },
            { "MT6739", "Quad-core (2018)" },
            { "MT6750", "Helio P10 (2016)" },
            { "MT6755", "Helio P10 (2016)" },
            { "MT6757", "Helio P20 (2016)" },
            { "MT6758", "Helio P30 (2017)" },
            { "MT6761", "Helio A22 (2018)" },
            { "MT6762", "Helio P22 (2018)" },
            { "MT6765", "Helio P35 (2018)" },
            { "MT6768", "Helio G85 (2020)" },
            { "MT6769", "Helio G80 (2020)" },
            { "MT6771", "Helio P60 (2018)" },
            { "MT6779", "Helio P90 (2018)" },
            { "MT6781", "Helio G96 (2021)" },
            { "MT6785", "Helio G90T (2019)" },
            { "MT6789", "Helio G99 (2022)" },
            { "MT6833", "Dimensity 700 (2020)" },
            { "MT6853", "Dimensity 720 (2020)" },
            { "MT6873", "Dimensity 800U (2020)" },
            { "MT6875", "Dimensity 820 (2020)" },
            { "MT6877", "Dimensity 900 (2021)" },
            { "MT6879", "Dimensity 920 (2021)" },
            { "MT6883", "Dimensity 1100 (2021)" },
            { "MT6885", "Dimensity 1000+ (2020)" },
            { "MT6889", "Dimensity 1000+ (2020)" },
            { "MT6893", "Dimensity 1200 (2021)" },
            { "MT6895", "Dimensity 8100 (2022)" },
            { "MT6983", "Dimensity 9000 (2022)" },
            { "MT6985", "Dimensity 9200 (2023)" },
            { "MT6989", "Dimensity 9300 (2024)" }
        };

        public MtkService(string toolsPath)
        {
            _toolsPath = toolsPath;
            _mtkClientPath = Path.Combine(toolsPath, "mtkclient");
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhoneRomFlashTool/1.0");
        }

        public bool IsMtkClientInstalled()
        {
            return File.Exists(Path.Combine(_mtkClientPath, "mtk.py")) ||
                   File.Exists(Path.Combine(_mtkClientPath, "mtkclient-main", "mtk.py"));
        }

        public async Task<bool> DownloadMtkClientAsync(IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Downloading MTKClient (bkerler/mtkclient)...");
                var zipPath = Path.Combine(_toolsPath, "mtkclient.zip");

                using var response = await _httpClient.GetAsync(MTKCLIENT_URL, HttpCompletionOption.ResponseHeadersRead, ct);
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

                LogMessage?.Invoke(this, "Extracting MTKClient...");
                if (Directory.Exists(_mtkClientPath))
                    Directory.Delete(_mtkClientPath, true);

                ZipFile.ExtractToDirectory(zipPath, _mtkClientPath);
                File.Delete(zipPath);

                LogMessage?.Invoke(this, "MTKClient installed successfully");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Download error: {ex.Message}");
                return false;
            }
        }

        public async Task<List<MtkDevice>> DetectMtkDevicesAsync()
        {
            var devices = new List<MtkDevice>();

            try
            {
                // Search for MTK devices (VID: 0E8D)
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_0E8D%'");

                foreach (var obj in searcher.Get())
                {
                    var deviceId = obj["DeviceID"]?.ToString() ?? "";
                    var name = obj["Name"]?.ToString() ?? "";

                    var device = new MtkDevice();

                    if (deviceId.Contains("PID_0003") || deviceId.Contains("PID_2000"))
                    {
                        device.Mode = "Preloader/BROM";
                    }
                    else if (deviceId.Contains("PID_2001"))
                    {
                        device.Mode = "Meta Mode";
                    }
                    else if (deviceId.Contains("PID_201C") || deviceId.Contains("PID_201D"))
                    {
                        device.Mode = "DA Mode";
                        device.IsDaEnabled = true;
                    }
                    else continue;

                    // Get COM port
                    if (name.Contains("COM"))
                    {
                        var match = Regex.Match(name, @"COM(\d+)");
                        if (match.Success)
                            device.Port = $"COM{match.Groups[1].Value}";
                    }

                    // Try to get device info using mtkclient
                    if (IsMtkClientInstalled() && device.Mode.Contains("BROM"))
                    {
                        var info = await GetDeviceInfoViaMtkAsync();
                        device.Chipset = info.GetValueOrDefault("chipset", "");
                        device.HwCode = info.GetValueOrDefault("hwcode", "");
                        device.IsSecureBoot = info.GetValueOrDefault("secure", "") == "True";
                        device.SocId = info.GetValueOrDefault("socid", "");
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

        private async Task<Dictionary<string, string>> GetDeviceInfoViaMtkAsync()
        {
            var info = new Dictionary<string, string>();
            try
            {
                var result = await RunMtkCommandAsync("payload", TimeSpan.FromSeconds(10));

                var hwMatch = Regex.Match(result, @"hw_code:\s*(0x[0-9A-Fa-f]+)");
                if (hwMatch.Success)
                    info["hwcode"] = hwMatch.Groups[1].Value;

                var chipMatch = Regex.Match(result, @"(MT\d+)");
                if (chipMatch.Success)
                    info["chipset"] = chipMatch.Groups[1].Value;

                info["secure"] = result.Contains("Secure boot: True") ? "True" : "False";

                var socMatch = Regex.Match(result, @"socid:\s*([0-9A-Fa-f]+)");
                if (socMatch.Success)
                    info["socid"] = socMatch.Groups[1].Value;
            }
            catch { }
            return info;
        }

        public async Task<List<ScatterEntry>> ParseScatterFileAsync(string scatterPath)
        {
            var entries = new List<ScatterEntry>();

            try
            {
                if (!File.Exists(scatterPath))
                {
                    LogMessage?.Invoke(this, "Scatter file not found");
                    return entries;
                }

                var lines = await File.ReadAllLinesAsync(scatterPath);
                ScatterEntry? currentEntry = null;

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    if (trimmedLine.StartsWith("- partition_index:"))
                    {
                        if (currentEntry != null)
                            entries.Add(currentEntry);
                        currentEntry = new ScatterEntry();
                    }
                    else if (currentEntry != null)
                    {
                        if (trimmedLine.StartsWith("partition_name:"))
                            currentEntry.PartitionName = trimmedLine.Replace("partition_name:", "").Trim();
                        else if (trimmedLine.StartsWith("file_name:"))
                            currentEntry.FileName = trimmedLine.Replace("file_name:", "").Trim();
                        else if (trimmedLine.StartsWith("linear_start_addr:"))
                        {
                            var addr = trimmedLine.Replace("linear_start_addr:", "").Trim();
                            if (addr.StartsWith("0x"))
                                currentEntry.StartAddress = Convert.ToInt64(addr, 16);
                        }
                        else if (trimmedLine.StartsWith("partition_size:"))
                        {
                            var size = trimmedLine.Replace("partition_size:", "").Trim();
                            if (size.StartsWith("0x"))
                                currentEntry.Length = Convert.ToInt64(size, 16);
                        }
                        else if (trimmedLine.StartsWith("is_download:"))
                            currentEntry.IsDownload = trimmedLine.Contains("true");
                        else if (trimmedLine.StartsWith("type:"))
                            currentEntry.Type = trimmedLine.Replace("type:", "").Trim();
                        else if (trimmedLine.StartsWith("region:"))
                            currentEntry.Region = trimmedLine.Replace("region:", "").Trim();
                    }
                }

                if (currentEntry != null)
                    entries.Add(currentEntry);

                LogMessage?.Invoke(this, $"Parsed {entries.Count} partitions from scatter file");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Scatter parse error: {ex.Message}");
            }

            return entries;
        }

        public async Task<List<ScatterEntry>> ReadGptFromDeviceAsync(CancellationToken ct = default)
        {
            var partitions = new List<ScatterEntry>();

            try
            {
                if (!IsMtkClientInstalled())
                {
                    LogMessage?.Invoke(this, "MTKClient not installed. Downloading...");
                    await DownloadMtkClientAsync(null, ct);
                }

                LogMessage?.Invoke(this, "Reading GPT from device...");
                var result = await RunMtkCommandAsync("printgpt", TimeSpan.FromSeconds(30), ct);

                // Parse GPT output
                var lines = result.Split('\n');
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"(\w+)\s+(0x[0-9A-Fa-f]+)\s+(0x[0-9A-Fa-f]+)\s+\(([\d.]+\s*\w+)\)");
                    if (match.Success)
                    {
                        partitions.Add(new ScatterEntry
                        {
                            PartitionName = match.Groups[1].Value,
                            StartAddress = Convert.ToInt64(match.Groups[2].Value, 16),
                            Length = Convert.ToInt64(match.Groups[3].Value, 16)
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

        public async Task<bool> FlashPartitionAsync(string partitionName, string imagePath,
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    LogMessage?.Invoke(this, $"Image not found: {imagePath}");
                    return false;
                }

                if (!IsMtkClientInstalled())
                {
                    await DownloadMtkClientAsync(progress, ct);
                }

                LogMessage?.Invoke(this, $"Flashing {partitionName}...");
                progress?.Report(10);

                var args = $"w {partitionName} \"{imagePath}\"";
                var result = await RunMtkCommandAsync(args, TimeSpan.FromMinutes(10), ct, p => progress?.Report(10 + (int)(p * 0.9)));

                var success = result.Contains("Done") || result.Contains("successfully") || result.Contains("ok");
                progress?.Report(100);

                LogMessage?.Invoke(this, success ? $"Flash {partitionName} completed" : $"Flash failed");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Flash error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> FlashFirmwareAsync(string scatterPath, List<string> selectedPartitions,
            string firmwarePath, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                if (!IsMtkClientInstalled())
                {
                    await DownloadMtkClientAsync(progress, ct);
                }

                LogMessage?.Invoke(this, "Starting MTK firmware flash...");

                var scatter = await ParseScatterFileAsync(scatterPath);
                int total = selectedPartitions.Count;
                int current = 0;

                foreach (var partName in selectedPartitions)
                {
                    if (ct.IsCancellationRequested) break;

                    var entry = scatter.Find(s => s.PartitionName == partName);
                    if (entry == null) continue;

                    var imagePath = Path.Combine(firmwarePath, entry.FileName);
                    if (!File.Exists(imagePath))
                    {
                        LogMessage?.Invoke(this, $"Skipping {partName}: file not found");
                        continue;
                    }

                    LogMessage?.Invoke(this, $"Flashing {partName}...");

                    var args = $"w {partName} \"{imagePath}\"";
                    await RunMtkCommandAsync(args, TimeSpan.FromMinutes(5), ct);

                    current++;
                    progress?.Report((current * 100) / total);
                }

                LogMessage?.Invoke(this, "Flash completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Flash error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ReadPartitionAsync(string partitionName, string outputPath,
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                if (!IsMtkClientInstalled())
                {
                    await DownloadMtkClientAsync(progress, ct);
                }

                LogMessage?.Invoke(this, $"Reading {partitionName}...");
                progress?.Report(10);

                var args = $"r {partitionName} \"{outputPath}\"";
                var result = await RunMtkCommandAsync(args, TimeSpan.FromMinutes(10), ct, p => progress?.Report(10 + (int)(p * 0.9)));

                var success = File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
                progress?.Report(100);

                LogMessage?.Invoke(this, success ? $"Saved to {outputPath}" : "Read failed");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Read error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ErasePartitionAsync(string partitionName, CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, $"Erasing {partitionName}...");

                var args = $"e {partitionName}";
                var result = await RunMtkCommandAsync(args, TimeSpan.FromMinutes(2), ct);

                var success = result.Contains("Done") || result.Contains("ok");
                LogMessage?.Invoke(this, success ? $"Erased {partitionName}" : "Erase failed");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Erase error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> FormatDeviceAsync(string formatType = "data", CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, $"Formatting {formatType}...");

                string args = formatType.ToLower() switch
                {
                    "all" => "e metadata,userdata,cache",
                    "data" => "e userdata",
                    "cache" => "e cache",
                    "nvram" => "e nvram",
                    _ => $"e {formatType}"
                };

                var result = await RunMtkCommandAsync(args, TimeSpan.FromMinutes(5), ct);
                var success = result.Contains("Done") || result.Contains("ok");

                LogMessage?.Invoke(this, success ? "Format completed" : "Format failed");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Format error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RebootDeviceAsync(string mode = "", CancellationToken ct = default)
        {
            try
            {
                var args = mode.ToLower() switch
                {
                    "recovery" => "reset recovery",
                    "bootloader" => "reset bootloader",
                    "fastboot" => "reset fastboot",
                    "brom" => "reset brom",
                    _ => "reset"
                };

                await RunMtkCommandAsync(args, TimeSpan.FromSeconds(10), ct);
                LogMessage?.Invoke(this, $"Rebooting to {(string.IsNullOrEmpty(mode) ? "system" : mode)}...");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Reboot error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UnlockBootloaderAsync(CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Attempting bootloader unlock...");

                var result = await RunMtkCommandAsync("seccfg unlock", TimeSpan.FromSeconds(30), ct);
                var success = result.Contains("Done") || result.Contains("unlocked");

                LogMessage?.Invoke(this, success ? "Bootloader unlocked" : "Unlock failed (may need DA)");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Unlock error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> BypassAuthAsync(CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Attempting auth bypass (kamakiri/amonet)...");

                var result = await RunMtkCommandAsync("payload", TimeSpan.FromSeconds(60), ct);
                var success = result.Contains("Payload sent") || result.Contains("exploited");

                LogMessage?.Invoke(this, success ? "Auth bypassed!" : "Bypass failed (device may be patched)");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Bypass error: {ex.Message}");
                return false;
            }
        }

        public async Task<string> DumpPreloaderAsync(string outputPath, CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Dumping preloader...");

                var args = $"r preloader \"{outputPath}\"";
                var result = await RunMtkCommandAsync(args, TimeSpan.FromMinutes(2), ct);

                if (File.Exists(outputPath))
                {
                    LogMessage?.Invoke(this, $"Preloader saved: {outputPath}");
                    return outputPath;
                }

                return "";
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Dump error: {ex.Message}");
                return "";
            }
        }

        public async Task<string> DumpBootRomAsync(string outputPath, CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Dumping BootROM...");

                var args = $"dumpbrom --filename=\"{outputPath}\"";
                var result = await RunMtkCommandAsync(args, TimeSpan.FromMinutes(5), ct);

                if (File.Exists(outputPath))
                {
                    LogMessage?.Invoke(this, $"BootROM saved: {outputPath}");
                    return outputPath;
                }

                return "";
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Dump error: {ex.Message}");
                return "";
            }
        }

        private async Task<string> RunMtkCommandAsync(string arguments, TimeSpan timeout,
            CancellationToken ct = default, Action<int>? progressCallback = null)
        {
            var mtkScript = Path.Combine(_mtkClientPath, "mtkclient-main", "mtk.py");
            if (!File.Exists(mtkScript))
                mtkScript = Path.Combine(_mtkClientPath, "mtk.py");

            var pythonPath = await FindPythonAsync();
            if (string.IsNullOrEmpty(pythonPath))
            {
                throw new Exception("Python not found. Please install Python 3.8+");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{mtkScript}\" {arguments}",
                WorkingDirectory = Path.GetDirectoryName(mtkScript),
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

            var commonPaths = new[]
            {
                @"C:\Python312\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Python310\python.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\Python\Python312\python.exe")
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return "";
        }

        public string GetChipsetInfo(string chipset)
        {
            if (ChipsetInfo.TryGetValue(chipset.ToUpper(), out var info))
                return info;
            return "Unknown chipset";
        }

        public List<string> GetSupportedChipsets() => ChipsetInfo.Keys.ToList();
    }
}
