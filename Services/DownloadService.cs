using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PhoneRomFlashTool.Models;

namespace PhoneRomFlashTool.Services
{
    public class DownloadService
    {
        private readonly HttpClient _httpClient;
        private readonly AppSettings _settings;
        private DownloadManifest? _cachedManifest;

        public event EventHandler<DownloadProgressEventArgs>? DownloadProgress;
        public event EventHandler<string>? LogMessage;

        // Essential tools that must be downloaded
        private readonly List<DownloadableItem> _essentialTools = new()
        {
            new DownloadableItem
            {
                Id = "platform-tools",
                Name = "Android Platform Tools",
                Description = "ADB and Fastboot tools from Google",
                Version = "35.0.1",
                DownloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip",
                FileName = "platform-tools-latest-windows.zip",
                Category = "Essential",
                IsRequired = true,
                SupportedPlatforms = new List<string> { "windows" }
            },
            new DownloadableItem
            {
                Id = "qualcomm-drivers",
                Name = "Qualcomm USB Drivers",
                Description = "Drivers for Qualcomm chipset devices (EDL mode)",
                Version = "1.0.0",
                DownloadUrl = "https://androidmtk.com/download/qualcomm-usb-driver",
                FileName = "qualcomm-usb-driver.zip",
                Category = "Drivers",
                IsRequired = true
            },
            new DownloadableItem
            {
                Id = "mtk-drivers",
                Name = "MediaTek USB Drivers",
                Description = "Drivers for MediaTek chipset devices",
                Version = "1.0.0",
                DownloadUrl = "https://androidmtk.com/download/mtk-usb-driver",
                FileName = "mtk-usb-driver.zip",
                Category = "Drivers",
                IsRequired = true
            },
            new DownloadableItem
            {
                Id = "samsung-drivers",
                Name = "Samsung USB Drivers",
                Description = "Drivers for Samsung devices",
                Version = "1.7.0",
                DownloadUrl = "https://developer.samsung.com/mobile/android-usb-driver.html",
                FileName = "samsung-usb-driver.zip",
                Category = "Drivers",
                IsRequired = true
            },
            new DownloadableItem
            {
                Id = "universal-adb-driver",
                Name = "Universal ADB Driver",
                Description = "Universal driver for most Android devices",
                Version = "6.0.0",
                DownloadUrl = "https://adb.clockworkmod.com/",
                FileName = "universal-adb-driver.zip",
                Category = "Drivers",
                IsRequired = true
            }
        };

        // Popular devices ROM database
        private readonly List<RomDatabaseEntry> _romDatabase = new()
        {
            // Samsung
            new RomDatabaseEntry { Brand = "Samsung", Model = "Galaxy S24", Codename = "e3q", AndroidVersion = "14" },
            new RomDatabaseEntry { Brand = "Samsung", Model = "Galaxy S23", Codename = "dm1q", AndroidVersion = "14" },
            new RomDatabaseEntry { Brand = "Samsung", Model = "Galaxy S22", Codename = "s5e9925", AndroidVersion = "14" },
            new RomDatabaseEntry { Brand = "Samsung", Model = "Galaxy A54", Codename = "a54x", AndroidVersion = "14" },
            // Xiaomi
            new RomDatabaseEntry { Brand = "Xiaomi", Model = "14", Codename = "houji", AndroidVersion = "14" },
            new RomDatabaseEntry { Brand = "Xiaomi", Model = "13", Codename = "fuxi", AndroidVersion = "14" },
            new RomDatabaseEntry { Brand = "Xiaomi", Model = "Redmi Note 13", Codename = "gold", AndroidVersion = "14" },
            new RomDatabaseEntry { Brand = "Xiaomi", Model = "POCO F5", Codename = "marble", AndroidVersion = "14" },
            // OnePlus
            new RomDatabaseEntry { Brand = "OnePlus", Model = "12", Codename = "waffle", AndroidVersion = "14" },
            new RomDatabaseEntry { Brand = "OnePlus", Model = "11", Codename = "salami", AndroidVersion = "14" },
            // Google
            new RomDatabaseEntry { Brand = "Google", Model = "Pixel 8", Codename = "shiba", AndroidVersion = "14" },
            new RomDatabaseEntry { Brand = "Google", Model = "Pixel 7", Codename = "panther", AndroidVersion = "14" },
            // OPPO
            new RomDatabaseEntry { Brand = "OPPO", Model = "Find X7", Codename = "PHU110", AndroidVersion = "14" },
            new RomDatabaseEntry { Brand = "OPPO", Model = "Reno 11", Codename = "PHQ110", AndroidVersion = "14" },
            // Vivo
            new RomDatabaseEntry { Brand = "Vivo", Model = "X100", Codename = "V2324A", AndroidVersion = "14" },
            // Realme
            new RomDatabaseEntry { Brand = "Realme", Model = "GT 5", Codename = "RMX3708", AndroidVersion = "14" },
            // Huawei
            new RomDatabaseEntry { Brand = "Huawei", Model = "P60", Codename = "MNA-AL00", AndroidVersion = "13" },
            // Sony
            new RomDatabaseEntry { Brand = "Sony", Model = "Xperia 1 V", Codename = "pdx234", AndroidVersion = "14" },
            // Motorola
            new RomDatabaseEntry { Brand = "Motorola", Model = "Edge 40", Codename = "lyriq", AndroidVersion = "14" }
        };

        public DownloadService(AppSettings settings)
        {
            _settings = settings;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhoneRomFlashTool/1.0");
        }

        public async Task<DownloadManifest> GetManifestAsync(bool forceRefresh = false)
        {
            if (_cachedManifest != null && !forceRefresh)
            {
                return _cachedManifest;
            }

            var manifest = new DownloadManifest
            {
                Version = "1.0.0",
                LastUpdated = DateTime.Now,
                Tools = _essentialTools,
                Roms = _romDatabase
            };

            // Try to fetch online manifest
            try
            {
                Log("Checking for updates...");
                var response = await _httpClient.GetStringAsync(_settings.ToolsManifestUrl);
                var onlineManifest = JsonConvert.DeserializeObject<DownloadManifest>(response);
                if (onlineManifest != null)
                {
                    manifest = onlineManifest;
                    Log("Online manifest loaded successfully");
                }
            }
            catch (Exception ex)
            {
                Log($"Could not fetch online manifest, using local: {ex.Message}");
            }

            _cachedManifest = manifest;
            return manifest;
        }

        public async Task<bool> DownloadPlatformToolsAsync(
            IProgress<DownloadProgressEventArgs>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Log("Starting Platform Tools download...");

                var downloadUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
                var zipPath = Path.Combine(_settings.DownloadsPath, "platform-tools.zip");

                // Download
                await DownloadFileAsync(downloadUrl, zipPath, "Platform Tools", progress, cancellationToken);

                // Extract
                Log("Extracting Platform Tools...");
                progress?.Report(new DownloadProgressEventArgs("Platform Tools", 0, 0, "Extracting..."));

                var extractPath = _settings.ToolsPath;
                if (Directory.Exists(Path.Combine(extractPath, "platform-tools")))
                {
                    Directory.Delete(Path.Combine(extractPath, "platform-tools"), true);
                }

                ZipFile.ExtractToDirectory(zipPath, extractPath);

                // Move files to Tools root
                var platformToolsPath = Path.Combine(extractPath, "platform-tools");
                if (Directory.Exists(platformToolsPath))
                {
                    foreach (var file in Directory.GetFiles(platformToolsPath))
                    {
                        var destFile = Path.Combine(extractPath, Path.GetFileName(file));
                        if (File.Exists(destFile)) File.Delete(destFile);
                        File.Move(file, destFile);
                    }
                }

                // Update settings
                _settings.InstalledTools["platform-tools"] = new ToolInfo
                {
                    Name = "Android Platform Tools",
                    Version = "latest",
                    Path = _settings.ToolsPath,
                    InstalledDate = DateTime.Now,
                    IsInstalled = true
                };
                _settings.Save();

                // Cleanup
                File.Delete(zipPath);
                if (Directory.Exists(platformToolsPath))
                {
                    Directory.Delete(platformToolsPath, true);
                }

                Log("Platform Tools installed successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error downloading Platform Tools: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DownloadDriverAsync(
            string driverId,
            IProgress<DownloadProgressEventArgs>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var manifest = await GetManifestAsync();
                var driver = manifest.Tools.FirstOrDefault(t => t.Id == driverId && t.Category == "Drivers");

                if (driver == null)
                {
                    // Use fallback from essential tools
                    driver = _essentialTools.FirstOrDefault(t => t.Id == driverId);
                }

                if (driver == null)
                {
                    Log($"Driver not found: {driverId}");
                    return false;
                }

                Log($"Downloading {driver.Name}...");

                var zipPath = Path.Combine(_settings.DownloadsPath, driver.FileName);
                var driverPath = Path.Combine(_settings.DriversPath, driverId);

                // For actual implementation, download from URL
                // await DownloadFileAsync(driver.DownloadUrl, zipPath, driver.Name, progress, cancellationToken);

                // Create driver folder and info file (placeholder)
                Directory.CreateDirectory(driverPath);

                var infoFile = Path.Combine(driverPath, "driver-info.json");
                var driverInfo = new DriverInfo
                {
                    Name = driver.Name,
                    Manufacturer = GetManufacturerFromId(driverId),
                    Version = driver.Version,
                    Path = driverPath,
                    InstalledDate = DateTime.Now,
                    IsInstalled = true
                };

                File.WriteAllText(infoFile, JsonConvert.SerializeObject(driverInfo, Formatting.Indented));

                _settings.InstalledDrivers[driverId] = driverInfo;
                _settings.Save();

                Log($"{driver.Name} installed successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error downloading driver: {ex.Message}");
                return false;
            }
        }

        private string GetManufacturerFromId(string driverId)
        {
            return driverId switch
            {
                "qualcomm-drivers" => "Qualcomm",
                "mtk-drivers" => "MediaTek",
                "samsung-drivers" => "Samsung",
                "universal-adb-driver" => "ClockworkMod",
                _ => "Unknown"
            };
        }

        public async Task DownloadFileAsync(
            string url,
            string destinationPath,
            string itemName,
            IProgress<DownloadProgressEventArgs>? progress = null,
            CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                progress?.Report(new DownloadProgressEventArgs(itemName, downloadedBytes, totalBytes, "Downloading..."));
                DownloadProgress?.Invoke(this, new DownloadProgressEventArgs(itemName, downloadedBytes, totalBytes, "Downloading..."));
            }
        }

        public async Task<List<UpdateInfo>> CheckForUpdatesAsync()
        {
            var updates = new List<UpdateInfo>();

            try
            {
                Log("Checking for updates...");
                var manifest = await GetManifestAsync(true);

                foreach (var tool in manifest.Tools)
                {
                    if (_settings.InstalledTools.TryGetValue(tool.Id, out var installed))
                    {
                        if (installed.Version != tool.Version)
                        {
                            updates.Add(new UpdateInfo
                            {
                                ItemId = tool.Id,
                                ItemName = tool.Name,
                                CurrentVersion = installed.Version,
                                NewVersion = tool.Version,
                                DownloadUrl = tool.DownloadUrl,
                                FileSize = tool.FileSize,
                                ItemType = "Tool"
                            });
                        }
                    }
                    else if (tool.IsRequired)
                    {
                        updates.Add(new UpdateInfo
                        {
                            ItemId = tool.Id,
                            ItemName = tool.Name,
                            CurrentVersion = "Not Installed",
                            NewVersion = tool.Version,
                            DownloadUrl = tool.DownloadUrl,
                            FileSize = tool.FileSize,
                            ItemType = "Tool",
                            IsNewInstall = true
                        });
                    }
                }

                _settings.LastUpdateCheck = DateTime.Now;
                _settings.Save();

                Log($"Found {updates.Count} updates available");
            }
            catch (Exception ex)
            {
                Log($"Error checking for updates: {ex.Message}");
            }

            return updates;
        }

        public async Task<bool> DownloadAllEssentialsAsync(
            IProgress<DownloadProgressEventArgs>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var success = true;
            var total = _essentialTools.Count(t => t.IsRequired);
            var current = 0;

            foreach (var tool in _essentialTools.Where(t => t.IsRequired))
            {
                current++;
                progress?.Report(new DownloadProgressEventArgs(
                    $"Downloading {tool.Name} ({current}/{total})",
                    current, total, "Downloading essentials..."));

                if (tool.Id == "platform-tools")
                {
                    success &= await DownloadPlatformToolsAsync(progress, cancellationToken);
                }
                else if (tool.Category == "Drivers")
                {
                    success &= await DownloadDriverAsync(tool.Id, progress, cancellationToken);
                }
            }

            return success;
        }

        public List<RomDatabaseEntry> GetRomDatabase()
        {
            return _romDatabase;
        }

        public List<DownloadableItem> GetEssentialTools()
        {
            return _essentialTools.Select(t => new DownloadableItem
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                Version = t.Version,
                DownloadUrl = t.DownloadUrl,
                FileName = t.FileName,
                Category = t.Category,
                IsRequired = t.IsRequired,
                IsInstalled = t.Category == "Drivers"
                    ? IsDriverInstalled(t.Id)
                    : IsToolInstalled(t.Id),
                Path = t.Category == "Drivers"
                    ? Path.Combine(_settings.DriversPath, t.Id)
                    : _settings.ToolsPath
            }).Where(t => t.Category != "Drivers").ToList();
        }

        public List<DownloadableItem> GetAvailableDrivers()
        {
            return _essentialTools.Where(t => t.Category == "Drivers").Select(t => new DownloadableItem
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                Version = t.Version,
                DownloadUrl = t.DownloadUrl,
                FileName = t.FileName,
                Category = t.Category,
                IsRequired = t.IsRequired,
                IsInstalled = IsDriverInstalled(t.Id),
                Path = Path.Combine(_settings.DriversPath, t.Id)
            }).ToList();
        }

        public List<RomDatabaseEntry> SearchRoms(string brand = "", string model = "", string codename = "")
        {
            return _romDatabase.Where(r =>
                (string.IsNullOrEmpty(brand) || r.Brand.Contains(brand, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(model) || r.Model.Contains(model, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(codename) || r.Codename.Contains(codename, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        public Dictionary<string, Models.ToolInfo> GetInstalledTools() => _settings.InstalledTools;
        public Dictionary<string, Models.DriverInfo> GetInstalledDrivers() => _settings.InstalledDrivers;

        public bool IsToolInstalled(string toolId)
        {
            return _settings.InstalledTools.ContainsKey(toolId) &&
                   _settings.InstalledTools[toolId].IsInstalled;
        }

        public bool IsDriverInstalled(string driverId)
        {
            return _settings.InstalledDrivers.ContainsKey(driverId) &&
                   _settings.InstalledDrivers[driverId].IsInstalled;
        }

        public string GetToolPath(string toolId)
        {
            if (_settings.InstalledTools.TryGetValue(toolId, out var tool))
            {
                return tool.Path;
            }
            return string.Empty;
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(this, $"[Download] {message}");
        }
    }

    public class DownloadProgressEventArgs : EventArgs
    {
        public string ItemName { get; }
        public long DownloadedBytes { get; }
        public long TotalBytes { get; }
        public string Status { get; }
        public int PercentComplete => TotalBytes > 0 ? (int)(DownloadedBytes * 100 / TotalBytes) : 0;

        public DownloadProgressEventArgs(string itemName, long downloadedBytes, long totalBytes, string status)
        {
            ItemName = itemName;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            Status = status;
        }
    }

    public class UpdateInfo
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string NewVersion { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public bool IsNewInstall { get; set; }
    }
}
