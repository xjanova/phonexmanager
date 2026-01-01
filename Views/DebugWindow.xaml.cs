using System;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace PhoneRomFlashTool.Views
{
    public partial class DebugWindow : Window
    {
        private static DebugWindow? _instance;
        private static readonly StringBuilder _logBuffer = new();
        private static int _errorCount;
        private static int _warningCount;
        private static int _infoCount;

        public static DebugWindow Instance
        {
            get
            {
                if (_instance == null || !_instance.IsLoaded)
                {
                    _instance = new DebugWindow();
                }
                return _instance;
            }
        }

        public DebugWindow()
        {
            InitializeComponent();
            RefreshLog();
        }

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var prefix = level switch
            {
                LogLevel.Error => "[ERROR]",
                LogLevel.Warning => "[WARN]",
                LogLevel.Info => "[INFO]",
                LogLevel.Debug => "[DEBUG]",
                _ => "[LOG]"
            };

            var logLine = $"[{timestamp}] {prefix} {message}\n";
            _logBuffer.Append(logLine);

            switch (level)
            {
                case LogLevel.Error: _errorCount++; break;
                case LogLevel.Warning: _warningCount++; break;
                default: _infoCount++; break;
            }

            _instance?.RefreshLog();
        }

        public static void LogError(string message) => Log(message, LogLevel.Error);
        public static void LogWarning(string message) => Log(message, LogLevel.Warning);
        public static void LogInfo(string message) => Log(message, LogLevel.Info);
        public static void LogDebug(string message) => Log(message, LogLevel.Debug);

        public static void LogException(Exception ex, string context = "")
        {
            var message = string.IsNullOrEmpty(context)
                ? $"Exception: {ex.Message}\n{ex.StackTrace}"
                : $"{context}: {ex.Message}\n{ex.StackTrace}";
            Log(message, LogLevel.Error);
        }

        private void RefreshLog()
        {
            Dispatcher.Invoke(() =>
            {
                DebugLogTextBox.Text = _logBuffer.ToString();
                DebugLogTextBox.ScrollToEnd();
                ErrorCount.Text = _errorCount.ToString();
                WarningCount.Text = _warningCount.ToString();
                InfoCount.Text = _infoCount.ToString();
            });
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _logBuffer.Clear();
            _errorCount = 0;
            _warningCount = 0;
            _infoCount = 0;
            RefreshLog();
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var systemInfo = GetSystemInfo();
                var fullLog = $"=== PhoneX Manager Debug Log ===\n{systemInfo}\n\n=== Log ===\n{_logBuffer}";
                Clipboard.SetText(fullLog);
                MessageBox.Show("Debug log copied to clipboard!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SendToDev_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var systemInfo = GetSystemInfo();
                var subject = Uri.EscapeDataString("PhoneX Manager - Bug Report");
                var body = Uri.EscapeDataString($"=== PhoneX Manager Debug Log ===\n{systemInfo}\n\n=== Log ===\n{_logBuffer}");

                // Truncate if too long for mailto
                if (body.Length > 1800)
                {
                    body = Uri.EscapeDataString($"=== PhoneX Manager Debug Log ===\n{systemInfo}\n\n[Log truncated - please paste from clipboard]\n\nErrors: {_errorCount}, Warnings: {_warningCount}");
                    Clipboard.SetText($"=== PhoneX Manager Debug Log ===\n{systemInfo}\n\n=== Log ===\n{_logBuffer}");
                }

                var mailto = $"mailto:theboymaniii@gmail.com?subject={subject}&body={body}";
                Process.Start(new ProcessStartInfo(mailto) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open email client: {ex.Message}\n\nPlease manually send the log to: theboymaniii@gmail.com",
                    "Email Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string GetSystemInfo()
        {
            return $@"App Version: v1.0.0
OS: {Environment.OSVersion}
.NET Runtime: {Environment.Version}
Machine: {Environment.MachineName}
User: {Environment.UserName}
Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Errors: {_errorCount}
Warnings: {_warningCount}";
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _instance = null;
        }
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}
