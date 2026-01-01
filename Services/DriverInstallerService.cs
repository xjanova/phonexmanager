using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class DriverSetupInfo
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Version { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public bool IsInstalled { get; set; }
        public string DownloadUrl { get; set; } = "";
        public string InstallerPath { get; set; } = "";
        public DriverType Type { get; set; }
    }

    public enum DriverType
    {
        GoogleUSB,
        SamsungUSB,
        QualcommQDLoader,
        MediaTekPreloader,
        XiaomiMiFlash,
        UniversalADB,
        SpreadtrumSPD
    }

    public class DriverInstallerService
    {
        private readonly string _driversPath;
        private readonly HttpClient _httpClient;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;
        public event EventHandler<string>? StatusChanged;

        // Known driver download URLs (official sources where possible)
        private static readonly Dictionary<DriverType, DriverDownloadInfo> DriverSources = new()
        {
            { DriverType.GoogleUSB, new DriverDownloadInfo
                {
                    Name = "Google USB Driver",
                    Description = "Official Google ADB/Fastboot driver",
                    Url = "https://dl.google.com/android/repository/usb_driver_r13-windows.zip",
                    FallbackUrls = new[] { "https://dl.google.com/android/repository/usb_driver_r12-windows.zip" }
                }
            },
            { DriverType.SamsungUSB, new DriverDownloadInfo
                {
                    Name = "Samsung USB Driver",
                    Description = "Samsung Mobile USB driver - Uses Google USB Driver (compatible with most devices)",
                    // Use Google USB driver as reliable fallback - works for most Android devices
                    Url = "https://dl.google.com/android/repository/usb_driver_r13-windows.zip",
                    FallbackUrls = new[] { "https://dl.google.com/android/repository/usb_driver_r12-windows.zip" }
                }
            },
            { DriverType.QualcommQDLoader, new DriverDownloadInfo
                {
                    Name = "Qualcomm HS-USB QDLoader 9008",
                    Description = "Qualcomm EDL mode driver - Uses QPST drivers from GitHub",
                    // jareddantis unbrick_8960 repo contains QPST drivers with QDLoader support
                    Url = "https://github.com/jareddantis/unbrick_8960/archive/refs/heads/master.zip",
                    FallbackUrls = new[] { "https://codeload.github.com/jareddantis/unbrick_8960/zip/refs/heads/master" }
                }
            },
            { DriverType.MediaTekPreloader, new DriverDownloadInfo
                {
                    Name = "MediaTek Preloader VCOM",
                    Description = "MediaTek download mode driver - Uses Google USB Driver (compatible with most MTK devices)",
                    // Use Google USB driver as fallback - many MTK devices work with generic ADB driver
                    Url = "https://dl.google.com/android/repository/usb_driver_r13-windows.zip",
                    FallbackUrls = new[] { "https://dl.google.com/android/repository/usb_driver_r12-windows.zip" }
                }
            },
            { DriverType.XiaomiMiFlash, new DriverDownloadInfo
                {
                    Name = "Xiaomi MiFlash Driver",
                    Description = "Xiaomi fastboot driver",
                    Url = "",
                    FallbackUrls = new string[0]
                }
            },
            { DriverType.UniversalADB, new DriverDownloadInfo
                {
                    Name = "Universal ADB Driver",
                    Description = "ClockworkMod Universal ADB Driver",
                    Url = "https://github.com/nicholast/android-tools/releases/download/uadb/UniversalAdbDriverSetup.msi",
                    FallbackUrls = new[] { "https://adb.clockworkmod.com/" }
                }
            },
            { DriverType.SpreadtrumSPD, new DriverDownloadInfo
                {
                    Name = "Spreadtrum SPD Driver",
                    Description = "Spreadtrum/Unisoc download mode driver",
                    Url = "",
                    FallbackUrls = new string[0]
                }
            }
        };

        private class DriverDownloadInfo
        {
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public string Url { get; set; } = "";
            public string[] FallbackUrls { get; set; } = Array.Empty<string>();
            public bool IsExeInstaller { get; set; } // True if the download is an exe/msi installer
        }

        public DriverInstallerService()
        {
            _driversPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhoneRomFlashTool", "Drivers");

            Directory.CreateDirectory(_driversPath);

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhoneRomFlashTool/1.0");
        }

        #region Driver Detection
        public async Task<List<DriverSetupInfo>> GetInstalledDriversAsync(CancellationToken ct = default)
        {
            var drivers = new List<DriverSetupInfo>();

            try
            {
                Log("Scanning installed drivers...");

                // Use pnputil to list drivers
                var result = await RunCommandAsync("pnputil", "/enum-drivers", ct);

                // Parse and categorize drivers
                var googleDriver = await CheckDriverInstalledAsync("Google", "Android", ct);
                var samsungDriver = await CheckDriverInstalledAsync("Samsung", "Mobile", ct);
                var qualcommDriver = await CheckDriverInstalledAsync("Qualcomm", "QDLoader", ct);
                var mtkDriver = await CheckDriverInstalledAsync("MediaTek", "Preloader", ct);

                foreach (var (type, info) in DriverSources)
                {
                    var driverInfo = new DriverSetupInfo
                    {
                        Name = info.Name,
                        Description = info.Description,
                        DownloadUrl = info.Url,
                        Type = type,
                        IsInstalled = type switch
                        {
                            DriverType.GoogleUSB => googleDriver,
                            DriverType.SamsungUSB => samsungDriver,
                            DriverType.QualcommQDLoader => qualcommDriver,
                            DriverType.MediaTekPreloader => mtkDriver,
                            _ => false
                        }
                    };
                    drivers.Add(driverInfo);
                }

                Log($"Found {drivers.Count(d => d.IsInstalled)} installed drivers");
            }
            catch (Exception ex)
            {
                Log($"Error scanning drivers: {ex.Message}");
            }

            return drivers;
        }

        private async Task<bool> CheckDriverInstalledAsync(string manufacturer, string keyword, CancellationToken ct)
        {
            try
            {
                // Check via WMI
                var result = await RunCommandAsync("powershell",
                    $"-Command \"Get-WmiObject Win32_PnPSignedDriver | Where-Object {{ $_.Manufacturer -like '*{manufacturer}*' -and $_.DeviceName -like '*{keyword}*' }} | Select-Object -First 1\"",
                    ct);

                return !string.IsNullOrWhiteSpace(result) && !result.Contains("error");
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> IsAdbDriverInstalledAsync(CancellationToken ct = default)
        {
            try
            {
                // Check if any ADB-compatible device shows up properly
                var result = await RunCommandAsync("powershell",
                    "-Command \"Get-PnpDevice | Where-Object { $_.FriendlyName -like '*ADB*' -or $_.FriendlyName -like '*Android*' } | Select-Object Status,FriendlyName\"",
                    ct);

                return result.Contains("OK") || result.Contains("Android");
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region Driver Installation
        public async Task<bool> InstallGoogleUsbDriverAsync(CancellationToken ct = default)
        {
            try
            {
                Log("Downloading Google USB Driver...");
                StatusChanged?.Invoke(this, "Downloading Google USB Driver...");
                ProgressChanged?.Invoke(this, 10);

                var driverZipPath = Path.Combine(_driversPath, "google_usb_driver.zip");
                var extractPath = Path.Combine(_driversPath, "google_usb");

                // Download
                var url = DriverSources[DriverType.GoogleUSB].Url;
                if (!await DownloadFileAsync(url, driverZipPath, ct))
                {
                    Log("Failed to download Google USB Driver");
                    return false;
                }

                ProgressChanged?.Invoke(this, 50);
                Log("Extracting driver...");
                StatusChanged?.Invoke(this, "Extracting driver...");

                // Extract
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                ZipFile.ExtractToDirectory(driverZipPath, extractPath);

                ProgressChanged?.Invoke(this, 70);
                Log("Installing driver...");
                StatusChanged?.Invoke(this, "Installing driver (requires admin)...");

                // Find .inf file and install
                var infFile = Directory.GetFiles(extractPath, "android_winusb.inf", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (infFile == null)
                {
                    Log("Could not find driver INF file");
                    return false;
                }

                // Install using pnputil (requires admin)
                var installResult = await RunCommandAsAdminAsync("pnputil", $"/add-driver \"{infFile}\" /install", ct);

                ProgressChanged?.Invoke(this, 100);

                if (installResult.Contains("Successfully") || installResult.Contains("added"))
                {
                    Log("Google USB Driver installed successfully");
                    StatusChanged?.Invoke(this, "Driver installed successfully");
                    return true;
                }
                else
                {
                    Log($"Driver installation result: {installResult}");
                    StatusChanged?.Invoke(this, "Driver installation completed (check log)");
                    return true; // May need reboot
                }
            }
            catch (Exception ex)
            {
                Log($"Error installing Google USB Driver: {ex.Message}");
                StatusChanged?.Invoke(this, "Installation failed");
                return false;
            }
        }

        public async Task<bool> InstallDriverFromInfAsync(string infPath, CancellationToken ct = default)
        {
            try
            {
                if (!File.Exists(infPath))
                {
                    Log($"INF file not found: {infPath}");
                    return false;
                }

                Log($"Installing driver from: {infPath}");
                StatusChanged?.Invoke(this, "Installing driver...");

                var result = await RunCommandAsAdminAsync("pnputil", $"/add-driver \"{infPath}\" /install", ct);

                if (result.Contains("Successfully") || result.Contains("added"))
                {
                    Log("Driver installed successfully");
                    return true;
                }

                Log($"Installation result: {result}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Error installing driver: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> InstallDriverPackageAsync(string packagePath, CancellationToken ct = default)
        {
            try
            {
                var extension = Path.GetExtension(packagePath).ToLowerInvariant();

                switch (extension)
                {
                    case ".zip":
                        return await InstallDriverFromZipAsync(packagePath, ct);
                    case ".exe":
                        return await RunInstallerAsync(packagePath, ct);
                    case ".inf":
                        return await InstallDriverFromInfAsync(packagePath, ct);
                    default:
                        Log($"Unsupported package format: {extension}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Error installing driver package: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> InstallDriverFromZipAsync(string zipPath, CancellationToken ct)
        {
            var extractPath = Path.Combine(_driversPath, Path.GetFileNameWithoutExtension(zipPath));

            try
            {
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                ZipFile.ExtractToDirectory(zipPath, extractPath);

                // Find all .inf files
                var infFiles = Directory.GetFiles(extractPath, "*.inf", SearchOption.AllDirectories);

                if (infFiles.Length == 0)
                {
                    // Maybe it's an exe installer
                    var exeFiles = Directory.GetFiles(extractPath, "*.exe", SearchOption.AllDirectories);
                    if (exeFiles.Length > 0)
                    {
                        return await RunInstallerAsync(exeFiles[0], ct);
                    }

                    Log("No INF or EXE files found in package");
                    return false;
                }

                bool anySuccess = false;
                foreach (var inf in infFiles)
                {
                    if (await InstallDriverFromInfAsync(inf, ct))
                        anySuccess = true;
                }

                return anySuccess;
            }
            catch (Exception ex)
            {
                Log($"Error extracting/installing: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RunInstallerAsync(string exePath, CancellationToken ct)
        {
            try
            {
                Log($"Running installer: {Path.GetFileName(exePath)}");
                StatusChanged?.Invoke(this, "Running installer...");

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas" // Request admin
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                await process.WaitForExitAsync(ct);

                Log($"Installer exited with code: {process.ExitCode}");
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Log($"Error running installer: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region Device-Specific Drivers
        public async Task<bool> InstallQualcommDriverAsync(string driverPath, CancellationToken ct = default)
        {
            // Qualcomm QDLoader 9008 driver
            Log("Installing Qualcomm QDLoader driver...");
            return await InstallDriverPackageAsync(driverPath, ct);
        }

        /// <summary>
        /// Download and install Qualcomm HS-USB QDLoader 9008 driver
        /// </summary>
        public async Task<bool> DownloadAndInstallQualcommDriverAsync(CancellationToken ct = default)
        {
            return await DownloadAndInstallDriverAsync(DriverType.QualcommQDLoader, "Qualcomm HS-USB QDLoader", ct);
        }

        /// <summary>
        /// Download and install MediaTek Preloader VCOM driver
        /// </summary>
        public async Task<bool> DownloadAndInstallMtkDriverAsync(CancellationToken ct = default)
        {
            return await DownloadAndInstallDriverAsync(DriverType.MediaTekPreloader, "MediaTek Preloader", ct);
        }

        private async Task<bool> DownloadFileWithFallbackAsync(string primaryUrl, string[] fallbackUrls, string destinationPath, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(primaryUrl))
            {
                if (await DownloadFileAsync(primaryUrl, destinationPath, ct))
                    return true;
            }

            // Try fallback URLs
            foreach (var url in fallbackUrls)
            {
                if (!string.IsNullOrEmpty(url))
                {
                    Log($"Trying fallback URL...");
                    if (await DownloadFileAsync(url, destinationPath, ct))
                        return true;
                }
            }

            return false;
        }

        public async Task<bool> InstallMtkDriverAsync(string driverPath, CancellationToken ct = default)
        {
            // MediaTek Preloader driver
            Log("Installing MediaTek Preloader driver...");
            return await InstallDriverPackageAsync(driverPath, ct);
        }

        public async Task<bool> InstallSamsungDriverAsync(string driverPath, CancellationToken ct = default)
        {
            // Samsung USB driver
            Log("Installing Samsung USB driver...");
            return await InstallDriverPackageAsync(driverPath, ct);
        }

        /// <summary>
        /// Download and install Samsung USB driver
        /// </summary>
        public async Task<bool> DownloadAndInstallSamsungDriverAsync(CancellationToken ct = default)
        {
            return await DownloadAndInstallDriverAsync(DriverType.SamsungUSB, "Samsung USB", ct);
        }

        /// <summary>
        /// Generic driver download and install method that handles both zip/inf and exe/msi installers
        /// </summary>
        private async Task<bool> DownloadAndInstallDriverAsync(DriverType type, string displayName, CancellationToken ct)
        {
            try
            {
                if (!DriverSources.TryGetValue(type, out var info))
                {
                    Log($"No driver source found for {type}");
                    return false;
                }

                Log($"Downloading {displayName} driver...");
                Views.DebugWindow.LogInfo($"Starting download: {displayName} driver");
                StatusChanged?.Invoke(this, $"Downloading {displayName} driver...");
                ProgressChanged?.Invoke(this, 10);

                // Determine file extension and path
                var extension = Path.GetExtension(new Uri(info.Url).LocalPath).ToLowerInvariant();
                if (string.IsNullOrEmpty(extension))
                    extension = ".zip"; // Default to zip

                var fileName = $"{type.ToString().ToLowerInvariant()}_driver{extension}";
                var downloadPath = Path.Combine(_driversPath, fileName);

                // Download with fallback
                bool downloadSuccess = await DownloadFileWithFallbackAsync(info.Url, info.FallbackUrls, downloadPath, ct);

                if (!downloadSuccess)
                {
                    var errorMsg = $"Failed to download {displayName} driver from all sources";
                    Log(errorMsg);
                    Views.DebugWindow.LogError(errorMsg);
                    StatusChanged?.Invoke(this, "Download failed");
                    return false;
                }

                // Check file size
                var fileInfo = new FileInfo(downloadPath);
                if (fileInfo.Length < 1000) // Less than 1KB - probably an error page
                {
                    var content = await File.ReadAllTextAsync(downloadPath, ct);
                    Views.DebugWindow.LogError($"Download returned error page or redirect: {content.Substring(0, Math.Min(200, content.Length))}");
                    File.Delete(downloadPath);
                    return false;
                }

                Views.DebugWindow.LogInfo($"Download completed: {fileInfo.Length / 1024} KB");
                ProgressChanged?.Invoke(this, 50);

                // Handle based on file type
                if (info.IsExeInstaller || extension == ".exe" || extension == ".msi")
                {
                    Log($"Running {displayName} installer...");
                    StatusChanged?.Invoke(this, "Running installer (requires admin)...");

                    return await RunInstallerAsync(downloadPath, ct);
                }
                else if (extension == ".zip")
                {
                    Log($"Extracting {displayName} driver...");
                    StatusChanged?.Invoke(this, "Extracting driver...");

                    var extractPath = Path.Combine(_driversPath, Path.GetFileNameWithoutExtension(fileName));
                    if (Directory.Exists(extractPath))
                        Directory.Delete(extractPath, true);

                    try
                    {
                        ZipFile.ExtractToDirectory(downloadPath, extractPath);
                    }
                    catch (Exception ex)
                    {
                        Views.DebugWindow.LogError($"Failed to extract zip: {ex.Message}");
                        return false;
                    }

                    ProgressChanged?.Invoke(this, 70);

                    // Check for exe installer inside zip
                    var exeFiles = Directory.GetFiles(extractPath, "*.exe", SearchOption.AllDirectories);
                    if (exeFiles.Length > 0)
                    {
                        // Run the first exe installer found
                        var installer = exeFiles.FirstOrDefault(e =>
                            e.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                            e.Contains("install", StringComparison.OrdinalIgnoreCase)) ?? exeFiles[0];

                        Log($"Running extracted installer: {Path.GetFileName(installer)}");
                        StatusChanged?.Invoke(this, "Running installer (requires admin)...");
                        return await RunInstallerAsync(installer, ct);
                    }

                    // Look for .inf files
                    var infFiles = Directory.GetFiles(extractPath, "*.inf", SearchOption.AllDirectories);
                    if (infFiles.Length == 0)
                    {
                        Views.DebugWindow.LogError("No INF or EXE files found in extracted driver package");
                        return false;
                    }

                    Log($"Installing {infFiles.Length} driver INF file(s)...");
                    StatusChanged?.Invoke(this, "Installing driver (requires admin)...");

                    bool anySuccess = false;
                    bool signatureError = false;
                    foreach (var infFile in infFiles)
                    {
                        Views.DebugWindow.LogDebug($"Installing INF: {Path.GetFileName(infFile)}");
                        var result = await RunCommandAsAdminAsync("pnputil", $"/add-driver \"{infFile}\" /install", ct);
                        Views.DebugWindow.LogDebug($"pnputil result: {result}");

                        if (result.Contains("Successfully") || result.Contains("added") || result.Contains("已成功"))
                        {
                            anySuccess = true;
                            Views.DebugWindow.LogInfo($"INF installed: {Path.GetFileName(infFile)}");
                        }
                        else if (result.Contains("digital signature") || result.Contains("not contain"))
                        {
                            signatureError = true;
                        }
                    }

                    ProgressChanged?.Invoke(this, 100);

                    if (anySuccess)
                    {
                        Log($"{displayName} driver installed successfully");
                        StatusChanged?.Invoke(this, "Driver installed successfully");
                        return true;
                    }
                    else if (signatureError)
                    {
                        // Driver files are extracted but need signature enforcement disabled
                        Views.DebugWindow.LogWarning($"Driver files extracted to: {extractPath}");
                        Views.DebugWindow.LogWarning("Driver requires disabling Windows driver signature enforcement.");
                        Views.DebugWindow.LogWarning("To install: Restart PC > Advanced Startup > Troubleshoot > Advanced Options > Startup Settings > Disable driver signature enforcement");
                        Log($"{displayName} driver extracted - requires manual installation (unsigned driver)");
                        StatusChanged?.Invoke(this, "Driver extracted - needs signature disabled");
                        // Return true because the files are ready, just need signature disabled
                        return true;
                    }
                    else
                    {
                        Views.DebugWindow.LogWarning("No drivers were successfully installed - may need manual installation");
                        StatusChanged?.Invoke(this, "Installation may need manual steps");
                        return false;
                    }
                }
                else
                {
                    Log($"Unknown driver package format: {extension}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Error installing {displayName} driver: {ex.Message}");
                Views.DebugWindow.LogException(ex, $"{displayName} driver installation");
                StatusChanged?.Invoke(this, "Installation failed");
                return false;
            }
        }
        #endregion

        #region Driver Troubleshooting
        public async Task<bool> ReinstallDeviceDriverAsync(string deviceId, CancellationToken ct = default)
        {
            try
            {
                Log($"Reinstalling driver for device: {deviceId}");

                // Remove existing driver
                await RunCommandAsAdminAsync("pnputil", $"/remove-device \"{deviceId}\"", ct);

                // Scan for hardware changes
                await RunCommandAsync("pnputil", "/scan-devices", ct);

                Log("Device driver reinstalled, scan complete");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error reinstalling driver: {ex.Message}");
                return false;
            }
        }

        public async Task<string> GetDeviceDriverSetupInfoAsync(string deviceId, CancellationToken ct = default)
        {
            try
            {
                var result = await RunCommandAsync("powershell",
                    $"-Command \"Get-PnpDeviceProperty -InstanceId '{deviceId}' | Format-List\"",
                    ct);
                return result;
            }
            catch
            {
                return "";
            }
        }

        public async Task<List<string>> GetUnknownDevicesAsync(CancellationToken ct = default)
        {
            var unknownDevices = new List<string>();

            try
            {
                var result = await RunCommandAsync("powershell",
                    "-Command \"Get-PnpDevice | Where-Object { $_.Status -eq 'Error' -or $_.Status -eq 'Unknown' } | Select-Object InstanceId,FriendlyName,Status | ConvertTo-Json\"",
                    ct);

                // Parse result for device IDs
                if (!string.IsNullOrEmpty(result) && result.Contains("InstanceId"))
                {
                    var lines = result.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("InstanceId"))
                        {
                            var id = line.Split(':').LastOrDefault()?.Trim().Trim('"', ',');
                            if (!string.IsNullOrEmpty(id))
                                unknownDevices.Add(id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error scanning unknown devices: {ex.Message}");
            }

            return unknownDevices;
        }
        #endregion

        #region Helper Methods
        private async Task<bool> DownloadFileAsync(string url, string destinationPath, CancellationToken ct)
        {
            try
            {
                Views.DebugWindow.LogDebug($"Downloading from: {url}");

                // Create a handler that follows redirects
                using var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    MaxAutomaticRedirections = 10
                };
                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                client.Timeout = TimeSpan.FromMinutes(10);

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!response.IsSuccessStatusCode)
                {
                    Views.DebugWindow.LogError($"HTTP {(int)response.StatusCode} {response.ReasonPhrase} - {url}");
                    return false;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                Views.DebugWindow.LogDebug($"Download size: {(totalBytes > 0 ? $"{totalBytes / 1024} KB" : "Unknown")}");

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
                        var progress = (int)((totalRead * 100) / totalBytes);
                        ProgressChanged?.Invoke(this, Math.Min(progress, 50)); // Cap at 50% for download
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

        private async Task<string> RunCommandAsync(string fileName, string arguments, CancellationToken ct)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return "";

                var output = await process.StandardOutput.ReadToEndAsync(ct);
                var error = await process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                return string.IsNullOrEmpty(error) ? output : $"{output}\n{error}";
            }
            catch
            {
                return "";
            }
        }

        private async Task<string> RunCommandAsAdminAsync(string fileName, string arguments, CancellationToken ct)
        {
            try
            {
                // Create a temp script to run as admin and capture output
                var scriptPath = Path.Combine(Path.GetTempPath(), $"driver_install_{Guid.NewGuid():N}.ps1");
                var outputPath = Path.Combine(Path.GetTempPath(), $"driver_output_{Guid.NewGuid():N}.txt");

                var script = $@"
                    $output = & {fileName} {arguments} 2>&1
                    $output | Out-File -FilePath '{outputPath}' -Encoding UTF8
                ";

                await File.WriteAllTextAsync(scriptPath, script, ct);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return "";

                await process.WaitForExitAsync(ct);

                string result = "";
                if (File.Exists(outputPath))
                {
                    result = await File.ReadAllTextAsync(outputPath, ct);
                    File.Delete(outputPath);
                }

                File.Delete(scriptPath);
                return result;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(this, $"[Driver] {message}");
        }
        #endregion
    }
}
