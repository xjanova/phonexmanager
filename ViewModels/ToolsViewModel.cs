using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using PhoneRomFlashTool.Services;

namespace PhoneRomFlashTool.ViewModels
{
    public class ToolsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<string>? LogMessage;

        private readonly AdbTerminalService? _adbService;
        private readonly ChecksumService _checksumService;

        // ADB Terminal
        private string _terminalInput = "";
        public string TerminalInput
        {
            get => _terminalInput;
            set { _terminalInput = value; OnPropertyChanged(); }
        }

        private string _terminalOutput = "";
        public string TerminalOutput
        {
            get => _terminalOutput;
            set { _terminalOutput = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> CommandHistory { get; } = new();

        // Device Info
        private string _deviceInfo = "";
        public string DeviceInfo
        {
            get => _deviceInfo;
            set { _deviceInfo = value; OnPropertyChanged(); }
        }

        // Checksum
        private string _checksumFilePath = "";
        public string ChecksumFilePath
        {
            get => _checksumFilePath;
            set { _checksumFilePath = value; OnPropertyChanged(); }
        }

        private string _expectedHash = "";
        public string ExpectedHash
        {
            get => _expectedHash;
            set { _expectedHash = value; OnPropertyChanged(); }
        }

        private ChecksumResult? _checksumResult;
        public ChecksumResult? ChecksumResult
        {
            get => _checksumResult;
            set { _checksumResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChecksumResult)); }
        }

        public bool HasChecksumResult => ChecksumResult != null;

        private int _checksumProgress;
        public int ChecksumProgress
        {
            get => _checksumProgress;
            set { _checksumProgress = value; OnPropertyChanged(); }
        }

        private bool _isCalculating;
        public bool IsCalculating
        {
            get => _isCalculating;
            set { _isCalculating = value; OnPropertyChanged(); }
        }

        // Logcat
        private string _logcatOutput = "";
        public string LogcatOutput
        {
            get => _logcatOutput;
            set { _logcatOutput = value; OnPropertyChanged(); }
        }

        private int _logcatLines = 100;
        public int LogcatLines
        {
            get => _logcatLines;
            set { _logcatLines = value; OnPropertyChanged(); }
        }

        // Commands
        public ICommand ExecuteCommandCommand { get; }
        public ICommand GetDeviceInfoCommand { get; }
        public ICommand BrowseFileCommand { get; }
        public ICommand CalculateChecksumCommand { get; }
        public ICommand VerifyChecksumCommand { get; }
        public ICommand CopyHashCommand { get; }
        public ICommand TakeScreenshotCommand { get; }
        public ICommand GetLogcatCommand { get; }
        public ICommand ClearLogcatCommand { get; }
        public ICommand QuickCommandCommand { get; }

        public ToolsViewModel()
        {
            _checksumService = new ChecksumService();
            _checksumService.ProgressChanged += (s, p) => ChecksumProgress = p;

            // Try to find ADB
            var adbPath = FindAdbPath();
            if (!string.IsNullOrEmpty(adbPath))
            {
                _adbService = new AdbTerminalService(adbPath);
                _adbService.OutputReceived += (s, output) =>
                    TerminalOutput += output + Environment.NewLine;
            }

            ExecuteCommandCommand = new RelayCommand(async () => await ExecuteCommand());
            GetDeviceInfoCommand = new RelayCommand(async () => await GetDeviceInfo());
            BrowseFileCommand = new RelayCommand(BrowseFile);
            CalculateChecksumCommand = new RelayCommand(async () => await CalculateChecksum());
            VerifyChecksumCommand = new RelayCommand(async () => await VerifyChecksum());
            CopyHashCommand = new RelayCommandWithParam<string>(CopyHash);
            TakeScreenshotCommand = new RelayCommand(async () => await TakeScreenshot());
            GetLogcatCommand = new RelayCommand(async () => await GetLogcat());
            ClearLogcatCommand = new RelayCommand(async () => await ClearLogcat());
            QuickCommandCommand = new RelayCommandWithParam<string>(async (cmd) => await ExecuteQuickCommand(cmd));

            TerminalOutput = "ADB Terminal Ready.\nType 'help' for available commands.\n\n";
        }

        private string FindAdbPath()
        {
            // Check common locations
            var possiblePaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhoneRomFlashTool", "Tools", "platform-tools", "adb.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "Local", "Android", "Sdk", "platform-tools", "adb.exe"),
                @"C:\platform-tools\adb.exe",
                @"C:\adb\adb.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return "";
        }

        private async Task ExecuteCommand()
        {
            if (string.IsNullOrWhiteSpace(TerminalInput)) return;

            var command = TerminalInput.Trim();
            CommandHistory.Insert(0, command);
            TerminalOutput += $"> {command}\n";
            TerminalInput = "";

            if (command.ToLower() == "help")
            {
                TerminalOutput += GetHelpText();
                return;
            }

            if (command.ToLower() == "clear")
            {
                TerminalOutput = "";
                return;
            }

            if (_adbService == null)
            {
                TerminalOutput += "[ERROR] ADB not found. Please download Platform Tools first.\n\n";
                return;
            }

            try
            {
                var result = await _adbService.ExecuteCommandAsync(command);
                TerminalOutput += result + "\n";
                LogMessage?.Invoke(this, $"Executed: {command}");
            }
            catch (Exception ex)
            {
                TerminalOutput += $"[ERROR] {ex.Message}\n\n";
            }
        }

        private async Task ExecuteQuickCommand(string? command)
        {
            if (string.IsNullOrEmpty(command) || _adbService == null) return;

            TerminalOutput += $"> {command}\n";

            try
            {
                var result = await _adbService.ExecuteCommandAsync(command);
                TerminalOutput += result + "\n";
            }
            catch (Exception ex)
            {
                TerminalOutput += $"[ERROR] {ex.Message}\n\n";
            }
        }

        private string GetHelpText()
        {
            return @"
=== ADB Terminal Commands ===

Device:
  devices              - List connected devices
  shell                - Open shell
  reboot              - Reboot device
  reboot recovery     - Reboot to recovery
  reboot bootloader   - Reboot to fastboot

Apps:
  install <apk>       - Install APK
  uninstall <package> - Uninstall app
  shell pm list packages - List packages

Files:
  push <local> <remote>   - Push file to device
  pull <remote> <local>   - Pull file from device

Info:
  shell getprop            - Get all properties
  shell dumpsys battery    - Battery info
  shell df                 - Storage info

Terminal:
  help                - Show this help
  clear               - Clear terminal

";
        }

        private async Task GetDeviceInfo()
        {
            if (_adbService == null)
            {
                DeviceInfo = "ADB not found. Please download Platform Tools first.";
                return;
            }

            DeviceInfo = "Loading device information...";

            try
            {
                DeviceInfo = await _adbService.GetDeviceInfoAsync();
                LogMessage?.Invoke(this, "Device info loaded");
            }
            catch (Exception ex)
            {
                DeviceInfo = $"Error: {ex.Message}";
            }
        }

        private void BrowseFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "ROM Files|*.zip;*.img;*.tar;*.md5;*.bin|All Files|*.*",
                Title = "Select ROM File to Verify"
            };

            if (dialog.ShowDialog() == true)
            {
                ChecksumFilePath = dialog.FileName;
            }
        }

        private async Task CalculateChecksum()
        {
            if (string.IsNullOrEmpty(ChecksumFilePath) || !File.Exists(ChecksumFilePath))
            {
                LogMessage?.Invoke(this, "Please select a valid file");
                return;
            }

            IsCalculating = true;
            ChecksumProgress = 0;
            ChecksumResult = null;

            try
            {
                ChecksumResult = await _checksumService.CalculateChecksumsAsync(ChecksumFilePath);
                LogMessage?.Invoke(this, $"Checksums calculated for {ChecksumResult.FileName}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error: {ex.Message}");
            }
            finally
            {
                IsCalculating = false;
            }
        }

        private async Task VerifyChecksum()
        {
            if (string.IsNullOrEmpty(ChecksumFilePath) || !File.Exists(ChecksumFilePath))
            {
                LogMessage?.Invoke(this, "Please select a valid file");
                return;
            }

            if (string.IsNullOrEmpty(ExpectedHash))
            {
                LogMessage?.Invoke(this, "Please enter expected hash");
                return;
            }

            IsCalculating = true;
            ChecksumProgress = 0;
            ChecksumResult = null;

            try
            {
                ChecksumResult = await _checksumService.VerifyChecksumAsync(ChecksumFilePath, ExpectedHash);

                if (ChecksumResult.IsValid)
                {
                    LogMessage?.Invoke(this, $"Checksum VALID! Matched {ChecksumResult.MatchedAlgorithm}");
                }
                else
                {
                    LogMessage?.Invoke(this, "Checksum INVALID! File may be corrupted or wrong file.");
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error: {ex.Message}");
            }
            finally
            {
                IsCalculating = false;
            }
        }

        private void CopyHash(string? hashType)
        {
            if (ChecksumResult == null) return;

            var hash = hashType switch
            {
                "MD5" => ChecksumResult.MD5,
                "SHA1" => ChecksumResult.SHA1,
                "SHA256" => ChecksumResult.SHA256,
                _ => ""
            };

            if (!string.IsNullOrEmpty(hash))
            {
                System.Windows.Clipboard.SetText(hash);
                LogMessage?.Invoke(this, $"{hashType} copied to clipboard");
            }
        }

        private async Task TakeScreenshot()
        {
            if (_adbService == null)
            {
                LogMessage?.Invoke(this, "ADB not found");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image|*.png",
                FileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var result = await _adbService.TakeScreenshotAsync(dialog.FileName);
                    LogMessage?.Invoke(this, result);
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke(this, $"Error: {ex.Message}");
                }
            }
        }

        private async Task GetLogcat()
        {
            if (_adbService == null)
            {
                LogcatOutput = "ADB not found";
                return;
            }

            LogcatOutput = "Loading logcat...";

            try
            {
                LogcatOutput = await _adbService.GetLogcatAsync(LogcatLines);
                LogMessage?.Invoke(this, $"Loaded {LogcatLines} logcat lines");
            }
            catch (Exception ex)
            {
                LogcatOutput = $"Error: {ex.Message}";
            }
        }

        private async Task ClearLogcat()
        {
            if (_adbService == null) return;

            try
            {
                await _adbService.ClearLogcatAsync();
                LogcatOutput = "Logcat cleared.";
                LogMessage?.Invoke(this, "Logcat cleared");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error: {ex.Message}");
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
