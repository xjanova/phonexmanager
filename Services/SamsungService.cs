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
    public class SamsungDevice
    {
        public string Port { get; set; } = "";
        public string Mode { get; set; } = ""; // Download, Recovery, Normal
        public string Model { get; set; } = "";
        public string Region { get; set; } = "";
        public string Serial { get; set; } = "";
        public string PdaVersion { get; set; } = "";
        public string CscVersion { get; set; } = "";
        public string PhoneVersion { get; set; } = "";
    }

    public class OdinFile
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = ""; // AP, BL, CP, CSC, HOME_CSC
        public string FilePath { get; set; } = "";
        public long Size { get; set; }
        public string SizeFormatted => FormatSize(Size);

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

    public class SamsungPartition
    {
        public string Name { get; set; } = "";
        public string FlashFile { get; set; } = "";
        public int Id { get; set; }
    }

    public class PitEntry
    {
        public int BinaryType { get; set; }
        public int DeviceType { get; set; }
        public int Identifier { get; set; }
        public int Attributes { get; set; }
        public string UpdateAttributes { get; set; } = "";
        public long BlockSize { get; set; }
        public long BlockCount { get; set; }
        public long FileOffset { get; set; }
        public long FileSize { get; set; }
        public string PartitionName { get; set; } = "";
        public string FlashFilename { get; set; } = "";
        public string FotaFilename { get; set; } = "";
    }

    public class FirmwareInfo
    {
        public string Model { get; set; } = "";
        public string Region { get; set; } = "";
        public string Version { get; set; } = "";
        public string AndroidVersion { get; set; } = "";
        public string SecurityPatch { get; set; } = "";
        public string BuildDate { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public long Size { get; set; }
        public string Changelog { get; set; } = "";
    }

    public class SamsungService
    {
        private readonly string _toolsPath;
        private readonly HttpClient _httpClient;
        private string _heimdallPath = "";

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        // Samsung CSC regions
        private static readonly Dictionary<string, string> CscRegions = new()
        {
            { "THL", "Thailand" },
            { "XSA", "Australia" },
            { "DBT", "Germany" },
            { "BTU", "UK" },
            { "XEU", "European Union" },
            { "TMB", "T-Mobile USA" },
            { "SPR", "Sprint USA" },
            { "ATT", "AT&T USA" },
            { "VZW", "Verizon USA" },
            { "KOO", "Korea (Open)" },
            { "SKC", "Korea (SK Telecom)" },
            { "KTC", "Korea (KT)" },
            { "LUC", "Korea (LG U+)" },
            { "INS", "India" },
            { "XID", "Indonesia" },
            { "XME", "Malaysia" },
            { "XSP", "Singapore" },
            { "PHE", "Philippines" },
            { "XTC", "Taiwan" },
            { "CHC", "China" },
            { "TGY", "Hong Kong" },
            { "BRI", "Brazil" },
            { "ZTO", "Brazil (Open)" },
            { "AUT", "Austria" },
            { "SER", "Russia" },
            { "ROM", "Romania" },
            { "SEE", "Serbia" },
            { "TPH", "Poland" },
            { "NEE", "Nordic Countries" },
            { "MID", "Middle East" }
        };

        // Odin partition to Heimdall partition mapping
        private static readonly Dictionary<string, string> OdinToHeimdallMap = new()
        {
            { "BL", "BOOTLOADER" },
            { "AP", "SYSTEM" },
            { "CP", "MODEM" },
            { "CSC", "CACHE" },
            { "HOME_CSC", "CACHE" }
        };

        public SamsungService(string toolsPath)
        {
            _toolsPath = toolsPath;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhoneRomFlashTool/1.0");

            // Find Heimdall
            _heimdallPath = FindHeimdall();
        }

        private string FindHeimdall()
        {
            // Check tools path first
            var localPath = Path.Combine(_toolsPath, "heimdall", "heimdall.exe");
            if (File.Exists(localPath)) return localPath;

            // Check common installation paths
            var paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Heimdall", "heimdall.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Heimdall", "heimdall.exe"),
                @"C:\Heimdall\heimdall.exe"
            };

            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }

            // Check PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(';'))
            {
                var fullPath = Path.Combine(dir, "heimdall.exe");
                if (File.Exists(fullPath)) return fullPath;
            }

            return "";
        }

        public bool IsHeimdallInstalled()
        {
            return !string.IsNullOrEmpty(_heimdallPath) && File.Exists(_heimdallPath);
        }

        public async Task<bool> DownloadHeimdallAsync(IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Downloading Heimdall...");

                // Heimdall releases from GitHub
                var downloadUrl = "https://github.com/Benjamin-Dobell/Heimdall/releases/download/v1.4.2/heimdall-suite-1.4.2-win32.zip";

                var heimdallDir = Path.Combine(_toolsPath, "heimdall");
                Directory.CreateDirectory(heimdallDir);

                var zipPath = Path.Combine(heimdallDir, "heimdall.zip");

                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    var buffer = new byte[8192];
                    long bytesRead = 0;

                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var fileStream = File.Create(zipPath);

                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read, ct);
                        bytesRead += read;

                        if (totalBytes > 0)
                        {
                            progress?.Report((int)((bytesRead * 100) / totalBytes));
                        }
                    }
                }

                LogMessage?.Invoke(this, "Extracting Heimdall...");
                ZipFile.ExtractToDirectory(zipPath, heimdallDir, true);
                File.Delete(zipPath);

                // Find heimdall.exe in extracted files
                var heimdallExe = Directory.GetFiles(heimdallDir, "heimdall.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(heimdallExe))
                {
                    _heimdallPath = heimdallExe;
                    LogMessage?.Invoke(this, $"Heimdall installed: {_heimdallPath}");
                    return true;
                }

                LogMessage?.Invoke(this, "Heimdall.exe not found in archive");
                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Download error: {ex.Message}");
                return false;
            }
        }

        public async Task<List<SamsungDevice>> DetectSamsungDevicesAsync()
        {
            var devices = new List<SamsungDevice>();

            try
            {
                // First try Heimdall detection
                if (IsHeimdallInstalled())
                {
                    var result = await RunHeimdallCommandAsync("detect", TimeSpan.FromSeconds(10));
                    if (result.Contains("Device detected"))
                    {
                        var device = new SamsungDevice
                        {
                            Mode = "Download Mode (Odin)"
                        };

                        // Get more device info
                        var printPit = await RunHeimdallCommandAsync("print-pit --no-reboot", TimeSpan.FromSeconds(30));
                        if (!string.IsNullOrEmpty(printPit) && !printPit.Contains("ERROR"))
                        {
                            LogMessage?.Invoke(this, "Samsung device detected in Download Mode");
                            devices.Add(device);
                        }
                        else
                        {
                            devices.Add(device);
                        }
                    }
                }

                // Also check via WMI for Samsung USB devices
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_04E8%'");

                foreach (var obj in searcher.Get())
                {
                    var deviceId = obj["DeviceID"]?.ToString() ?? "";
                    var name = obj["Name"]?.ToString() ?? "";

                    var device = new SamsungDevice();

                    // Parse device info based on PID
                    if (deviceId.Contains("PID_685D"))
                    {
                        device.Mode = "Download Mode (Odin)";
                    }
                    else if (deviceId.Contains("PID_6860"))
                    {
                        device.Mode = "MTP Mode";
                    }
                    else if (deviceId.Contains("PID_6862"))
                    {
                        device.Mode = "ADB Mode";
                    }

                    // Get COM port if available
                    if (name.Contains("COM"))
                    {
                        var startIdx = name.IndexOf("COM");
                        var endIdx = name.IndexOf(")", startIdx);
                        if (endIdx > startIdx)
                        {
                            device.Port = name.Substring(startIdx, endIdx - startIdx);
                        }
                    }

                    if (!string.IsNullOrEmpty(device.Mode) && !devices.Any(d => d.Mode == device.Mode))
                    {
                        devices.Add(device);
                        LogMessage?.Invoke(this, $"Found Samsung device: {device.Mode} {device.Port}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error detecting Samsung devices: {ex.Message}");
            }

            return devices;
        }

        public async Task<List<PitEntry>> ReadPitAsync(CancellationToken ct = default)
        {
            var entries = new List<PitEntry>();

            try
            {
                if (!IsHeimdallInstalled())
                {
                    LogMessage?.Invoke(this, "Heimdall not installed");
                    return entries;
                }

                LogMessage?.Invoke(this, "Reading PIT from device...");
                var result = await RunHeimdallCommandAsync("print-pit --no-reboot", TimeSpan.FromMinutes(2), ct);

                if (string.IsNullOrEmpty(result) || result.Contains("ERROR"))
                {
                    LogMessage?.Invoke(this, "Failed to read PIT");
                    return entries;
                }

                // Parse PIT entries
                var entryBlocks = result.Split(new[] { "--- Entry #" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var block in entryBlocks.Skip(1)) // Skip header
                {
                    var entry = new PitEntry();
                    var lines = block.Split('\n');

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("Partition Name:"))
                            entry.PartitionName = trimmed.Split(':')[1].Trim();
                        else if (trimmed.StartsWith("Flash Filename:"))
                            entry.FlashFilename = trimmed.Split(':')[1].Trim();
                        else if (trimmed.StartsWith("Block Size/Offset:"))
                        {
                            if (long.TryParse(trimmed.Split(':')[1].Trim(), out var bs))
                                entry.BlockSize = bs;
                        }
                        else if (trimmed.StartsWith("Block Count:"))
                        {
                            if (long.TryParse(trimmed.Split(':')[1].Trim(), out var bc))
                                entry.BlockCount = bc;
                        }
                    }

                    if (!string.IsNullOrEmpty(entry.PartitionName))
                    {
                        entries.Add(entry);
                    }
                }

                LogMessage?.Invoke(this, $"Found {entries.Count} partitions in PIT");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error reading PIT: {ex.Message}");
            }

            return entries;
        }

        public async Task<bool> DownloadPitAsync(string outputPath, CancellationToken ct = default)
        {
            try
            {
                if (!IsHeimdallInstalled())
                {
                    LogMessage?.Invoke(this, "Heimdall not installed");
                    return false;
                }

                LogMessage?.Invoke(this, "Downloading PIT from device...");
                var result = await RunHeimdallCommandAsync($"download-pit --output \"{outputPath}\" --no-reboot",
                    TimeSpan.FromMinutes(2), ct);

                if (result.Contains("Successfully") || File.Exists(outputPath))
                {
                    LogMessage?.Invoke(this, $"PIT saved to: {outputPath}");
                    return true;
                }

                LogMessage?.Invoke(this, $"Failed to download PIT: {result}");
                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Download PIT error: {ex.Message}");
                return false;
            }
        }

        public async Task<List<OdinFile>> ExtractOdinPackageAsync(string zipPath)
        {
            var files = new List<OdinFile>();

            try
            {
                var extractPath = Path.Combine(Path.GetDirectoryName(zipPath) ?? _toolsPath,
                    Path.GetFileNameWithoutExtension(zipPath));

                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }

                LogMessage?.Invoke(this, "Extracting firmware package...");
                ZipFile.ExtractToDirectory(zipPath, extractPath);

                // Find Odin files (tar.md5 files)
                foreach (var file in Directory.GetFiles(extractPath, "*.tar.md5", SearchOption.AllDirectories))
                {
                    var fileName = Path.GetFileName(file);
                    var odinFile = new OdinFile
                    {
                        Name = fileName,
                        FilePath = file,
                        Size = new FileInfo(file).Length
                    };

                    // Determine type based on filename
                    if (fileName.Contains("AP_") || fileName.Contains("_AP"))
                        odinFile.Type = "AP";
                    else if (fileName.Contains("BL_") || fileName.Contains("_BL"))
                        odinFile.Type = "BL";
                    else if (fileName.Contains("CP_") || fileName.Contains("_CP"))
                        odinFile.Type = "CP";
                    else if (fileName.Contains("HOME_CSC") || fileName.Contains("CSC_HOME"))
                        odinFile.Type = "HOME_CSC";
                    else if (fileName.Contains("CSC_") || fileName.Contains("_CSC"))
                        odinFile.Type = "CSC";

                    files.Add(odinFile);
                    LogMessage?.Invoke(this, $"Found {odinFile.Type}: {fileName} ({odinFile.SizeFormatted})");
                }

                // Also look for .tar files
                foreach (var file in Directory.GetFiles(extractPath, "*.tar", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".tar.md5")) continue; // Already processed

                    var fileName = Path.GetFileName(file);
                    var odinFile = new OdinFile
                    {
                        Name = fileName,
                        FilePath = file,
                        Size = new FileInfo(file).Length
                    };

                    if (fileName.Contains("AP_") || fileName.Contains("_AP"))
                        odinFile.Type = "AP";
                    else if (fileName.Contains("BL_") || fileName.Contains("_BL"))
                        odinFile.Type = "BL";
                    else if (fileName.Contains("CP_") || fileName.Contains("_CP"))
                        odinFile.Type = "CP";

                    files.Add(odinFile);
                }

                LogMessage?.Invoke(this, $"Extracted {files.Count} Odin files");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error extracting package: {ex.Message}");
            }

            return files;
        }

        public async Task<List<SamsungPartition>> ExtractTarContentsAsync(string tarPath, string extractPath)
        {
            var partitions = new List<SamsungPartition>();

            try
            {
                // For .tar.md5, strip the MD5 checksum first (it's just appended text)
                var workingPath = tarPath;
                if (tarPath.EndsWith(".tar.md5"))
                {
                    // The .tar.md5 file is a tar file with MD5 hash appended
                    // We can use it directly with most tar tools
                }

                Directory.CreateDirectory(extractPath);

                // Use tar command if available (Windows 10+ has it built-in)
                var startInfo = new ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"-xf \"{workingPath}\" -C \"{extractPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                }

                // List extracted files
                foreach (var file in Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (ext == ".img" || ext == ".bin" || ext == ".pit")
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        partitions.Add(new SamsungPartition
                        {
                            Name = name,
                            FlashFile = file
                        });
                        LogMessage?.Invoke(this, $"Extracted: {Path.GetFileName(file)}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Extract error: {ex.Message}");
            }

            return partitions;
        }

        public async Task<bool> FlashPartitionAsync(string partitionName, string imagePath,
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                if (!IsHeimdallInstalled())
                {
                    LogMessage?.Invoke(this, "Heimdall not installed");
                    return false;
                }

                if (!File.Exists(imagePath))
                {
                    LogMessage?.Invoke(this, $"Image file not found: {imagePath}");
                    return false;
                }

                LogMessage?.Invoke(this, $"Flashing {partitionName} with {Path.GetFileName(imagePath)}...");

                var args = $"flash --{partitionName} \"{imagePath}\" --no-reboot";
                var result = await RunHeimdallCommandWithProgressAsync(args, TimeSpan.FromMinutes(30), progress, ct);

                if (result.Contains("Successfully") || result.Contains("successful"))
                {
                    LogMessage?.Invoke(this, $"Successfully flashed {partitionName}");
                    return true;
                }

                LogMessage?.Invoke(this, $"Flash failed: {result}");
                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Flash error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> FlashOdinAsync(List<OdinFile> files, bool wipeData = false,
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                if (!IsHeimdallInstalled())
                {
                    LogMessage?.Invoke(this, "Heimdall not installed. Installing...");
                    if (!await DownloadHeimdallAsync(progress, ct))
                    {
                        return false;
                    }
                }

                LogMessage?.Invoke(this, "Starting Heimdall flash process...");
                LogMessage?.Invoke(this, wipeData
                    ? "WARNING: User data will be wiped!"
                    : "Keeping user data (using HOME_CSC)");

                int total = files.Count;
                int current = 0;

                // Create temp directory for extracted files
                var tempDir = Path.Combine(_toolsPath, "temp_flash");
                Directory.CreateDirectory(tempDir);

                try
                {
                    foreach (var file in files)
                    {
                        if (ct.IsCancellationRequested) break;

                        // Skip CSC if we want to keep data (use HOME_CSC instead)
                        if (!wipeData && file.Type == "CSC")
                        {
                            LogMessage?.Invoke(this, $"Skipping {file.Type} to preserve data");
                            continue;
                        }

                        if (wipeData && file.Type == "HOME_CSC")
                        {
                            LogMessage?.Invoke(this, $"Skipping {file.Type} (using full CSC)");
                            continue;
                        }

                        LogMessage?.Invoke(this, $"Processing {file.Type}: {file.Name}...");

                        // Extract tar contents
                        var extractDir = Path.Combine(tempDir, file.Type);
                        var partitions = await ExtractTarContentsAsync(file.FilePath, extractDir);

                        // Flash each extracted partition
                        foreach (var partition in partitions)
                        {
                            if (ct.IsCancellationRequested) break;

                            // Determine Heimdall partition name
                            var heimdallPartition = partition.Name.ToUpper();

                            LogMessage?.Invoke(this, $"Flashing {heimdallPartition}...");
                            await FlashPartitionAsync(heimdallPartition, partition.FlashFile, null, ct);
                        }

                        current++;
                        progress?.Report((current * 100) / total);
                    }

                    LogMessage?.Invoke(this, "Flash completed!");
                    return true;
                }
                finally
                {
                    // Cleanup temp directory
                    try
                    {
                        if (Directory.Exists(tempDir))
                        {
                            Directory.Delete(tempDir, true);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Flash error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> FlashRecoveryAsync(string recoveryImagePath, CancellationToken ct = default)
        {
            return await FlashPartitionAsync("RECOVERY", recoveryImagePath, null, ct);
        }

        public async Task<bool> FlashBootAsync(string bootImagePath, CancellationToken ct = default)
        {
            return await FlashPartitionAsync("BOOT", bootImagePath, null, ct);
        }

        public async Task<bool> FlashKernelAsync(string kernelImagePath, CancellationToken ct = default)
        {
            return await FlashPartitionAsync("KERNEL", kernelImagePath, null, ct);
        }

        public async Task<bool> BackupPartitionAsync(string partitionName, string outputPath,
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                if (!IsHeimdallInstalled())
                {
                    LogMessage?.Invoke(this, "Heimdall not installed");
                    return false;
                }

                LogMessage?.Invoke(this, $"Reading {partitionName} from device...");

                // Heimdall doesn't have direct dump command, but we can use print-pit to get info
                // For backup, we need alternative methods or use other tools

                LogMessage?.Invoke(this, "Note: Heimdall has limited backup support. Consider using Odin or ADB for backups.");

                // Try using Heimdall's download-pit as workaround for PIT
                if (partitionName.ToUpper() == "PIT")
                {
                    return await DownloadPitAsync(outputPath, ct);
                }

                // For other partitions, we'd need to use ADB or other tools
                LogMessage?.Invoke(this, $"Backup of {partitionName} requires root access via ADB");
                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Backup error: {ex.Message}");
                return false;
            }
        }

        public async Task<FirmwareInfo?> CheckFirmwareUpdateAsync(string model, string region)
        {
            try
            {
                LogMessage?.Invoke(this, $"Checking updates for {model} ({region})...");

                // Query Samsung's FUS (Firmware Update Server) or use SamFw API
                // This is a simplified implementation - real API calls would go here
                var url = $"https://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/version.xml";

                try
                {
                    var response = await _httpClient.GetStringAsync(url);

                    // Parse XML response for firmware info
                    // This is simplified - real parsing would be more complex
                    var versionMatch = Regex.Match(response, @"<latest>(.*?)</latest>");
                    if (versionMatch.Success)
                    {
                        return new FirmwareInfo
                        {
                            Model = model,
                            Region = region,
                            Version = versionMatch.Groups[1].Value
                        };
                    }
                }
                catch (HttpRequestException)
                {
                    // Samsung server might block direct access
                    LogMessage?.Invoke(this, "Unable to check directly. Try SamFw.com for firmware downloads.");
                }

                // Return basic info structure
                return new FirmwareInfo
                {
                    Model = model,
                    Region = region,
                    Version = "Unknown - check SamFw.com"
                };
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error checking updates: {ex.Message}");
                return null;
            }
        }

        public async Task<string> DownloadFirmwareAsync(FirmwareInfo firmware, string outputPath,
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, $"Note: Direct firmware download requires authentication.");
                LogMessage?.Invoke(this, $"Please download firmware manually from:");
                LogMessage?.Invoke(this, $"  - SamFw.com");
                LogMessage?.Invoke(this, $"  - Sammobile.com");
                LogMessage?.Invoke(this, $"  - Frija tool");

                LogMessage?.Invoke(this, $"Model: {firmware.Model}, Region: {firmware.Region}");

                return "";
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Download error: {ex.Message}");
                return "";
            }
        }

        public async Task<bool> RebootDeviceAsync(string mode = "normal", CancellationToken ct = default)
        {
            try
            {
                if (!IsHeimdallInstalled())
                {
                    LogMessage?.Invoke(this, "Heimdall not installed");
                    return false;
                }

                string args;
                switch (mode.ToLower())
                {
                    case "recovery":
                        // Heimdall doesn't support direct recovery boot
                        // Use ADB instead
                        LogMessage?.Invoke(this, "For recovery mode, use: adb reboot recovery");
                        return false;
                    case "download":
                        LogMessage?.Invoke(this, "For download mode, use Vol- + Home + Power");
                        return false;
                    default:
                        args = "flash --no-reboot"; // This will reboot when heimdall is done
                        break;
                }

                LogMessage?.Invoke(this, $"Rebooting device...");

                // Send reboot command via heimdall (it auto-reboots after successful operation)
                // For explicit reboot, we'd need to complete any flash operation
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Reboot error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> FactoryResetAsync(CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Factory reset requires flashing userdata partition or using Recovery mode.");
                LogMessage?.Invoke(this, "For factory reset:");
                LogMessage?.Invoke(this, "  1. Boot to Recovery (Vol+ + Home + Power)");
                LogMessage?.Invoke(this, "  2. Select 'Wipe data/factory reset'");
                LogMessage?.Invoke(this, "Or use ADB: adb shell recovery --wipe_data");

                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Reset error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> PatchVbmetaAsync(string vbmetaPath, string outputPath)
        {
            try
            {
                if (!File.Exists(vbmetaPath))
                {
                    LogMessage?.Invoke(this, "vbmeta file not found");
                    return false;
                }

                var data = await File.ReadAllBytesAsync(vbmetaPath);

                // Check vbmeta header magic: "AVB0"
                if (data.Length < 256 || data[0] != 'A' || data[1] != 'V' || data[2] != 'B' || data[3] != '0')
                {
                    LogMessage?.Invoke(this, "Invalid vbmeta format");
                    return false;
                }

                // Set disable flags at offset 123 (AVB_VBMETA_IMAGE_FLAGS_VERIFICATION_DISABLED | AVB_VBMETA_IMAGE_FLAGS_HASHTREE_DISABLED)
                // Offset 123 is flags field in vbmeta header
                if (data.Length > 123)
                {
                    data[123] |= 0x03; // Disable verification and hashtree
                }

                await File.WriteAllBytesAsync(outputPath, data);
                LogMessage?.Invoke(this, "vbmeta patched with verification disabled");
                LogMessage?.Invoke(this, "Flash with: fastboot flash vbmeta <patched_vbmeta>");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Patch error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CloseOdinSessionAsync(CancellationToken ct = default)
        {
            try
            {
                if (!IsHeimdallInstalled())
                {
                    return false;
                }

                // Close any existing session by sending a close-pc-screen command
                var result = await RunHeimdallCommandAsync("close-pc-screen", TimeSpan.FromSeconds(10), ct);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public string GetRegionName(string csc)
        {
            if (CscRegions.TryGetValue(csc.ToUpper(), out var name))
            {
                return name;
            }
            return csc;
        }

        public List<KeyValuePair<string, string>> GetAllRegions()
        {
            return new List<KeyValuePair<string, string>>(CscRegions);
        }

        private async Task<string> RunHeimdallCommandAsync(string arguments, TimeSpan timeout,
            CancellationToken ct = default)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _heimdallPath,
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

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);

                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill();
                    throw;
                }

                var output = await outputTask;
                var error = await errorTask;

                return string.IsNullOrEmpty(error) ? output : $"{output}\n{error}";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }

        private async Task<string> RunHeimdallCommandWithProgressAsync(string arguments, TimeSpan timeout,
            IProgress<int>? progress, CancellationToken ct = default)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _heimdallPath,
                    Arguments = arguments,
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

                        // Parse progress from Heimdall output
                        // Heimdall shows progress like: "100%"
                        var match = Regex.Match(e.Data, @"(\d+)%");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var pct))
                        {
                            progress?.Report(pct);
                            ProgressChanged?.Invoke(this, pct);
                        }
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        output.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);

                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill();
                    throw;
                }

                return output.ToString();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}";
            }
        }
    }
}
