using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    /// <summary>
    /// Debloat Service - Remove/Disable unwanted system apps
    /// </summary>
    public class DebloatService
    {
        private readonly AdbService _adbService;
        private readonly string _profilesPath;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        // Predefined bloatware lists by manufacturer
        private static readonly Dictionary<string, List<BloatwareApp>> BloatwareLists = new()
        {
            ["samsung"] = new List<BloatwareApp>
            {
                new("com.samsung.android.app.tips", "Samsung Tips", "Safe"),
                new("com.samsung.android.game.gamehome", "Game Launcher", "Safe"),
                new("com.samsung.android.game.gametools", "Game Tools", "Safe"),
                new("com.samsung.android.bixby.agent", "Bixby Voice", "Caution"),
                new("com.samsung.android.bixby.service", "Bixby Service", "Caution"),
                new("com.samsung.android.visionintelligence", "Bixby Vision", "Safe"),
                new("com.samsung.android.arzone", "AR Zone", "Safe"),
                new("com.samsung.android.aremoji", "AR Emoji", "Safe"),
                new("com.facebook.katana", "Facebook", "Safe"),
                new("com.facebook.appmanager", "Facebook App Manager", "Safe"),
                new("com.facebook.system", "Facebook System", "Safe"),
                new("com.microsoft.office.outlook", "Outlook", "Safe"),
                new("com.microsoft.skydrive", "OneDrive", "Safe"),
                new("com.linkedin.android", "LinkedIn", "Safe"),
            },
            ["xiaomi"] = new List<BloatwareApp>
            {
                new("com.miui.analytics", "MIUI Analytics", "Safe"),
                new("com.miui.msa.global", "MSA", "Safe"),
                new("com.xiaomi.glgm", "Games", "Safe"),
                new("com.miui.videoplayer", "Mi Video", "Safe"),
                new("com.miui.player", "Mi Music", "Safe"),
                new("com.mi.globalminusscreen", "App Vault", "Safe"),
                new("com.miui.cloudbackup", "Mi Cloud Backup", "Caution"),
                new("com.xiaomi.midrop", "ShareMe", "Safe"),
                new("com.miui.bugreport", "Bug Report", "Safe"),
                new("com.miui.yellowpage", "Yellow Pages", "Safe"),
            },
            ["google"] = new List<BloatwareApp>
            {
                new("com.google.android.apps.docs", "Google Docs", "Safe"),
                new("com.google.android.apps.tachyon", "Google Duo", "Safe"),
                new("com.google.android.videos", "Google Play Movies", "Safe"),
                new("com.google.android.music", "Google Play Music", "Safe"),
                new("com.google.android.apps.books", "Google Play Books", "Safe"),
                new("com.google.android.apps.magazines", "Google News", "Safe"),
                new("com.google.android.youtube.music", "YouTube Music", "Safe"),
            },
            ["huawei"] = new List<BloatwareApp>
            {
                new("com.huawei.himovie", "Huawei Video", "Safe"),
                new("com.huawei.music", "Huawei Music", "Safe"),
                new("com.huawei.appmarket", "AppGallery", "Caution"),
                new("com.huawei.hicloud", "Huawei Cloud", "Caution"),
                new("com.huawei.hifolder", "HiFolder", "Safe"),
            },
            ["oppo"] = new List<BloatwareApp>
            {
                new("com.heytap.market", "OPPO App Market", "Caution"),
                new("com.heytap.browser", "OPPO Browser", "Safe"),
                new("com.coloros.gamespace", "Game Space", "Safe"),
                new("com.coloros.weather2", "Weather", "Safe"),
            },
            ["vivo"] = new List<BloatwareApp>
            {
                new("com.vivo.browser", "Vivo Browser", "Safe"),
                new("com.vivo.appstore", "Vivo App Store", "Caution"),
                new("com.vivo.game", "Game Center", "Safe"),
            }
        };

        public DebloatService(AdbService adbService)
        {
            _adbService = adbService;
            _profilesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhoneRomFlashTool", "DebloatProfiles");
            Directory.CreateDirectory(_profilesPath);
        }

        #region Device Detection
        public async Task<string> DetectManufacturerAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                var brand = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.product.brand");
                return brand?.Trim()?.ToLowerInvariant() ?? "";
            }
            catch
            {
                return "";
            }
        }

        public async Task<List<InstalledApp>> GetInstalledAppsAsync(string serial, CancellationToken ct = default)
        {
            var apps = new List<InstalledApp>();

            try
            {
                Log("Getting installed apps...");

                // Get all packages
                var result = await _adbService.ExecuteAdbCommandAsync(serial, "shell pm list packages -f");

                if (!string.IsNullOrEmpty(result))
                {
                    var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        // Format: package:/data/app/com.example.app-xxx/base.apk=com.example.app
                        var parts = line.Replace("package:", "").Split('=');
                        if (parts.Length == 2)
                        {
                            apps.Add(new InstalledApp
                            {
                                PackageName = parts[1].Trim(),
                                ApkPath = parts[0].Trim(),
                                IsSystem = parts[0].Contains("/system/")
                            });
                        }
                    }
                }

                Log($"Found {apps.Count} apps");
            }
            catch (Exception ex)
            {
                Log($"Error getting apps: {ex.Message}");
            }

            return apps;
        }
        #endregion

        #region Bloatware Lists
        public List<BloatwareApp> GetBloatwareList(string manufacturer)
        {
            var list = new List<BloatwareApp>();

            // Add manufacturer-specific bloatware
            if (BloatwareLists.TryGetValue(manufacturer.ToLowerInvariant(), out var manuList))
            {
                list.AddRange(manuList);
            }

            // Always add Google bloatware
            if (BloatwareLists.TryGetValue("google", out var googleList))
            {
                list.AddRange(googleList);
            }

            return list;
        }

        public async Task<List<BloatwareApp>> ScanForBloatwareAsync(string serial, CancellationToken ct = default)
        {
            var manufacturer = await DetectManufacturerAsync(serial, ct);
            var installed = await GetInstalledAppsAsync(serial, ct);
            var bloatware = GetBloatwareList(manufacturer);

            // Mark installed bloatware
            foreach (var app in bloatware)
            {
                app.IsInstalled = installed.Any(i => i.PackageName == app.PackageName);
            }

            return bloatware.Where(b => b.IsInstalled).ToList();
        }
        #endregion

        #region Debloat Operations
        public async Task<bool> DisableAppAsync(string serial, string packageName, CancellationToken ct = default)
        {
            try
            {
                Log($"Disabling: {packageName}");

                var result = await _adbService.ExecuteAdbCommandAsync(serial, $"shell pm disable-user --user 0 {packageName}");

                return result?.Contains("disabled") == true || result?.Contains("new state") == true;
            }
            catch (Exception ex)
            {
                Log($"Error disabling {packageName}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EnableAppAsync(string serial, string packageName, CancellationToken ct = default)
        {
            try
            {
                Log($"Enabling: {packageName}");

                var result = await _adbService.ExecuteAdbCommandAsync(serial, $"shell pm enable {packageName}");

                return result?.Contains("enabled") == true || result?.Contains("new state") == true;
            }
            catch (Exception ex)
            {
                Log($"Error enabling {packageName}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UninstallAppAsync(string serial, string packageName, bool keepData = true, CancellationToken ct = default)
        {
            try
            {
                Log($"Uninstalling: {packageName}");

                var cmd = keepData
                    ? $"shell pm uninstall -k --user 0 {packageName}"
                    : $"shell pm uninstall --user 0 {packageName}";

                var result = await _adbService.ExecuteAdbCommandAsync(serial, cmd);

                return result?.Contains("Success") == true;
            }
            catch (Exception ex)
            {
                Log($"Error uninstalling {packageName}: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ReinstallAppAsync(string serial, string packageName, CancellationToken ct = default)
        {
            try
            {
                Log($"Reinstalling: {packageName}");

                var result = await _adbService.ExecuteAdbCommandAsync(serial, $"shell cmd package install-existing {packageName}");

                return result?.Contains("installed") == true;
            }
            catch (Exception ex)
            {
                Log($"Error reinstalling {packageName}: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region Batch Operations
        public async Task<DebloatResult> DebloatBatchAsync(string serial, List<string> packages, DebloatAction action, CancellationToken ct = default)
        {
            var result = new DebloatResult();
            int processed = 0;

            foreach (var pkg in packages)
            {
                if (ct.IsCancellationRequested) break;

                bool success = action switch
                {
                    DebloatAction.Disable => await DisableAppAsync(serial, pkg, ct),
                    DebloatAction.Uninstall => await UninstallAppAsync(serial, pkg, true, ct),
                    DebloatAction.Enable => await EnableAppAsync(serial, pkg, ct),
                    DebloatAction.Reinstall => await ReinstallAppAsync(serial, pkg, ct),
                    _ => false
                };

                if (success)
                    result.Succeeded.Add(pkg);
                else
                    result.Failed.Add(pkg);

                processed++;
                ProgressChanged?.Invoke(this, (processed * 100) / packages.Count);
            }

            Log($"Completed: {result.Succeeded.Count} succeeded, {result.Failed.Count} failed");
            return result;
        }
        #endregion

        #region Profiles
        public async Task SaveProfileAsync(string name, List<string> packages, CancellationToken ct = default)
        {
            var path = Path.Combine(_profilesPath, $"{name}.json");
            var json = JsonSerializer.Serialize(packages, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json, ct);
            Log($"Profile saved: {name}");
        }

        public async Task<List<string>> LoadProfileAsync(string name, CancellationToken ct = default)
        {
            var path = Path.Combine(_profilesPath, $"{name}.json");
            if (!File.Exists(path)) return new List<string>();

            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }

        public List<string> GetSavedProfiles()
        {
            return Directory.GetFiles(_profilesPath, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => n != null)
                .Select(n => n!)
                .ToList();
        }
        #endregion

        private void Log(string message) => LogMessage?.Invoke(this, message);
    }

    #region Models
    public class BloatwareApp
    {
        public string PackageName { get; set; }
        public string Name { get; set; }
        public string RiskLevel { get; set; }
        public bool IsInstalled { get; set; }

        public BloatwareApp(string packageName, string name, string riskLevel)
        {
            PackageName = packageName;
            Name = name;
            RiskLevel = riskLevel;
        }
    }

    public class InstalledApp
    {
        public string PackageName { get; set; } = "";
        public string ApkPath { get; set; } = "";
        public bool IsSystem { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public class DebloatResult
    {
        public List<string> Succeeded { get; set; } = new();
        public List<string> Failed { get; set; } = new();
    }

    public enum DebloatAction
    {
        Disable,
        Enable,
        Uninstall,
        Reinstall
    }
    #endregion
}
