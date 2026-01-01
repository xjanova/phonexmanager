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
    /// Network/Carrier Unlock Service
    /// </summary>
    public class NetworkUnlockService
    {
        private readonly AdbService _adbService;
        private readonly string _toolsPath;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        public NetworkUnlockService(AdbService adbService)
        {
            _adbService = adbService;
            _toolsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhoneRomFlashTool", "Tools");
        }

        #region Network Lock Detection
        public async Task<NetworkLockStatus> CheckNetworkLockAsync(string serial, CancellationToken ct = default)
        {
            var status = new NetworkLockStatus();

            try
            {
                Log("Checking network lock status...");
                ProgressChanged?.Invoke(this, 10);

                // Get SIM status
                var simState = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop gsm.sim.state");
                status.SimState = simState?.Trim() ?? "";
                ProgressChanged?.Invoke(this, 20);

                // Check network lock
                var networkLock = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop gsm.sim.operator.alpha");
                status.CurrentOperator = networkLock?.Trim() ?? "";

                // Check if locked
                var lockStatus = await _adbService.ExecuteAdbCommandAsync(serial, "shell service call iphonesubinfo 15");
                status.IsLocked = !string.IsNullOrEmpty(lockStatus) && lockStatus.Contains("Parcel");

                ProgressChanged?.Invoke(this, 50);

                // Get carrier info
                var carrier = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.carrier");
                status.OriginalCarrier = carrier?.Trim() ?? "";

                // Check Samsung specific lock
                var samsungLock = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ril.simlock");
                if (!string.IsNullOrEmpty(samsungLock?.Trim()))
                {
                    status.IsSamsungDevice = true;
                    status.SamsungLockType = samsungLock.Trim();
                }

                ProgressChanged?.Invoke(this, 80);

                // Get ICCID
                var iccid = await _adbService.ExecuteAdbCommandAsync(serial, "shell service call iphonesubinfo 12");
                status.Iccid = ParseServiceCallResult(iccid);

                ProgressChanged?.Invoke(this, 100);

                Log($"Network Lock: {(status.IsLocked ? "Locked" : "Unlocked")}, Carrier: {status.CurrentOperator}");
            }
            catch (Exception ex)
            {
                Log($"Error checking network lock: {ex.Message}");
            }

            return status;
        }

        private string ParseServiceCallResult(string? result)
        {
            if (string.IsNullOrEmpty(result)) return "";

            var matches = Regex.Matches(result, @"'([^']*)'");
            var sb = new System.Text.StringBuilder();

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    sb.Append(match.Groups[1].Value.Replace(".", "").Trim());
                }
            }

            return Regex.Replace(sb.ToString(), @"[^\dA-Fa-f]", "");
        }
        #endregion

        #region Unlock Methods
        public async Task<bool> UnlockWithNckAsync(string serial, string nckCode, CancellationToken ct = default)
        {
            try
            {
                Log($"Attempting NCK unlock...");
                ProgressChanged?.Invoke(this, 10);

                // Try different unlock methods
                // Method 1: Direct AT command
                var result = await _adbService.ExecuteAdbCommandAsync(serial, $"shell service call phone 25 i32 0 s16 \"{nckCode}\"");
                ProgressChanged?.Invoke(this, 50);

                if (result?.Contains("success") == true || result?.Contains("0 unlock") == true)
                {
                    Log("NCK unlock successful");
                    ProgressChanged?.Invoke(this, 100);
                    return true;
                }

                // Method 2: Settings command
                result = await _adbService.ExecuteAdbCommandAsync(serial, $"shell am broadcast -a android.provider.Telephony.SECRET_CODE -d android_secret_code://{nckCode}");
                ProgressChanged?.Invoke(this, 80);

                Log("NCK code applied - check device");
                ProgressChanged?.Invoke(this, 100);
                return true;
            }
            catch (Exception ex)
            {
                Log($"NCK unlock failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UnlockSamsungAsync(string serial, string unlockCode, CancellationToken ct = default)
        {
            try
            {
                Log("Attempting Samsung network unlock...");
                ProgressChanged?.Invoke(this, 10);

                // Samsung specific unlock via service menu
                await _adbService.ExecuteAdbCommandAsync(serial, "shell am start -a android.intent.action.DIAL -d tel:*#0011#");
                await Task.Delay(2000, ct);

                ProgressChanged?.Invoke(this, 30);

                // Try direct unlock
                var result = await _adbService.ExecuteAdbCommandAsync(serial, $"shell service call phone 37 i32 1 s16 \"{unlockCode}\"");

                ProgressChanged?.Invoke(this, 70);

                if (result?.Contains("success") == true)
                {
                    Log("Samsung unlock successful");
                    return true;
                }

                // Alternative: Enter code via input
                await _adbService.ExecuteAdbCommandAsync(serial, $"shell input text {unlockCode}");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell input keyevent KEYCODE_ENTER");

                ProgressChanged?.Invoke(this, 100);
                Log("Unlock code entered - verify on device");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Samsung unlock failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CheckUnlockEligibilityAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                Log("Checking unlock eligibility...");

                // Check remaining unlock attempts
                var attempts = await _adbService.ExecuteAdbCommandAsync(serial, "shell service call phone 26");

                if (attempts?.Contains("0") == true)
                {
                    Log("Warning: No unlock attempts remaining!");
                    return false;
                }

                // Check SIM status
                var simState = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop gsm.sim.state");
                if (simState?.Contains("NETWORK_LOCKED") == true)
                {
                    Log("Device is network locked - eligible for unlock");
                    return true;
                }

                if (simState?.Contains("READY") == true)
                {
                    Log("SIM is ready - device may already be unlocked");
                    return true;
                }

                Log($"SIM State: {simState?.Trim()}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error checking eligibility: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region APN Settings
        public async Task<bool> ResetApnSettingsAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                Log("Resetting APN settings...");

                await _adbService.ExecuteAdbCommandAsync(serial, "shell content delete --uri content://telephony/carriers");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell content delete --uri content://telephony/carriers/preferapn");

                // Trigger APN reset
                await _adbService.ExecuteAdbCommandAsync(serial, "shell am broadcast -a android.intent.action.APN_CHANGED");

                Log("APN settings reset - restart may be required");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error resetting APN: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AddApnAsync(string serial, ApnSettings apn, CancellationToken ct = default)
        {
            try
            {
                Log($"Adding APN: {apn.Name}");

                var cmd = $"shell content insert --uri content://telephony/carriers " +
                    $"--bind name:s:{apn.Name} " +
                    $"--bind apn:s:{apn.Apn} " +
                    $"--bind proxy:s:{apn.Proxy} " +
                    $"--bind port:s:{apn.Port} " +
                    $"--bind user:s:{apn.Username} " +
                    $"--bind password:s:{apn.Password} " +
                    $"--bind mcc:s:{apn.Mcc} " +
                    $"--bind mnc:s:{apn.Mnc} " +
                    $"--bind type:s:{apn.Type}";

                await _adbService.ExecuteAdbCommandAsync(serial, cmd);

                Log("APN added successfully");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error adding APN: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region Common Carrier Codes
        public Dictionary<string, string> GetCommonCarrierCodes()
        {
            return new Dictionary<string, string>
            {
                { "AT&T", "000000" },
                { "T-Mobile", "111111" },
                { "Verizon", "000000" },
                { "Sprint", "000000" },
                { "Generic", "12345678" }
            };
        }
        #endregion

        private void Log(string message) => LogMessage?.Invoke(this, message);
    }

    #region Models
    public class NetworkLockStatus
    {
        public bool IsLocked { get; set; }
        public string SimState { get; set; } = "";
        public string CurrentOperator { get; set; } = "";
        public string OriginalCarrier { get; set; } = "";
        public string Iccid { get; set; } = "";
        public bool IsSamsungDevice { get; set; }
        public string SamsungLockType { get; set; } = "";
        public int RemainingAttempts { get; set; } = -1;
    }

    public class ApnSettings
    {
        public string Name { get; set; } = "";
        public string Apn { get; set; } = "";
        public string Proxy { get; set; } = "";
        public string Port { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Mcc { get; set; } = "";
        public string Mnc { get; set; } = "";
        public string Type { get; set; } = "default,supl";
    }
    #endregion
}
