using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class FrpStatus
    {
        public bool IsLocked { get; set; }
        public string GoogleAccount { get; set; } = "";
        public string FrpPartition { get; set; } = "";
        public string DeviceState { get; set; } = "";
        public long FrpSize { get; set; }
    }

    public class FrpBypassService
    {
        private readonly string _adbPath;
        private readonly string _fastbootPath;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        // Known FRP partition names across devices
        private static readonly string[] FrpPartitionNames = new[]
        {
            "frp", "FRP", "config", "persistent", "persist",
            "persistentdata", "pdb", "proinfo", "oem"
        };

        public FrpBypassService(string adbPath)
        {
            _adbPath = adbPath;
            _fastbootPath = Path.Combine(Path.GetDirectoryName(adbPath) ?? "", "fastboot.exe");
        }

        public async Task<FrpStatus> CheckFrpStatusAsync(CancellationToken ct = default)
        {
            var status = new FrpStatus();

            try
            {
                LogMessage?.Invoke(this, "Checking FRP status...");

                // Try to get FRP status via settings
                var frpResult = await RunAdbCommandAsync(
                    "shell settings get secure user_setup_complete",
                    TimeSpan.FromSeconds(10), ct);

                status.IsLocked = frpResult.Trim() != "1";

                // Check for Google account
                var accountResult = await RunAdbCommandAsync(
                    "shell pm list packages | grep -i google",
                    TimeSpan.FromSeconds(10), ct);

                // Try to find FRP partition
                foreach (var partName in FrpPartitionNames)
                {
                    var checkResult = await RunAdbCommandAsync(
                        $"shell ls -la /dev/block/by-name/{partName} 2>/dev/null",
                        TimeSpan.FromSeconds(5), ct);

                    if (!string.IsNullOrEmpty(checkResult) && !checkResult.Contains("No such file"))
                    {
                        status.FrpPartition = partName;
                        break;
                    }
                }

                // Check device state
                var stateResult = await RunAdbCommandAsync(
                    "shell getprop ro.boot.verifiedbootstate",
                    TimeSpan.FromSeconds(5), ct);
                status.DeviceState = stateResult.Trim();

                LogMessage?.Invoke(this, $"FRP Status: {(status.IsLocked ? "Locked" : "Unlocked")}");
                LogMessage?.Invoke(this, $"FRP Partition: {status.FrpPartition}");
                LogMessage?.Invoke(this, $"Boot State: {status.DeviceState}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error checking FRP: {ex.Message}");
            }

            return status;
        }

        public async Task<bool> BypassViaAdbAsync(CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Attempting FRP bypass via ADB...");
                LogMessage?.Invoke(this, "This requires device to be in ADB mode with some access.");

                // Method 1: Try to disable FRP via content provider
                LogMessage?.Invoke(this, "Method 1: Disabling setup wizard...");
                await RunAdbCommandAsync(
                    "shell content insert --uri content://settings/secure --bind name:s:user_setup_complete --bind value:s:1",
                    TimeSpan.FromSeconds(10), ct);

                await RunAdbCommandAsync(
                    "shell pm disable-user --user 0 com.google.android.setupwizard",
                    TimeSpan.FromSeconds(10), ct);

                ProgressChanged?.Invoke(this, 30);

                // Method 2: Try to remove Google account database (requires root)
                LogMessage?.Invoke(this, "Method 2: Attempting to clear account data...");
                await RunAdbCommandAsync(
                    "shell rm -rf /data/system/users/0/accounts.db*",
                    TimeSpan.FromSeconds(10), ct);

                await RunAdbCommandAsync(
                    "shell rm -rf /data/data/com.google.android.gms/databases/gservices.db*",
                    TimeSpan.FromSeconds(10), ct);

                ProgressChanged?.Invoke(this, 60);

                // Method 3: Try to bypass via am commands
                LogMessage?.Invoke(this, "Method 3: Starting alternative launcher...");
                await RunAdbCommandAsync(
                    "shell am start -n com.android.settings/.Settings",
                    TimeSpan.FromSeconds(10), ct);

                ProgressChanged?.Invoke(this, 80);

                // Restart device
                LogMessage?.Invoke(this, "Restarting device...");
                await RunAdbCommandAsync("reboot", TimeSpan.FromSeconds(5), ct);

                ProgressChanged?.Invoke(this, 100);
                LogMessage?.Invoke(this, "Bypass attempt completed. Device will restart.");

                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"ADB bypass error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> BypassViaFastbootAsync(string frpImagePath = "",
            CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Attempting FRP bypass via Fastboot...");
                LogMessage?.Invoke(this, "Device must be in Fastboot/Bootloader mode.");

                // Check if we have a custom FRP image or should create blank one
                string imageToFlash;

                if (!string.IsNullOrEmpty(frpImagePath) && File.Exists(frpImagePath))
                {
                    imageToFlash = frpImagePath;
                    LogMessage?.Invoke(this, $"Using provided FRP image: {frpImagePath}");
                }
                else
                {
                    // Create blank FRP image
                    imageToFlash = Path.Combine(Path.GetTempPath(), "blank_frp.img");
                    await CreateBlankFrpImageAsync(imageToFlash, ct);
                    LogMessage?.Invoke(this, "Created blank FRP image");
                }

                ProgressChanged?.Invoke(this, 20);

                // Try to flash FRP partition with different names
                bool success = false;
                foreach (var partName in new[] { "frp", "FRP", "persistent", "config" })
                {
                    LogMessage?.Invoke(this, $"Trying to flash {partName} partition...");

                    var result = await RunFastbootCommandAsync(
                        $"flash {partName} \"{imageToFlash}\"",
                        TimeSpan.FromMinutes(2), ct);

                    if (result.Contains("OKAY") || result.Contains("success"))
                    {
                        LogMessage?.Invoke(this, $"Successfully flashed {partName}");
                        success = true;
                        break;
                    }

                    ProgressChanged?.Invoke(this, 20 + (Array.IndexOf(new[] { "frp", "FRP", "persistent", "config" }, partName) * 15));
                }

                // Clean up temp file
                if (imageToFlash.Contains(Path.GetTempPath()) && File.Exists(imageToFlash))
                {
                    File.Delete(imageToFlash);
                }

                ProgressChanged?.Invoke(this, 80);

                if (success)
                {
                    // Reboot device
                    LogMessage?.Invoke(this, "Rebooting device...");
                    await RunFastbootCommandAsync("reboot", TimeSpan.FromSeconds(10), ct);
                    ProgressChanged?.Invoke(this, 100);
                    LogMessage?.Invoke(this, "FRP partition cleared. Device will restart.");
                }
                else
                {
                    LogMessage?.Invoke(this, "Could not find FRP partition to flash.");
                }

                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Fastboot bypass error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EraseDataPartitionAsync(CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "WARNING: This will erase ALL user data!");
                LogMessage?.Invoke(this, "Erasing userdata and cache partitions...");

                var result1 = await RunFastbootCommandAsync("erase userdata", TimeSpan.FromMinutes(5), ct);
                ProgressChanged?.Invoke(this, 30);

                var result2 = await RunFastbootCommandAsync("erase cache", TimeSpan.FromMinutes(2), ct);
                ProgressChanged?.Invoke(this, 60);

                // Also try to erase metadata if exists
                await RunFastbootCommandAsync("erase metadata", TimeSpan.FromMinutes(1), ct);
                ProgressChanged?.Invoke(this, 80);

                // Reboot
                await RunFastbootCommandAsync("reboot", TimeSpan.FromSeconds(10), ct);
                ProgressChanged?.Invoke(this, 100);

                LogMessage?.Invoke(this, "Data partitions erased. Device will restart.");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Erase error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> BackupFrpPartitionAsync(string outputPath, CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Backing up FRP partition...");

                // Find FRP partition
                string frpPartition = "";
                foreach (var partName in FrpPartitionNames)
                {
                    var checkResult = await RunAdbCommandAsync(
                        $"shell ls /dev/block/by-name/{partName} 2>/dev/null",
                        TimeSpan.FromSeconds(5), ct);

                    if (!string.IsNullOrEmpty(checkResult) && !checkResult.Contains("No such file"))
                    {
                        frpPartition = partName;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(frpPartition))
                {
                    LogMessage?.Invoke(this, "FRP partition not found");
                    return false;
                }

                LogMessage?.Invoke(this, $"Found FRP partition: {frpPartition}");

                // Dump partition (requires root)
                var tempPath = $"/sdcard/frp_backup_{DateTime.Now:yyyyMMdd_HHmmss}.img";
                await RunAdbCommandAsync(
                    $"shell su -c 'dd if=/dev/block/by-name/{frpPartition} of={tempPath}'",
                    TimeSpan.FromMinutes(2), ct);

                // Pull file
                await RunAdbCommandAsync($"pull {tempPath} \"{outputPath}\"", TimeSpan.FromMinutes(2), ct);

                // Clean up
                await RunAdbCommandAsync($"shell rm {tempPath}", TimeSpan.FromSeconds(10), ct);

                LogMessage?.Invoke(this, $"FRP backup saved to: {outputPath}");
                return File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Backup error: {ex.Message}");
                return false;
            }
        }

        private async Task CreateBlankFrpImageAsync(string outputPath, CancellationToken ct)
        {
            // Create a 512KB blank FRP image (common size)
            var blankData = new byte[512 * 1024];
            await File.WriteAllBytesAsync(outputPath, blankData, ct);
        }

        public async Task<string> GetGoogleAccountInfoAsync(CancellationToken ct = default)
        {
            try
            {
                var result = await RunAdbCommandAsync(
                    "shell dumpsys account 2>/dev/null | grep -i 'Account {'",
                    TimeSpan.FromSeconds(10), ct);

                return result;
            }
            catch
            {
                return "";
            }
        }

        public async Task<bool> BypassViaApkInstallAsync(string apkPath, CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Installing FRP bypass APK...");

                if (!File.Exists(apkPath))
                {
                    LogMessage?.Invoke(this, "APK file not found");
                    return false;
                }

                var result = await RunAdbCommandAsync(
                    $"install -r \"{apkPath}\"",
                    TimeSpan.FromMinutes(2), ct);

                if (result.Contains("Success"))
                {
                    LogMessage?.Invoke(this, "APK installed successfully");

                    // Try to start the bypass app
                    // Common bypass app package names
                    var packageNames = new[]
                    {
                        "com.rootjunky.frpbypass",
                        "com.samsung.android.app.galaxyfinder",
                        "com.oasisfeng.island"
                    };

                    foreach (var pkg in packageNames)
                    {
                        await RunAdbCommandAsync(
                            $"shell am start -n {pkg}/.MainActivity",
                            TimeSpan.FromSeconds(5), ct);
                    }

                    return true;
                }

                LogMessage?.Invoke(this, $"Install failed: {result}");
                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"APK install error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EnableAdbFromRecoveryAsync(CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Note: This method requires TWRP or similar custom recovery.");
                LogMessage?.Invoke(this, "Boot to recovery first, then run this command.");

                // In TWRP, we might be able to access /data
                await RunAdbCommandAsync(
                    "shell mount /data",
                    TimeSpan.FromSeconds(10), ct);

                // Clear FRP lock file if exists
                await RunAdbCommandAsync(
                    "shell rm -f /data/data/com.google.android.gms/databases/gservices.db",
                    TimeSpan.FromSeconds(10), ct);

                await RunAdbCommandAsync(
                    "shell rm -f /data/system/users/0/accounts.db*",
                    TimeSpan.FromSeconds(10), ct);

                await RunAdbCommandAsync(
                    "shell rm -f /data/misc/persistent_data/partition.key",
                    TimeSpan.FromSeconds(10), ct);

                LogMessage?.Invoke(this, "Cleared FRP-related files from recovery.");
                LogMessage?.Invoke(this, "Wipe data/factory reset and reboot.");

                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Recovery bypass error: {ex.Message}");
                return false;
            }
        }

        public List<string> GetBypassMethods()
        {
            return new List<string>
            {
                "ADB Method - Requires USB debugging enabled",
                "Fastboot Method - Flash blank FRP partition",
                "APK Method - Install bypass APK",
                "Recovery Method - Clear via TWRP/custom recovery",
                "Erase Data - Factory reset via fastboot"
            };
        }

        private async Task<string> RunAdbCommandAsync(string arguments, TimeSpan timeout,
            CancellationToken ct = default)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return "";

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);

                var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
                var error = await process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);

                return string.IsNullOrEmpty(error) ? output : $"{output}\n{error}";
            }
            catch
            {
                return "";
            }
        }

        private async Task<string> RunFastbootCommandAsync(string arguments, TimeSpan timeout,
            CancellationToken ct = default)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _fastbootPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return "";

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);

                var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
                var error = await process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);

                // Fastboot sends normal output to stderr
                return $"{output}\n{error}";
            }
            catch
            {
                return "";
            }
        }
    }
}
