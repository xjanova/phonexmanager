using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class DeviceFile
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public string Permissions { get; set; } = "";
        public string Owner { get; set; } = "";
        public string Group { get; set; } = "";
        public DateTime ModifiedDate { get; set; }
        public string SizeFormatted => IsDirectory ? "<DIR>" : FormatSize(Size);

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

    public class StorageInfo
    {
        public string MountPoint { get; set; } = "";
        public string FileSystem { get; set; } = "";
        public long TotalSize { get; set; }
        public long UsedSize { get; set; }
        public long FreeSize { get; set; }
        public int UsagePercent => TotalSize > 0 ? (int)((UsedSize * 100) / TotalSize) : 0;
    }

    public class FileManagerService
    {
        private readonly string _adbPath;
        private string _currentPath = "/sdcard";
        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        public string CurrentPath => _currentPath;

        public FileManagerService(string adbPath)
        {
            _adbPath = adbPath;
        }

        public async Task<List<DeviceFile>> ListDirectoryAsync(string path = "")
        {
            var files = new List<DeviceFile>();

            if (string.IsNullOrEmpty(path))
            {
                path = _currentPath;
            }
            else
            {
                _currentPath = path;
            }

            try
            {
                // Use ls -la for detailed listing
                var result = await RunAdbCommandAsync($"shell ls -la \"{path}\"");
                var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if (line.StartsWith("total") || string.IsNullOrWhiteSpace(line))
                        continue;

                    var file = ParseLsLine(line, path);
                    if (file != null)
                    {
                        files.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error listing directory: {ex.Message}");
            }

            return files;
        }

        private DeviceFile? ParseLsLine(string line, string basePath)
        {
            try
            {
                // Format: drwxrwxrwx owner group size date time name
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 7) return null;

                var permissions = parts[0];
                var isDirectory = permissions.StartsWith("d");
                var owner = parts.Length > 2 ? parts[2] : "";
                var group = parts.Length > 3 ? parts[3] : "";
                var size = parts.Length > 4 && long.TryParse(parts[4], out var s) ? s : 0;
                var name = string.Join(" ", parts[7..]); // Name might have spaces

                if (name == "." || name == ".." || string.IsNullOrEmpty(name))
                    return null;

                return new DeviceFile
                {
                    Name = name,
                    Path = Path.Combine(basePath, name).Replace("\\", "/"),
                    IsDirectory = isDirectory,
                    Size = isDirectory ? 0 : size,
                    Permissions = permissions,
                    Owner = owner,
                    Group = group
                };
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> PushFileAsync(string localPath, string remotePath,
            IProgress<int>? progress = null)
        {
            try
            {
                if (!File.Exists(localPath))
                {
                    LogMessage?.Invoke(this, $"Local file not found: {localPath}");
                    return false;
                }

                LogMessage?.Invoke(this, $"Pushing {Path.GetFileName(localPath)}...");

                var result = await RunAdbCommandAsync($"push \"{localPath}\" \"{remotePath}\"");

                if (result.Contains("error") || result.Contains("failed"))
                {
                    LogMessage?.Invoke(this, $"Push failed: {result}");
                    return false;
                }

                LogMessage?.Invoke(this, "Push completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Push error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> PullFileAsync(string remotePath, string localPath,
            IProgress<int>? progress = null)
        {
            try
            {
                LogMessage?.Invoke(this, $"Pulling {Path.GetFileName(remotePath)}...");

                var result = await RunAdbCommandAsync($"pull \"{remotePath}\" \"{localPath}\"");

                if (result.Contains("error") || result.Contains("failed") || result.Contains("does not exist"))
                {
                    LogMessage?.Invoke(this, $"Pull failed: {result}");
                    return false;
                }

                LogMessage?.Invoke(this, $"Pulled to {localPath}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Pull error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteFileAsync(string remotePath, bool recursive = false)
        {
            try
            {
                var cmd = recursive ? $"shell rm -rf \"{remotePath}\"" : $"shell rm \"{remotePath}\"";
                var result = await RunAdbCommandAsync(cmd);

                if (result.Contains("error") || result.Contains("failed"))
                {
                    LogMessage?.Invoke(this, $"Delete failed: {result}");
                    return false;
                }

                LogMessage?.Invoke(this, $"Deleted: {remotePath}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Delete error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CreateDirectoryAsync(string remotePath)
        {
            try
            {
                var result = await RunAdbCommandAsync($"shell mkdir -p \"{remotePath}\"");
                LogMessage?.Invoke(this, $"Created directory: {remotePath}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Create directory error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> MoveFileAsync(string sourcePath, string destPath)
        {
            try
            {
                var result = await RunAdbCommandAsync($"shell mv \"{sourcePath}\" \"{destPath}\"");
                LogMessage?.Invoke(this, $"Moved to: {destPath}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Move error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CopyFileAsync(string sourcePath, string destPath)
        {
            try
            {
                var result = await RunAdbCommandAsync($"shell cp \"{sourcePath}\" \"{destPath}\"");
                LogMessage?.Invoke(this, $"Copied to: {destPath}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Copy error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ChmodAsync(string remotePath, string permissions)
        {
            try
            {
                var result = await RunAdbCommandAsync($"shell chmod {permissions} \"{remotePath}\"");
                LogMessage?.Invoke(this, $"Changed permissions to {permissions}: {remotePath}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Chmod error: {ex.Message}");
                return false;
            }
        }

        public async Task<List<StorageInfo>> GetStorageInfoAsync()
        {
            var storages = new List<StorageInfo>();

            try
            {
                var result = await RunAdbCommandAsync("shell df -h");
                var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if (line.StartsWith("Filesystem")) continue;

                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 6) continue;

                    // Only include relevant mount points
                    var mountPoint = parts[5];
                    if (mountPoint.StartsWith("/data") || mountPoint.StartsWith("/storage") ||
                        mountPoint.StartsWith("/sdcard") || mountPoint.StartsWith("/system"))
                    {
                        storages.Add(new StorageInfo
                        {
                            MountPoint = mountPoint,
                            FileSystem = parts[0],
                            TotalSize = ParseSize(parts[1]),
                            UsedSize = ParseSize(parts[2]),
                            FreeSize = ParseSize(parts[3])
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error getting storage info: {ex.Message}");
            }

            return storages;
        }

        private long ParseSize(string sizeStr)
        {
            try
            {
                sizeStr = sizeStr.ToUpper().Trim();
                double multiplier = 1;

                if (sizeStr.EndsWith("K"))
                {
                    multiplier = 1024;
                    sizeStr = sizeStr.TrimEnd('K');
                }
                else if (sizeStr.EndsWith("M"))
                {
                    multiplier = 1024 * 1024;
                    sizeStr = sizeStr.TrimEnd('M');
                }
                else if (sizeStr.EndsWith("G"))
                {
                    multiplier = 1024 * 1024 * 1024;
                    sizeStr = sizeStr.TrimEnd('G');
                }

                if (double.TryParse(sizeStr, out var value))
                {
                    return (long)(value * multiplier);
                }
            }
            catch { }

            return 0;
        }

        public async Task<string> ReadFileContentAsync(string remotePath, int maxLines = 100)
        {
            try
            {
                var result = await RunAdbCommandAsync($"shell head -n {maxLines} \"{remotePath}\"");
                return result;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        public async Task<bool> WriteFileContentAsync(string remotePath, string content)
        {
            try
            {
                // Write through temp file
                var tempFile = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempFile, content);

                await PushFileAsync(tempFile, remotePath);

                File.Delete(tempFile);
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Write error: {ex.Message}");
                return false;
            }
        }

        public void NavigateUp()
        {
            if (_currentPath == "/" || _currentPath == "/sdcard")
                return;

            _currentPath = Path.GetDirectoryName(_currentPath)?.Replace("\\", "/") ?? "/";
        }

        public void NavigateTo(string path)
        {
            _currentPath = path;
        }

        public async Task<List<string>> SearchFilesAsync(string pattern, string startPath = "/sdcard")
        {
            var results = new List<string>();

            try
            {
                var result = await RunAdbCommandAsync($"shell find \"{startPath}\" -name \"{pattern}\" 2>/dev/null");
                var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        results.Add(line.Trim());
                    }
                }

                LogMessage?.Invoke(this, $"Found {results.Count} files matching '{pattern}'");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Search error: {ex.Message}");
            }

            return results;
        }

        public async Task<bool> InstallApkAsync(string apkPath, bool allowDowngrade = false)
        {
            try
            {
                var flags = allowDowngrade ? "-r -d" : "-r";
                var result = await RunAdbCommandAsync($"install {flags} \"{apkPath}\"");

                if (result.Contains("Success"))
                {
                    LogMessage?.Invoke(this, "APK installed successfully");
                    return true;
                }

                LogMessage?.Invoke(this, $"Install failed: {result}");
                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Install error: {ex.Message}");
                return false;
            }
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
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                return string.IsNullOrEmpty(error) ? output : output + "\n" + error;
            }
            catch
            {
                return "";
            }
        }
    }
}
