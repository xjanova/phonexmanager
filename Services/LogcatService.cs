using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public enum LogLevel
    {
        Verbose,
        Debug,
        Info,
        Warning,
        Error,
        Fatal,
        Silent
    }

    public class LogcatEntry
    {
        public DateTime Timestamp { get; set; }
        public int ProcessId { get; set; }
        public int ThreadId { get; set; }
        public LogLevel Level { get; set; }
        public string Tag { get; set; } = "";
        public string Message { get; set; } = "";
        public string RawLine { get; set; } = "";

        public string LevelChar => Level switch
        {
            LogLevel.Verbose => "V",
            LogLevel.Debug => "D",
            LogLevel.Info => "I",
            LogLevel.Warning => "W",
            LogLevel.Error => "E",
            LogLevel.Fatal => "F",
            _ => "?"
        };

        public string FormattedLine => $"{Timestamp:HH:mm:ss.fff} {ProcessId,5} {ThreadId,5} {LevelChar} {Tag}: {Message}";
    }

    public class LogcatFilter
    {
        public LogLevel MinLevel { get; set; } = LogLevel.Verbose;
        public string? TagFilter { get; set; }
        public string? MessageFilter { get; set; }
        public int? ProcessId { get; set; }
        public bool UseRegex { get; set; }

        public bool Matches(LogcatEntry entry)
        {
            if (entry.Level < MinLevel) return false;

            if (ProcessId.HasValue && entry.ProcessId != ProcessId.Value) return false;

            if (!string.IsNullOrEmpty(TagFilter))
            {
                if (UseRegex)
                {
                    if (!Regex.IsMatch(entry.Tag, TagFilter, RegexOptions.IgnoreCase))
                        return false;
                }
                else
                {
                    if (!entry.Tag.Contains(TagFilter, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }

            if (!string.IsNullOrEmpty(MessageFilter))
            {
                if (UseRegex)
                {
                    if (!Regex.IsMatch(entry.Message, MessageFilter, RegexOptions.IgnoreCase))
                        return false;
                }
                else
                {
                    if (!entry.Message.Contains(MessageFilter, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }

            return true;
        }
    }

    public class LogcatService : IDisposable
    {
        private readonly string _adbPath;
        private Process? _logcatProcess;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentQueue<LogcatEntry> _logBuffer;
        private readonly int _maxBufferSize;
        private bool _isRunning;

        public event EventHandler<LogcatEntry>? LogReceived;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<Exception>? ErrorOccurred;

        public bool IsRunning => _isRunning;
        public IEnumerable<LogcatEntry> BufferedLogs => _logBuffer.ToArray();

        // Standard logcat format regex
        // Format: MM-DD HH:MM:SS.mmm PID TID LEVEL TAG: MESSAGE
        private static readonly Regex LogcatRegex = new(
            @"^(\d{2}-\d{2})\s+(\d{2}:\d{2}:\d{2}\.\d{3})\s+(\d+)\s+(\d+)\s+([VDIWEF])\s+(.+?):\s*(.*)$",
            RegexOptions.Compiled);

        // Brief format: LEVEL/TAG(PID): MESSAGE
        private static readonly Regex BriefRegex = new(
            @"^([VDIWEF])/(.+?)\(\s*(\d+)\):\s*(.*)$",
            RegexOptions.Compiled);

        public LogcatService(string adbPath, int maxBufferSize = 10000)
        {
            _adbPath = adbPath;
            _maxBufferSize = maxBufferSize;
            _logBuffer = new ConcurrentQueue<LogcatEntry>();
        }

        public async Task StartAsync(LogcatFilter? filter = null, string format = "threadtime",
            CancellationToken ct = default)
        {
            if (_isRunning)
            {
                StatusChanged?.Invoke(this, "Logcat already running");
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                var args = $"logcat -v {format}";

                // Add level filter
                if (filter?.MinLevel > LogLevel.Verbose)
                {
                    var levelChar = filter.MinLevel switch
                    {
                        LogLevel.Debug => "D",
                        LogLevel.Info => "I",
                        LogLevel.Warning => "W",
                        LogLevel.Error => "E",
                        LogLevel.Fatal => "F",
                        _ => "V"
                    };
                    args += $" *:{levelChar}";
                }

                // Add tag filter
                if (!string.IsNullOrEmpty(filter?.TagFilter) && !filter.UseRegex)
                {
                    args += $" -s {filter.TagFilter}";
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _logcatProcess = new Process { StartInfo = startInfo };
                _logcatProcess.Start();
                _isRunning = true;

                StatusChanged?.Invoke(this, "Logcat started");

                // Start reading output
                _ = Task.Run(() => ReadOutputAsync(filter, _cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                throw;
            }
        }

        private async Task ReadOutputAsync(LogcatFilter? filter, CancellationToken ct)
        {
            try
            {
                if (_logcatProcess == null) return;

                using var reader = _logcatProcess.StandardOutput;

                while (!ct.IsCancellationRequested && !reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (string.IsNullOrEmpty(line)) continue;

                    var entry = ParseLogLine(line);
                    if (entry == null) continue;

                    // Apply filter
                    if (filter != null && !filter.Matches(entry)) continue;

                    // Add to buffer
                    _logBuffer.Enqueue(entry);

                    // Trim buffer if needed
                    while (_logBuffer.Count > _maxBufferSize)
                    {
                        _logBuffer.TryDequeue(out _);
                    }

                    // Notify listeners
                    LogReceived?.Invoke(this, entry);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
            finally
            {
                _isRunning = false;
                StatusChanged?.Invoke(this, "Logcat stopped");
            }
        }

        private LogcatEntry? ParseLogLine(string line)
        {
            // Try threadtime format first
            var match = LogcatRegex.Match(line);
            if (match.Success)
            {
                var year = DateTime.Now.Year;
                var dateStr = $"{year}-{match.Groups[1].Value} {match.Groups[2].Value}";

                return new LogcatEntry
                {
                    Timestamp = DateTime.TryParse(dateStr, out var ts) ? ts : DateTime.Now,
                    ProcessId = int.TryParse(match.Groups[3].Value, out var pid) ? pid : 0,
                    ThreadId = int.TryParse(match.Groups[4].Value, out var tid) ? tid : 0,
                    Level = ParseLevel(match.Groups[5].Value),
                    Tag = match.Groups[6].Value.Trim(),
                    Message = match.Groups[7].Value,
                    RawLine = line
                };
            }

            // Try brief format
            match = BriefRegex.Match(line);
            if (match.Success)
            {
                return new LogcatEntry
                {
                    Timestamp = DateTime.Now,
                    ProcessId = int.TryParse(match.Groups[3].Value, out var pid) ? pid : 0,
                    Level = ParseLevel(match.Groups[1].Value),
                    Tag = match.Groups[2].Value.Trim(),
                    Message = match.Groups[4].Value,
                    RawLine = line
                };
            }

            // Return as-is if no match
            return new LogcatEntry
            {
                Timestamp = DateTime.Now,
                Level = LogLevel.Info,
                Tag = "raw",
                Message = line,
                RawLine = line
            };
        }

        private static LogLevel ParseLevel(string level)
        {
            return level.ToUpperInvariant() switch
            {
                "V" => LogLevel.Verbose,
                "D" => LogLevel.Debug,
                "I" => LogLevel.Info,
                "W" => LogLevel.Warning,
                "E" => LogLevel.Error,
                "F" => LogLevel.Fatal,
                _ => LogLevel.Info
            };
        }

        public void Stop()
        {
            _cts?.Cancel();

            try
            {
                if (_logcatProcess != null && !_logcatProcess.HasExited)
                {
                    _logcatProcess.Kill();
                    _logcatProcess.Dispose();
                    _logcatProcess = null;
                }
            }
            catch { }

            _isRunning = false;
            StatusChanged?.Invoke(this, "Logcat stopped");
        }

        public async Task ClearLogsAsync(CancellationToken ct = default)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = "logcat -c",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync(ct);
                }

                // Clear local buffer
                while (_logBuffer.TryDequeue(out _)) { }

                StatusChanged?.Invoke(this, "Logs cleared");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        public async Task<List<LogcatEntry>> GetRecentLogsAsync(int count = 1000,
            LogcatFilter? filter = null, CancellationToken ct = default)
        {
            var entries = new List<LogcatEntry>();

            try
            {
                var args = $"logcat -d -t {count} -v threadtime";

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
                if (process == null) return entries;

                while (!process.StandardOutput.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();

                    var line = await process.StandardOutput.ReadLineAsync(ct);
                    if (string.IsNullOrEmpty(line)) continue;

                    var entry = ParseLogLine(line);
                    if (entry == null) continue;

                    if (filter == null || filter.Matches(entry))
                    {
                        entries.Add(entry);
                    }
                }

                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }

            return entries;
        }

        public async Task<bool> SaveLogsAsync(string outputPath, IEnumerable<LogcatEntry>? entries = null,
            CancellationToken ct = default)
        {
            try
            {
                var logsToSave = entries ?? _logBuffer.ToArray();

                using var writer = new StreamWriter(outputPath);

                foreach (var entry in logsToSave)
                {
                    ct.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(entry.FormattedLine);
                }

                StatusChanged?.Invoke(this, $"Saved {logsToSave.Count()} logs to {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                return false;
            }
        }

        public async Task<string> GetDmesgAsync(bool requireRoot = true, CancellationToken ct = default)
        {
            try
            {
                var args = requireRoot ? "shell su -c dmesg" : "shell dmesg";

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

                var output = await process.StandardOutput.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                return output;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                return "";
            }
        }

        public async Task<List<(int pid, string name)>> GetRunningProcessesAsync(CancellationToken ct = default)
        {
            var processes = new List<(int pid, string name)>();

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = "shell ps -A",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return processes;

                var lines = (await process.StandardOutput.ReadToEndAsync(ct)).Split('\n');
                await process.WaitForExitAsync(ct);

                foreach (var line in lines.Skip(1)) // Skip header
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 9)
                    {
                        if (int.TryParse(parts[1], out var pid))
                        {
                            processes.Add((pid, parts[8]));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }

            return processes;
        }

        public async Task<Dictionary<string, int>> GetLogStatisticsAsync(CancellationToken ct = default)
        {
            var stats = new Dictionary<string, int>();
            var entries = await GetRecentLogsAsync(5000, null, ct);

            stats["Total"] = entries.Count;
            stats["Verbose"] = entries.Count(e => e.Level == LogLevel.Verbose);
            stats["Debug"] = entries.Count(e => e.Level == LogLevel.Debug);
            stats["Info"] = entries.Count(e => e.Level == LogLevel.Info);
            stats["Warning"] = entries.Count(e => e.Level == LogLevel.Warning);
            stats["Error"] = entries.Count(e => e.Level == LogLevel.Error);
            stats["Fatal"] = entries.Count(e => e.Level == LogLevel.Fatal);

            // Top tags by count
            var tagCounts = entries.GroupBy(e => e.Tag)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => $"Tag:{g.Key}", g => g.Count());

            foreach (var kv in tagCounts)
            {
                stats[kv.Key] = kv.Value;
            }

            return stats;
        }

        public async Task StartBugreportAsync(string outputPath, IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            try
            {
                StatusChanged?.Invoke(this, "Generating bugreport...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = $"bugreport \"{outputPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return;

                // Monitor progress from stderr
                var errorTask = Task.Run(async () =>
                {
                    while (!process.StandardError.EndOfStream)
                    {
                        var line = await process.StandardError.ReadLineAsync(ct);
                        if (line?.Contains('%') == true)
                        {
                            var match = Regex.Match(line, @"(\d+)%");
                            if (match.Success && int.TryParse(match.Groups[1].Value, out var pct))
                            {
                                progress?.Report(pct);
                            }
                        }
                    }
                }, ct);

                await process.WaitForExitAsync(ct);
                await errorTask;

                StatusChanged?.Invoke(this, $"Bugreport saved to: {outputPath}");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
