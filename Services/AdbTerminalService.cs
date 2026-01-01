using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class AdbTerminalService
    {
        private readonly string _adbPath;
        public event EventHandler<string>? OutputReceived;
        public event EventHandler<string>? ErrorReceived;

        public AdbTerminalService(string adbPath)
        {
            _adbPath = adbPath;
        }

        public async Task<string> ExecuteCommandAsync(string command)
        {
            if (!File.Exists(_adbPath))
            {
                return "Error: ADB not found. Please download Platform Tools first.";
            }

            try
            {
                var output = new StringBuilder();
                var error = new StringBuilder();

                var processInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = processInfo };

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        output.AppendLine(e.Data);
                        OutputReceived?.Invoke(this, e.Data);
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        error.AppendLine(e.Data);
                        ErrorReceived?.Invoke(this, e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                if (error.Length > 0)
                {
                    return output.ToString() + "\n[ERROR] " + error.ToString();
                }

                return output.ToString();
            }
            catch (Exception ex)
            {
                return $"Error executing command: {ex.Message}";
            }
        }

        public async Task<string> GetDeviceInfoAsync()
        {
            var sb = new StringBuilder();

            sb.AppendLine("=== Device Information ===");
            sb.AppendLine();

            // Basic Info
            sb.AppendLine($"Model: {await GetPropAsync("ro.product.model")}");
            sb.AppendLine($"Brand: {await GetPropAsync("ro.product.brand")}");
            sb.AppendLine($"Device: {await GetPropAsync("ro.product.device")}");
            sb.AppendLine($"Manufacturer: {await GetPropAsync("ro.product.manufacturer")}");
            sb.AppendLine();

            // System Info
            sb.AppendLine("=== System ===");
            sb.AppendLine($"Android Version: {await GetPropAsync("ro.build.version.release")}");
            sb.AppendLine($"SDK Level: {await GetPropAsync("ro.build.version.sdk")}");
            sb.AppendLine($"Build ID: {await GetPropAsync("ro.build.id")}");
            sb.AppendLine($"Build Fingerprint: {await GetPropAsync("ro.build.fingerprint")}");
            sb.AppendLine($"Security Patch: {await GetPropAsync("ro.build.version.security_patch")}");
            sb.AppendLine();

            // Hardware
            sb.AppendLine("=== Hardware ===");
            sb.AppendLine($"CPU ABI: {await GetPropAsync("ro.product.cpu.abi")}");
            sb.AppendLine($"Hardware: {await GetPropAsync("ro.hardware")}");
            sb.AppendLine($"Platform: {await GetPropAsync("ro.board.platform")}");
            sb.AppendLine($"Baseband: {await GetPropAsync("gsm.version.baseband")}");
            sb.AppendLine();

            // Identifiers
            sb.AppendLine("=== Identifiers ===");
            sb.AppendLine($"Serial: {await GetPropAsync("ro.serialno")}");
            var imei = await ExecuteCommandAsync("shell service call iphonesubinfo 1");
            sb.AppendLine($"IMEI: (use *#06# on device)");
            sb.AppendLine();

            // Bootloader
            sb.AppendLine("=== Bootloader ===");
            sb.AppendLine($"Bootloader: {await GetPropAsync("ro.bootloader")}");
            sb.AppendLine($"Secure Boot: {await GetPropAsync("ro.boot.verifiedbootstate")}");
            sb.AppendLine($"Encryption: {await GetPropAsync("ro.crypto.state")}");

            return sb.ToString();
        }

        public async Task<string> GetPropAsync(string prop)
        {
            var result = await ExecuteCommandAsync($"shell getprop {prop}");
            return result.Trim();
        }

        public async Task<string> TakeScreenshotAsync(string savePath)
        {
            try
            {
                // Take screenshot on device
                await ExecuteCommandAsync("shell screencap -p /sdcard/screenshot.png");

                // Pull to PC
                await ExecuteCommandAsync($"pull /sdcard/screenshot.png \"{savePath}\"");

                // Delete from device
                await ExecuteCommandAsync("shell rm /sdcard/screenshot.png");

                return $"Screenshot saved to: {savePath}";
            }
            catch (Exception ex)
            {
                return $"Error taking screenshot: {ex.Message}";
            }
        }

        public async Task<string> StartScreenRecordAsync(string savePath, int seconds = 180)
        {
            try
            {
                await ExecuteCommandAsync($"shell screenrecord --time-limit {seconds} /sdcard/recording.mp4");
                await ExecuteCommandAsync($"pull /sdcard/recording.mp4 \"{savePath}\"");
                await ExecuteCommandAsync("shell rm /sdcard/recording.mp4");

                return $"Recording saved to: {savePath}";
            }
            catch (Exception ex)
            {
                return $"Error recording screen: {ex.Message}";
            }
        }

        public async Task<string> GetLogcatAsync(int lines = 100)
        {
            return await ExecuteCommandAsync($"logcat -d -t {lines}");
        }

        public async Task<string> ClearLogcatAsync()
        {
            return await ExecuteCommandAsync("logcat -c");
        }

        public async Task<string> InstallApkAsync(string apkPath)
        {
            return await ExecuteCommandAsync($"install \"{apkPath}\"");
        }

        public async Task<string> UninstallPackageAsync(string packageName)
        {
            return await ExecuteCommandAsync($"uninstall {packageName}");
        }

        public async Task<string> ListPackagesAsync(bool systemOnly = false)
        {
            var flag = systemOnly ? "-s" : "";
            return await ExecuteCommandAsync($"shell pm list packages {flag}");
        }

        public async Task<string> RebootAsync(string mode = "")
        {
            if (string.IsNullOrEmpty(mode))
            {
                return await ExecuteCommandAsync("reboot");
            }
            return await ExecuteCommandAsync($"reboot {mode}");
        }
    }
}
