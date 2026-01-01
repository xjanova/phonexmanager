using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using PhoneRomFlashTool.Services;

namespace PhoneRomFlashTool.ViewModels
{
    /// <summary>
    /// Tool category for organizing tools in UI
    /// </summary>
    public enum ToolCategory
    {
        Essential,      // ADB, Fastboot
        Samsung,        // Thor, Heimdall, Freya, SamFirm
        Qualcomm,       // EDL Client, EDL-NG
        MediaTek,       // MTK Client
        Xiaomi,         // Xiaomi Tools
        Huawei,         // Huawei Unlock
        Unisoc,         // Unisoc Unlock
        Utility         // Scrcpy
    }

    /// <summary>
    /// Represents a tool with UI-friendly properties
    /// </summary>
    public class ToolInfo : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Tooltip { get; set; } = "";
        public ToolCategory Category { get; set; }
        public string Icon { get; set; } = "🔧";
        public bool RequiresPython { get; set; }
        public bool RequiresJava { get; set; }
        public bool RequiresDotNet { get; set; }

        private bool _isInstalled;
        public bool IsInstalled
        {
            get => _isInstalled;
            set { _isInstalled = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); }
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); }
        }

        public string StatusText => IsInstalled ? "Installed" : "Not Installed";
        public string StatusColor => IsInstalled ? "#10B981" : "#EF4444";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ToolsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<string>? LogMessage;

        private readonly AdbTerminalService? _adbService;
        private readonly ChecksumService _checksumService;
        private readonly ToolsDownloadService _toolsService;
        private readonly string _toolsPath;

        // Tools Collection - organized by category
        public ObservableCollection<ToolInfo> AllTools { get; } = new();
        public ObservableCollection<ToolInfo> EssentialTools { get; } = new();
        public ObservableCollection<ToolInfo> SamsungTools { get; } = new();
        public ObservableCollection<ToolInfo> QualcommTools { get; } = new();
        public ObservableCollection<ToolInfo> MediaTekTools { get; } = new();
        public ObservableCollection<ToolInfo> OtherTools { get; } = new();

        // Selected Tool
        private ToolInfo? _selectedTool;
        public ToolInfo? SelectedTool
        {
            get => _selectedTool;
            set { _selectedTool = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedTool)); }
        }
        public bool HasSelectedTool => SelectedTool != null;

        // Tool operation status
        private string _toolStatus = "Ready";
        public string ToolStatus
        {
            get => _toolStatus;
            set { _toolStatus = value; OnPropertyChanged(); }
        }

        private int _toolProgress;
        public int ToolProgress
        {
            get => _toolProgress;
            set { _toolProgress = value; OnPropertyChanged(); }
        }

        private bool _isToolOperating;
        public bool IsToolOperating
        {
            get => _isToolOperating;
            set { _isToolOperating = value; OnPropertyChanged(); }
        }

        // Selected Tool Category Tab
        private int _selectedCategoryIndex;
        public int SelectedCategoryIndex
        {
            get => _selectedCategoryIndex;
            set { _selectedCategoryIndex = value; OnPropertyChanged(); }
        }

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

        // Tool Management Commands
        public ICommand LaunchToolCommand { get; }
        public ICommand InstallToolCommand { get; }
        public ICommand OpenToolFolderCommand { get; }
        public ICommand RefreshToolsCommand { get; }

        // Fastboot Commands
        public ICommand FastbootDevicesCommand { get; }
        public ICommand FastbootRebootCommand { get; }
        public ICommand FastbootFlashCommand { get; }
        public ICommand FastbootUnlockCommand { get; }
        public ICommand FastbootLockCommand { get; }
        public ICommand FastbootGetVarCommand { get; }
        public ICommand BrowseFastbootImageCommand { get; }

        // Scrcpy Commands
        public ICommand LaunchScrcpyCommand { get; }
        public ICommand LaunchScrcpyRecordCommand { get; }

        // Fastboot properties
        private string _fastbootPartition = "boot";
        public string FastbootPartition
        {
            get => _fastbootPartition;
            set { _fastbootPartition = value; OnPropertyChanged(); }
        }

        private string _fastbootImagePath = "";
        public string FastbootImagePath
        {
            get => _fastbootImagePath;
            set { _fastbootImagePath = value; OnPropertyChanged(); }
        }

        private string _fastbootOutput = "";
        public string FastbootOutput
        {
            get => _fastbootOutput;
            set { _fastbootOutput = value; OnPropertyChanged(); }
        }

        // Scrcpy properties
        private int _scrcpyBitrate = 8;
        public int ScrcpyBitrate
        {
            get => _scrcpyBitrate;
            set { _scrcpyBitrate = value; OnPropertyChanged(); }
        }

        private int _scrcpyMaxFps = 60;
        public int ScrcpyMaxFps
        {
            get => _scrcpyMaxFps;
            set { _scrcpyMaxFps = value; OnPropertyChanged(); }
        }

        private bool _scrcpyFullscreen;
        public bool ScrcpyFullscreen
        {
            get => _scrcpyFullscreen;
            set { _scrcpyFullscreen = value; OnPropertyChanged(); }
        }

        private bool _scrcpyShowTouches;
        public bool ScrcpyShowTouches
        {
            get => _scrcpyShowTouches;
            set { _scrcpyShowTouches = value; OnPropertyChanged(); }
        }

        private bool _scrcpyStayAwake = true;
        public bool ScrcpyStayAwake
        {
            get => _scrcpyStayAwake;
            set { _scrcpyStayAwake = value; OnPropertyChanged(); }
        }

        public ToolsViewModel()
        {
            _toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            Directory.CreateDirectory(_toolsPath);

            _checksumService = new ChecksumService();
            _checksumService.ProgressChanged += (s, p) => ChecksumProgress = p;

            _toolsService = new ToolsDownloadService();
            _toolsService.ProgressChanged += (s, p) => ToolProgress = p;
            _toolsService.StatusChanged += (s, status) => ToolStatus = status;
            _toolsService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);

            // Try to find ADB
            var adbPath = FindAdbPath();
            if (!string.IsNullOrEmpty(adbPath))
            {
                _adbService = new AdbTerminalService(adbPath);
                _adbService.OutputReceived += (s, output) =>
                    TerminalOutput += output + Environment.NewLine;
            }

            // Initialize commands
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

            // Tool management commands
            LaunchToolCommand = new RelayCommandWithParam<ToolInfo>(async (tool) => await LaunchTool(tool));
            InstallToolCommand = new RelayCommandWithParam<ToolInfo>(async (tool) => await InstallTool(tool));
            OpenToolFolderCommand = new RelayCommand(OpenToolFolder);
            RefreshToolsCommand = new RelayCommand(async () => await RefreshToolsStatus());

            // Fastboot commands
            FastbootDevicesCommand = new RelayCommand(async () => await ExecuteFastboot("devices"));
            FastbootRebootCommand = new RelayCommand(async () => await ExecuteFastboot("reboot"));
            FastbootFlashCommand = new RelayCommand(async () => await FlashPartition());
            FastbootUnlockCommand = new RelayCommand(async () => await ExecuteFastboot("flashing unlock"));
            FastbootLockCommand = new RelayCommand(async () => await ExecuteFastboot("flashing lock"));
            FastbootGetVarCommand = new RelayCommand(async () => await ExecuteFastboot("getvar all"));
            BrowseFastbootImageCommand = new RelayCommand(BrowseFastbootImage);

            // Scrcpy commands
            LaunchScrcpyCommand = new RelayCommand(async () => await LaunchScrcpy(false));
            LaunchScrcpyRecordCommand = new RelayCommand(async () => await LaunchScrcpy(true));

            // Initialize tools list
            InitializeToolsList();

            TerminalOutput = "ADB Terminal Ready.\nType 'help' for available commands.\n\n";
            FastbootOutput = "Fastboot Ready.\nConnect device in Fastboot mode to begin.\n";

            // Check tools status on startup
            _ = RefreshToolsStatus();
        }

        private void InitializeToolsList()
        {
            // Essential Tools
            AllTools.Add(new ToolInfo
            {
                Id = "adb",
                Name = "ADB (Android Debug Bridge)",
                Description = "Command-line tool for Android device communication",
                Tooltip = "ADB allows you to communicate with Android devices, install apps, transfer files, and run shell commands.",
                Category = ToolCategory.Essential,
                Icon = "📱"
            });

            AllTools.Add(new ToolInfo
            {
                Id = "fastboot",
                Name = "Fastboot",
                Description = "Flash partitions, unlock bootloader",
                Tooltip = "Fastboot is used to flash system images, unlock bootloaders, and modify partitions on Android devices.",
                Category = ToolCategory.Essential,
                Icon = "⚡"
            });

            // Samsung Tools
            AllTools.Add(new ToolInfo
            {
                Id = "thor",
                Name = "Thor (Samsung-Loki)",
                Description = "Modern Samsung flash tool - .NET based",
                Tooltip = "Thor is a modern alternative to Odin. It can flash Samsung firmware with better performance and stability. Requires .NET 7+.",
                Category = ToolCategory.Samsung,
                Icon = "⚡",
                RequiresDotNet = true
            });

            AllTools.Add(new ToolInfo
            {
                Id = "heimdall",
                Name = "Heimdall",
                Description = "Open-source Samsung flash tool",
                Tooltip = "Heimdall is an open-source cross-platform tool for flashing Samsung devices. Alternative to Odin.",
                Category = ToolCategory.Samsung,
                Icon = "🔷"
            });

            AllTools.Add(new ToolInfo
            {
                Id = "freya",
                Name = "Freya",
                Description = "Samsung flash tool with fast features",
                Tooltip = "Freya provides fast read/write operations for Samsung devices including partition repartitioning.",
                Category = ToolCategory.Samsung,
                Icon = "❄️"
            });

            AllTools.Add(new ToolInfo
            {
                Id = "samfirm",
                Name = "SamFirm",
                Description = "Samsung firmware downloader",
                Tooltip = "Download Samsung firmware directly from official Samsung servers. No account required.",
                Category = ToolCategory.Samsung,
                Icon = "📥"
            });

            // Qualcomm Tools
            AllTools.Add(new ToolInfo
            {
                Id = "edl-client",
                Name = "EDL Client (bkerler)",
                Description = "Qualcomm EDL/Firehose tool",
                Tooltip = "EDL Client allows communication with Qualcomm devices in EDL (Emergency Download) mode. Requires Python.",
                Category = ToolCategory.Qualcomm,
                Icon = "🔌",
                RequiresPython = true
            });

            AllTools.Add(new ToolInfo
            {
                Id = "edl-ng",
                Name = "EDL-NG",
                Description = "Modern Qualcomm EDL tool with GUI",
                Tooltip = "Next-generation EDL tool with graphical interface. Easier to use than command-line EDL client. Requires .NET 9.",
                Category = ToolCategory.Qualcomm,
                Icon = "🆕",
                RequiresDotNet = true
            });

            // MediaTek Tools
            AllTools.Add(new ToolInfo
            {
                Id = "mtk-client",
                Name = "MTK Client (bkerler)",
                Description = "MediaTek BROM/Preloader tool",
                Tooltip = "MTK Client communicates with MediaTek devices in BROM/Preloader mode for flashing and unlocking. Requires Python.",
                Category = ToolCategory.MediaTek,
                Icon = "🔧",
                RequiresPython = true
            });

            // Other Tools
            AllTools.Add(new ToolInfo
            {
                Id = "xiaomi-tools",
                Name = "Xiaomi ADB/Fastboot Tools",
                Description = "Complete Xiaomi device management",
                Tooltip = "All-in-one tool for Xiaomi devices: unlock bootloader, flash ROM, manage apps. Requires Java.",
                Category = ToolCategory.Xiaomi,
                Icon = "🍊",
                RequiresJava = true
            });

            AllTools.Add(new ToolInfo
            {
                Id = "huawei-unlock",
                Name = "Huawei Unlock Tool",
                Description = "Huawei bootloader unlock & FRP bypass",
                Tooltip = "Unlock Huawei bootloaders and bypass FRP (Factory Reset Protection).",
                Category = ToolCategory.Huawei,
                Icon = "🔓"
            });

            AllTools.Add(new ToolInfo
            {
                Id = "unisoc-unlock",
                Name = "Unisoc Bootloader Unlock",
                Description = "Unisoc/Spreadtrum unlock tool",
                Tooltip = "Unlock bootloader on Unisoc/Spreadtrum devices using CVE-2022-38694 exploit. Requires Python.",
                Category = ToolCategory.Unisoc,
                Icon = "🔑",
                RequiresPython = true
            });

            AllTools.Add(new ToolInfo
            {
                Id = "scrcpy",
                Name = "Scrcpy",
                Description = "Android screen mirror & control",
                Tooltip = "Mirror and control your Android device screen on PC via USB or WiFi. Very low latency.",
                Category = ToolCategory.Utility,
                Icon = "🖥️"
            });

            // Organize by category
            foreach (var tool in AllTools)
            {
                switch (tool.Category)
                {
                    case ToolCategory.Essential:
                        EssentialTools.Add(tool);
                        break;
                    case ToolCategory.Samsung:
                        SamsungTools.Add(tool);
                        break;
                    case ToolCategory.Qualcomm:
                        QualcommTools.Add(tool);
                        break;
                    case ToolCategory.MediaTek:
                        MediaTekTools.Add(tool);
                        break;
                    default:
                        OtherTools.Add(tool);
                        break;
                }
            }
        }

        private async Task RefreshToolsStatus()
        {
            try
            {
                var tools = await _toolsService.GetAllToolsStatusAsync();

                foreach (var tool in AllTools)
                {
                    var matchedTool = tools.Find(t =>
                        t.Type.ToString().Equals(tool.Id, StringComparison.OrdinalIgnoreCase) ||
                        t.Name.Contains(tool.Name, StringComparison.OrdinalIgnoreCase));

                    if (matchedTool != null)
                    {
                        tool.IsInstalled = matchedTool.IsInstalled;
                    }
                    else
                    {
                        // Check manually
                        tool.IsInstalled = CheckToolInstalled(tool.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error checking tools: {ex.Message}");
            }
        }

        private bool CheckToolInstalled(string toolId)
        {
            var searchPatterns = toolId switch
            {
                "adb" => new[] { "adb.exe" },
                "fastboot" => new[] { "fastboot.exe" },
                "thor" => new[] { "Thor.exe", "TheAirBlow.Thor.Shell.exe" },
                "heimdall" => new[] { "heimdall.exe", "heimdall-frontend.exe" },
                "freya" => new[] { "Freya.exe" },
                "samfirm" => new[] { "SamFirm.exe", "SamFirm_Reborn.exe" },
                "edl-client" => new[] { "edl.py" },
                "edl-ng" => new[] { "edl-ng.exe" },
                "mtk-client" => new[] { "mtk.py" },
                "xiaomi-tools" => new[] { "XiaomiADBFastbootTools.jar" },
                "huawei-unlock" => new[] { "HuaweiUnlock.exe" },
                "unisoc-unlock" => new[] { "unlock.py", "exploit.py" },
                "scrcpy" => new[] { "scrcpy.exe" },
                _ => Array.Empty<string>()
            };

            foreach (var pattern in searchPatterns)
            {
                // Check in Tools folder
                var files = Directory.GetFiles(_toolsPath, pattern, SearchOption.AllDirectories);
                if (files.Length > 0) return true;

                // Check in PATH
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var path in pathEnv.Split(Path.PathSeparator))
                {
                    if (File.Exists(Path.Combine(path, pattern)))
                        return true;
                }
            }

            return false;
        }

        private async Task LaunchTool(ToolInfo? tool)
        {
            if (tool == null) return;

            try
            {
                IsToolOperating = true;
                ToolStatus = $"Launching {tool.Name}...";

                var exePath = FindToolExecutable(tool.Id);
                if (string.IsNullOrEmpty(exePath))
                {
                    LogMessage?.Invoke(this, $"{tool.Name} not found. Please install it first.");
                    ToolStatus = "Tool not found";
                    return;
                }

                tool.IsRunning = true;

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };

                // Special handling for Java apps
                if (tool.RequiresJava && exePath.EndsWith(".jar"))
                {
                    psi.FileName = "java";
                    psi.Arguments = $"-jar \"{exePath}\"";
                }

                // Special handling for Python scripts
                if (tool.RequiresPython && exePath.EndsWith(".py"))
                {
                    psi.FileName = "python";
                    psi.Arguments = $"\"{exePath}\"";
                }

                Process.Start(psi);
                ToolStatus = $"{tool.Name} launched";
                LogMessage?.Invoke(this, $"Launched {tool.Name}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error launching {tool.Name}: {ex.Message}");
                ToolStatus = "Launch failed";
            }
            finally
            {
                IsToolOperating = false;
                if (tool != null) tool.IsRunning = false;
            }
        }

        private string FindToolExecutable(string toolId)
        {
            var searchPatterns = toolId switch
            {
                "adb" => new[] { "adb.exe" },
                "fastboot" => new[] { "fastboot.exe" },
                "thor" => new[] { "Thor.exe", "TheAirBlow.Thor.Shell.exe" },
                "heimdall" => new[] { "heimdall-frontend.exe", "heimdall.exe" },
                "freya" => new[] { "Freya.exe" },
                "samfirm" => new[] { "SamFirm_Reborn.exe", "SamFirm.exe" },
                "edl-client" => new[] { "edl.py" },
                "edl-ng" => new[] { "edl-ng.exe" },
                "mtk-client" => new[] { "mtk.py" },
                "xiaomi-tools" => new[] { "XiaomiADBFastbootTools.jar" },
                "huawei-unlock" => new[] { "HuaweiUnlock.exe" },
                "unisoc-unlock" => new[] { "unlock.py" },
                "scrcpy" => new[] { "scrcpy.exe" },
                _ => Array.Empty<string>()
            };

            foreach (var pattern in searchPatterns)
            {
                var files = Directory.GetFiles(_toolsPath, pattern, SearchOption.AllDirectories);
                if (files.Length > 0) return files[0];

                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var path in pathEnv.Split(Path.PathSeparator))
                {
                    var fullPath = Path.Combine(path, pattern);
                    if (File.Exists(fullPath)) return fullPath;
                }
            }

            return "";
        }

        private async Task InstallTool(ToolInfo? tool)
        {
            if (tool == null) return;

            try
            {
                IsToolOperating = true;
                ToolStatus = $"Installing {tool.Name}...";
                ToolProgress = 0;

                var toolType = tool.Id switch
                {
                    "adb" or "fastboot" => ToolType.ADB,
                    "thor" => ToolType.Thor,
                    "heimdall" => ToolType.Heimdall,
                    "freya" => ToolType.Freya,
                    "samfirm" => ToolType.SamFirm,
                    "edl-client" => ToolType.EdlClient,
                    "edl-ng" => ToolType.EdlNg,
                    "mtk-client" => ToolType.MTKClient,
                    "xiaomi-tools" => ToolType.XiaomiAdbFastboot,
                    "huawei-unlock" => ToolType.HuaweiUnlock,
                    "unisoc-unlock" => ToolType.UnisocUnlock,
                    "scrcpy" => ToolType.Scrcpy,
                    _ => ToolType.ADB
                };

                var result = await _toolsService.DownloadAndInstallToolAsync(toolType);

                if (result)
                {
                    tool.IsInstalled = true;
                    ToolStatus = $"{tool.Name} installed successfully";
                    LogMessage?.Invoke(this, $"{tool.Name} installed successfully");
                }
                else
                {
                    ToolStatus = $"Failed to install {tool.Name}";
                    LogMessage?.Invoke(this, $"Failed to install {tool.Name}");
                }
            }
            catch (Exception ex)
            {
                ToolStatus = "Installation failed";
                LogMessage?.Invoke(this, $"Error: {ex.Message}");
            }
            finally
            {
                IsToolOperating = false;
                ToolProgress = 0;
            }
        }

        private void OpenToolFolder()
        {
            try
            {
                Process.Start("explorer.exe", _toolsPath);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error: {ex.Message}");
            }
        }

        // Fastboot operations
        private async Task ExecuteFastboot(string command)
        {
            var fastbootPath = FindToolExecutable("fastboot");
            if (string.IsNullOrEmpty(fastbootPath))
            {
                FastbootOutput += "[ERROR] Fastboot not found. Please install Platform Tools first.\n";
                return;
            }

            FastbootOutput += $"> fastboot {command}\n";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fastbootPath,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return;

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                FastbootOutput += output;
                if (!string.IsNullOrEmpty(error))
                    FastbootOutput += error;
                FastbootOutput += "\n";
            }
            catch (Exception ex)
            {
                FastbootOutput += $"[ERROR] {ex.Message}\n";
            }
        }

        private async Task FlashPartition()
        {
            if (string.IsNullOrEmpty(FastbootImagePath) || !File.Exists(FastbootImagePath))
            {
                FastbootOutput += "[ERROR] Please select a valid image file.\n";
                return;
            }

            await ExecuteFastboot($"flash {FastbootPartition} \"{FastbootImagePath}\"");
        }

        private void BrowseFastbootImage()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files|*.img;*.bin|All Files|*.*",
                Title = "Select Partition Image to Flash"
            };

            if (dialog.ShowDialog() == true)
            {
                FastbootImagePath = dialog.FileName;
            }
        }

        // Scrcpy operations
        private async Task LaunchScrcpy(bool record)
        {
            var scrcpyPath = FindToolExecutable("scrcpy");
            if (string.IsNullOrEmpty(scrcpyPath))
            {
                LogMessage?.Invoke(this, "Scrcpy not found. Please install it first.");
                return;
            }

            try
            {
                var args = $"--bit-rate {ScrcpyBitrate}M --max-fps {ScrcpyMaxFps}";

                if (ScrcpyFullscreen)
                    args += " --fullscreen";
                if (ScrcpyShowTouches)
                    args += " --show-touches";
                if (ScrcpyStayAwake)
                    args += " --stay-awake";

                if (record)
                {
                    var dialog = new SaveFileDialog
                    {
                        Filter = "MP4 Video|*.mp4",
                        FileName = $"screen_record_{DateTime.Now:yyyyMMdd_HHmmss}.mp4"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        args += $" --record \"{dialog.FileName}\"";
                    }
                    else
                    {
                        return;
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = scrcpyPath,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(scrcpyPath)
                };

                Process.Start(psi);
                LogMessage?.Invoke(this, "Scrcpy launched");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error launching Scrcpy: {ex.Message}");
            }
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
