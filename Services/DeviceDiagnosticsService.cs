using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    // Bootloader Status
    public class BootloaderStatus
    {
        public bool IsUnlocked { get; set; }
        public string Status { get; set; } = "Unknown";
        public string OemUnlockAllowed { get; set; } = "Unknown";
        public string SecureBoot { get; set; } = "Unknown";
        public string VerifiedBoot { get; set; } = "Unknown";
        public string WarrantyBit { get; set; } = "Unknown";
        public string FrpStatus { get; set; } = "Unknown";
        public string DeviceState { get; set; } = "Unknown";
    }

    // SafetyNet/Play Integrity
    public class IntegrityStatus
    {
        public bool BasicIntegrity { get; set; }
        public bool CtsProfileMatch { get; set; }
        public bool MeetsDeviceIntegrity { get; set; }
        public bool MeetsBasicIntegrity { get; set; }
        public bool MeetsStrongIntegrity { get; set; }
        public string EvaluationType { get; set; } = "Unknown";
        public List<string> FailureReasons { get; set; } = new();
        public DateTime CheckTime { get; set; }
    }

    // Battery Health
    public class BatteryHealth
    {
        public int Level { get; set; }
        public string Status { get; set; } = "Unknown";
        public string Health { get; set; } = "Unknown";
        public string Technology { get; set; } = "Unknown";
        public double Temperature { get; set; }
        public double Voltage { get; set; }
        public int CurrentNow { get; set; }
        public int ChargeCounter { get; set; }
        public int DesignCapacity { get; set; }
        public int CurrentCapacity { get; set; }
        public int CycleCount { get; set; }
        public double HealthPercent => DesignCapacity > 0 ? (CurrentCapacity * 100.0 / DesignCapacity) : 0;
        public bool IsCharging { get; set; }
        public string ChargeType { get; set; } = "None";
    }

    // Sensor Info
    public class SensorInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Vendor { get; set; } = "";
        public double Version { get; set; }
        public double MaxRange { get; set; }
        public double Resolution { get; set; }
        public double Power { get; set; }
        public bool IsAvailable { get; set; }
        public string CurrentValue { get; set; } = "";
    }

    // Network Info
    public class NetworkInfo
    {
        public string ConnectionType { get; set; } = "Unknown";
        public string MobileNetworkType { get; set; } = "Unknown";
        public string Operator { get; set; } = "Unknown";
        public string SimState { get; set; } = "Unknown";
        public string IpAddress { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public string WifiSsid { get; set; } = "";
        public int WifiSignal { get; set; }
        public int MobileSignal { get; set; }
        public bool IsRoaming { get; set; }
        public string Imsi { get; set; } = "";
        public string Iccid { get; set; } = "";
        public List<ApnSetting> ApnSettings { get; set; } = new();
    }

    public class ApnSetting
    {
        public string Name { get; set; } = "";
        public string Apn { get; set; } = "";
        public string Type { get; set; } = "";
        public string Proxy { get; set; } = "";
        public int Port { get; set; }
        public string Mcc { get; set; } = "";
        public string Mnc { get; set; } = "";
        public bool IsDefault { get; set; }
    }

    // Benchmark Results
    public class BenchmarkResult
    {
        public string TestName { get; set; } = "";
        public double Score { get; set; }
        public string Unit { get; set; } = "";
        public TimeSpan Duration { get; set; }
        public string Category { get; set; } = "";
    }

    public class DeviceBenchmark
    {
        public int CpuSingleCore { get; set; }
        public int CpuMultiCore { get; set; }
        public int MemorySpeed { get; set; }
        public int StorageRead { get; set; }
        public int StorageWrite { get; set; }
        public int GpuScore { get; set; }
        public int OverallScore { get; set; }
        public List<BenchmarkResult> DetailedResults { get; set; } = new();
        public DateTime TestTime { get; set; }
    }

    // App Info
    public class AppInfo
    {
        public string PackageName { get; set; } = "";
        public string AppName { get; set; } = "";
        public string Version { get; set; } = "";
        public int VersionCode { get; set; }
        public string InstalledPath { get; set; } = "";
        public long ApkSize { get; set; }
        public long DataSize { get; set; }
        public long CacheSize { get; set; }
        public bool IsSystemApp { get; set; }
        public bool IsEnabled { get; set; }
        public DateTime InstallTime { get; set; }
        public DateTime UpdateTime { get; set; }
        public string TargetSdk { get; set; } = "";
        public string MinSdk { get; set; } = "";
        public List<string> Permissions { get; set; } = new();
    }

    public class DeviceDiagnosticsService
    {
        private readonly string _adbPath;
        private readonly string _fastbootPath;
        public event EventHandler<string>? LogMessage;

        public DeviceDiagnosticsService(string toolsPath)
        {
            _adbPath = System.IO.Path.Combine(toolsPath, "platform-tools", "adb.exe");
            _fastbootPath = System.IO.Path.Combine(toolsPath, "platform-tools", "fastboot.exe");
        }

        #region Bootloader Status

        public async Task<BootloaderStatus> CheckBootloaderStatusAsync()
        {
            var status = new BootloaderStatus();

            try
            {
                // Check via ADB first
                var props = await RunAdbCommandAsync("shell getprop");

                // Check ro.boot.flash.locked
                var flashLocked = ExtractProp(props, "ro.boot.flash.locked");
                if (!string.IsNullOrEmpty(flashLocked))
                {
                    status.IsUnlocked = flashLocked == "0";
                    status.Status = flashLocked == "0" ? "Unlocked" : "Locked";
                }

                // Check ro.boot.verifiedbootstate
                var verifiedBoot = ExtractProp(props, "ro.boot.verifiedbootstate");
                status.VerifiedBoot = string.IsNullOrEmpty(verifiedBoot) ? "Unknown" : verifiedBoot;

                // Check OEM unlock allowed
                var oemUnlock = ExtractProp(props, "sys.oem_unlock_allowed");
                if (string.IsNullOrEmpty(oemUnlock))
                    oemUnlock = ExtractProp(props, "ro.oem_unlock_supported");
                status.OemUnlockAllowed = oemUnlock == "1" ? "Allowed" : (oemUnlock == "0" ? "Not Allowed" : "Unknown");

                // Check secure boot
                var secureBoot = ExtractProp(props, "ro.boot.secureboot");
                status.SecureBoot = string.IsNullOrEmpty(secureBoot) ? "Unknown" : (secureBoot == "1" ? "Enabled" : "Disabled");

                // Samsung specific - Warranty bit
                var warranty = ExtractProp(props, "ro.boot.warranty_bit");
                if (!string.IsNullOrEmpty(warranty))
                    status.WarrantyBit = warranty == "0" ? "OK" : "Tripped";

                // Check FRP status
                var frp = await RunAdbCommandAsync("shell settings get secure user_setup_complete");
                status.FrpStatus = frp.Trim() == "1" ? "Setup Complete" : "Active";

                // Device state
                var deviceState = ExtractProp(props, "ro.boot.veritymode");
                status.DeviceState = string.IsNullOrEmpty(deviceState) ? "Unknown" : deviceState;

                LogMessage?.Invoke(this, $"Bootloader: {status.Status}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Bootloader check error: {ex.Message}");
            }

            return status;
        }

        public async Task<BootloaderStatus> CheckBootloaderViaFastbootAsync()
        {
            var status = new BootloaderStatus();

            try
            {
                var result = await RunFastbootCommandAsync("getvar all");
                var lines = result.Split('\n');

                foreach (var line in lines)
                {
                    if (line.Contains("unlocked:"))
                    {
                        status.IsUnlocked = line.Contains("yes");
                        status.Status = status.IsUnlocked ? "Unlocked" : "Locked";
                    }
                    else if (line.Contains("secure:"))
                    {
                        status.SecureBoot = line.Contains("yes") ? "Enabled" : "Disabled";
                    }
                }

                LogMessage?.Invoke(this, $"Fastboot Bootloader: {status.Status}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Fastboot check error: {ex.Message}");
            }

            return status;
        }

        #endregion

        #region SafetyNet/Play Integrity

        public async Task<IntegrityStatus> CheckPlayIntegrityAsync()
        {
            var status = new IntegrityStatus { CheckTime = DateTime.Now };

            try
            {
                // Check if Play Services is installed
                var playServices = await RunAdbCommandAsync("shell pm list packages com.google.android.gms");
                if (!playServices.Contains("com.google.android.gms"))
                {
                    status.FailureReasons.Add("Google Play Services not installed");
                    return status;
                }

                // Check various indicators that affect SafetyNet/Play Integrity
                var props = await RunAdbCommandAsync("shell getprop");

                // Check for root indicators
                var suCheck = await RunAdbCommandAsync("shell which su");
                if (!string.IsNullOrEmpty(suCheck.Trim()))
                    status.FailureReasons.Add("SU binary detected");

                // Check Magisk
                var magisk = await RunAdbCommandAsync("shell ls /data/adb/magisk");
                if (!magisk.Contains("No such file"))
                    status.FailureReasons.Add("Magisk detected (check DenyList)");

                // Check verified boot state
                var verifiedBoot = ExtractProp(props, "ro.boot.verifiedbootstate");
                if (verifiedBoot != "green")
                    status.FailureReasons.Add($"Verified boot state: {verifiedBoot}");

                // Check bootloader
                var flashLocked = ExtractProp(props, "ro.boot.flash.locked");
                if (flashLocked == "0")
                    status.FailureReasons.Add("Bootloader unlocked");

                // Check SELinux
                var selinux = await RunAdbCommandAsync("shell getenforce");
                if (!selinux.Contains("Enforcing"))
                    status.FailureReasons.Add($"SELinux: {selinux.Trim()}");

                // Check for test-keys
                var buildTags = ExtractProp(props, "ro.build.tags");
                if (buildTags?.Contains("test-keys") == true)
                    status.FailureReasons.Add("Test keys build");

                // Check debuggable
                var debuggable = ExtractProp(props, "ro.debuggable");
                if (debuggable == "1")
                    status.FailureReasons.Add("System is debuggable");

                // Estimate integrity based on checks
                status.BasicIntegrity = status.FailureReasons.Count == 0;
                status.CtsProfileMatch = status.BasicIntegrity && verifiedBoot == "green";
                status.MeetsBasicIntegrity = status.BasicIntegrity;
                status.MeetsDeviceIntegrity = status.CtsProfileMatch;
                status.MeetsStrongIntegrity = status.MeetsDeviceIntegrity && flashLocked != "0";

                if (status.MeetsStrongIntegrity)
                    status.EvaluationType = "STRONG";
                else if (status.MeetsDeviceIntegrity)
                    status.EvaluationType = "DEVICE";
                else if (status.MeetsBasicIntegrity)
                    status.EvaluationType = "BASIC";
                else
                    status.EvaluationType = "NONE";

                LogMessage?.Invoke(this, $"Play Integrity: {status.EvaluationType}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Integrity check error: {ex.Message}");
                status.FailureReasons.Add($"Check error: {ex.Message}");
            }

            return status;
        }

        #endregion

        #region Battery Health

        public async Task<BatteryHealth> GetBatteryHealthAsync()
        {
            var battery = new BatteryHealth();

            try
            {
                var result = await RunAdbCommandAsync("shell dumpsys battery");
                var lines = result.Split('\n');

                foreach (var line in lines)
                {
                    var parts = line.Split(':');
                    if (parts.Length < 2) continue;

                    var key = parts[0].Trim().ToLower();
                    var value = parts[1].Trim();

                    switch (key)
                    {
                        case "level":
                            int.TryParse(value, out int level);
                            battery.Level = level;
                            break;
                        case "status":
                            battery.Status = ParseBatteryStatus(value);
                            battery.IsCharging = value != "1" && value != "5"; // Not discharging or not charging
                            break;
                        case "health":
                            battery.Health = ParseBatteryHealth(value);
                            break;
                        case "technology":
                            battery.Technology = value;
                            break;
                        case "temperature":
                            if (int.TryParse(value, out int temp))
                                battery.Temperature = temp / 10.0;
                            break;
                        case "voltage":
                            if (int.TryParse(value, out int volt))
                                battery.Voltage = volt / 1000.0;
                            break;
                    }
                }

                // Get additional battery info
                var batteryInfo = await RunAdbCommandAsync("shell cat /sys/class/power_supply/battery/uevent");
                foreach (var line in batteryInfo.Split('\n'))
                {
                    if (line.StartsWith("POWER_SUPPLY_CHARGE_FULL_DESIGN="))
                    {
                        if (int.TryParse(line.Split('=')[1], out int design))
                            battery.DesignCapacity = design / 1000; // Convert to mAh
                    }
                    else if (line.StartsWith("POWER_SUPPLY_CHARGE_FULL="))
                    {
                        if (int.TryParse(line.Split('=')[1], out int current))
                            battery.CurrentCapacity = current / 1000;
                    }
                    else if (line.StartsWith("POWER_SUPPLY_CYCLE_COUNT="))
                    {
                        int.TryParse(line.Split('=')[1], out int cycles);
                        battery.CycleCount = cycles;
                    }
                    else if (line.StartsWith("POWER_SUPPLY_CURRENT_NOW="))
                    {
                        int.TryParse(line.Split('=')[1], out int current);
                        battery.CurrentNow = current / 1000; // Convert to mA
                    }
                }

                // Get charge type
                var chargeType = await RunAdbCommandAsync("shell cat /sys/class/power_supply/battery/charge_type 2>/dev/null");
                battery.ChargeType = string.IsNullOrEmpty(chargeType.Trim()) ? "Unknown" : chargeType.Trim();

                LogMessage?.Invoke(this, $"Battery: {battery.Level}% ({battery.Health})");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Battery check error: {ex.Message}");
            }

            return battery;
        }

        private string ParseBatteryStatus(string value)
        {
            return value switch
            {
                "1" => "Unknown",
                "2" => "Charging",
                "3" => "Discharging",
                "4" => "Not Charging",
                "5" => "Full",
                _ => value
            };
        }

        private string ParseBatteryHealth(string value)
        {
            return value switch
            {
                "1" => "Unknown",
                "2" => "Good",
                "3" => "Overheat",
                "4" => "Dead",
                "5" => "Over Voltage",
                "6" => "Unspecified Failure",
                "7" => "Cold",
                _ => value
            };
        }

        #endregion

        #region Sensor Testing

        public async Task<List<SensorInfo>> GetSensorListAsync()
        {
            var sensors = new List<SensorInfo>();

            try
            {
                var result = await RunAdbCommandAsync("shell dumpsys sensorservice");
                var lines = result.Split('\n');

                SensorInfo? currentSensor = null;

                foreach (var line in lines)
                {
                    if (line.Contains("Sensor List:"))
                        continue;

                    // New sensor entry typically starts with a hex address or sensor name
                    if (line.Contains("| ") && !line.StartsWith(" "))
                    {
                        if (currentSensor != null)
                            sensors.Add(currentSensor);

                        currentSensor = new SensorInfo { IsAvailable = true };

                        // Parse sensor name and type
                        var match = Regex.Match(line, @"\| (.+?) \| (.+?) \|");
                        if (match.Success)
                        {
                            currentSensor.Name = match.Groups[1].Value.Trim();
                            currentSensor.Type = match.Groups[2].Value.Trim();
                        }
                    }
                    else if (currentSensor != null)
                    {
                        if (line.Contains("vendor:"))
                            currentSensor.Vendor = line.Split(':')[1].Trim();
                        else if (line.Contains("version:") && double.TryParse(Regex.Match(line, @"[\d.]+").Value, out double ver))
                            currentSensor.Version = ver;
                        else if (line.Contains("maxRange:") && double.TryParse(Regex.Match(line, @"[\d.]+").Value, out double range))
                            currentSensor.MaxRange = range;
                        else if (line.Contains("resolution:") && double.TryParse(Regex.Match(line, @"[\d.]+").Value, out double res))
                            currentSensor.Resolution = res;
                        else if (line.Contains("power:") && double.TryParse(Regex.Match(line, @"[\d.]+").Value, out double power))
                            currentSensor.Power = power;
                    }
                }

                if (currentSensor != null)
                    sensors.Add(currentSensor);

                LogMessage?.Invoke(this, $"Found {sensors.Count} sensors");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Sensor list error: {ex.Message}");
            }

            return sensors;
        }

        public async Task<Dictionary<string, string>> TestSensorAsync(string sensorType)
        {
            var results = new Dictionary<string, string>();

            try
            {
                // This would need a helper app on device to properly test sensors
                // For now, we check if sensor is reporting data
                var result = await RunAdbCommandAsync($"shell dumpsys sensorservice | grep -A5 \"{sensorType}\"");
                results["raw_output"] = result;
                results["status"] = result.Contains("active") ? "Active" : "Inactive";
            }
            catch (Exception ex)
            {
                results["error"] = ex.Message;
            }

            return results;
        }

        #endregion

        #region Network Info

        public async Task<NetworkInfo> GetNetworkInfoAsync()
        {
            var info = new NetworkInfo();

            try
            {
                // Get connection type
                var connectivity = await RunAdbCommandAsync("shell dumpsys connectivity | grep -i \"active network\"");
                if (connectivity.Contains("WIFI"))
                    info.ConnectionType = "WiFi";
                else if (connectivity.Contains("MOBILE"))
                    info.ConnectionType = "Mobile Data";
                else if (connectivity.Contains("ETHERNET"))
                    info.ConnectionType = "Ethernet";

                // Get WiFi info
                var wifiInfo = await RunAdbCommandAsync("shell dumpsys wifi | grep -E \"SSID|signal|ip\"");
                var ssidMatch = Regex.Match(wifiInfo, @"SSID: (.+?),");
                if (ssidMatch.Success)
                    info.WifiSsid = ssidMatch.Groups[1].Value;

                var signalMatch = Regex.Match(wifiInfo, @"signal: (-?\d+)");
                if (signalMatch.Success)
                    int.TryParse(signalMatch.Groups[1].Value, out int wifiSignal);

                // Get IP address
                var ipResult = await RunAdbCommandAsync("shell ip addr show wlan0 | grep inet");
                var ipMatch = Regex.Match(ipResult, @"inet (\d+\.\d+\.\d+\.\d+)");
                if (ipMatch.Success)
                    info.IpAddress = ipMatch.Groups[1].Value;

                // Get mobile network info
                var telephony = await RunAdbCommandAsync("shell dumpsys telephony.registry");

                var operatorMatch = Regex.Match(telephony, @"mOperatorAlphaLong=(.+?)\n");
                if (operatorMatch.Success)
                    info.Operator = operatorMatch.Groups[1].Value.Trim();

                var networkTypeMatch = Regex.Match(telephony, @"mDataNetworkType=(\d+)");
                if (networkTypeMatch.Success)
                {
                    info.MobileNetworkType = ParseNetworkType(networkTypeMatch.Groups[1].Value);
                }

                info.IsRoaming = telephony.Contains("mDataRoaming=true");

                // Get SIM state
                var simState = await RunAdbCommandAsync("shell getprop gsm.sim.state");
                info.SimState = simState.Trim();

                // Get APN settings
                info.ApnSettings = await GetApnSettingsAsync();

                LogMessage?.Invoke(this, $"Network: {info.ConnectionType} - {info.Operator}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Network info error: {ex.Message}");
            }

            return info;
        }

        private string ParseNetworkType(string typeCode)
        {
            return typeCode switch
            {
                "0" => "Unknown",
                "1" => "GPRS",
                "2" => "EDGE",
                "3" => "UMTS",
                "4" => "CDMA",
                "5" => "EVDO_0",
                "6" => "EVDO_A",
                "7" => "1xRTT",
                "8" => "HSDPA",
                "9" => "HSUPA",
                "10" => "HSPA",
                "11" => "iDEN",
                "12" => "EVDO_B",
                "13" => "LTE",
                "14" => "eHRPD",
                "15" => "HSPA+",
                "16" => "GSM",
                "17" => "TD_SCDMA",
                "18" => "IWLAN",
                "19" => "LTE_CA",
                "20" => "5G NR",
                _ => $"Type {typeCode}"
            };
        }

        public async Task<List<ApnSetting>> GetApnSettingsAsync()
        {
            var apns = new List<ApnSetting>();

            try
            {
                var result = await RunAdbCommandAsync("shell content query --uri content://telephony/carriers/current");
                var rows = result.Split(new[] { "Row:" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row)) continue;

                    var apn = new ApnSetting();

                    var nameMatch = Regex.Match(row, @"name=(.+?),");
                    if (nameMatch.Success) apn.Name = nameMatch.Groups[1].Value;

                    var apnMatch = Regex.Match(row, @"apn=(.+?),");
                    if (apnMatch.Success) apn.Apn = apnMatch.Groups[1].Value;

                    var typeMatch = Regex.Match(row, @"type=(.+?),");
                    if (typeMatch.Success) apn.Type = typeMatch.Groups[1].Value;

                    var mccMatch = Regex.Match(row, @"mcc=(\d+)");
                    if (mccMatch.Success) apn.Mcc = mccMatch.Groups[1].Value;

                    var mncMatch = Regex.Match(row, @"mnc=(\d+)");
                    if (mncMatch.Success) apn.Mnc = mncMatch.Groups[1].Value;

                    if (!string.IsNullOrEmpty(apn.Apn))
                        apns.Add(apn);
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"APN query error: {ex.Message}");
            }

            return apns;
        }

        #endregion

        #region App Manager

        public async Task<List<AppInfo>> GetInstalledAppsAsync(bool includeSystem = false)
        {
            var apps = new List<AppInfo>();

            try
            {
                var cmd = includeSystem ? "shell pm list packages -f" : "shell pm list packages -f -3";
                var result = await RunAdbCommandAsync(cmd);
                var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if (!line.StartsWith("package:")) continue;

                    var match = Regex.Match(line, @"package:(.+)=(.+)");
                    if (!match.Success) continue;

                    var app = new AppInfo
                    {
                        InstalledPath = match.Groups[1].Value,
                        PackageName = match.Groups[2].Value.Trim(),
                        IsSystemApp = match.Groups[1].Value.StartsWith("/system/")
                    };

                    // Get additional info
                    var dumpResult = await RunAdbCommandAsync($"shell dumpsys package {app.PackageName} | head -50");

                    var versionMatch = Regex.Match(dumpResult, @"versionName=(.+)");
                    if (versionMatch.Success) app.Version = versionMatch.Groups[1].Value.Trim();

                    var versionCodeMatch = Regex.Match(dumpResult, @"versionCode=(\d+)");
                    if (versionCodeMatch.Success) int.TryParse(versionCodeMatch.Groups[1].Value, out int vc);

                    var labelMatch = Regex.Match(dumpResult, @"Application label: (.+)");
                    if (labelMatch.Success) app.AppName = labelMatch.Groups[1].Value.Trim();
                    else app.AppName = app.PackageName;

                    var enabledMatch = Regex.Match(dumpResult, @"enabled=(\d)");
                    app.IsEnabled = !enabledMatch.Success || enabledMatch.Groups[1].Value == "1";

                    apps.Add(app);
                }

                LogMessage?.Invoke(this, $"Found {apps.Count} apps");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"App list error: {ex.Message}");
            }

            return apps;
        }

        public async Task<bool> InstallApkAsync(string apkPath, bool allowDowngrade = false)
        {
            try
            {
                var flags = allowDowngrade ? "-r -d" : "-r";
                var result = await RunAdbCommandAsync($"install {flags} \"{apkPath}\"");
                var success = result.Contains("Success");
                LogMessage?.Invoke(this, success ? "APK installed successfully" : $"Install failed: {result}");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Install error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UninstallAppAsync(string packageName, bool keepData = false)
        {
            try
            {
                var cmd = keepData ? $"shell pm uninstall -k {packageName}" : $"uninstall {packageName}";
                var result = await RunAdbCommandAsync(cmd);
                var success = result.Contains("Success");
                LogMessage?.Invoke(this, success ? $"Uninstalled {packageName}" : $"Uninstall failed: {result}");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Uninstall error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> BackupApkAsync(string packageName, string outputPath)
        {
            try
            {
                // Get APK path
                var pathResult = await RunAdbCommandAsync($"shell pm path {packageName}");
                var match = Regex.Match(pathResult, @"package:(.+\.apk)");
                if (!match.Success)
                {
                    LogMessage?.Invoke(this, "Could not find APK path");
                    return false;
                }

                var apkPath = match.Groups[1].Value.Trim();
                var result = await RunAdbCommandAsync($"pull \"{apkPath}\" \"{outputPath}\"");
                var success = !result.Contains("error");
                LogMessage?.Invoke(this, success ? $"APK saved to {outputPath}" : $"Backup failed: {result}");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Backup error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DisableAppAsync(string packageName)
        {
            var result = await RunAdbCommandAsync($"shell pm disable-user --user 0 {packageName}");
            return result.Contains("disabled");
        }

        public async Task<bool> EnableAppAsync(string packageName)
        {
            var result = await RunAdbCommandAsync($"shell pm enable {packageName}");
            return result.Contains("enabled");
        }

        public async Task<bool> ClearAppDataAsync(string packageName)
        {
            var result = await RunAdbCommandAsync($"shell pm clear {packageName}");
            return result.Contains("Success");
        }

        public async Task<bool> ForceStopAppAsync(string packageName)
        {
            await RunAdbCommandAsync($"shell am force-stop {packageName}");
            return true;
        }

        #endregion

        #region Device Benchmark

        public async Task<DeviceBenchmark> RunBenchmarkAsync(IProgress<int>? progress = null)
        {
            var benchmark = new DeviceBenchmark { TestTime = DateTime.Now };

            try
            {
                progress?.Report(10);
                LogMessage?.Invoke(this, "Starting CPU benchmark...");

                // CPU Single Core Test
                var cpuSingle = await RunCpuBenchmarkAsync(1);
                benchmark.CpuSingleCore = cpuSingle;
                benchmark.DetailedResults.Add(new BenchmarkResult
                {
                    TestName = "CPU Single Core",
                    Score = cpuSingle,
                    Category = "CPU"
                });
                progress?.Report(30);

                // CPU Multi Core Test
                LogMessage?.Invoke(this, "Running multi-core test...");
                var cpuMulti = await RunCpuBenchmarkAsync(0); // 0 = all cores
                benchmark.CpuMultiCore = cpuMulti;
                benchmark.DetailedResults.Add(new BenchmarkResult
                {
                    TestName = "CPU Multi Core",
                    Score = cpuMulti,
                    Category = "CPU"
                });
                progress?.Report(50);

                // Memory Speed Test
                LogMessage?.Invoke(this, "Running memory test...");
                var memSpeed = await RunMemoryBenchmarkAsync();
                benchmark.MemorySpeed = memSpeed;
                benchmark.DetailedResults.Add(new BenchmarkResult
                {
                    TestName = "Memory Speed",
                    Score = memSpeed,
                    Unit = "MB/s",
                    Category = "Memory"
                });
                progress?.Report(70);

                // Storage Test
                LogMessage?.Invoke(this, "Running storage test...");
                var (read, write) = await RunStorageBenchmarkAsync();
                benchmark.StorageRead = read;
                benchmark.StorageWrite = write;
                benchmark.DetailedResults.Add(new BenchmarkResult
                {
                    TestName = "Storage Read",
                    Score = read,
                    Unit = "MB/s",
                    Category = "Storage"
                });
                benchmark.DetailedResults.Add(new BenchmarkResult
                {
                    TestName = "Storage Write",
                    Score = write,
                    Unit = "MB/s",
                    Category = "Storage"
                });
                progress?.Report(90);

                // Calculate overall score
                benchmark.OverallScore = (benchmark.CpuSingleCore * 2 + benchmark.CpuMultiCore +
                                          benchmark.MemorySpeed + benchmark.StorageRead + benchmark.StorageWrite) / 6;

                progress?.Report(100);
                LogMessage?.Invoke(this, $"Benchmark complete. Score: {benchmark.OverallScore}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Benchmark error: {ex.Message}");
            }

            return benchmark;
        }

        private async Task<int> RunCpuBenchmarkAsync(int cores)
        {
            try
            {
                // Simple math computation test
                var taskset = cores > 0 ? $"taskset {1 << (cores - 1)}" : "";
                var cmd = $"shell {taskset} sh -c 'i=0; while [ $i -lt 100000 ]; do i=$((i+1)); done; echo done'";

                var sw = Stopwatch.StartNew();
                await RunAdbCommandAsync(cmd);
                sw.Stop();

                // Score calculation: faster = higher score (inverse of time)
                var baseScore = 10000;
                var score = (int)(baseScore * 1000 / Math.Max(sw.ElapsedMilliseconds, 1));
                return Math.Min(score, 10000);
            }
            catch
            {
                return 0;
            }
        }

        private async Task<int> RunMemoryBenchmarkAsync()
        {
            try
            {
                // Simple memory bandwidth test using dd
                var result = await RunAdbCommandAsync("shell dd if=/dev/zero of=/dev/null bs=1M count=100 2>&1");
                var match = Regex.Match(result, @"(\d+\.?\d*)\s*(MB|GB)/s");
                if (match.Success)
                {
                    double.TryParse(match.Groups[1].Value, out double speed);
                    if (match.Groups[2].Value == "GB") speed *= 1024;
                    return (int)speed;
                }
                return 1000; // Default estimate
            }
            catch
            {
                return 0;
            }
        }

        private async Task<(int read, int write)> RunStorageBenchmarkAsync()
        {
            try
            {
                // Write test
                var writeResult = await RunAdbCommandAsync("shell dd if=/dev/zero of=/data/local/tmp/bench.tmp bs=1M count=50 2>&1");
                var writeMatch = Regex.Match(writeResult, @"(\d+\.?\d*)\s*(MB|GB)/s");
                double writeSpeed = 0;
                if (writeMatch.Success)
                {
                    double.TryParse(writeMatch.Groups[1].Value, out writeSpeed);
                    if (writeMatch.Groups[2].Value == "GB") writeSpeed *= 1024;
                }

                // Read test
                var readResult = await RunAdbCommandAsync("shell dd if=/data/local/tmp/bench.tmp of=/dev/null bs=1M 2>&1");
                var readMatch = Regex.Match(readResult, @"(\d+\.?\d*)\s*(MB|GB)/s");
                double readSpeed = 0;
                if (readMatch.Success)
                {
                    double.TryParse(readMatch.Groups[1].Value, out readSpeed);
                    if (readMatch.Groups[2].Value == "GB") readSpeed *= 1024;
                }

                // Cleanup
                await RunAdbCommandAsync("shell rm /data/local/tmp/bench.tmp");

                return ((int)readSpeed, (int)writeSpeed);
            }
            catch
            {
                return (0, 0);
            }
        }

        #endregion

        #region Helper Methods

        private string ExtractProp(string props, string propName)
        {
            var match = Regex.Match(props, $@"\[{Regex.Escape(propName)}\]:\s*\[(.+?)\]");
            return match.Success ? match.Groups[1].Value : "";
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

        private async Task<string> RunFastbootCommandAsync(string command)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _fastbootPath,
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

                return output + error; // Fastboot often outputs to stderr
            }
            catch
            {
                return "";
            }
        }

        #endregion
    }
}
