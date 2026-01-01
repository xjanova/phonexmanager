using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    /// <summary>
    /// IMEI/MEID/Serial Number Tool Service
    /// </summary>
    public class ImeiToolService
    {
        private readonly AdbService _adbService;
        private readonly string _backupPath;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        public ImeiToolService(AdbService adbService)
        {
            _adbService = adbService;
            _backupPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PhoneRomFlashTool", "IMEI_Backups");
            Directory.CreateDirectory(_backupPath);
        }

        #region Read IMEI/MEID
        public async Task<DeviceIdentifiers> ReadIdentifiersAsync(string serial, CancellationToken ct = default)
        {
            var identifiers = new DeviceIdentifiers();

            try
            {
                Log("Reading device identifiers...");
                ProgressChanged?.Invoke(this, 10);

                // Read IMEI via service call
                identifiers.Imei1 = await ReadImeiViaServiceCallAsync(serial, 0);
                identifiers.Imei2 = await ReadImeiViaServiceCallAsync(serial, 1);
                ProgressChanged?.Invoke(this, 30);

                // Fallback: Read via getprop
                if (string.IsNullOrEmpty(identifiers.Imei1))
                {
                    var imeiProp = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop persist.radio.imei");
                    identifiers.Imei1 = imeiProp?.Trim() ?? "";
                }
                ProgressChanged?.Invoke(this, 50);

                // Read MEID (CDMA)
                identifiers.Meid = await ReadMeidAsync(serial);

                // Read Serial Number
                var serialNo = await _adbService.ExecuteAdbCommandAsync(serial, "shell getprop ro.serialno");
                identifiers.SerialNumber = serialNo?.Trim() ?? "";

                // Read WiFi MAC
                var wifiMac = await _adbService.ExecuteAdbCommandAsync(serial, "shell cat /sys/class/net/wlan0/address 2>/dev/null");
                identifiers.WifiMac = wifiMac?.Trim() ?? "";

                // Read Bluetooth MAC
                var btMac = await _adbService.ExecuteAdbCommandAsync(serial, "shell settings get secure bluetooth_address");
                identifiers.BluetoothMac = btMac?.Trim() ?? "";

                ProgressChanged?.Invoke(this, 80);

                // Validate IMEIs
                identifiers.Imei1Valid = ValidateImei(identifiers.Imei1);
                identifiers.Imei2Valid = ValidateImei(identifiers.Imei2);

                ProgressChanged?.Invoke(this, 100);

                Log($"IMEI1: {identifiers.Imei1} ({(identifiers.Imei1Valid ? "Valid" : "Invalid")})");
                if (!string.IsNullOrEmpty(identifiers.Imei2))
                    Log($"IMEI2: {identifiers.Imei2}");
            }
            catch (Exception ex)
            {
                Log($"Error reading identifiers: {ex.Message}");
            }

            return identifiers;
        }

        private async Task<string> ReadImeiViaServiceCallAsync(string serial, int slot)
        {
            try
            {
                string result;

                if (slot == 0)
                {
                    result = await _adbService.ExecuteAdbCommandAsync(serial, "shell service call iphonesubinfo 1");
                }
                else
                {
                    result = await _adbService.ExecuteAdbCommandAsync(serial, $"shell service call iphonesubinfo 3 i32 {slot}");
                }

                return ParseServiceCallResult(result);
            }
            catch
            {
                return "";
            }
        }

        private async Task<string> ReadMeidAsync(string serial)
        {
            try
            {
                var result = await _adbService.ExecuteAdbCommandAsync(serial, "shell service call iphonesubinfo 6");
                return ParseServiceCallResult(result);
            }
            catch
            {
                return "";
            }
        }

        private string ParseServiceCallResult(string? result)
        {
            if (string.IsNullOrEmpty(result)) return "";

            var sb = new StringBuilder();
            var matches = Regex.Matches(result, @"'([^']*)'");

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var part = match.Groups[1].Value.Replace(".", "").Trim();
                    sb.Append(part);
                }
            }

            return Regex.Replace(sb.ToString(), @"[^\d]", "");
        }
        #endregion

        #region IMEI Validation
        public bool ValidateImei(string? imei)
        {
            if (string.IsNullOrEmpty(imei) || imei.Length < 14 || imei.Length > 16)
                return false;

            return ValidateLuhn(imei.Substring(0, 14));
        }

        private bool ValidateLuhn(string number)
        {
            int sum = 0;
            bool alternate = false;

            for (int i = number.Length - 1; i >= 0; i--)
            {
                if (!char.IsDigit(number[i])) return false;

                int n = number[i] - '0';
                if (alternate)
                {
                    n *= 2;
                    if (n > 9) n -= 9;
                }
                sum += n;
                alternate = !alternate;
            }

            return sum % 10 == 0;
        }

        public string CalculateCheckDigit(string imei14)
        {
            if (imei14.Length != 14) return "";

            int sum = 0;
            for (int i = 0; i < 14; i++)
            {
                int digit = imei14[i] - '0';
                if (i % 2 == 1)
                {
                    digit *= 2;
                    if (digit > 9) digit -= 9;
                }
                sum += digit;
            }

            return ((10 - (sum % 10)) % 10).ToString();
        }
        #endregion

        #region EFS Backup
        public async Task<bool> BackupEfsAsync(string serial, CancellationToken ct = default)
        {
            try
            {
                Log("Backing up EFS...");
                ProgressChanged?.Invoke(this, 10);

                var rootCheck = await _adbService.ExecuteAdbCommandAsync(serial, "shell su -c id");
                if (rootCheck?.Contains("uid=0") != true)
                {
                    Log("Root access required for EFS backup");
                    return false;
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupDir = Path.Combine(_backupPath, $"{serial}_{timestamp}");
                Directory.CreateDirectory(backupDir);

                ProgressChanged?.Invoke(this, 30);

                // Backup EFS partition
                await _adbService.ExecuteAdbCommandAsync(serial, "shell su -c 'dd if=/dev/block/bootdevice/by-name/efs of=/sdcard/efs.img'");
                await _adbService.ExecuteAdbCommandAsync(serial, $"pull /sdcard/efs.img \"{Path.Combine(backupDir, "efs.img")}\"");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm /sdcard/efs.img");

                ProgressChanged?.Invoke(this, 70);

                // Save identifiers
                var ids = await ReadIdentifiersAsync(serial, ct);
                var infoPath = Path.Combine(backupDir, "identifiers.txt");
                await File.WriteAllTextAsync(infoPath,
                    $"IMEI1: {ids.Imei1}\nIMEI2: {ids.Imei2}\nMEID: {ids.Meid}\nSerial: {ids.SerialNumber}\nBackup: {DateTime.Now}",
                    ct);

                ProgressChanged?.Invoke(this, 100);
                Log($"EFS backup saved to: {backupDir}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error backing up EFS: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RestoreEfsAsync(string serial, string backupDir, CancellationToken ct = default)
        {
            try
            {
                Log($"Restoring EFS from: {backupDir}");

                var efsPath = Path.Combine(backupDir, "efs.img");
                if (!File.Exists(efsPath))
                {
                    Log("EFS backup file not found");
                    return false;
                }

                var rootCheck = await _adbService.ExecuteAdbCommandAsync(serial, "shell su -c id");
                if (rootCheck?.Contains("uid=0") != true)
                {
                    Log("Root access required for EFS restore");
                    return false;
                }

                ProgressChanged?.Invoke(this, 30);

                await _adbService.ExecuteAdbCommandAsync(serial, $"push \"{efsPath}\" /sdcard/efs.img");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell su -c 'dd if=/sdcard/efs.img of=/dev/block/bootdevice/by-name/efs'");
                await _adbService.ExecuteAdbCommandAsync(serial, "shell rm /sdcard/efs.img");

                ProgressChanged?.Invoke(this, 100);
                Log("EFS restore completed - reboot required");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error restoring EFS: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region TAC Info
        public TacInfo GetTacInfo(string imei)
        {
            var tac = imei.Length >= 8 ? imei.Substring(0, 8) : "";

            var tacDb = new Dictionary<string, (string Brand, string Model)>
            {
                { "35332509", ("Samsung", "Galaxy S21") },
                { "35274011", ("Samsung", "Galaxy S20") },
                { "86754100", ("Xiaomi", "Redmi Note") },
                { "35390611", ("Apple", "iPhone 12") },
                { "35980510", ("Huawei", "P40") },
            };

            if (tacDb.TryGetValue(tac, out var info))
            {
                return new TacInfo { Tac = tac, Brand = info.Brand, Model = info.Model, IsKnown = true };
            }

            return new TacInfo { Tac = tac, IsKnown = false };
        }
        #endregion

        private void Log(string message) => LogMessage?.Invoke(this, message);
    }

    #region Models
    public class DeviceIdentifiers
    {
        public string Imei1 { get; set; } = "";
        public string Imei2 { get; set; } = "";
        public string Meid { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string WifiMac { get; set; } = "";
        public string BluetoothMac { get; set; } = "";
        public bool Imei1Valid { get; set; }
        public bool Imei2Valid { get; set; }
    }

    public class TacInfo
    {
        public string Tac { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Model { get; set; } = "";
        public bool IsKnown { get; set; }
    }
    #endregion
}
