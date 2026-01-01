using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    /// <summary>
    /// Xiaomi Mi Account and Bootloader Unlock Service
    /// </summary>
    public class XiaomiAuthService
    {
        private readonly AdbService _adbService;
        private readonly string _toolsPath;
        private readonly HttpClient _httpClient;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        public XiaomiAuthService(AdbService adbService)
        {
            _adbService = adbService;
            _toolsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhoneRomFlashTool", "Tools");
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "XiaomiFlashTool/1.0");
        }

        #region Mi Account Status
        public async Task<MiAccountStatus> CheckMiAccountStatusAsync(string serial, CancellationToken ct = default)
        {
            var status = new MiAccountStatus();

            try
            {
                Log("Checking Mi Account status...");

                // Check via getprop
                var miAccountProp = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.miui.cust_variant");
                status.CustVariant = miAccountProp?.Trim() ?? "";

                // Check FRP status
                var frpStatus = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.frp.pst");
                status.HasFrp = !string.IsNullOrEmpty(frpStatus?.Trim());

                // Check account sync status
                var accountCheck = await _adbService.ExecuteAdbCommandAsync(serial, "shell dumpsys account 2>/dev/null | grep -i xiaomi");
                status.HasMiAccount = !string.IsNullOrEmpty(accountCheck?.Trim());

                // Check cloud backup status
                var cloudStatus = await _adbService.ExecuteAdbCommandAsync(serial, "shell settings get secure xiaomi_finddevice_status 2>/dev/null");
                status.FindDeviceEnabled = cloudStatus?.Trim() == "1";

                // Check activation lock
                var lockStatus = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop persist.sys.device_provisioned");
                status.IsProvisioned = lockStatus?.Trim() == "1";

                status.IsChecked = true;
                Log($"Mi Account Status: {(status.HasMiAccount ? "Logged In" : "Not Logged In")}");
            }
            catch (Exception ex)
            {
                Log($"Error checking Mi Account: {ex.Message}");
            }

            return status;
        }
        #endregion

        #region Bootloader Unlock
        public async Task<XiaomiBootloaderStatus> CheckBootloaderStatusAsync(string serial, CancellationToken ct = default)
        {
            var status = new XiaomiBootloaderStatus();

            try
            {
                Log("Checking bootloader status...");

                // Check via fastboot if available
                var fbUnlock = await _adbService.ExecuteFastbootCommandAsync(serial, "getvar unlocked");
                status.IsUnlocked = fbUnlock?.Contains("yes") == true;

                // Get MIUI version
                var miuiVersion = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.miui.ui.version.name");
                status.MiuiVersion = miuiVersion?.Trim() ?? "";

                // Get unlock status from prop
                var unlockProp = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop sys.oem_unlock_allowed");
                status.OemUnlockAllowed = unlockProp?.Trim() == "1";

                // Check if device is Xiaomi
                var brand = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.product.brand");
                status.IsXiaomiDevice = brand?.Trim()?.ToLowerInvariant() switch
                {
                    "xiaomi" or "redmi" or "poco" => true,
                    _ => false
                };

                status.IsChecked = true;
                Log($"Bootloader: {(status.IsUnlocked ? "Unlocked" : "Locked")}");
            }
            catch (Exception ex)
            {
                Log($"Error checking bootloader: {ex.Message}");
            }

            return status;
        }

        public async Task<XiaomiDeviceInfo> GetDeviceInfoAsync(string serial, CancellationToken ct = default)
        {
            var info = new XiaomiDeviceInfo();

            try
            {
                Log("Getting Xiaomi device info...");

                info.Model = (await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.product.model"))?.Trim() ?? "";
                info.Device = (await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.product.device"))?.Trim() ?? "";
                info.Brand = (await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.product.brand"))?.Trim() ?? "";
                info.MiuiVersion = (await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.miui.ui.version.name"))?.Trim() ?? "";
                info.AndroidVersion = (await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.build.version.release"))?.Trim() ?? "";
                info.BuildNumber = (await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.build.display.id"))?.Trim() ?? "";
                info.SerialNumber = serial;

                Log($"Device: {info.Model} ({info.Device}) MIUI {info.MiuiVersion}");
            }
            catch (Exception ex)
            {
                Log($"Error getting device info: {ex.Message}");
            }

            return info;
        }
        #endregion

        #region Mi Account Bypass Methods
        public async Task<bool> TryRemoveMiAccountAdbAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                Log("Attempting Mi Account removal via ADB...");
                ProgressChanged?.Invoke(this, 10);

                // Clear Mi Account data
                await _adbService.ExecuteAdbCommandAsync(serial, "shell pm clear com.xiaomi.account");
                ProgressChanged?.Invoke(this, 30);

                // Clear cloud service
                await _adbService.ExecuteAdbCommandAsync(serial, "shell pm clear com.miui.cloudservice");
                ProgressChanged?.Invoke(this, 50);

                // Clear find device
                await _adbService.ExecuteAdbCommandAsync(serial, "shell pm clear com.xiaomi.finddevice");
                ProgressChanged?.Invoke(this, 70);

                // Disable Mi Account
                await _adbService.ExecuteAdbCommandAsync(serial, "shell pm disable-user com.xiaomi.account");
                ProgressChanged?.Invoke(this, 90);

                // Reboot
                await _adbService.ExecuteAdbCommandAsync(serial, "reboot");
                ProgressChanged?.Invoke(this, 100);

                Log("Mi Account removal attempted - device rebooting");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error removing Mi Account: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> BypassMiAccountViaRecoveryAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                Log("Attempting bypass via Recovery...");

                // Boot to recovery
                await _adbService.ExecuteAdbCommandAsync(serial, "reboot recovery");
                await Task.Delay(10000, ct);

                // In TWRP/custom recovery
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm -rf /data/system/users/0/accounts.db*");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm -rf /data/system/users/0/settings_*.xml");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm -rf /data/misc/keystore/*");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm -rf /data/data/com.xiaomi.account");

                await _adbService.ExecuteAdbCommandAsync(serial, "reboot");

                Log("Bypass via Recovery completed");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error during recovery bypass: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region Utility Methods
        public async Task<bool> DisableFindDeviceAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                Log("Disabling Find Device...");

                await _adbService.ExecuteAdbCommandAsync(serial, "shell settings put secure xiaomi_finddevice_status 0");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell pm disable-user --user 0 com.xiaomi.finddevice");

                Log("Find Device disabled");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error disabling Find Device: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EnableOemUnlockAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                Log("Enabling OEM Unlock...");

                await _adbService.ExecuteAdbCommandAsync(serial, "shell settings put global oem_unlock_allowed 1");

                var verify = await _adbService.ExecuteAdbCommandAsync(serial, "shell settings get global oem_unlock_allowed");

                if (verify?.Trim() == "1")
                {
                    Log("OEM Unlock enabled");
                    return true;
                }

                Log("OEM Unlock may require Developer Options");
                return false;
            }
            catch (Exception ex)
            {
                Log($"Error enabling OEM Unlock: {ex.Message}");
                return false;
            }
        }
        #endregion

        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }
    }

    #region Models
    public class MiAccountStatus
    {
        public bool IsChecked { get; set; }
        public bool HasMiAccount { get; set; }
        public bool HasFrp { get; set; }
        public bool FindDeviceEnabled { get; set; }
        public bool IsProvisioned { get; set; }
        public string CustVariant { get; set; } = "";
    }

    public class XiaomiBootloaderStatus
    {
        public bool IsChecked { get; set; }
        public bool IsUnlocked { get; set; }
        public bool OemUnlockAllowed { get; set; }
        public bool IsXiaomiDevice { get; set; }
        public string MiuiVersion { get; set; } = "";
    }

    public class XiaomiDeviceInfo
    {
        public string Model { get; set; } = "";
        public string Device { get; set; } = "";
        public string Brand { get; set; } = "";
        public string MiuiVersion { get; set; } = "";
        public string AndroidVersion { get; set; } = "";
        public string BuildNumber { get; set; } = "";
        public string SerialNumber { get; set; } = "";
    }
    #endregion
}
