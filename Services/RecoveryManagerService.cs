using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    /// <summary>
    /// Custom Recovery Manager Service
    /// </summary>
    public class RecoveryManagerService
    {
        private readonly AdbService _adbService;
        private readonly string _toolsPath;
        private readonly string _recoveriesPath;
        private readonly HttpClient _httpClient;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        public RecoveryManagerService(AdbService adbService)
        {
            _adbService = adbService;
            _toolsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhoneRomFlashTool", "Tools");
            _recoveriesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhoneRomFlashTool", "Recoveries");

            Directory.CreateDirectory(_recoveriesPath);

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhoneRomFlashTool/1.0");
        }

        #region Recovery Detection
        public async Task<RecoveryInfo> DetectRecoveryAsync(string serial, CancellationToken ct = default)
        {
            var info = new RecoveryInfo();

            try
            {
                Log("Detecting recovery...");

                // Check if in recovery mode
                var devices = await _adbService.ExecuteAdbCommandAsync(serial, "devices");
                info.IsInRecovery = devices?.Contains("recovery") == true;

                if (info.IsInRecovery)
                {
                    // Try to detect TWRP
                    var twrpCheck = await _adbService.ExecuteAdbCommandAsync(serial, "shell cat /etc/twrp.fstab 2>/dev/null");
                    if (!string.IsNullOrEmpty(twrpCheck))
                    {
                        info.RecoveryType = RecoveryType.TWRP;

                        var version = await _adbService.ExecuteAdbCommandAsync(serial, "shell cat /etc/tw_version 2>/dev/null");
                        info.Version = version?.Trim() ?? "";
                    }

                    // Check OrangeFox
                    var ofCheck = await _adbService.ExecuteAdbCommandAsync(serial, "shell cat /FFiles/OF_version 2>/dev/null");
                    if (!string.IsNullOrEmpty(ofCheck))
                    {
                        info.RecoveryType = RecoveryType.OrangeFox;
                        info.Version = ofCheck.Trim();
                    }
                }
                else
                {
                    // Check recovery partition info from system
                    var recoveryProp = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.build.recovery_fingerprint");
                    info.RecoveryFingerprint = recoveryProp?.Trim() ?? "";
                }

                Log($"Recovery: {info.RecoveryType} {info.Version}");
            }
            catch (Exception ex)
            {
                Log($"Error detecting recovery: {ex.Message}");
            }

            return info;
        }
        #endregion

        #region Flash Recovery
        public async Task<bool> FlashRecoveryAsync(string serial, string recoveryPath, CancellationToken ct = default)
        {
            try
            {
                Log($"Flashing recovery: {Path.GetFileName(recoveryPath)}");
                ProgressChanged?.Invoke(this, 10);

                if (!File.Exists(recoveryPath))
                {
                    Log("Recovery file not found");
                    return false;
                }

                // Check if in fastboot mode
                var fastbootDevices = await _adbService.ExecuteFastbootCommandAsync(serial, "devices");
                bool inFastboot = fastbootDevices?.Contains(serial) == true;

                if (!inFastboot)
                {
                    Log("Rebooting to bootloader...");
                    await _adbService.ExecuteAdbCommandAsync(serial, "reboot bootloader");
                    await Task.Delay(10000, ct);
                }

                ProgressChanged?.Invoke(this, 30);

                // Flash recovery
                Log("Flashing recovery partition...");
                var flashResult = await _adbService.ExecuteFastbootCommandAsync(serial, $"flash recovery \"{recoveryPath}\"");

                ProgressChanged?.Invoke(this, 80);

                if (flashResult?.Contains("OKAY") == true || flashResult?.Contains("Finished") == true)
                {
                    Log("Recovery flashed successfully");
                    ProgressChanged?.Invoke(this, 100);
                    return true;
                }
                else
                {
                    Log($"Flash result: {flashResult}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"Error flashing recovery: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> BootRecoveryAsync(string serial, string recoveryPath, CancellationToken ct = default)
        {
            try
            {
                Log($"Booting recovery temporarily: {Path.GetFileName(recoveryPath)}");
                ProgressChanged?.Invoke(this, 10);

                // Reboot to bootloader
                await _adbService.ExecuteAdbCommandAsync(serial, "reboot bootloader");
                await Task.Delay(8000, ct);

                ProgressChanged?.Invoke(this, 40);

                // Boot recovery without flashing
                var bootResult = await _adbService.ExecuteFastbootCommandAsync(serial, $"boot \"{recoveryPath}\"");

                ProgressChanged?.Invoke(this, 100);

                if (bootResult?.Contains("OKAY") == true)
                {
                    Log("Recovery booted successfully");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Log($"Error booting recovery: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region TWRP Commands
        public async Task<bool> TwrpWipeAsync(string serial, string partition, CancellationToken ct = default)
        {
            try
            {
                Log($"Wiping {partition}...");

                var result = await _adbService.ExecuteAdbCommandAsync(serial, $"shell twrp wipe {partition}");

                Log($"Wipe result: {result?.Trim()}");
                return result?.Contains("Done") == true || result?.Contains("success") == true;
            }
            catch (Exception ex)
            {
                Log($"Error wiping: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> TwrpBackupAsync(string serial, string partitions, string backupName, CancellationToken ct = default)
        {
            try
            {
                Log($"Creating TWRP backup: {backupName}");
                ProgressChanged?.Invoke(this, 10);

                // partitions: S=System, D=Data, C=Cache, B=Boot, R=Recovery
                var result = await _adbService.ExecuteAdbCommandAsync(serial, $"shell twrp backup {partitions} {backupName}");

                ProgressChanged?.Invoke(this, 100);
                Log("Backup completed");
                return result?.Contains("Done") == true;
            }
            catch (Exception ex)
            {
                Log($"Error creating backup: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> TwrpRestoreAsync(string serial, string backupName, CancellationToken ct = default)
        {
            try
            {
                Log($"Restoring TWRP backup: {backupName}");
                ProgressChanged?.Invoke(this, 10);

                var result = await _adbService.ExecuteAdbCommandAsync(serial, $"shell twrp restore {backupName}");

                ProgressChanged?.Invoke(this, 100);
                Log("Restore completed");
                return result?.Contains("Done") == true;
            }
            catch (Exception ex)
            {
                Log($"Error restoring backup: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> TwrpInstallZipAsync(string serial, string zipPath, CancellationToken ct = default)
        {
            try
            {
                Log($"Installing: {Path.GetFileName(zipPath)}");
                ProgressChanged?.Invoke(this, 10);

                // Push zip to device
                var remotePath = $"/sdcard/{Path.GetFileName(zipPath)}";
                await _adbService.ExecuteAdbCommandAsync(serial, $"push \"{zipPath}\" {remotePath}");

                ProgressChanged?.Invoke(this, 50);

                // Install via TWRP
                var result = await _adbService.ExecuteAdbCommandAsync(serial, $"shell twrp install {remotePath}");

                ProgressChanged?.Invoke(this, 90);

                // Cleanup
                await _adbService.ExecuteAdbCommandAsync(serial, $"shell rm {remotePath}");

                ProgressChanged?.Invoke(this, 100);
                Log("Installation completed");
                return result?.Contains("Done") == true || result?.Contains("success") == true;
            }
            catch (Exception ex)
            {
                Log($"Error installing zip: {ex.Message}");
                return false;
            }
        }

        public async Task<List<string>> ListTwrpBackupsAsync(string serial, CancellationToken ct = default)
        {
            var backups = new List<string>();

            try
            {
                var result = await _adbService.ExecuteAdbCommandAsync(serial, "shell ls /data/media/0/TWRP/BACKUPS/*/");

                if (!string.IsNullOrEmpty(result))
                {
                    backups.AddRange(result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(b => b.Trim())
                        .Where(b => !string.IsNullOrEmpty(b)));
                }
            }
            catch (Exception ex)
            {
                Log($"Error listing backups: {ex.Message}");
            }

            return backups;
        }
        #endregion

        #region Reboot Commands
        public async Task RebootToRecoveryAsync(string serial)
        {
            Log("Rebooting to recovery...");
            await _adbService.ExecuteAdbCommandAsync(serial, "reboot recovery");
        }

        public async Task RebootToBootloaderAsync(string serial)
        {
            Log("Rebooting to bootloader...");
            await _adbService.ExecuteAdbCommandAsync(serial, "reboot bootloader");
        }

        public async Task RebootToSystemAsync(string serial)
        {
            Log("Rebooting to system...");
            await _adbService.ExecuteAdbCommandAsync(serial, "reboot");
        }
        #endregion

        private void Log(string message) => LogMessage?.Invoke(this, message);
    }

    #region Models
    public class RecoveryInfo
    {
        public RecoveryType RecoveryType { get; set; } = RecoveryType.Unknown;
        public string Version { get; set; } = "";
        public bool IsInRecovery { get; set; }
        public string RecoveryFingerprint { get; set; } = "";
    }

    public enum RecoveryType
    {
        Unknown,
        Stock,
        TWRP,
        OrangeFox,
        PitchBlack,
        SHRP
    }
    #endregion
}
