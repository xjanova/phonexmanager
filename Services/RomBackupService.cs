using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PhoneRomFlashTool.Models;

namespace PhoneRomFlashTool.Services
{
    public class RomBackupService
    {
        private readonly AdbService _adbService;
        private readonly string _backupFolder;

        public event EventHandler<ProgressEventArgs>? ProgressChanged;
        public event EventHandler<string>? LogMessage;

        public RomBackupService(AdbService adbService)
        {
            _adbService = adbService;
            _backupFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PhoneRomFlashTool",
                "Backups"
            );
            Directory.CreateDirectory(_backupFolder);
        }

        public async Task<RomInfo?> BackupPartitionAsync(
            string deviceId,
            string partitionName,
            IProgress<ProgressEventArgs>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var device = await _adbService.GetDeviceInfoAsync(deviceId);
                if (device == null)
                {
                    Log("Device not found");
                    return null;
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupDir = Path.Combine(_backupFolder, $"{device.Brand}_{device.Model}", timestamp);
                Directory.CreateDirectory(backupDir);

                Log($"Starting backup of {partitionName} partition...");
                progress?.Report(new ProgressEventArgs(0, $"Preparing backup of {partitionName}..."));

                // ค้นหา partition path
                var partitionPath = await FindPartitionPathAsync(deviceId, partitionName);
                if (string.IsNullOrEmpty(partitionPath))
                {
                    Log($"Partition {partitionName} not found");
                    return null;
                }

                var outputFile = Path.Combine(backupDir, $"{partitionName}.img");

                // ใช้ dd เพื่อ dump partition
                var command = $"shell \"su -c 'dd if={partitionPath} of=/sdcard/{partitionName}.img bs=4096'\"";
                Log($"Executing: {command}");
                await _adbService.ExecuteAdbCommandAsync(deviceId, command);

                progress?.Report(new ProgressEventArgs(50, $"Pulling {partitionName} from device..."));

                // Pull ไฟล์ออกจากอุปกรณ์
                await _adbService.ExecuteAdbCommandAsync(deviceId, $"pull /sdcard/{partitionName}.img \"{outputFile}\"");

                // ลบไฟล์ temp บนอุปกรณ์
                await _adbService.ExecuteAdbCommandAsync(deviceId, $"shell rm /sdcard/{partitionName}.img");

                if (!File.Exists(outputFile))
                {
                    Log("Backup file was not created");
                    return null;
                }

                progress?.Report(new ProgressEventArgs(90, "Calculating checksum..."));

                var romInfo = new RomInfo
                {
                    FilePath = outputFile,
                    FileName = Path.GetFileName(outputFile),
                    FileSize = new FileInfo(outputFile).Length,
                    Checksum = await CalculateChecksumAsync(outputFile),
                    Type = GetRomType(partitionName),
                    BackupDate = DateTime.Now,
                    DeviceModel = $"{device.Brand} {device.Model}",
                    AndroidVersion = device.AndroidVersion,
                    Partitions = new List<PartitionInfo>
                    {
                        new PartitionInfo
                        {
                            Name = partitionName,
                            FilePath = outputFile,
                            Size = new FileInfo(outputFile).Length
                        }
                    }
                };

                // บันทึก metadata
                var metadataPath = Path.Combine(backupDir, "backup_info.json");
                await File.WriteAllTextAsync(metadataPath, JsonConvert.SerializeObject(romInfo, Formatting.Indented));

                progress?.Report(new ProgressEventArgs(100, "Backup completed!"));
                Log($"Backup completed: {outputFile}");

                return romInfo;
            }
            catch (Exception ex)
            {
                Log($"Backup error: {ex.Message}");
                return null;
            }
        }

        public async Task<RomInfo?> BackupFullRomAsync(
            string deviceId,
            IProgress<ProgressEventArgs>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var partitions = new[] { "boot", "recovery", "system", "vendor", "userdata" };
            var device = await _adbService.GetDeviceInfoAsync(deviceId);
            if (device == null) return null;

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupDir = Path.Combine(_backupFolder, $"{device.Brand}_{device.Model}", timestamp);
            Directory.CreateDirectory(backupDir);

            var romInfo = new RomInfo
            {
                BackupDate = DateTime.Now,
                DeviceModel = $"{device.Brand} {device.Model}",
                AndroidVersion = device.AndroidVersion,
                Type = RomType.FullRom
            };

            int completed = 0;
            foreach (var partition in partitions)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var partitionProgress = (double)completed / partitions.Length * 100;
                progress?.Report(new ProgressEventArgs((int)partitionProgress, $"Backing up {partition}..."));

                var partInfo = await BackupSinglePartitionAsync(deviceId, partition, backupDir);
                if (partInfo != null)
                {
                    romInfo.Partitions.Add(partInfo);
                }
                completed++;
            }

            romInfo.FilePath = backupDir;
            romInfo.FileName = Path.GetFileName(backupDir);
            romInfo.FileSize = GetDirectorySize(backupDir);
            romInfo.Checksum = await CalculateDirectoryChecksumAsync(backupDir);

            var metadataPath = Path.Combine(backupDir, "backup_info.json");
            await File.WriteAllTextAsync(metadataPath, JsonConvert.SerializeObject(romInfo, Formatting.Indented));

            progress?.Report(new ProgressEventArgs(100, "Full backup completed!"));
            return romInfo;
        }

        private async Task<PartitionInfo?> BackupSinglePartitionAsync(string deviceId, string partitionName, string backupDir)
        {
            try
            {
                var partitionPath = await FindPartitionPathAsync(deviceId, partitionName);
                if (string.IsNullOrEmpty(partitionPath)) return null;

                var outputFile = Path.Combine(backupDir, $"{partitionName}.img");

                // Dump partition
                await _adbService.ExecuteAdbCommandAsync(deviceId,
                    $"shell \"su -c 'dd if={partitionPath} of=/sdcard/{partitionName}.img bs=4096'\"");

                // Pull file
                await _adbService.ExecuteAdbCommandAsync(deviceId,
                    $"pull /sdcard/{partitionName}.img \"{outputFile}\"");

                // Clean up
                await _adbService.ExecuteAdbCommandAsync(deviceId,
                    $"shell rm /sdcard/{partitionName}.img");

                if (File.Exists(outputFile))
                {
                    return new PartitionInfo
                    {
                        Name = partitionName,
                        FilePath = outputFile,
                        Size = new FileInfo(outputFile).Length
                    };
                }
            }
            catch (Exception ex)
            {
                Log($"Error backing up {partitionName}: {ex.Message}");
            }

            return null;
        }

        private async Task<string> FindPartitionPathAsync(string deviceId, string partitionName)
        {
            var possiblePaths = new[]
            {
                $"/dev/block/bootdevice/by-name/{partitionName}",
                $"/dev/block/platform/*/by-name/{partitionName}",
                $"/dev/block/by-name/{partitionName}",
                $"/dev/block/{partitionName}"
            };

            foreach (var path in possiblePaths)
            {
                var result = await _adbService.ExecuteAdbCommandAsync(deviceId,
                    $"shell \"su -c 'ls {path} 2>/dev/null'\"");

                if (!string.IsNullOrWhiteSpace(result) && !result.Contains("No such file"))
                {
                    return result.Trim();
                }
            }

            // ลองค้นหาแบบ wildcard
            var searchResult = await _adbService.ExecuteAdbCommandAsync(deviceId,
                $"shell \"su -c 'find /dev/block -name {partitionName} 2>/dev/null | head -1'\"");

            return searchResult.Trim();
        }

        public async Task<List<PartitionInfo>> GetDevicePartitionsAsync(string deviceId)
        {
            var partitions = new List<PartitionInfo>();

            try
            {
                // ลอง by-name path ก่อน
                var result = await _adbService.ExecuteAdbCommandAsync(deviceId,
                    "shell \"su -c 'ls -la /dev/block/bootdevice/by-name/ 2>/dev/null || ls -la /dev/block/by-name/ 2>/dev/null'\"");

                var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 9)
                    {
                        var name = parts[^1];
                        if (name.Contains("->"))
                        {
                            var nameIndex = Array.IndexOf(parts, "->");
                            if (nameIndex > 0)
                            {
                                name = parts[nameIndex - 1];
                            }
                        }

                        partitions.Add(new PartitionInfo
                        {
                            Name = name,
                            Type = DeterminePartitionType(name)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error getting partitions: {ex.Message}");
            }

            return partitions;
        }

        private static string DeterminePartitionType(string name)
        {
            return name.ToLower() switch
            {
                "boot" => "Kernel/Ramdisk",
                "recovery" => "Recovery Image",
                "system" => "System Partition",
                "vendor" => "Vendor Partition",
                "userdata" or "data" => "User Data",
                "cache" => "Cache",
                "persist" => "Persistent Data",
                "modem" or "radio" => "Modem/Radio",
                _ => "Unknown"
            };
        }

        private static RomType GetRomType(string partitionName)
        {
            return partitionName.ToLower() switch
            {
                "boot" => RomType.Boot,
                "recovery" => RomType.Recovery,
                "system" => RomType.System,
                "vendor" => RomType.Vendor,
                "userdata" or "data" => RomType.Data,
                _ => RomType.Custom
            };
        }

        private static async Task<string> CalculateChecksumAsync(string filePath)
        {
            using var sha256 = SHA256.Create();
            await using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private async Task<string> CalculateDirectoryChecksumAsync(string directory)
        {
            var sb = new StringBuilder();
            foreach (var file in Directory.GetFiles(directory, "*.img"))
            {
                var checksum = await CalculateChecksumAsync(file);
                sb.AppendLine($"{Path.GetFileName(file)}:{checksum}");
            }
            return sb.ToString();
        }

        private static long GetDirectorySize(string directory)
        {
            long size = 0;
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                size += new FileInfo(file).Length;
            }
            return size;
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(this, $"[Backup] {message}");
        }
    }

    public class ProgressEventArgs : EventArgs
    {
        public int Percentage { get; }
        public string Message { get; }

        public ProgressEventArgs(int percentage, string message)
        {
            Percentage = percentage;
            Message = message;
        }
    }
}
