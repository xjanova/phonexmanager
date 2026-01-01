using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class MagiskInfo
    {
        public string Version { get; set; } = "";
        public int VersionCode { get; set; }
        public string DownloadUrl { get; set; } = "";
        public string Changelog { get; set; } = "";
    }

    public class MagiskModule
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Enabled { get; set; }
    }

    public class RootStatus
    {
        public bool IsRooted { get; set; }
        public string RootMethod { get; set; } = ""; // Magisk, SuperSU, KernelSU
        public string MagiskVersion { get; set; } = "";
        public bool HasSafetyNetPass { get; set; }
        public bool IsDenyListEnabled { get; set; }
        public bool IsZygiskEnabled { get; set; }
    }

    public class MagiskService
    {
        private readonly HttpClient _httpClient;
        private readonly string _adbPath;
        private readonly string _cachePath;
        public event EventHandler<string>? LogMessage;

        private const string MAGISK_RELEASES_URL = "https://api.github.com/repos/topjohnwu/Magisk/releases/latest";
        private const string MAGISK_CANARY_URL = "https://raw.githubusercontent.com/topjohnwu/magisk-files/master/canary.json";

        public MagiskService(string adbPath)
        {
            _adbPath = adbPath;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhoneRomFlashTool/1.0");

            _cachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhoneRomFlashTool", "Magisk");

            if (!Directory.Exists(_cachePath))
            {
                Directory.CreateDirectory(_cachePath);
            }
        }

        public async Task<MagiskInfo> GetLatestMagiskAsync(bool canary = false)
        {
            try
            {
                string json;
                if (canary)
                {
                    json = await _httpClient.GetStringAsync(MAGISK_CANARY_URL);
                    var canaryData = JsonDocument.Parse(json);
                    return new MagiskInfo
                    {
                        Version = canaryData.RootElement.GetProperty("magisk").GetProperty("version").GetString() ?? "",
                        VersionCode = canaryData.RootElement.GetProperty("magisk").GetProperty("versionCode").GetInt32(),
                        DownloadUrl = canaryData.RootElement.GetProperty("magisk").GetProperty("link").GetString() ?? "",
                        Changelog = "Canary build - Latest development version"
                    };
                }
                else
                {
                    json = await _httpClient.GetStringAsync(MAGISK_RELEASES_URL);
                    var releaseData = JsonDocument.Parse(json);
                    var assets = releaseData.RootElement.GetProperty("assets");

                    string downloadUrl = "";
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".apk"))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }

                    return new MagiskInfo
                    {
                        Version = releaseData.RootElement.GetProperty("tag_name").GetString() ?? "",
                        DownloadUrl = downloadUrl,
                        Changelog = releaseData.RootElement.GetProperty("body").GetString() ?? ""
                    };
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Failed to get Magisk info: {ex.Message}");
                return new MagiskInfo();
            }
        }

        public async Task<string> DownloadMagiskAsync(MagiskInfo magisk, IProgress<int>? progress = null)
        {
            if (string.IsNullOrEmpty(magisk.DownloadUrl))
            {
                return "";
            }

            var fileName = $"Magisk-{magisk.Version}.apk";
            var localPath = Path.Combine(_cachePath, fileName);

            if (File.Exists(localPath))
            {
                return localPath;
            }

            try
            {
                using var response = await _httpClient.GetAsync(magisk.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                var totalBytes = response.Content.Headers.ContentLength ?? -1;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(localPath, FileMode.Create);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        progress?.Report((int)((totalRead * 100) / totalBytes));
                    }
                }

                LogMessage?.Invoke(this, $"Downloaded Magisk {magisk.Version}");
                return localPath;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Failed to download Magisk: {ex.Message}");
                return "";
            }
        }

        public async Task<RootStatus> CheckRootStatusAsync()
        {
            var status = new RootStatus();

            try
            {
                // Check for Magisk
                var magiskResult = await RunAdbCommandAsync("shell su -c 'magisk -v'");
                if (!string.IsNullOrEmpty(magiskResult) && !magiskResult.Contains("not found"))
                {
                    status.IsRooted = true;
                    status.RootMethod = "Magisk";
                    status.MagiskVersion = magiskResult.Trim();

                    // Check Magisk features
                    var zygisk = await RunAdbCommandAsync("shell su -c 'magisk --sqlite \"SELECT value FROM settings WHERE key=\\\"zygisk\\\"\"'");
                    status.IsZygiskEnabled = zygisk.Contains("1");

                    var denylist = await RunAdbCommandAsync("shell su -c 'magisk --sqlite \"SELECT value FROM settings WHERE key=\\\"denylist\\\"\"'");
                    status.IsDenyListEnabled = denylist.Contains("1");
                }
                else
                {
                    // Check for KernelSU
                    var ksuResult = await RunAdbCommandAsync("shell su -c 'ksud -V'");
                    if (!string.IsNullOrEmpty(ksuResult) && !ksuResult.Contains("not found"))
                    {
                        status.IsRooted = true;
                        status.RootMethod = "KernelSU";
                        status.MagiskVersion = ksuResult.Trim();
                    }
                    else
                    {
                        // Check for generic root
                        var suResult = await RunAdbCommandAsync("shell su -c 'id'");
                        if (suResult.Contains("uid=0"))
                        {
                            status.IsRooted = true;
                            status.RootMethod = "Unknown";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error checking root: {ex.Message}");
            }

            return status;
        }

        public async Task<List<MagiskModule>> GetInstalledModulesAsync()
        {
            var modules = new List<MagiskModule>();

            try
            {
                var result = await RunAdbCommandAsync("shell su -c 'ls /data/adb/modules'");
                if (string.IsNullOrEmpty(result) || result.Contains("not found"))
                {
                    return modules;
                }

                var moduleIds = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var id in moduleIds)
                {
                    var moduleId = id.Trim();
                    if (string.IsNullOrEmpty(moduleId)) continue;

                    var propResult = await RunAdbCommandAsync($"shell su -c 'cat /data/adb/modules/{moduleId}/module.prop'");
                    if (string.IsNullOrEmpty(propResult)) continue;

                    var module = new MagiskModule { Id = moduleId };

                    foreach (var line in propResult.Split('\n'))
                    {
                        if (line.StartsWith("name="))
                            module.Name = line.Substring(5);
                        else if (line.StartsWith("version="))
                            module.Version = line.Substring(8);
                        else if (line.StartsWith("author="))
                            module.Author = line.Substring(7);
                        else if (line.StartsWith("description="))
                            module.Description = line.Substring(12);
                    }

                    // Check if disabled
                    var disableCheck = await RunAdbCommandAsync($"shell su -c 'test -f /data/adb/modules/{moduleId}/disable && echo disabled'");
                    module.Enabled = !disableCheck.Contains("disabled");

                    modules.Add(module);
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error getting modules: {ex.Message}");
            }

            return modules;
        }

        public async Task<bool> InstallMagiskAsync(string apkPath)
        {
            try
            {
                // Push APK to device
                await RunAdbCommandAsync($"push \"{apkPath}\" /data/local/tmp/magisk.apk");

                // Install APK
                var result = await RunAdbCommandAsync("shell pm install -r /data/local/tmp/magisk.apk");

                // Clean up
                await RunAdbCommandAsync("shell rm /data/local/tmp/magisk.apk");

                return result.Contains("Success");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error installing Magisk: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> PatchBootImageAsync(string bootImgPath, string outputPath)
        {
            try
            {
                // This requires Magisk app to be installed
                // We'll use the Magisk app's patch functionality

                // Push boot image
                await RunAdbCommandAsync($"push \"{bootImgPath}\" /data/local/tmp/boot.img");

                // Trigger Magisk to patch
                var result = await RunAdbCommandAsync("shell su -c '/data/adb/magisk/boot_patch.sh /data/local/tmp/boot.img'");

                if (result.Contains("Output file is written to"))
                {
                    // Pull patched boot
                    await RunAdbCommandAsync($"pull /data/local/tmp/magisk_patched*.img \"{outputPath}\"");
                    LogMessage?.Invoke(this, "Boot image patched successfully");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error patching boot: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ToggleModuleAsync(string moduleId, bool enable)
        {
            try
            {
                if (enable)
                {
                    await RunAdbCommandAsync($"shell su -c 'rm /data/adb/modules/{moduleId}/disable'");
                }
                else
                {
                    await RunAdbCommandAsync($"shell su -c 'touch /data/adb/modules/{moduleId}/disable'");
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> RemoveModuleAsync(string moduleId)
        {
            try
            {
                await RunAdbCommandAsync($"shell su -c 'touch /data/adb/modules/{moduleId}/remove'");
                LogMessage?.Invoke(this, $"Module {moduleId} marked for removal (reboot required)");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> HideRootFromAppAsync(string packageName)
        {
            try
            {
                await RunAdbCommandAsync($"shell su -c 'magisk --denylist add {packageName}'");
                LogMessage?.Invoke(this, $"Added {packageName} to Magisk DenyList");
                return true;
            }
            catch
            {
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
