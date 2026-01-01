using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class ScrcpySettings
    {
        // Display
        public int MaxSize { get; set; } = 0; // 0 = original size
        public int MaxFps { get; set; } = 60;
        public int BitRate { get; set; } = 8; // Mbps
        public bool FullScreen { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool Borderless { get; set; }
        public int Rotation { get; set; } = 0; // 0, 90, 180, 270

        // Control
        public bool ShowTouches { get; set; }
        public bool StayAwake { get; set; } = true;
        public bool TurnScreenOff { get; set; }
        public bool NoControl { get; set; }
        public bool NoAudio { get; set; }

        // Recording
        public bool Record { get; set; }
        public string RecordPath { get; set; } = "";
        public string RecordFormat { get; set; } = "mp4"; // mp4, mkv

        // Advanced
        public string Crop { get; set; } = ""; // width:height:x:y
        public int DisplayId { get; set; } = 0;
        public string Serial { get; set; } = "";
        public bool TcpIp { get; set; }
        public string TcpIpAddress { get; set; } = "";
        public int TcpIpPort { get; set; } = 5555;
    }

    public class ScreenMirrorService
    {
        private readonly string _toolsPath;
        private readonly string _scrcpyPath;
        private readonly string _adbPath;
        private Process? _scrcpyProcess;
        private Process? _recordProcess;

        public event EventHandler<string>? LogMessage;
        public event EventHandler? MirrorStarted;
        public event EventHandler? MirrorStopped;

        public bool IsRunning => _scrcpyProcess != null && !_scrcpyProcess.HasExited;
        public bool IsRecording => _recordProcess != null && !_recordProcess.HasExited;

        private const string ScrcpyVersion = "3.1";
        private const string ScrcpyDownloadUrl = "https://github.com/Genymobile/scrcpy/releases/download/v{0}/scrcpy-win64-v{0}.zip";

        public ScreenMirrorService(string toolsPath)
        {
            _toolsPath = toolsPath;
            _scrcpyPath = Path.Combine(toolsPath, "scrcpy");
            _adbPath = Path.Combine(toolsPath, "platform-tools", "adb.exe");
        }

        public bool IsScrcpyInstalled()
        {
            return File.Exists(Path.Combine(_scrcpyPath, "scrcpy.exe"));
        }

        public async Task<bool> DownloadScrcpyAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                LogMessage?.Invoke(this, $"Downloading scrcpy v{ScrcpyVersion}...");

                var url = string.Format(ScrcpyDownloadUrl, ScrcpyVersion);
                var zipPath = Path.Combine(_toolsPath, "scrcpy.zip");

                using var httpClient = new HttpClient();
                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;

                using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            progress?.Report((int)((downloadedBytes * 100) / totalBytes));
                        }
                    }
                }

                LogMessage?.Invoke(this, "Extracting scrcpy...");

                // Extract
                if (Directory.Exists(_scrcpyPath))
                    Directory.Delete(_scrcpyPath, true);

                ZipFile.ExtractToDirectory(zipPath, _toolsPath);

                // Rename extracted folder
                var extractedFolder = Directory.GetDirectories(_toolsPath, "scrcpy-*")[0];
                Directory.Move(extractedFolder, _scrcpyPath);

                // Cleanup
                File.Delete(zipPath);

                LogMessage?.Invoke(this, "scrcpy installed successfully");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Download error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> StartMirrorAsync(ScrcpySettings? settings = null)
        {
            if (IsRunning)
            {
                LogMessage?.Invoke(this, "Mirror already running");
                return true;
            }

            if (!IsScrcpyInstalled())
            {
                LogMessage?.Invoke(this, "scrcpy not installed");
                return false;
            }

            settings ??= new ScrcpySettings();

            try
            {
                var args = BuildScrcpyArgs(settings);
                LogMessage?.Invoke(this, $"Starting scrcpy: {args}");

                _scrcpyProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(_scrcpyPath, "scrcpy.exe"),
                        Arguments = args,
                        WorkingDirectory = _scrcpyPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = false
                    },
                    EnableRaisingEvents = true
                };

                _scrcpyProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        LogMessage?.Invoke(this, e.Data);
                };

                _scrcpyProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        LogMessage?.Invoke(this, e.Data);
                };

                _scrcpyProcess.Exited += (s, e) =>
                {
                    MirrorStopped?.Invoke(this, EventArgs.Empty);
                    LogMessage?.Invoke(this, "Mirror stopped");
                };

                _scrcpyProcess.Start();
                _scrcpyProcess.BeginOutputReadLine();
                _scrcpyProcess.BeginErrorReadLine();

                await Task.Delay(1000);

                if (!_scrcpyProcess.HasExited)
                {
                    MirrorStarted?.Invoke(this, EventArgs.Empty);
                    LogMessage?.Invoke(this, "Mirror started");
                    return true;
                }

                LogMessage?.Invoke(this, "Failed to start mirror");
                return false;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Start error: {ex.Message}");
                return false;
            }
        }

        private string BuildScrcpyArgs(ScrcpySettings settings)
        {
            var args = new System.Text.StringBuilder();

            // Serial/Connection
            if (!string.IsNullOrEmpty(settings.Serial))
                args.Append($"-s {settings.Serial} ");
            else if (settings.TcpIp && !string.IsNullOrEmpty(settings.TcpIpAddress))
                args.Append($"--tcpip={settings.TcpIpAddress}:{settings.TcpIpPort} ");

            // Display
            if (settings.MaxSize > 0)
                args.Append($"-m {settings.MaxSize} ");

            if (settings.MaxFps > 0 && settings.MaxFps < 60)
                args.Append($"--max-fps {settings.MaxFps} ");

            if (settings.BitRate != 8)
                args.Append($"-b {settings.BitRate}M ");

            if (settings.FullScreen)
                args.Append("-f ");

            if (settings.AlwaysOnTop)
                args.Append("--always-on-top ");

            if (settings.Borderless)
                args.Append("--window-borderless ");

            if (settings.Rotation > 0)
                args.Append($"--rotation {settings.Rotation / 90} ");

            if (!string.IsNullOrEmpty(settings.Crop))
                args.Append($"--crop {settings.Crop} ");

            if (settings.DisplayId > 0)
                args.Append($"--display-id {settings.DisplayId} ");

            // Control
            if (settings.ShowTouches)
                args.Append("--show-touches ");

            if (settings.StayAwake)
                args.Append("-w ");

            if (settings.TurnScreenOff)
                args.Append("-S ");

            if (settings.NoControl)
                args.Append("-n ");

            if (settings.NoAudio)
                args.Append("--no-audio ");

            // Recording
            if (settings.Record && !string.IsNullOrEmpty(settings.RecordPath))
            {
                args.Append($"-r \"{settings.RecordPath}\" ");
                if (settings.RecordFormat == "mkv")
                    args.Append("--record-format=mkv ");
            }

            return args.ToString().Trim();
        }

        public void StopMirror()
        {
            if (_scrcpyProcess != null && !_scrcpyProcess.HasExited)
            {
                try
                {
                    _scrcpyProcess.Kill();
                    _scrcpyProcess.Dispose();
                    _scrcpyProcess = null;
                    LogMessage?.Invoke(this, "Mirror stopped");
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke(this, $"Stop error: {ex.Message}");
                }
            }
        }

        public async Task<bool> StartRecordingAsync(string outputPath, ScrcpySettings? settings = null)
        {
            if (IsRecording)
            {
                LogMessage?.Invoke(this, "Already recording");
                return false;
            }

            settings ??= new ScrcpySettings();
            settings.Record = true;
            settings.RecordPath = outputPath;
            settings.NoControl = true; // Record only mode

            try
            {
                var args = BuildScrcpyArgs(settings);
                args += " --no-window"; // No display, just record

                _recordProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(_scrcpyPath, "scrcpy.exe"),
                        Arguments = args,
                        WorkingDirectory = _scrcpyPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                _recordProcess.Start();
                LogMessage?.Invoke(this, $"Recording to: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Record error: {ex.Message}");
                return false;
            }
        }

        public void StopRecording()
        {
            if (_recordProcess != null && !_recordProcess.HasExited)
            {
                try
                {
                    _recordProcess.Kill();
                    _recordProcess.Dispose();
                    _recordProcess = null;
                    LogMessage?.Invoke(this, "Recording stopped");
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke(this, $"Stop recording error: {ex.Message}");
                }
            }
        }

        public async Task<bool> TakeScreenshotAsync(string outputPath)
        {
            try
            {
                var devicePath = "/sdcard/screenshot.png";

                // Take screenshot via ADB
                await RunAdbCommandAsync($"shell screencap -p {devicePath}");

                // Pull to PC
                await RunAdbCommandAsync($"pull {devicePath} \"{outputPath}\"");

                // Cleanup
                await RunAdbCommandAsync($"shell rm {devicePath}");

                LogMessage?.Invoke(this, $"Screenshot saved: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Screenshot error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ConnectWirelessAsync(string ipAddress, int port = 5555)
        {
            try
            {
                // Enable TCP/IP mode
                await RunAdbCommandAsync($"tcpip {port}");
                await Task.Delay(2000);

                // Connect
                var result = await RunAdbCommandAsync($"connect {ipAddress}:{port}");

                var success = result.Contains("connected");
                LogMessage?.Invoke(this, success ? $"Connected to {ipAddress}:{port}" : $"Connection failed: {result}");
                return success;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Wireless connect error: {ex.Message}");
                return false;
            }
        }

        public async Task<string> GetDeviceIpAddressAsync()
        {
            try
            {
                var result = await RunAdbCommandAsync("shell ip addr show wlan0");
                var match = Regex.Match(result, @"inet (\d+\.\d+\.\d+\.\d+)");
                return match.Success ? match.Groups[1].Value : "";
            }
            catch
            {
                return "";
            }
        }

        public async Task<bool> DisconnectWirelessAsync(string ipAddress = "")
        {
            try
            {
                var cmd = string.IsNullOrEmpty(ipAddress) ? "disconnect" : $"disconnect {ipAddress}";
                var result = await RunAdbCommandAsync(cmd);
                LogMessage?.Invoke(this, result);
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Disconnect error: {ex.Message}");
                return false;
            }
        }

        public async Task SendKeyEventAsync(int keyCode)
        {
            await RunAdbCommandAsync($"shell input keyevent {keyCode}");
        }

        public async Task SendTextAsync(string text)
        {
            // Escape special characters
            var escapedText = text.Replace(" ", "%s").Replace("'", "\\'").Replace("\"", "\\\"");
            await RunAdbCommandAsync($"shell input text \"{escapedText}\"");
        }

        public async Task SendTapAsync(int x, int y)
        {
            await RunAdbCommandAsync($"shell input tap {x} {y}");
        }

        public async Task SendSwipeAsync(int x1, int y1, int x2, int y2, int durationMs = 300)
        {
            await RunAdbCommandAsync($"shell input swipe {x1} {y1} {x2} {y2} {durationMs}");
        }

        // Common key events
        public static class KeyCodes
        {
            public const int Home = 3;
            public const int Back = 4;
            public const int Call = 5;
            public const int EndCall = 6;
            public const int VolumeUp = 24;
            public const int VolumeDown = 25;
            public const int Power = 26;
            public const int Camera = 27;
            public const int Menu = 82;
            public const int Enter = 66;
            public const int Tab = 61;
            public const int Delete = 67;
            public const int Search = 84;
            public const int MediaPlayPause = 85;
            public const int MediaNext = 87;
            public const int MediaPrevious = 88;
            public const int Mute = 91;
            public const int AppSwitch = 187;
            public const int Screenshot = 120;
            public const int Brightness_Down = 220;
            public const int Brightness_Up = 221;
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
