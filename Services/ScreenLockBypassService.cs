using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    /// <summary>
    /// Screen Lock Bypass Service
    /// </summary>
    public class ScreenLockBypassService
    {
        private readonly AdbService _adbService;
        private readonly string _toolsPath;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        public ScreenLockBypassService(AdbService adbService)
        {
            _adbService = adbService;
            _toolsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhoneRomFlashTool", "Tools");
        }

        #region Lock Detection
        public async Task<LockInfo> DetectLockTypeAsync(string serial, CancellationToken ct = default)
        {
            var lockInfo = new LockInfo();

            try
            {
                Log("Detecting lock type...");

                // Check lock screen type
                var lockType = await _adbService.ExecuteAdbCommandAsync(serial, "shell settings get secure lockscreen.password_type");

                if (!string.IsNullOrEmpty(lockType?.Trim()))
                {
                    lockInfo.PasswordType = ParsePasswordType(lockType.Trim());
                }

                // Check device encryption
                var encryption = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.crypto.state");
                lockInfo.IsEncrypted = encryption?.Trim() == "encrypted";

                // Check if device is locked
                var powerState = await _adbService.ExecuteAdbCommandAsync(serial, "shell dumpsys power | grep mHoldingDisplaySuspendBlocker");
                lockInfo.IsScreenOn = powerState?.Contains("true") == true;

                // Check Samsung Knox
                var knox = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.boot.warranty_bit");
                lockInfo.HasKnox = !string.IsNullOrEmpty(knox?.Trim());

                Log($"Lock Type: {lockInfo.PasswordType}, Encrypted: {lockInfo.IsEncrypted}");
            }
            catch (Exception ex)
            {
                Log($"Error detecting lock: {ex.Message}");
            }

            return lockInfo;
        }

        private PasswordType ParsePasswordType(string type)
        {
            return type switch
            {
                "65536" => PasswordType.None,
                "131072" => PasswordType.Pattern,
                "262144" => PasswordType.Pin,
                "327680" or "393216" => PasswordType.Password,
                _ => PasswordType.Unknown
            };
        }
        #endregion

        #region Bypass Methods
        public async Task<bool> BypassViaAdbAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                Log("Attempting ADB bypass...");
                ProgressChanged?.Invoke(this, 10);

                // Method 1: Delete lock files (requires root or debug access)
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm /data/system/gesture.key");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm /data/system/password.key");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm /data/system/locksettings.db");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm /data/system/locksettings.db-wal");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm /data/system/locksettings.db-shm");

                ProgressChanged?.Invoke(this, 50);

                // Method 2: Reset lock via settings (Android 4.4+)
                await _adbService.ExecuteAdbCommandAsync(serial, "shell settings put secure lockscreen.password_type 65536");

                ProgressChanged?.Invoke(this, 80);

                // Reboot
                await _adbService.ExecuteAdbCommandAsync(serial, "reboot");

                ProgressChanged?.Invoke(this, 100);
                Log("ADB bypass completed - device rebooting");
                return true;
            }
            catch (Exception ex)
            {
                Log($"ADB bypass failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> BypassViaRecoveryAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                Log("Attempting Recovery bypass...");
                ProgressChanged?.Invoke(this, 10);

                // Boot to recovery
                await _adbService.ExecuteAdbCommandAsync(serial, "reboot recovery");
                await Task.Delay(15000, ct);

                ProgressChanged?.Invoke(this, 30);

                // In TWRP/custom recovery - delete lock files
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm -f /data/system/*.key");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm -f /data/system/locksettings.db*");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm -f /data/system/gatekeeper*.key");

                ProgressChanged?.Invoke(this, 70);

                // Also clear password from database if accessible
                await _adbService.ExecuteAdbCommandAsync(serial, "shell sqlite3 /data/system/locksettings.db \"DELETE FROM locksettings WHERE name='lock_pattern_autolock'\"");

                // Reboot to system
                await _adbService.ExecuteAdbCommandAsync(serial, "reboot");

                ProgressChanged?.Invoke(this, 100);
                Log("Recovery bypass completed");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Recovery bypass failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> BypassViaRootAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                Log("Attempting Root bypass...");
                ProgressChanged?.Invoke(this, 10);

                // Check root access
                var rootCheck = await _adbService.ExecuteAdbCommandAsync(serial, "shell su -c id");
                if (rootCheck?.Contains("uid=0") != true)
                {
                    Log("Root access required");
                    return false;
                }

                ProgressChanged?.Invoke(this, 30);

                // Remove lock files with root
                await _adbService.ExecuteAdbCommandAsync(serial, "shell su -c 'rm -f /data/system/*.key'");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell su -c 'rm -f /data/system/locksettings.db*'");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell su -c 'rm -f /data/system/gatekeeper*.key'");

                ProgressChanged?.Invoke(this, 70);

                // Kill system UI to force lock refresh
                await _adbService.ExecuteAdbCommandAsync(serial, "shell su -c 'am force-stop com.android.systemui'");

                ProgressChanged?.Invoke(this, 100);
                Log("Root bypass completed - try unlocking now");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Root bypass failed: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region Samsung Specific
        public async Task<bool> BypassSamsungFrpAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                Log("Attempting Samsung FRP bypass...");
                ProgressChanged?.Invoke(this, 10);

                // Disable FRP via ADB
                await _adbService.ExecuteAdbCommandAsync(serial, "shell content insert --uri content://settings/secure --bind name:s:user_setup_complete --bind value:s:1");
                ProgressChanged?.Invoke(this, 30);

                await _adbService.ExecuteAdbCommandAsync(serial, "shell am start -n com.google.android.gsf.login/");
                ProgressChanged?.Invoke(this, 50);

                await _adbService.ExecuteAdbCommandAsync(serial, "shell am broadcast -a android.intent.action.MASTER_CLEAR");
                ProgressChanged?.Invoke(this, 70);

                // Clear Google account data
                await _adbService.ExecuteAdbCommandAsync(serial, "shell pm disable-user --user 0 com.google.android.gsf");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell pm disable-user --user 0 com.google.android.gms");

                ProgressChanged?.Invoke(this, 100);
                Log("Samsung FRP bypass attempted");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Samsung FRP bypass failed: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region Generic FRP Bypass
        public async Task<bool> BypassGenericFrpAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                Log("Attempting generic FRP bypass...");
                ProgressChanged?.Invoke(this, 10);

                // Skip setup wizard
                await _adbService.ExecuteAdbCommandAsync(serial, "shell settings put global device_provisioned 1");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell settings put secure user_setup_complete 1");

                ProgressChanged?.Invoke(this, 30);

                // Disable FRP packages
                var frpPackages = new[]
                {
                    "com.google.android.setupwizard",
                    "com.google.android.gms",
                    "com.google.android.gsf",
                    "com.google.android.gsf.login"
                };

                int step = 50 / frpPackages.Length;
                int progress = 30;

                foreach (var pkg in frpPackages)
                {
                    await _adbService.ExecuteAdbCommandAsync(serial, $"shell pm disable-user --user 0 {pkg}");
                    progress += step;
                    ProgressChanged?.Invoke(this, progress);
                }

                // Delete FRP data
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm -rf /data/data/com.google.android.gsf");

                ProgressChanged?.Invoke(this, 100);
                Log("Generic FRP bypass completed");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Generic FRP bypass failed: {ex.Message}");
                return false;
            }
        }
        #endregion

        private void Log(string message) => LogMessage?.Invoke(this, message);
    }

    #region Models
    public class LockInfo
    {
        public PasswordType PasswordType { get; set; } = PasswordType.Unknown;
        public bool IsEncrypted { get; set; }
        public bool IsScreenOn { get; set; }
        public bool HasKnox { get; set; }
        public bool HasFrp { get; set; }
    }

    public enum PasswordType
    {
        Unknown,
        None,
        Pattern,
        Pin,
        Password,
        Fingerprint,
        Face
    }
    #endregion
}
