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
        SamsungFRP,
        // New tools from GitHub 2024-2025
        Thor,              // Samsung Loki Thor - Modern Odin alternative
        EdlNg,             // EDL-NG - Modern Qualcomm EDL tool
        XiaomiAdbFastboot, // Xiaomi ADB/Fastboot Tools
        Freya,             // Freya - Samsung flash tool
        HuaweiUnlock,      // Huawei bootloader unlock
        UnisocUnlock,      // Unisoc CVE-2022-38694 bootloader unlock
        SamFirm,           // Samsung Firmware Downloader
        Scrcpy             // Screen copy/mirror tool
    }

    public class ToolsDownloadService
    {
        private readonly string _toolsPath;
        private readonly HttpClient _httpClient;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;
        public event EventHandler<string>? StatusChanged;

        // Official/reliable download sources with fallbacks
        private static readonly Dictionary<ToolType, ToolDownloadInfo> ToolSources = new()
        {
            {
                ToolType.ADB,
                new ToolDownloadInfo
                {
                    Name = "Android Platform Tools (ADB & Fastboot)",
                    Description = "Official Google ADB and Fastboot tools",
                    Url = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip",
                    FallbackUrls = new[]
                    {
                        "https://androidsdkmanager.azurewebsites.net/Packages/GetLatestPackage?name=platform-tools",
                        "https://github.com/nicholast/android-tools/releases/latest/download/platform-tools-windows.zip"
                    },
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
                    Url = "https://bitbucket.org/benjamin_dobell/heimdall/downloads/heimdall-suite-1.4.0-win32.zip",
                    FallbackUrls = Array.Empty<string>(),
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
                    FallbackUrls = new[]
                    {
                        "https://codeload.github.com/bkerler/edl/zip/refs/heads/master"
                    },
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
                    FallbackUrls = new[]
                    {
                        "https://codeload.github.com/bkerler/mtkclient/zip/refs/heads/main"
                    },
                    ExecutableNames = new[] { "mtk.py" },
                    ExtractFolder = "mtkclient-main",
                    RequiresPython = true
                }
            },
            // ===== NEW TOOLS 2024-2025 =====
            {
                ToolType.Thor,
                new ToolDownloadInfo
                {
                    Name = "Thor (Samsung-Loki)",
                    Description = "Modern Samsung flash tool - Better than Odin/Heimdall (.NET 9)",
                    Url = "https://github.com/Samsung-Loki/Thor/releases/download/1.1.0/Thor-Windows.exe",
                    FallbackUrls = Array.Empty<string>(),
                    ExecutableNames = new[] { "Thor.exe", "Thor-Windows.exe" },
                    ExtractFolder = "",
                    IsSingleExecutable = true
                }
            },
            {
                ToolType.EdlNg,
                new ToolDownloadInfo
                {
                    Name = "EDL Client (bkerler)",
                    Description = "Qualcomm EDL/Firehose Python tool - Use 'python edl' command",
                    Url = "https://github.com/bkerler/edl/archive/refs/heads/master.zip",
                    FallbackUrls = new[]
                    {
                        "https://codeload.github.com/bkerler/edl/zip/refs/heads/master"
                    },
                    ExecutableNames = new[] { "edl", "edl.py" },
                    ExtractFolder = "edl-master",
                    RequiresPython = true
                }
            },
            {
                ToolType.XiaomiAdbFastboot,
                new ToolDownloadInfo
                {
                    Name = "Xiaomi ADB/Fastboot Tools",
                    Description = "Xiaomi device management - Requires Java 11+ to run .jar file",
                    Url = "https://raw.githubusercontent.com/kirthandev/MIUI-Debloater-official/main/src/tools/Xiaomi%20ADB%20%26%20Fastboot%20Tools.jar",
                    FallbackUrls = new[]
                    {
                        "https://github.com/INeatFreak/XiaomiADBFastbootTools/raw/main/XiaomiADBFastbootTools.jar"
                    },
                    ExecutableNames = new[] { "XiaomiADBFastbootTools.jar", "Xiaomi ADB & Fastboot Tools.jar" },
                    ExtractFolder = "",
                    RequiresPython = false, // Requires Java
                    IsSingleExecutable = true
                }
            },
            {
                ToolType.Freya,
                new ToolDownloadInfo
                {
                    Name = "Freya",
                    Description = "Samsung flash tool with fast features - Read, Flash, Repartition",
                    Url = "https://github.com/Alephgsm/Freya/releases/download/freya/Freya.exe",
                    FallbackUrls = Array.Empty<string>(),
                    ExecutableNames = new[] { "Freya.exe" },
                    ExtractFolder = "",
                    IsSingleExecutable = true
                }
            },
            {
                ToolType.HuaweiUnlock,
                new ToolDownloadInfo
                {
                    Name = "PotatoNV (Huawei Unlock)",
                    Description = "Huawei bootloader unlock for Kirin 960/95x/65x/620",
                    Url = "https://github.com/mashed-potatoes/PotatoNV/releases/download/2022.03/PotatoNV-next-v2.2.1_2022.03-x86.zip",
                    FallbackUrls = Array.Empty<string>(),
                    ExecutableNames = new[] { "PotatoNV-next.exe", "PotatoNV.exe" },
                    ExtractFolder = ""
                }
            },
            {
                ToolType.UnisocUnlock,
                new ToolDownloadInfo
                {
                    Name = "Unisoc Bootloader Unlock",
                    Description = "CVE-2022-38694 exploit - Download device-specific from GitHub releases",
                    Url = "https://github.com/TomKing062/CVE-2022-38694_unlock_bootloader/archive/refs/tags/1.72.zip",
                    FallbackUrls = new[]
                    {
                        "https://codeload.github.com/TomKing062/CVE-2022-38694_unlock_bootloader/zip/refs/tags/1.72"
                    },
                    ExecutableNames = new[] { "unlock.py", "main.py" },
                    ExtractFolder = "CVE-2022-38694_unlock_bootloader-1.72",
                    RequiresPython = true
                }
            },
            {
                ToolType.SamFirm,
                new ToolDownloadInfo
                {
                    Name = "SamFirm / Frija",
                    Description = "Samsung firmware downloader - Direct from Samsung servers",
                    Url = "https://github.com/SlackingVeteran/frija/releases/download/v2.0.23364.3/Frija_v2.0.23364.3.zip",
                    FallbackUrls = new[]
                    {
                        "https://github.com/ivanmeler/SamFirm_Reborn/releases/download/0.3.6.8/SamFirm_Reborn_0.3.6.8.zip"
                    },
                    ExecutableNames = new[] { "Frija.exe", "SamFirm.exe", "SamFirm_Reborn.exe" },
                    ExtractFolder = ""
                }
            },
            {
                ToolType.Scrcpy,
                new ToolDownloadInfo
                {
                    Name = "Scrcpy",
                    Description = "Android screen mirror & control via USB/WiFi",
                    Url = "https://github.com/Genymobile/scrcpy/releases/download/v3.3.4/scrcpy-win64-v3.3.4.zip",
                    FallbackUrls = new[]
                    {
                        "https://github.com/Genymobile/scrcpy/releases/download/v3.1/scrcpy-win64-v3.1.zip",
                        "https://github.com/Genymobile/scrcpy/releases/download/v2.7/scrcpy-win64-v2.7.zip"
                    },
                    ExecutableNames = new[] { "scrcpy.exe" },
                    ExtractFolder = ""
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
                // Skip version check for GUI applications - they don't support --version flag
                // and will open a window instead
                if (type == ToolType.Freya ||
                    type == ToolType.HuaweiUnlock ||
                    type == ToolType.SamFirm ||
                    type == ToolType.Scrcpy)
                {
                    // For GUI apps, just check if file exists and return "Installed"
                    return File.Exists(toolPath) ? "Installed" : "Unknown";
                }

                // Skip Python/Java tools - they require runtime to check version
                if (type == ToolType.UnisocUnlock ||
                    type == ToolType.EdlClient ||
                    type == ToolType.MTKClient ||
                    type == ToolType.XiaomiAdbFastboot)
                {
                    return File.Exists(toolPath) ? "Installed" : "Unknown";
                }

                string arguments = type switch
                {
                    ToolType.ADB => "version",
                    ToolType.Fastboot => "--version",
                    ToolType.Heimdall => "version",
                    ToolType.Thor => "--version",
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

                // Set a timeout to prevent hanging
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var output = await process.StandardOutput.ReadToEndAsync(linkedCts.Token);
                await process.WaitForExitAsync(linkedCts.Token);

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
                Views.DebugWindow.LogInfo($"Starting download: {info.Name}");
                StatusChanged?.Invoke(this, $"Downloading {info.Name}...");
                ProgressChanged?.Invoke(this, 0);

                var fileName = Path.GetFileName(new Uri(info.Url).LocalPath);
                var downloadPath = Path.Combine(Path.GetTempPath(), fileName);

                // Try primary URL first
                var downloadSuccess = await DownloadFileAsync(info.Url, downloadPath, ct);

                // If primary fails, try fallback URLs
                if (!downloadSuccess && info.FallbackUrls.Length > 0)
                {
                    for (int i = 0; i < info.FallbackUrls.Length; i++)
                    {
                        var fallbackUrl = info.FallbackUrls[i];
                        Log($"Primary download failed. Trying fallback {i + 1}/{info.FallbackUrls.Length}...");
                        Views.DebugWindow.LogWarning($"Trying fallback URL: {fallbackUrl}");
                        StatusChanged?.Invoke(this, $"Trying fallback server {i + 1}...");

                        downloadSuccess = await DownloadFileAsync(fallbackUrl, downloadPath, ct);
                        if (downloadSuccess) break;
                    }
                }

                if (!downloadSuccess)
                {
                    var errorMsg = $"Failed to download {info.Name} from all sources";
                    Log(errorMsg);
                    Views.DebugWindow.LogError(errorMsg);
                    StatusChanged?.Invoke(this, "Download failed - check Debug for details");
                    return false;
                }

                // Validate downloaded file
                var fileInfo = new FileInfo(downloadPath);
                if (fileInfo.Length < 1000) // Less than 1KB - probably an error page or empty file
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(downloadPath, ct);
                        Views.DebugWindow.LogError($"Download returned error/empty: {content.Substring(0, Math.Min(200, content.Length))}");
                    }
                    catch { }
                    File.Delete(downloadPath);
                    StatusChanged?.Invoke(this, "Download failed - file was empty or invalid");
                    return false;
                }

                Views.DebugWindow.LogInfo($"Downloaded {fileInfo.Length / 1024} KB");
                ProgressChanged?.Invoke(this, 70);

                // Handle single executable vs zip
                if (info.IsSingleExecutable ||
                    Path.GetExtension(downloadPath).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(downloadPath).Equals(".jar", StringComparison.OrdinalIgnoreCase))
                {
                    Log("Copying executable...");
                    StatusChanged?.Invoke(this, "Installing executable...");

                    var destPath = Path.Combine(_toolsPath, Path.GetFileName(downloadPath));
                    File.Copy(downloadPath, destPath, true);
                    File.Delete(downloadPath);
                }
                else if (Path.GetExtension(downloadPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    Log("Extracting...");
                    StatusChanged?.Invoke(this, "Extracting files...");

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
                    File.Delete(downloadPath);
                }
                else
                {
                    // Unknown file type, just copy it
                    var destPath = Path.Combine(_toolsPath, Path.GetFileName(downloadPath));
                    File.Copy(downloadPath, destPath, true);
                    File.Delete(downloadPath);
                }

                ProgressChanged?.Invoke(this, 90);

                // Verify installation
                var installedPath = FindToolPath(info.ExecutableNames);
                var installed = !string.IsNullOrEmpty(installedPath);

                // For Python tools, check if the script files exist in Tools folder
                if (!installed && info.RequiresPython)
                {
                    foreach (var exeName in info.ExecutableNames)
                    {
                        var pyPath = Directory.GetFiles(_toolsPath, exeName, SearchOption.AllDirectories).FirstOrDefault();
                        if (pyPath != null)
                        {
                            installed = true;
                            installedPath = pyPath;
                            Views.DebugWindow.LogInfo($"Python tool found at: {pyPath}");
                            break;
                        }
                    }
                }

                ProgressChanged?.Invoke(this, 100);

                if (installed)
                {
                    Log($"{info.Name} installed successfully");
                    Views.DebugWindow.LogInfo($"{info.Name} installed to: {installedPath}");
                    StatusChanged?.Invoke(this, $"{info.Name} installed successfully");
                }
                else
                {
                    Log($"{info.Name} installation may require additional setup");
                    Views.DebugWindow.LogWarning($"{info.Name} - could not verify installation. Expected files: {string.Join(", ", info.ExecutableNames)}");
                    StatusChanged?.Invoke(this, "Installation completed (verify manually)");
                }

                // For Python tools, return true if files were extracted even if verification fails
                // because Python tools need Python runtime to be "installed"
                return installed || info.RequiresPython;
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
                Views.DebugWindow.LogDebug($"Downloading from: {url}");

                // Create handler with redirect support for GitHub downloads
                using var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    MaxAutomaticRedirections = 10
                };
                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                client.Timeout = TimeSpan.FromMinutes(30);

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMsg = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} - {url}";
                    Views.DebugWindow.LogError(errorMsg);
                    return false;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                Views.DebugWindow.LogDebug($"Download size: {(totalBytes > 0 ? $"{totalBytes / 1024} KB ({totalBytes / 1024 / 1024} MB)" : "Unknown (chunked transfer)")}");

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

                Views.DebugWindow.LogInfo($"Download completed: {Path.GetFileName(destinationPath)} ({totalRead / 1024} KB)");
                return true;
            }
            catch (HttpRequestException ex)
            {
                Views.DebugWindow.LogError($"Network error: {ex.Message}");
                Log($"Network error: {ex.Message}");
                return false;
            }
            catch (TaskCanceledException)
            {
                Views.DebugWindow.LogWarning("Download cancelled or timed out");
                Log("Download cancelled or timed out");
                return false;
            }
            catch (Exception ex)
            {
                Views.DebugWindow.LogException(ex, "Download failed");
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
            public string[] FallbackUrls { get; set; } = Array.Empty<string>();
            public string[] ExecutableNames { get; set; } = Array.Empty<string>();
            public string ExtractFolder { get; set; } = "";
            public bool RequiresPython { get; set; }
            public bool IsSingleExecutable { get; set; } // For single .exe downloads (no zip)
        }
    }
}
