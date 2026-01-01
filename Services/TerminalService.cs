using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class TerminalSession : IDisposable
    {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
        public string Type { get; set; } = "adb"; // adb, fastboot, shell
        public Process? Process { get; set; }
        public StreamWriter? Input { get; set; }
        public bool IsConnected { get; set; }
        public DateTime StartTime { get; set; } = DateTime.Now;
        public List<string> History { get; } = new();

        public void Dispose()
        {
            try
            {
                Input?.Dispose();
                if (Process != null && !Process.HasExited)
                {
                    Process.Kill();
                }
                Process?.Dispose();
            }
            catch { }
        }
    }

    public class TerminalService : IDisposable
    {
        private readonly string _adbPath;
        private readonly string _fastbootPath;
        private readonly ConcurrentDictionary<string, TerminalSession> _sessions;
        private TerminalSession? _activeSession;

        public event EventHandler<string>? OutputReceived;
        public event EventHandler<string>? ErrorReceived;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<bool>? ConnectionChanged;

        public bool IsConnected => _activeSession?.IsConnected ?? false;
        public string? ActiveSessionId => _activeSession?.Id;

        public TerminalService(string adbPath, string fastbootPath = "")
        {
            _adbPath = adbPath;
            _fastbootPath = string.IsNullOrEmpty(fastbootPath)
                ? Path.Combine(Path.GetDirectoryName(adbPath) ?? "", "fastboot.exe")
                : fastbootPath;
            _sessions = new ConcurrentDictionary<string, TerminalSession>();
        }

        public async Task<TerminalSession> StartAdbShellAsync(string deviceSerial = "",
            CancellationToken ct = default)
        {
            var session = new TerminalSession { Type = "adb" };

            try
            {
                var args = string.IsNullOrEmpty(deviceSerial)
                    ? "shell"
                    : $"-s {deviceSerial} shell";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                session.Process = new Process { StartInfo = startInfo };
                session.Process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        session.History.Add($"> {e.Data}");
                        OutputReceived?.Invoke(this, e.Data);
                    }
                };

                session.Process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        session.History.Add($"! {e.Data}");
                        ErrorReceived?.Invoke(this, e.Data);
                    }
                };

                session.Process.Start();
                session.Process.BeginOutputReadLine();
                session.Process.BeginErrorReadLine();

                session.Input = session.Process.StandardInput;
                session.IsConnected = true;

                _sessions[session.Id] = session;
                _activeSession = session;

                StatusChanged?.Invoke(this, $"ADB shell connected (session: {session.Id})");
                ConnectionChanged?.Invoke(this, true);

                return session;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Failed to start ADB shell: {ex.Message}");
                session.Dispose();
                throw;
            }
        }

        public async Task<TerminalSession> StartRootShellAsync(string deviceSerial = "",
            CancellationToken ct = default)
        {
            var session = await StartAdbShellAsync(deviceSerial, ct);

            // Try to get root
            await SendCommandAsync("su", session.Id, ct);
            await Task.Delay(500, ct); // Wait for su prompt

            return session;
        }

        public async Task<string> SendCommandAsync(string command, string? sessionId = null,
            CancellationToken ct = default)
        {
            var session = string.IsNullOrEmpty(sessionId)
                ? _activeSession
                : _sessions.TryGetValue(sessionId, out var s) ? s : null;

            if (session?.Input == null || session.Process?.HasExited == true)
            {
                throw new InvalidOperationException("No active terminal session");
            }

            session.History.Add($"$ {command}");

            await session.Input.WriteLineAsync(command);
            await session.Input.FlushAsync();

            // For one-shot commands, we don't wait for response in interactive mode
            return "";
        }

        public async Task<string> ExecuteCommandAsync(string command, string? deviceSerial = null,
            TimeSpan? timeout = null, CancellationToken ct = default)
        {
            try
            {
                var args = string.IsNullOrEmpty(deviceSerial)
                    ? $"shell {command}"
                    : $"-s {deviceSerial} shell {command}";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return "";

                var outputBuilder = new StringBuilder();

                var outputTask = Task.Run(async () =>
                {
                    while (!process.StandardOutput.EndOfStream)
                    {
                        var line = await process.StandardOutput.ReadLineAsync(ct);
                        if (line != null)
                        {
                            outputBuilder.AppendLine(line);
                            OutputReceived?.Invoke(this, line);
                        }
                    }
                }, ct);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout ?? TimeSpan.FromMinutes(2));

                try
                {
                    await process.WaitForExitAsync(cts.Token);
                    await outputTask;
                }
                catch (OperationCanceledException)
                {
                    process.Kill();
                    throw;
                }

                return outputBuilder.ToString();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(this, $"Command error: {ex.Message}");
                return "";
            }
        }

        public async Task<string> ExecuteRootCommandAsync(string command, string? deviceSerial = null,
            CancellationToken ct = default)
        {
            return await ExecuteCommandAsync($"su -c '{command}'", deviceSerial, null, ct);
        }

        public async Task<string> ExecuteFastbootCommandAsync(string command, string? deviceSerial = null,
            TimeSpan? timeout = null, CancellationToken ct = default)
        {
            try
            {
                var args = string.IsNullOrEmpty(deviceSerial)
                    ? command
                    : $"-s {deviceSerial} {command}";

                var startInfo = new ProcessStartInfo
                {
                    FileName = _fastbootPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return "";

                var output = new StringBuilder();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout ?? TimeSpan.FromMinutes(5));

                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill();
                    throw;
                }

                var stdout = await outputTask;
                var stderr = await errorTask;

                if (!string.IsNullOrEmpty(stdout))
                {
                    output.AppendLine(stdout);
                    OutputReceived?.Invoke(this, stdout);
                }

                if (!string.IsNullOrEmpty(stderr))
                {
                    output.AppendLine(stderr);
                    // Fastboot sends info to stderr
                    OutputReceived?.Invoke(this, stderr);
                }

                return output.ToString();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(this, $"Fastboot error: {ex.Message}");
                return "";
            }
        }

        public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            try
            {
                var result = await ExecuteCommandAsync("echo CONNECTION_OK", null, TimeSpan.FromSeconds(5), ct);
                return result.Contains("CONNECTION_OK");
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> TestRootAsync(CancellationToken ct = default)
        {
            try
            {
                var result = await ExecuteRootCommandAsync("id", null, ct);
                return result.Contains("uid=0");
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GetDeviceInfoAsync(CancellationToken ct = default)
        {
            var info = new StringBuilder();

            try
            {
                info.AppendLine("=== Device Info ===");

                var model = await ExecuteCommandAsync("getprop ro.product.model", null, TimeSpan.FromSeconds(5), ct);
                info.AppendLine($"Model: {model.Trim()}");

                var manufacturer = await ExecuteCommandAsync("getprop ro.product.manufacturer", null, TimeSpan.FromSeconds(5), ct);
                info.AppendLine($"Manufacturer: {manufacturer.Trim()}");

                var android = await ExecuteCommandAsync("getprop ro.build.version.release", null, TimeSpan.FromSeconds(5), ct);
                info.AppendLine($"Android: {android.Trim()}");

                var sdk = await ExecuteCommandAsync("getprop ro.build.version.sdk", null, TimeSpan.FromSeconds(5), ct);
                info.AppendLine($"SDK: {sdk.Trim()}");

                var kernel = await ExecuteCommandAsync("uname -r", null, TimeSpan.FromSeconds(5), ct);
                info.AppendLine($"Kernel: {kernel.Trim()}");

                var uptime = await ExecuteCommandAsync("uptime", null, TimeSpan.FromSeconds(5), ct);
                info.AppendLine($"Uptime: {uptime.Trim()}");
            }
            catch (Exception ex)
            {
                info.AppendLine($"Error: {ex.Message}");
            }

            return info.ToString();
        }

        public async Task<Dictionary<string, string>> GetAllPropsAsync(CancellationToken ct = default)
        {
            var props = new Dictionary<string, string>();

            try
            {
                var result = await ExecuteCommandAsync("getprop", null, TimeSpan.FromSeconds(10), ct);
                var lines = result.Split('\n');

                foreach (var line in lines)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"\[(.+?)\]:\s*\[(.+?)\]");
                    if (match.Success)
                    {
                        props[match.Groups[1].Value] = match.Groups[2].Value;
                    }
                }
            }
            catch { }

            return props;
        }

        public async Task<string> RunScriptAsync(string scriptContent, CancellationToken ct = default)
        {
            var output = new StringBuilder();

            var lines = scriptContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;

                ct.ThrowIfCancellationRequested();

                OutputReceived?.Invoke(this, $"$ {line}");
                var result = await ExecuteCommandAsync(line.Trim(), null, TimeSpan.FromMinutes(2), ct);
                output.AppendLine($"$ {line}");
                output.AppendLine(result);
            }

            return output.ToString();
        }

        public void CloseSession(string? sessionId = null)
        {
            var session = string.IsNullOrEmpty(sessionId)
                ? _activeSession
                : _sessions.TryGetValue(sessionId, out var s) ? s : null;

            if (session != null)
            {
                session.IsConnected = false;
                session.Dispose();
                _sessions.TryRemove(session.Id, out _);

                if (_activeSession?.Id == session.Id)
                {
                    _activeSession = null;
                }

                StatusChanged?.Invoke(this, $"Session {session.Id} closed");
                ConnectionChanged?.Invoke(this, false);
            }
        }

        public void CloseAllSessions()
        {
            foreach (var session in _sessions.Values)
            {
                session.Dispose();
            }
            _sessions.Clear();
            _activeSession = null;

            StatusChanged?.Invoke(this, "All sessions closed");
            ConnectionChanged?.Invoke(this, false);
        }

        public List<TerminalSession> GetAllSessions()
        {
            return new List<TerminalSession>(_sessions.Values);
        }

        public void SetActiveSession(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                _activeSession = session;
                StatusChanged?.Invoke(this, $"Active session: {sessionId}");
            }
        }

        public async Task<string> GetShellEnvironmentAsync(CancellationToken ct = default)
        {
            return await ExecuteCommandAsync("env", null, TimeSpan.FromSeconds(5), ct);
        }

        public async Task<string> GetNetworkInfoAsync(CancellationToken ct = default)
        {
            var info = new StringBuilder();

            var ipAddr = await ExecuteCommandAsync("ip addr", null, TimeSpan.FromSeconds(5), ct);
            info.AppendLine("=== IP Addresses ===");
            info.AppendLine(ipAddr);

            var netstat = await ExecuteCommandAsync("netstat -tlnp 2>/dev/null || ss -tlnp", null, TimeSpan.FromSeconds(5), ct);
            info.AppendLine("=== Listening Ports ===");
            info.AppendLine(netstat);

            return info.ToString();
        }

        public async Task<string> GetStorageInfoAsync(CancellationToken ct = default)
        {
            return await ExecuteCommandAsync("df -h", null, TimeSpan.FromSeconds(5), ct);
        }

        public async Task<string> GetProcessListAsync(CancellationToken ct = default)
        {
            return await ExecuteCommandAsync("ps -A", null, TimeSpan.FromSeconds(10), ct);
        }

        public void Dispose()
        {
            CloseAllSessions();
        }
    }
}
