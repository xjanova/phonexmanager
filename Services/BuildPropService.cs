using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class BuildPropEntry
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public string OriginalValue { get; set; } = "";
        public bool IsModified => Value != OriginalValue;
        public bool IsComment { get; set; }
        public string Category { get; set; } = "Other";
    }

    public class BuildPropFile
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public List<BuildPropEntry> Entries { get; set; } = new();
        public bool IsModified => Entries.Any(e => e.IsModified);
        public DateTime LoadTime { get; set; }
    }

    public class BuildPropService
    {
        private readonly string _adbPath;
        public event EventHandler<string>? LogMessage;

        // Common build.prop locations
        public static readonly List<string> PropFiles = new()
        {
            "/system/build.prop",
            "/vendor/build.prop",
            "/product/build.prop",
            "/system_ext/build.prop",
            "/odm/build.prop",
            "/system/system/build.prop"
        };

        // Common props organized by category
        public static readonly Dictionary<string, List<(string Key, string Description)>> CommonProps = new()
        {
            ["Device Info"] = new()
            {
                ("ro.product.model", "Device Model"),
                ("ro.product.brand", "Brand"),
                ("ro.product.name", "Product Name"),
                ("ro.product.device", "Device Codename"),
                ("ro.product.manufacturer", "Manufacturer"),
                ("ro.build.display.id", "Build Display ID"),
                ("ro.build.version.release", "Android Version"),
                ("ro.build.version.sdk", "SDK Version"),
                ("ro.build.version.security_patch", "Security Patch Level"),
                ("ro.build.fingerprint", "Build Fingerprint")
            },
            ["Performance"] = new()
            {
                ("dalvik.vm.heapsize", "Dalvik Heap Size"),
                ("dalvik.vm.heapgrowthlimit", "Heap Growth Limit"),
                ("dalvik.vm.heapminfree", "Heap Min Free"),
                ("dalvik.vm.heapmaxfree", "Heap Max Free"),
                ("dalvik.vm.heaptargetutilization", "Heap Target Utilization"),
                ("persist.sys.dalvik.vm.lib.2", "ART Library"),
                ("ro.config.hw_quickpoweron", "Quick Power On")
            },
            ["Display"] = new()
            {
                ("ro.sf.lcd_density", "LCD Density"),
                ("persist.sys.ui.hw", "Hardware UI"),
                ("ro.opengles.version", "OpenGL ES Version"),
                ("debug.hwui.render_dirty_regions", "Render Dirty Regions"),
                ("persist.sys.use_dithering", "Use Dithering"),
                ("ro.config.density_override", "Density Override")
            },
            ["Network"] = new()
            {
                ("net.tcp.buffersize.default", "TCP Buffer Default"),
                ("net.tcp.buffersize.wifi", "TCP Buffer WiFi"),
                ("net.tcp.buffersize.lte", "TCP Buffer LTE"),
                ("net.tcp.buffersize.hspap", "TCP Buffer HSPAP"),
                ("wifi.interface", "WiFi Interface"),
                ("ro.telephony.default_network", "Default Network Type")
            },
            ["Audio"] = new()
            {
                ("ro.config.media_vol_steps", "Media Volume Steps"),
                ("ro.config.vc_call_vol_steps", "Call Volume Steps"),
                ("persist.audio.fluence.speaker", "Speaker Fluence"),
                ("persist.audio.fluence.voicecall", "Voice Call Fluence"),
                ("ro.audio.flinger_standbytime_ms", "Audio Standby Time")
            },
            ["Battery"] = new()
            {
                ("ro.config.hw_power_saving", "HW Power Saving"),
                ("pm.sleep_mode", "Sleep Mode"),
                ("ro.ril.power_collapse", "Power Collapse"),
                ("wifi.supplicant_scan_interval", "WiFi Scan Interval"),
                ("power.saving.mode", "Power Saving Mode")
            },
            ["Security"] = new()
            {
                ("ro.secure", "Secure Boot"),
                ("ro.debuggable", "Debuggable"),
                ("ro.adb.secure", "ADB Secure"),
                ("ro.build.tags", "Build Tags"),
                ("ro.boot.verifiedbootstate", "Verified Boot State"),
                ("ro.oem_unlock_supported", "OEM Unlock Supported")
            },
            ["Camera"] = new()
            {
                ("camera.hal1.packagelist", "HAL1 Package List"),
                ("persist.camera.HAL3.enabled", "HAL3 Enabled"),
                ("persist.camera.eis.enable", "EIS Enable"),
                ("persist.camera.is_type", "IS Type")
            }
        };

        // Popular modifications
        public static readonly Dictionary<string, Dictionary<string, string>> PopularMods = new()
        {
            ["Increase DPI"] = new()
            {
                { "ro.sf.lcd_density", "480" }
            },
            ["Decrease DPI"] = new()
            {
                { "ro.sf.lcd_density", "320" }
            },
            ["Enable ADB Root"] = new()
            {
                { "ro.secure", "0" },
                { "ro.adb.secure", "0" },
                { "ro.debuggable", "1" }
            },
            ["Disable ADB Root"] = new()
            {
                { "ro.secure", "1" },
                { "ro.adb.secure", "1" },
                { "ro.debuggable", "0" }
            },
            ["Increase Performance"] = new()
            {
                { "dalvik.vm.heapsize", "512m" },
                { "dalvik.vm.heapgrowthlimit", "256m" },
                { "ro.config.hw_quickpoweron", "true" }
            },
            ["Better WiFi"] = new()
            {
                { "wifi.supplicant_scan_interval", "180" },
                { "net.tcp.buffersize.wifi", "524288,1048576,2097152,262144,524288,1048576" }
            },
            ["Battery Saver"] = new()
            {
                { "ro.config.hw_power_saving", "true" },
                { "pm.sleep_mode", "1" },
                { "wifi.supplicant_scan_interval", "300" }
            }
        };

        public BuildPropService(string adbPath)
        {
            _adbPath = adbPath;
        }

        public async Task<List<BuildPropFile>> GetAvailablePropFilesAsync()
        {
            var available = new List<BuildPropFile>();

            foreach (var path in PropFiles)
            {
                var exists = await RunAdbCommandAsync($"shell \"[ -f {path} ] && echo exists\"");
                if (exists.Contains("exists"))
                {
                    available.Add(new BuildPropFile
                    {
                        Path = path,
                        Name = Path.GetFileName(path) + " (" + Path.GetDirectoryName(path)?.Replace("/", "") + ")"
                    });
                }
            }

            LogMessage?.Invoke(this, $"Found {available.Count} prop files");
            return available;
        }

        public async Task<BuildPropFile> LoadPropFileAsync(string path)
        {
            var propFile = new BuildPropFile
            {
                Path = path,
                Name = Path.GetFileName(path),
                LoadTime = DateTime.Now
            };

            try
            {
                var content = await RunAdbCommandAsync($"shell cat \"{path}\"");
                var lines = content.Split('\n');

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();

                    if (string.IsNullOrEmpty(trimmed))
                        continue;

                    if (trimmed.StartsWith("#"))
                    {
                        propFile.Entries.Add(new BuildPropEntry
                        {
                            Key = trimmed,
                            Value = "",
                            OriginalValue = "",
                            IsComment = true
                        });
                        continue;
                    }

                    var equalsIndex = trimmed.IndexOf('=');
                    if (equalsIndex > 0)
                    {
                        var key = trimmed.Substring(0, equalsIndex);
                        var value = trimmed.Substring(equalsIndex + 1);

                        var entry = new BuildPropEntry
                        {
                            Key = key,
                            Value = value,
                            OriginalValue = value,
                            Category = GetCategory(key)
                        };

                        propFile.Entries.Add(entry);
                    }
                }

                LogMessage?.Invoke(this, $"Loaded {propFile.Entries.Count} entries from {path}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error loading {path}: {ex.Message}");
            }

            return propFile;
        }

        private string GetCategory(string key)
        {
            foreach (var category in CommonProps)
            {
                if (category.Value.Any(p => p.Key == key))
                    return category.Key;
            }

            if (key.StartsWith("ro.product") || key.StartsWith("ro.build"))
                return "Device Info";
            if (key.StartsWith("dalvik") || key.StartsWith("persist.sys"))
                return "Performance";
            if (key.Contains("display") || key.Contains("lcd") || key.Contains("density"))
                return "Display";
            if (key.Contains("net") || key.Contains("wifi") || key.Contains("telephony"))
                return "Network";
            if (key.Contains("audio") || key.Contains("media"))
                return "Audio";
            if (key.Contains("power") || key.Contains("battery"))
                return "Battery";
            if (key.Contains("secure") || key.Contains("debug") || key.Contains("adb"))
                return "Security";
            if (key.Contains("camera"))
                return "Camera";

            return "Other";
        }

        public async Task<bool> SavePropFileAsync(BuildPropFile propFile, bool requireRoot = true)
        {
            try
            {
                var sb = new StringBuilder();

                foreach (var entry in propFile.Entries)
                {
                    if (entry.IsComment)
                    {
                        sb.AppendLine(entry.Key);
                    }
                    else
                    {
                        sb.AppendLine($"{entry.Key}={entry.Value}");
                    }
                }

                // Write to temp file
                var tempFile = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempFile, sb.ToString());

                // Push to device temp location
                var deviceTempPath = "/data/local/tmp/build.prop.tmp";
                await RunAdbCommandAsync($"push \"{tempFile}\" {deviceTempPath}");

                // Copy to destination (requires root for system partitions)
                if (requireRoot)
                {
                    // Remount system as rw
                    await RunAdbCommandAsync("shell su -c \"mount -o rw,remount /system\"");
                    await RunAdbCommandAsync("shell su -c \"mount -o rw,remount /vendor\"");

                    // Backup original
                    await RunAdbCommandAsync($"shell su -c \"cp {propFile.Path} {propFile.Path}.bak\"");

                    // Copy new file
                    await RunAdbCommandAsync($"shell su -c \"cp {deviceTempPath} {propFile.Path}\"");
                    await RunAdbCommandAsync($"shell su -c \"chmod 644 {propFile.Path}\"");

                    // Remount as ro
                    await RunAdbCommandAsync("shell su -c \"mount -o ro,remount /system\"");
                    await RunAdbCommandAsync("shell su -c \"mount -o ro,remount /vendor\"");
                }
                else
                {
                    await RunAdbCommandAsync($"shell cp {deviceTempPath} {propFile.Path}");
                }

                // Cleanup
                await RunAdbCommandAsync($"shell rm {deviceTempPath}");
                File.Delete(tempFile);

                LogMessage?.Invoke(this, $"Saved {propFile.Path}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Save error: {ex.Message}");
                return false;
            }
        }

        public async Task<string> GetPropAsync(string key)
        {
            var result = await RunAdbCommandAsync($"shell getprop {key}");
            return result.Trim();
        }

        public async Task<bool> SetPropAsync(string key, string value, bool persistent = false)
        {
            try
            {
                if (persistent)
                {
                    // Modify build.prop (requires root)
                    await RunAdbCommandAsync($"shell su -c \"setprop {key} {value}\"");
                    // Also write to persist storage if available
                    await RunAdbCommandAsync($"shell su -c \"echo '{key}={value}' >> /data/property/persistent_properties\"");
                }
                else
                {
                    // Runtime only
                    await RunAdbCommandAsync($"shell setprop {key} {value}");
                }

                LogMessage?.Invoke(this, $"Set {key}={value}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"SetProp error: {ex.Message}");
                return false;
            }
        }

        public async Task<Dictionary<string, string>> GetAllPropsAsync()
        {
            var props = new Dictionary<string, string>();

            try
            {
                var result = await RunAdbCommandAsync("shell getprop");
                var lines = result.Split('\n');

                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"\[(.+?)\]:\s*\[(.+?)\]");
                    if (match.Success)
                    {
                        props[match.Groups[1].Value] = match.Groups[2].Value;
                    }
                }

                LogMessage?.Invoke(this, $"Retrieved {props.Count} runtime properties");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"GetAllProps error: {ex.Message}");
            }

            return props;
        }

        public async Task<bool> ApplyModificationAsync(string modName)
        {
            if (!PopularMods.ContainsKey(modName))
            {
                LogMessage?.Invoke(this, $"Modification not found: {modName}");
                return false;
            }

            var mods = PopularMods[modName];
            var success = true;

            foreach (var mod in mods)
            {
                if (!await SetPropAsync(mod.Key, mod.Value, true))
                    success = false;
            }

            LogMessage?.Invoke(this, success ? $"Applied: {modName}" : $"Some modifications failed for: {modName}");
            return success;
        }

        public async Task<bool> RestoreBackupAsync(string propFilePath)
        {
            var backupPath = propFilePath + ".bak";

            try
            {
                var exists = await RunAdbCommandAsync($"shell \"[ -f {backupPath} ] && echo exists\"");
                if (!exists.Contains("exists"))
                {
                    LogMessage?.Invoke(this, "No backup found");
                    return false;
                }

                await RunAdbCommandAsync("shell su -c \"mount -o rw,remount /system\"");
                await RunAdbCommandAsync($"shell su -c \"cp {backupPath} {propFilePath}\"");
                await RunAdbCommandAsync("shell su -c \"mount -o ro,remount /system\"");

                LogMessage?.Invoke(this, "Restored from backup");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Restore error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ExportPropFileAsync(string propFilePath, string localPath)
        {
            try
            {
                var result = await RunAdbCommandAsync($"pull \"{propFilePath}\" \"{localPath}\"");
                var success = !result.Contains("error") && !result.Contains("failed");
                LogMessage?.Invoke(this, success ? $"Exported to {localPath}" : $"Export failed: {result}");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Export error: {ex.Message}");
                return false;
            }
        }

        public async Task<string> SearchPropsAsync(string searchTerm)
        {
            try
            {
                var result = await RunAdbCommandAsync($"shell getprop | grep -i \"{searchTerm}\"");
                return result;
            }
            catch
            {
                return "";
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
