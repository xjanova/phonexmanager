using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class ToolSetupInfo
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Version { get; set; } = "";
        public string ExecutableName { get; set; } = "";
        public bool IsInstalled { get; set; }
        public string InstalledPath { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public ToolType Type { get; set; }
        public long FileSize { get; set; }
    }

    public enum ToolType
    {
        ADB,
        Fastboot,
        Heimdall,
        Odin,
        QPST,
        SPFlashTool,
        MiFlash,
        EdlClient,
        MTKClient,
        SamsungFRP
    }

    public class ToolsDownloadService
    {
        private readonly string _toolsPath;
        private readonly HttpClient _httpClient;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;
        public event EventHandler<string>? StatusChanged;

        // Official/reliable download sources
        private static readonly Dictionary<ToolType, ToolDownloadInfo> ToolSources = new()
        {
            {
                ToolType.ADB,
                new ToolDownloadInfo
                {
                    Name = "Android Platform Tools (ADB & Fastboot)",
                    Description = "Official Google ADB and Fastboot tools",
                    Url = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip",
                    ExecutableNames = new[] { "adb.exe", "fastboot.exe" },
                    ExtractFolder = "platform-tools"
                }
            },
            {
                ToolType.Heimdall,
                new ToolDownloadInfo
                {
                    Name = "Heimdall",
                    Description = "Open-source Samsung flashing tool",
                    Url = "https://github.com/Benjamin-Dobell/Heimdall/releases/download/v1.4.2/heimdall-frontend-1.4.2-win32.zip",
                    ExecutableNames = new[] { "heimdall.exe", "heimdall-frontend.exe" },
                    ExtractFolder = ""
                }
            },
            {
                ToolType.EdlClient,
                new ToolDownloadInfo
                {
                    Name = "EDL Client (bkerler)",
                    Description = "Qualcomm EDL/Firehose tool",
                    Url = "https://github.com/bkerler/edl/archive/refs/heads/master.zip",
                    ExecutableNames = new[] { "edl.py" },
                    ExtractFolder = "edl-master",
                    RequiresPython = true
                }
            },
            {
                ToolType.MTKClient,
                new ToolDownloadInfo
                {
                    Name = "MTK Client (bkerler)",
                    Description = "MediaTek BROM/Preloader tool",
                    Url = "https://github.com/bkerler/mtkclient/archive/refs/heads/main.zip",
                    ExecutableNames = new[] { "mtk.py" },
                    ExtractFolder = "mtkclient-main",
                    RequiresPython = true
                }
            }
        };

        public ToolsDownloadService()
        {
            _toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            Directory.CreateDirectory(_toolsPath);

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhoneRomFlashTool/1.0");
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
        }

        #region Tool Detection
        public async Task<List<ToolSetupInfo>> GetAllToolsStatusAsync(CancellationToken ct = default)
        {
            var tools = new List<ToolSetupInfo>();

            foreach (var (type, info) in ToolSources)
            {
                var tool = new ToolSetupInfo
                {
                    Name = info.Name,
                    Description = info.Description,
                    DownloadUrl = info.Url,
                    Type = type,
                    ExecutableName = info.ExecutableNames.FirstOrDefault() ?? ""
                };

                // Check if installed
                var installedPath = FindToolPath(info.ExecutableNames);
                tool.IsInstalled = !string.IsNullOrEmpty(installedPath);
                tool.InstalledPath = installedPath;

                if (tool.IsInstalled)
                {
                    tool.Version = await GetToolVersionAsync(type, installedPath, ct);
                }

                tools.Add(tool);
            }

            return tools;
        }

        private string FindToolPath(string[] executableNames)
        {
            foreach (var exeName in executableNames)
            {
                // Check in Tools folder
                var toolsPath = Path.Combine(_toolsPath, exeName);
                if (File.Exists(toolsPath))
                    return toolsPath;

                // Check in subfolders
                var subfolderPath = Directory.GetFiles(_toolsPath, exeName, SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (subfolderPath != null)
                    return subfolderPath;

                // Check in PATH
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var path in pathEnv.Split(Path.PathSeparator))
                {
                    var fullPath = Path.Combine(path, exeName);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
            }

            return "";
        }

        private async Task<string> GetToolVersionAsync(ToolType type, string toolPath, CancellationToken ct)
        {
            try
            {
                string arguments = type switch
                {
                    ToolType.ADB => "version",
                    ToolType.Fastboot => "--version",
                    ToolType.Heimdall => "version",
                    _ => "--version"
                };

                var psi = new ProcessStartInfo
                {
                    FileName = toolPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return "Unknown";

                var output = await process.StandardOutput.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                // Extract version number
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("version") || line.Contains("Version"))
                    {
                        return line.Trim();
                    }
                }

                return lines.FirstOrDefault()?.Trim() ?? "Installed";
            }
            catch
            {
                return "Installed";
            }
        }

        public bool IsAdbInstalled()
        {
            return !string.IsNullOrEmpty(FindToolPath(new[] { "adb.exe" }));
        }

        public bool IsFastbootInstalled()
        {
            return !string.IsNullOrEmpty(FindToolPath(new[] { "fastboot.exe" }));
        }

        public string GetAdbPath()
        {
            return FindToolPath(new[] { "adb.exe" });
        }

        public string GetFastbootPath()
        {
            return FindToolPath(new[] { "fastboot.exe" });
        }
        #endregion

        #region Tool Download & Installation
        public async Task<bool> DownloadAndInstallToolAsync(ToolType type, CancellationToken ct = default)
        {
            if (!ToolSources.TryGetValue(type, out var info))
            {
                Log($"Unknown tool type: {type}");
                return false;
            }

            return await DownloadAndInstallToolAsync(info, ct);
        }

        public async Task<bool> DownloadAndInstallPlatformToolsAsync(CancellationToken ct = default)
        {
            return await DownloadAndInstallToolAsync(ToolType.ADB, ct);
        }

        private async Task<bool> DownloadAndInstallToolAsync(ToolDownloadInfo info, CancellationToken ct)
        {
            try
            {
                Log($"Downloading {info.Name}...");
                StatusChanged?.Invoke(this, $"Downloading {info.Name}...");
                ProgressChanged?.Invoke(this, 0);

                var fileName = Path.GetFileName(new Uri(info.Url).LocalPath);
                var downloadPath = Path.Combine(Path.GetTempPath(), fileName);

                // Download
                if (!await DownloadFileAsync(info.Url, downloadPath, ct))
                {
                    Log($"Failed to download {info.Name}");
                    return false;
                }

                ProgressChanged?.Invoke(this, 70);
                Log("Extracting...");
                StatusChanged?.Invoke(this, "Extracting files...");

                // Extract
                var extractPath = _toolsPath;
                if (Path.GetExtension(downloadPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract to temp first
                    var tempExtract = Path.Combine(Path.GetTempPath(), $"tool_extract_{Guid.NewGuid():N}");
                    ZipFile.ExtractToDirectory(downloadPath, tempExtract, true);

                    // Move files to Tools folder
                    var sourceDir = tempExtract;
                    if (!string.IsNullOrEmpty(info.ExtractFolder))
                    {
                        var subDir = Path.Combine(tempExtract, info.ExtractFolder);
                        if (Directory.Exists(subDir))
                            sourceDir = subDir;
                    }

                    // Copy all files
                    CopyDirectory(sourceDir, _toolsPath);

                    // Cleanup
                    Directory.Delete(tempExtract, true);
                }

                // Cleanup download
                File.Delete(downloadPath);

                ProgressChanged?.Invoke(this, 90);

                // Verify installation
                var installed = !string.IsNullOrEmpty(FindToolPath(info.ExecutableNames));

                ProgressChanged?.Invoke(this, 100);

                if (installed)
                {
                    Log($"{info.Name} installed successfully");
                    StatusChanged?.Invoke(this, $"{info.Name} installed successfully");
                }
                else
                {
                    Log($"{info.Name} installation may require additional setup");
                    StatusChanged?.Invoke(this, "Installation completed (verify manually)");
                }

                return installed;
            }
            catch (Exception ex)
            {
                Log($"Error installing {info.Name}: {ex.Message}");
                StatusChanged?.Invoke(this, "Installation failed");
                return false;
            }
        }

        public async Task<bool> InstallAllEssentialToolsAsync(CancellationToken ct = default)
        {
            var success = true;

            // Platform Tools (ADB + Fastboot)
            if (!IsAdbInstalled())
            {
                success &= await DownloadAndInstallToolAsync(ToolType.ADB, ct);
            }
            else
            {
                Log("ADB/Fastboot already installed");
            }

            return success;
        }
        #endregion

        #region Python Tools (EDL, MTK)
        public async Task<bool> IsPythonInstalledAsync(CancellationToken ct = default)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                await process.WaitForExitAsync(ct);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SetupPythonToolAsync(ToolType type, CancellationToken ct = default)
        {
            if (!await IsPythonInstalledAsync(ct))
            {
                Log("Python is required but not installed");
                StatusChanged?.Invoke(this, "Python required - please install Python 3.8+");
                return false;
            }

            if (!ToolSources.TryGetValue(type, out var info))
                return false;

            // Download repository
            if (!await DownloadAndInstallToolAsync(info, ct))
                return false;

            // Install Python dependencies
            var toolPath = Directory.GetDirectories(_toolsPath, info.ExtractFolder, SearchOption.AllDirectories)
                .FirstOrDefault();

            if (toolPath == null)
            {
                Log("Tool folder not found after extraction");
                return false;
            }

            var requirementsPath = Path.Combine(toolPath, "requirements.txt");
            if (File.Exists(requirementsPath))
            {
                Log("Installing Python dependencies...");
                StatusChanged?.Invoke(this, "Installing Python dependencies...");

                var psi = new ProcessStartInfo
                {
                    FileName = "pip",
                    Arguments = $"install -r \"{requirementsPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                    await process.WaitForExitAsync(ct);
            }

            Log($"{info.Name} setup complete");
            return true;
        }
        #endregion

        #region Environment Setup
        public async Task<bool> AddToolsToPathAsync(CancellationToken ct = default)
        {
            try
            {
                var currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";

                if (!currentPath.Contains(_toolsPath))
                {
                    var newPath = $"{_toolsPath};{currentPath}";
                    Environment.SetEnvironmentVariable("PATH", newPath, EnvironmentVariableTarget.User);

                    // Also update current process
                    Environment.SetEnvironmentVariable("PATH", $"{_toolsPath};{Environment.GetEnvironmentVariable("PATH")}");

                    Log($"Added {_toolsPath} to user PATH");
                    return true;
                }

                Log("Tools folder already in PATH");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error updating PATH: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> StartAdbServerAsync(CancellationToken ct = default)
        {
            try
            {
                var adbPath = GetAdbPath();
                if (string.IsNullOrEmpty(adbPath))
                {
                    Log("ADB not found");
                    return false;
                }

                Log("Starting ADB server...");

                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "start-server",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                    await process.WaitForExitAsync(ct);

                Log("ADB server started");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error starting ADB server: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> KillAdbServerAsync(CancellationToken ct = default)
        {
            try
            {
                var adbPath = GetAdbPath();
                if (string.IsNullOrEmpty(adbPath))
                    return false;

                var psi = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "kill-server",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                    await process.WaitForExitAsync(ct);

                Log("ADB server stopped");
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region Helper Methods
        private async Task<bool> DownloadFileAsync(string url, string destinationPath, CancellationToken ct)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progress = (int)((totalRead * 70) / totalBytes); // 0-70% for download
                        ProgressChanged?.Invoke(this, progress);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log($"Download error: {ex.Message}");
                return false;
            }
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(this, $"[Tools] {message}");
        }
        #endregion

        private class ToolDownloadInfo
        {
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public string Url { get; set; } = "";
            public string[] ExecutableNames { get; set; } = Array.Empty<string>();
            public string ExtractFolder { get; set; } = "";
            public bool RequiresPython { get; set; }
        }
    }
}
