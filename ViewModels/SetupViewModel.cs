using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Newtonsoft.Json;
using PhoneRomFlashTool.Services;
using PhoneRomFlashTool.Views;

namespace PhoneRomFlashTool.ViewModels
{
    public class SetupItem : INotifyPropertyChanged
    {
        private string _name = "";
        private string _description = "";
        private string _status = "Not Checked";
        private bool _isInstalled;
        private bool _isChecking;
        private bool _isInstalling;
        private int _progress;
        private string _toolId = "";

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public bool IsInstalled
        {
            get => _isInstalled;
            set { _isInstalled = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusIcon)); }
        }

        public bool IsChecking
        {
            get => _isChecking;
            set { _isChecking = value; OnPropertyChanged(); }
        }

        public bool IsInstalling
        {
            get => _isInstalling;
            set { _isInstalling = value; OnPropertyChanged(); }
        }

        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public string ToolId
        {
            get => _toolId;
            set { _toolId = value; OnPropertyChanged(); }
        }

        public string StatusIcon => IsInstalled ? "✓" : "✗";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Persistent installation state for stability
    public class InstallationState
    {
        public Dictionary<string, bool> InstalledTools { get; set; } = new();
        public Dictionary<string, bool> InstalledDrivers { get; set; } = new();
        public Dictionary<string, DateTime> InstallDates { get; set; } = new();
        public DateTime LastVerificationDate { get; set; }

        private static readonly string StatePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Data", "installation_state.json");

        public static InstallationState Load()
        {
            try
            {
                if (File.Exists(StatePath))
                {
                    var json = File.ReadAllText(StatePath);
                    return JsonConvert.DeserializeObject<InstallationState>(json) ?? new InstallationState();
                }
            }
            catch { }
            return new InstallationState();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(StatePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(StatePath, json);
            }
            catch { }
        }

        public void MarkToolInstalled(string toolId, bool installed)
        {
            InstalledTools[toolId] = installed;
            if (installed)
                InstallDates[toolId] = DateTime.Now;
            Save();
        }

        public void MarkDriverInstalled(string driverId, bool installed)
        {
            InstalledDrivers[driverId] = installed;
            if (installed)
                InstallDates[driverId] = DateTime.Now;
            Save();
        }

        public bool IsToolInstalled(string toolId)
        {
            return InstalledTools.TryGetValue(toolId, out var installed) && installed;
        }

        public bool IsDriverInstalled(string driverId)
        {
            return InstalledDrivers.TryGetValue(driverId, out var installed) && installed;
        }
    }

    public class SetupViewModel : INotifyPropertyChanged
    {
        private readonly ToolsDownloadService _toolsService;
        private readonly DriverInstallerService _driverService;
        private readonly InstallationState _installState;

        private bool _isChecking;
        private bool _isInstalling;
        private string _statusMessage = "";
        private int _overallProgress;
        private CancellationTokenSource? _cts;

        public ObservableCollection<SetupItem> ToolItems { get; } = new();
        public ObservableCollection<SetupItem> DriverItems { get; } = new();
        public ObservableCollection<string> Logs { get; } = new();

        #region Properties
        public bool IsChecking
        {
            get => _isChecking;
            set { _isChecking = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanInstall)); }
        }

        public bool IsInstalling
        {
            get => _isInstalling;
            set { _isInstalling = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanInstall)); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public int OverallProgress
        {
            get => _overallProgress;
            set { _overallProgress = value; OnPropertyChanged(); }
        }

        public bool CanInstall => !IsChecking && !IsInstalling;

        public bool AllToolsInstalled
        {
            get
            {
                // Check persistent state first for stability
                if (_installState.IsToolInstalled("platform-tools"))
                    return true;

                foreach (var item in ToolItems)
                    if (!item.IsInstalled) return false;
                return ToolItems.Count > 0;
            }
        }

        // Property to show standby screen when all essential tools are installed
        private bool _showStandbyScreen;
        public bool ShowStandbyScreen
        {
            get => _showStandbyScreen;
            set { _showStandbyScreen = value; OnPropertyChanged(); }
        }

        // Count of pending (not installed) items
        public int PendingToolsCount => ToolItems.Count > 0 ? ToolItems.Count(t => !t.IsInstalled) : 0;
        public int PendingDriversCount => DriverItems.Count > 0 ? DriverItems.Count(d => !d.IsInstalled) : 0;
        #endregion

        #region Commands
        public ICommand CheckAllCommand { get; }
        public ICommand InstallAllToolsCommand { get; }
        public ICommand InstallAllDriversCommand { get; }
        public ICommand InstallAllCommand { get; }
        public ICommand InstallPlatformToolsCommand { get; }
        public ICommand InstallGoogleDriverCommand { get; }
        public ICommand InstallSamsungDriverCommand { get; }
        public ICommand InstallQualcommDriverCommand { get; }
        public ICommand InstallMtkDriverCommand { get; }
        public ICommand InstallToolCommand { get; }
        public ICommand InstallDriverCommand { get; }
        public ICommand UninstallToolCommand { get; }
        public ICommand UninstallDriverCommand { get; }
        public ICommand OpenToolsFolderCommand { get; }
        public ICommand OpenDownloadPageCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ClearLogsCommand { get; }
        #endregion

        // Manual download URLs for when auto-download fails
        private static readonly Dictionary<string, string[]> ManualDownloadUrls = new()
        {
            // Tools
            ["platform-tools"] = new[] {
                "https://developer.android.com/studio/releases/platform-tools",
                "https://dl.google.com/android/repository/platform-tools-latest-windows.zip"
            },
            ["heimdall"] = new[] {
                "https://github.com/Benjamin-Dobell/Heimdall/releases",
                "https://glassechidna.com.au/heimdall/"
            },
            ["thor"] = new[] {
                "https://github.com/Samsung-Loki/Thor/releases",
                "https://github.com/TheAirBlow/Thor/releases"
            },
            ["scrcpy"] = new[] {
                "https://github.com/Genymobile/scrcpy/releases",
                "https://github.com/AUR/scrcpy/releases"
            },
            ["edl-client"] = new[] {
                "https://github.com/bkerler/edl/releases",
                "https://github.com/bkerler/edl"
            },
            ["mtk-client"] = new[] {
                "https://github.com/bkerler/mtkclient/releases",
                "https://github.com/bkerler/mtkclient"
            },
            ["samfirm"] = new[] {
                "https://github.com/ivanmeler/SamFirm_Reborn/releases",
                "https://github.com/jesec/SamFern/releases"
            },
            ["freya"] = new[] {
                "https://github.com/Samsung-Loki/Freya/releases"
            },
            // Drivers
            ["google-usb"] = new[] {
                "https://developer.android.com/studio/run/win-usb",
                "https://dl.google.com/android/repository/usb_driver_r13-windows.zip"
            },
            ["samsung-usb"] = new[] {
                "https://developer.samsung.com/android-usb-driver",
                "https://www.samsung.com/us/support/downloads/"
            },
            ["qualcomm-qdloader"] = new[] {
                "https://qcomdriver.com/qualcomm-hs-usb-qdloader-9008-driver/",
                "https://androidmtk.com/download-qualcomm-hs-usb-qdloader-9008-driver"
            },
            ["mtk-preloader"] = new[] {
                "https://androidmtk.com/download-mtk-preloader-usb-vcom-drivers",
                "https://spflashtools.com/mediatek-driver"
            }
        };

        // Additional properties for UI
        private string _currentStatus = "Ready";
        public string CurrentStatus
        {
            get => _currentStatus;
            set { _currentStatus = value; OnPropertyChanged(); }
        }

        private string _installLog = "";
        public string InstallLog
        {
            get => _installLog;
            set { _installLog = value; OnPropertyChanged(); }
        }

        public SetupViewModel()
        {
            _toolsService = new ToolsDownloadService();
            _driverService = new DriverInstallerService();
            _installState = InstallationState.Load();

            // Wire up events
            _toolsService.LogMessage += (s, msg) => AddLog(msg);
            _toolsService.StatusChanged += (s, status) => StatusMessage = status;
            _toolsService.ProgressChanged += (s, progress) => OverallProgress = progress;

            _driverService.LogMessage += (s, msg) => AddLog(msg);
            _driverService.StatusChanged += (s, status) => StatusMessage = status;
            _driverService.ProgressChanged += (s, progress) => OverallProgress = progress;

            // Initialize items
            InitializeItems();

            // Commands
            CheckAllCommand = new RelayCommand(async () => await CheckAllAsync());
            InstallAllToolsCommand = new RelayCommand(async () => await InstallAllToolsAsync());
            InstallAllDriversCommand = new RelayCommand(async () => await InstallAllDriversAsync());
            InstallAllCommand = new RelayCommand(async () => await InstallEverythingAsync());
            InstallPlatformToolsCommand = new RelayCommand(async () => await InstallPlatformToolsAsync());
            InstallGoogleDriverCommand = new RelayCommand(async () => await InstallGoogleDriverAsync());
            InstallSamsungDriverCommand = new RelayCommand(async () => await InstallSamsungDriverAsync());
            InstallQualcommDriverCommand = new RelayCommand(async () => await InstallQualcommDriverAsync());
            InstallMtkDriverCommand = new RelayCommand(async () => await InstallMtkDriverAsync());
            InstallToolCommand = new RelayCommandWithParam<string>(async (toolId) => await InstallToolByIdAsync(toolId));
            InstallDriverCommand = new RelayCommandWithParam<string>(async (driverId) => await InstallDriverByIdAsync(driverId));
            UninstallToolCommand = new RelayCommandWithParam<string>(async (toolId) => await UninstallToolByIdAsync(toolId));
            UninstallDriverCommand = new RelayCommandWithParam<string>(async (driverId) => await UninstallDriverByIdAsync(driverId));
            OpenToolsFolderCommand = new RelayCommand(OpenToolsFolder);
            OpenDownloadPageCommand = new RelayCommandWithParam<string>(OpenDownloadPage);
            CancelCommand = new RelayCommand(Cancel);
            ClearLogsCommand = new RelayCommand(() => Logs.Clear());

            // Auto-check on startup
            _ = CheckAllAsync();
        }

        private void InitializeItems()
        {
            // Tools - show all items with installation status
            ToolItems.Add(new SetupItem
            {
                ToolId = "platform-tools",
                Name = "Android Platform Tools",
                Description = "ADB and Fastboot command-line tools (Required)",
                IsInstalled = _installState.IsToolInstalled("platform-tools"),
                Status = _installState.IsToolInstalled("platform-tools") ? "✓ Installed" : "Not installed"
            });

            ToolItems.Add(new SetupItem
            {
                ToolId = "heimdall",
                Name = "Heimdall",
                Description = "Open-source Samsung flashing tool",
                IsInstalled = _installState.IsToolInstalled("heimdall"),
                Status = _installState.IsToolInstalled("heimdall") ? "✓ Installed" : "Not installed"
            });

            ToolItems.Add(new SetupItem
            {
                ToolId = "edl-client",
                Name = "EDL Client",
                Description = "Qualcomm Emergency Download mode tool (Requires Python)",
                IsInstalled = _installState.IsToolInstalled("edl-client"),
                Status = _installState.IsToolInstalled("edl-client") ? "✓ Installed" : "Not installed"
            });

            ToolItems.Add(new SetupItem
            {
                ToolId = "mtk-client",
                Name = "MTK Client",
                Description = "MediaTek BROM/Preloader tool (Requires Python)",
                IsInstalled = _installState.IsToolInstalled("mtk-client"),
                Status = _installState.IsToolInstalled("mtk-client") ? "✓ Installed" : "Not installed"
            });

            // ===== NEW TOOLS 2024-2025 =====
            ToolItems.Add(new SetupItem
            {
                ToolId = "thor",
                Name = "Thor (Samsung-Loki)",
                Description = "Modern Samsung flash tool - Better than Odin (.NET 7+)",
                IsInstalled = _installState.IsToolInstalled("thor"),
                Status = _installState.IsToolInstalled("thor") ? "✓ Installed" : "Not installed"
            });

            ToolItems.Add(new SetupItem
            {
                ToolId = "edl-ng",
                Name = "EDL-NG",
                Description = "Modern Qualcomm EDL tool with GUI",
                IsInstalled = _installState.IsToolInstalled("edl-ng"),
                Status = _installState.IsToolInstalled("edl-ng") ? "✓ Installed" : "Not installed"
            });

            ToolItems.Add(new SetupItem
            {
                ToolId = "xiaomi-tools",
                Name = "Xiaomi ADB/Fastboot Tools",
                Description = "Complete Xiaomi device management (Requires Java)",
                IsInstalled = _installState.IsToolInstalled("xiaomi-tools"),
                Status = _installState.IsToolInstalled("xiaomi-tools") ? "✓ Installed" : "Not installed"
            });

            ToolItems.Add(new SetupItem
            {
                ToolId = "freya",
                Name = "Freya",
                Description = "Samsung flash tool - Read, Flash, Repartition",
                IsInstalled = _installState.IsToolInstalled("freya"),
                Status = _installState.IsToolInstalled("freya") ? "✓ Installed" : "Not installed"
            });

            ToolItems.Add(new SetupItem
            {
                ToolId = "huawei-unlock",
                Name = "Huawei Unlock Tool",
                Description = "Huawei bootloader unlock & FRP bypass",
                IsInstalled = _installState.IsToolInstalled("huawei-unlock"),
                Status = _installState.IsToolInstalled("huawei-unlock") ? "✓ Installed" : "Not installed"
            });

            ToolItems.Add(new SetupItem
            {
                ToolId = "unisoc-unlock",
                Name = "Unisoc Bootloader Unlock",
                Description = "CVE-2022-38694 exploit for Unisoc devices (Requires Python)",
                IsInstalled = _installState.IsToolInstalled("unisoc-unlock"),
                Status = _installState.IsToolInstalled("unisoc-unlock") ? "✓ Installed" : "Not installed"
            });

            ToolItems.Add(new SetupItem
            {
                ToolId = "samfirm",
                Name = "SamFirm Tool",
                Description = "Samsung firmware downloader - Direct from Samsung",
                IsInstalled = _installState.IsToolInstalled("samfirm"),
                Status = _installState.IsToolInstalled("samfirm") ? "✓ Installed" : "Not installed"
            });

            ToolItems.Add(new SetupItem
            {
                ToolId = "scrcpy",
                Name = "Scrcpy",
                Description = "Android screen mirror & control via USB/WiFi",
                IsInstalled = _installState.IsToolInstalled("scrcpy"),
                Status = _installState.IsToolInstalled("scrcpy") ? "✓ Installed" : "Not installed"
            });

            // Drivers - show all items with installation status
            DriverItems.Add(new SetupItem
            {
                ToolId = "google-usb",
                Name = "Google USB Driver",
                Description = "Universal Android ADB/Fastboot driver",
                IsInstalled = _installState.IsDriverInstalled("google-usb"),
                Status = _installState.IsDriverInstalled("google-usb") ? "✓ Installed" : "Not installed"
            });

            DriverItems.Add(new SetupItem
            {
                ToolId = "qualcomm-qdloader",
                Name = "Qualcomm HS-USB QDLoader",
                Description = "Qualcomm EDL 9008 mode driver",
                IsInstalled = _installState.IsDriverInstalled("qualcomm-qdloader"),
                Status = _installState.IsDriverInstalled("qualcomm-qdloader") ? "✓ Installed" : "Not installed"
            });

            DriverItems.Add(new SetupItem
            {
                ToolId = "mtk-preloader",
                Name = "MediaTek Preloader",
                Description = "MTK download mode driver",
                IsInstalled = _installState.IsDriverInstalled("mtk-preloader"),
                Status = _installState.IsDriverInstalled("mtk-preloader") ? "✓ Installed" : "Not installed"
            });

            DriverItems.Add(new SetupItem
            {
                ToolId = "samsung-usb",
                Name = "Samsung USB Driver",
                Description = "Samsung Odin/Download mode driver",
                IsInstalled = _installState.IsDriverInstalled("samsung-usb"),
                Status = _installState.IsDriverInstalled("samsung-usb") ? "✓ Installed" : "Not installed"
            });

            // Check if we should show standby screen
            UpdateStandbyScreen();
        }

        private void UpdateStandbyScreen()
        {
            // Show standby if platform-tools is installed (essential)
            ShowStandbyScreen = _installState.IsToolInstalled("platform-tools") ||
                               (ToolItems.Count == 0 || ToolItems.All(t => t.IsInstalled));
            OnPropertyChanged(nameof(PendingToolsCount));
            OnPropertyChanged(nameof(PendingDriversCount));
        }

        private void MarkItemInstalled(SetupItem item, bool isTool, bool installed)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                item.IsInstalled = installed;
                item.Status = installed ? "✓ Installed" : "Not installed";

                if (isTool)
                {
                    _installState.MarkToolInstalled(item.ToolId, installed);
                }
                else
                {
                    _installState.MarkDriverInstalled(item.ToolId, installed);
                }
                UpdateStandbyScreen();
            });
        }

        #region Check Methods
        public async Task CheckAllAsync()
        {
            if (IsChecking) return;

            try
            {
                IsChecking = true;
                StatusMessage = "Checking installed tools and drivers...";
                OverallProgress = 0;

                _cts = new CancellationTokenSource();

                // Check tools with file verification
                AddLog("Verifying tools installation...");
                await CheckToolsAsync(_cts.Token);

                OverallProgress = 50;

                // Check drivers
                AddLog("Checking drivers...");
                await CheckDriversAsync(_cts.Token);

                OverallProgress = 100;

                // Update persistent state and remove installed items
                _installState.LastVerificationDate = DateTime.Now;
                _installState.Save();

                // Count installed from persistent state + current UI
                int totalToolsInstalled = _installState.InstalledTools.Count(kv => kv.Value);
                int totalDriversInstalled = _installState.InstalledDrivers.Count(kv => kv.Value);

                if (ToolItems.Count == 0 && DriverItems.Count == 0)
                {
                    StatusMessage = "All tools and drivers are installed!";
                }
                else
                {
                    StatusMessage = $"Pending: {ToolItems.Count} tools, {DriverItems.Count} drivers";
                }

                UpdateStandbyScreen();
                OnPropertyChanged(nameof(AllToolsInstalled));
            }
            catch (Exception ex)
            {
                AddLog($"Error checking: {ex.Message}");
            }
            finally
            {
                IsChecking = false;
            }
        }

        private async Task CheckToolsAsync(CancellationToken ct)
        {
            var tools = await _toolsService.GetAllToolsStatusAsync(ct);
            var itemsToRemove = new List<SetupItem>();

            foreach (var item in ToolItems)
            {
                item.IsChecking = true;
            }

            // Check each tool item by ToolId
            foreach (var item in ToolItems.ToList())
            {
                // First check persistent state - if marked as installed, trust it
                bool persistentInstalled = _installState.IsToolInstalled(item.ToolId);
                bool fileCheckInstalled = false;

                // Only do file check if not in persistent state (new install check)
                if (!persistentInstalled)
                {
                    switch (item.ToolId)
                    {
                        case "platform-tools":
                            fileCheckInstalled = _toolsService.IsAdbInstalled();
                            break;
                        case "heimdall":
                            var heimdall = tools.Find(t => t.Type == ToolType.Heimdall);
                            fileCheckInstalled = heimdall?.IsInstalled ?? false;
                            break;
                        case "edl-client":
                            var edl = tools.Find(t => t.Type == ToolType.EdlClient);
                            fileCheckInstalled = edl?.IsInstalled ?? false;
                            break;
                        case "mtk-client":
                            var mtk = tools.Find(t => t.Type == ToolType.MTKClient);
                            fileCheckInstalled = mtk?.IsInstalled ?? false;
                            break;
                        case "thor":
                            var thor = tools.Find(t => t.Type == ToolType.Thor);
                            fileCheckInstalled = thor?.IsInstalled ?? false;
                            break;
                        case "edl-ng":
                            var edlNg = tools.Find(t => t.Type == ToolType.EdlNg);
                            fileCheckInstalled = edlNg?.IsInstalled ?? false;
                            break;
                        case "xiaomi-tools":
                            var xiaomi = tools.Find(t => t.Type == ToolType.XiaomiAdbFastboot);
                            fileCheckInstalled = xiaomi?.IsInstalled ?? false;
                            break;
                        case "freya":
                            var freya = tools.Find(t => t.Type == ToolType.Freya);
                            fileCheckInstalled = freya?.IsInstalled ?? false;
                            break;
                        case "huawei-unlock":
                            var huawei = tools.Find(t => t.Type == ToolType.HuaweiUnlock);
                            fileCheckInstalled = huawei?.IsInstalled ?? false;
                            break;
                        case "unisoc-unlock":
                            var unisoc = tools.Find(t => t.Type == ToolType.UnisocUnlock);
                            fileCheckInstalled = unisoc?.IsInstalled ?? false;
                            break;
                        case "samfirm":
                            var samfirm = tools.Find(t => t.Type == ToolType.SamFirm);
                            fileCheckInstalled = samfirm?.IsInstalled ?? false;
                            break;
                        case "scrcpy":
                            var scrcpy = tools.Find(t => t.Type == ToolType.Scrcpy);
                            fileCheckInstalled = scrcpy?.IsInstalled ?? false;
                            break;
                    }
                }

                // Use persistent state OR file check result
                bool isInstalled = persistentInstalled || fileCheckInstalled;

                item.IsInstalled = isInstalled;
                item.Status = isInstalled ? "Installed" : "Not Found";
                item.IsChecking = false;

                // If installed, mark for removal and persist
                if (isInstalled)
                {
                    itemsToRemove.Add(item);
                }
            }

            // Remove installed items from list
            foreach (var item in itemsToRemove)
            {
                MarkItemInstalled(item, true, true);
                AddLog($"✓ {item.Name} verified and removed from pending list");
            }
        }

        private async Task CheckDriversAsync(CancellationToken ct)
        {
            var drivers = await _driverService.GetInstalledDriversAsync(ct);
            var itemsToRemove = new List<SetupItem>();

            foreach (var item in DriverItems)
            {
                item.IsChecking = true;
            }

            // Check each driver item by ToolId
            foreach (var item in DriverItems.ToList())
            {
                // First check persistent state - if marked as installed, trust it
                bool persistentInstalled = _installState.IsDriverInstalled(item.ToolId);
                bool systemCheckInstalled = false;

                // Only do system check if not in persistent state
                if (!persistentInstalled)
                {
                    switch (item.ToolId)
                    {
                        case "google-usb":
                            var googleUsb = drivers.Find(d => d.Type == DriverType.GoogleUSB);
                            systemCheckInstalled = googleUsb?.IsInstalled ?? false;
                            break;
                        case "qualcomm-qdloader":
                            var qualcomm = drivers.Find(d => d.Type == DriverType.QualcommQDLoader);
                            systemCheckInstalled = qualcomm?.IsInstalled ?? false;
                            break;
                        case "mtk-preloader":
                            var mtk = drivers.Find(d => d.Type == DriverType.MediaTekPreloader);
                            systemCheckInstalled = mtk?.IsInstalled ?? false;
                            break;
                        case "samsung-usb":
                            var samsung = drivers.Find(d => d.Type == DriverType.SamsungUSB);
                            systemCheckInstalled = samsung?.IsInstalled ?? false;
                            break;
                    }
                }

                // Use persistent state OR system check result
                bool isInstalled = persistentInstalled || systemCheckInstalled;

                item.IsInstalled = isInstalled;
                item.Status = isInstalled ? "Installed" : "Not Found";
                item.IsChecking = false;

                // If installed, mark for removal and persist
                if (isInstalled)
                {
                    itemsToRemove.Add(item);
                }
            }

            // Remove installed items from list
            foreach (var item in itemsToRemove)
            {
                MarkItemInstalled(item, true, true);
                AddLog($"✓ {item.Name} driver verified and removed from pending list");
            }
        }
        #endregion

        #region Install Methods
        private async Task InstallAllToolsAsync()
        {
            if (IsInstalling) return;

            try
            {
                IsInstalling = true;
                _cts = new CancellationTokenSource();

                // Install Platform Tools first (essential)
                await InstallPlatformToolsAsync();

                // Could add more tools here
                // await _toolsService.DownloadAndInstallToolAsync(ToolType.Heimdall, _cts.Token);

                await CheckToolsAsync(_cts.Token);
                OnPropertyChanged(nameof(AllToolsInstalled));
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private async Task InstallAllDriversAsync()
        {
            if (IsInstalling) return;

            try
            {
                IsInstalling = true;
                _cts = new CancellationTokenSource();

                // Install Google USB Driver (universal)
                await InstallGoogleDriverAsync();

                await CheckDriversAsync(_cts.Token);
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private async Task InstallPlatformToolsAsync()
        {
            if (IsInstalling) return;

            try
            {
                IsInstalling = true;
                CurrentStatus = "Installing Platform Tools...";
                _cts = new CancellationTokenSource();

                DebugWindow.LogInfo("Starting Platform Tools installation...");

                // Find platform-tools item
                var platformToolsItem = ToolItems.FirstOrDefault(t => t.ToolId == "platform-tools");
                if (platformToolsItem != null)
                {
                    platformToolsItem.IsInstalling = true;
                    platformToolsItem.Status = "Installing...";
                }

                var result = await _toolsService.DownloadAndInstallPlatformToolsAsync(_cts.Token);

                if (platformToolsItem != null)
                {
                    platformToolsItem.IsInstalling = false;
                    platformToolsItem.IsInstalled = result;
                    platformToolsItem.Status = result ? "Installed" : "Failed";

                    // If successful, mark as installed and persist
                    if (result)
                    {
                        MarkItemInstalled(platformToolsItem, true, true);
                        AddLog("✓ Android Platform Tools installed successfully!");
                        DebugWindow.LogInfo("Platform Tools installed successfully");
                    }
                    else
                    {
                        AddLog("✗ Platform Tools installation failed");
                        DebugWindow.LogError("Platform Tools installation failed - check download or permissions");
                        CurrentStatus = "Installation failed - see Debug panel";
                    }
                }

                // Start ADB server
                if (result)
                {
                    await _toolsService.StartAdbServerAsync(_cts.Token);
                }

                OnPropertyChanged(nameof(AllToolsInstalled));
            }
            catch (Exception ex)
            {
                DebugWindow.LogException(ex, "Platform Tools installation");
                AddLog($"✗ Error: {ex.Message}");
                CurrentStatus = "Installation failed - see Debug panel";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private async Task InstallGoogleDriverAsync()
        {
            if (IsInstalling) return;

            try
            {
                IsInstalling = true;
                CurrentStatus = "Installing Google USB Driver...";
                _cts = new CancellationTokenSource();

                DebugWindow.LogInfo("Starting Google USB Driver installation...");

                // Find google-usb driver item
                var googleUsbItem = DriverItems.FirstOrDefault(d => d.ToolId == "google-usb");
                if (googleUsbItem != null)
                {
                    googleUsbItem.IsInstalling = true;
                    googleUsbItem.Status = "Installing...";
                }

                var result = await _driverService.InstallGoogleUsbDriverAsync(_cts.Token);

                if (googleUsbItem != null)
                {
                    googleUsbItem.IsInstalling = false;
                    googleUsbItem.IsInstalled = result;
                    googleUsbItem.Status = result ? "Installed" : "Failed";

                    // If successful, mark as installed and persist
                    if (result)
                    {
                        MarkItemInstalled(googleUsbItem, false, true);
                        AddLog("✓ Google USB Driver installed successfully!");
                        DebugWindow.LogInfo("Google USB Driver installed successfully");
                    }
                    else
                    {
                        AddLog("✗ Google USB Driver installation failed");
                        DebugWindow.LogError("Google USB Driver installation failed - may need admin rights");
                    }
                }

                CurrentStatus = result ? "Google USB Driver installed!" : "Installation failed - see Debug panel";
            }
            catch (Exception ex)
            {
                DebugWindow.LogException(ex, "Google USB Driver installation");
                AddLog($"✗ Google USB Driver error: {ex.Message}");
                CurrentStatus = "Installation failed - see Debug panel";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private async Task InstallSamsungDriverAsync()
        {
            if (IsInstalling) return;

            try
            {
                IsInstalling = true;
                CurrentStatus = "Installing Samsung USB Driver...";
                _cts = new CancellationTokenSource();

                DebugWindow.LogInfo("Starting Samsung USB Driver installation...");

                var samsungItem = DriverItems.FirstOrDefault(d => d.ToolId == "samsung-usb");
                if (samsungItem != null)
                {
                    samsungItem.IsInstalling = true;
                    samsungItem.Status = "Installing...";
                }

                var result = await _driverService.DownloadAndInstallSamsungDriverAsync(_cts.Token);

                if (samsungItem != null)
                {
                    samsungItem.IsInstalling = false;
                    samsungItem.IsInstalled = result;
                    samsungItem.Status = result ? "Installed" : "Failed";

                    if (result)
                    {
                        MarkItemInstalled(samsungItem, false, true);
                        AddLog("✓ Samsung USB Driver installed successfully!");
                        DebugWindow.LogInfo("Samsung USB Driver installed successfully");
                    }
                    else
                    {
                        AddLog("✗ Samsung USB Driver installation failed");
                        DebugWindow.LogError("Samsung USB Driver installation failed");
                    }
                }

                CurrentStatus = result ? "Samsung USB Driver installed!" : "Installation failed - see Debug panel";
            }
            catch (Exception ex)
            {
                DebugWindow.LogException(ex, "Samsung USB Driver installation");
                AddLog($"✗ Samsung Driver error: {ex.Message}");
                CurrentStatus = "Installation failed - see Debug panel";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private async Task InstallQualcommDriverAsync()
        {
            if (IsInstalling) return;

            try
            {
                IsInstalling = true;
                CurrentStatus = "Installing Qualcomm QDLoader Driver...";
                _cts = new CancellationTokenSource();

                DebugWindow.LogInfo("Starting Qualcomm QDLoader Driver installation...");

                var qualcommItem = DriverItems.FirstOrDefault(d => d.ToolId == "qualcomm-qdloader");
                if (qualcommItem != null)
                {
                    qualcommItem.IsInstalling = true;
                    qualcommItem.Status = "Installing...";
                }

                var result = await _driverService.DownloadAndInstallQualcommDriverAsync(_cts.Token);

                if (qualcommItem != null)
                {
                    qualcommItem.IsInstalling = false;
                    qualcommItem.IsInstalled = result;
                    qualcommItem.Status = result ? "Installed" : "Failed";

                    if (result)
                    {
                        MarkItemInstalled(qualcommItem, false, true);
                        AddLog("✓ Qualcomm QDLoader Driver installed successfully!");
                        DebugWindow.LogInfo("Qualcomm QDLoader Driver installed successfully");
                    }
                    else
                    {
                        AddLog("✗ Qualcomm QDLoader Driver installation failed");
                        DebugWindow.LogError("Qualcomm QDLoader Driver installation failed");
                    }
                }

                CurrentStatus = result ? "Qualcomm Driver installed!" : "Installation failed - see Debug panel";
            }
            catch (Exception ex)
            {
                DebugWindow.LogException(ex, "Qualcomm QDLoader Driver installation");
                AddLog($"✗ Qualcomm Driver error: {ex.Message}");
                CurrentStatus = "Installation failed - see Debug panel";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private async Task InstallMtkDriverAsync()
        {
            if (IsInstalling) return;

            try
            {
                IsInstalling = true;
                CurrentStatus = "Installing MediaTek Preloader Driver...";
                _cts = new CancellationTokenSource();

                DebugWindow.LogInfo("Starting MediaTek Preloader Driver installation...");

                var mtkItem = DriverItems.FirstOrDefault(d => d.ToolId == "mtk-preloader");
                if (mtkItem != null)
                {
                    mtkItem.IsInstalling = true;
                    mtkItem.Status = "Installing...";
                }

                var result = await _driverService.DownloadAndInstallMtkDriverAsync(_cts.Token);

                if (mtkItem != null)
                {
                    mtkItem.IsInstalling = false;
                    mtkItem.IsInstalled = result;
                    mtkItem.Status = result ? "Installed" : "Failed";

                    if (result)
                    {
                        MarkItemInstalled(mtkItem, false, true);
                        AddLog("✓ MediaTek Preloader Driver installed successfully!");
                        DebugWindow.LogInfo("MediaTek Preloader Driver installed successfully");
                    }
                    else
                    {
                        AddLog("✗ MediaTek Preloader Driver installation failed");
                        DebugWindow.LogError("MediaTek Preloader Driver installation failed");
                    }
                }

                CurrentStatus = result ? "MediaTek Driver installed!" : "Installation failed - see Debug panel";
            }
            catch (Exception ex)
            {
                DebugWindow.LogException(ex, "MediaTek Preloader Driver installation");
                AddLog($"✗ MediaTek Driver error: {ex.Message}");
                CurrentStatus = "Installation failed - see Debug panel";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private async Task InstallEverythingAsync()
        {
            if (IsInstalling) return;

            int successCount = 0;
            int failCount = 0;
            int skippedCount = 0;

            try
            {
                IsInstalling = true;
                _cts = new CancellationTokenSource();

                // Get all tools and drivers that need to be installed
                var toolsToInstall = ToolItems.Where(t => !t.IsInstalled).ToList();
                var driversToInstall = DriverItems.Where(d => !d.IsInstalled).ToList();

                int totalSteps = toolsToInstall.Count + driversToInstall.Count;

                if (totalSteps == 0)
                {
                    CurrentStatus = "All items are already installed!";
                    AddLog("✓ All tools and drivers are already installed");
                    DebugWindow.LogInfo("Install Everything: All items already installed, nothing to do");
                    return;
                }

                // Count skipped items
                skippedCount = ToolItems.Count(t => t.IsInstalled) + DriverItems.Count(d => d.IsInstalled);

                CurrentStatus = $"Installing {totalSteps} items...";
                int step = 0;

                DebugWindow.LogInfo($"Starting Install Everything... ({totalSteps} items to install, {skippedCount} already installed)");

                // Install all tools first
                foreach (var tool in toolsToInstall)
                {
                    if (_cts?.Token.IsCancellationRequested == true) break;

                    step++;
                    CurrentStatus = $"[{step}/{totalSteps}] Installing {tool.Name}...";
                    OverallProgress = (step * 100) / totalSteps;

                    var result = await InstallToolInternalAsync(tool.ToolId);
                    if (result)
                        successCount++;
                    else
                        failCount++;
                }

                // Then install all drivers
                foreach (var driver in driversToInstall)
                {
                    if (_cts?.Token.IsCancellationRequested == true) break;

                    step++;
                    CurrentStatus = $"[{step}/{totalSteps}] Installing {driver.Name}...";
                    OverallProgress = (step * 100) / totalSteps;

                    var result = await InstallDriverInternalAsync(driver.ToolId);
                    if (result)
                        successCount++;
                    else
                        failCount++;
                }

                OverallProgress = 100;

                if (failCount == 0)
                {
                    CurrentStatus = $"All installations completed! ({skippedCount} already installed)";
                    AddLog($"✓ Installation completed! {successCount} installed, {skippedCount} skipped");
                    DebugWindow.LogInfo($"Install Everything completed: {successCount} installed, {skippedCount} skipped");
                }
                else
                {
                    CurrentStatus = $"Completed: {successCount} success, {failCount} failed, {skippedCount} skipped";
                    AddLog($"⚠️ Installation completed with {failCount} failures - check Debug panel");
                    DebugWindow.LogWarning($"Install Everything: {successCount} succeeded, {failCount} failed, {skippedCount} skipped");
                }

                // Refresh status
                await CheckAllAsync();
            }
            catch (Exception ex)
            {
                CurrentStatus = $"Error: {ex.Message}";
                AddLog($"Error during installation: {ex.Message}");
                DebugWindow.LogException(ex, "Install Everything");
            }
            finally
            {
                IsInstalling = false;
            }
        }

        // Generic internal method for installing any tool by ID
        private async Task<bool> InstallToolInternalAsync(string toolId)
        {
            var toolItem = ToolItems.FirstOrDefault(t => t.ToolId == toolId);
            try
            {
                if (toolItem != null)
                {
                    toolItem.IsInstalling = true;
                    toolItem.Status = "Installing...";
                }

                bool result = false;

                switch (toolId)
                {
                    case "platform-tools":
                        result = await _toolsService.DownloadAndInstallPlatformToolsAsync(_cts?.Token ?? CancellationToken.None);
                        if (result) await _toolsService.StartAdbServerAsync(_cts?.Token ?? CancellationToken.None);
                        break;
                    case "heimdall":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.Heimdall, _cts?.Token ?? CancellationToken.None);
                        break;
                    case "thor":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.Thor, _cts?.Token ?? CancellationToken.None);
                        break;
                    case "scrcpy":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.Scrcpy, _cts?.Token ?? CancellationToken.None);
                        break;
                    case "edl-client":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.EdlClient, _cts?.Token ?? CancellationToken.None);
                        break;
                    case "mtk-client":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.MTKClient, _cts?.Token ?? CancellationToken.None);
                        break;
                    case "edl-ng":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.EdlNg, _cts?.Token ?? CancellationToken.None);
                        break;
                    case "xiaomi-tools":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.XiaomiAdbFastboot, _cts?.Token ?? CancellationToken.None);
                        break;
                    case "freya":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.Freya, _cts?.Token ?? CancellationToken.None);
                        break;
                    case "samfirm":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.SamFirm, _cts?.Token ?? CancellationToken.None);
                        break;
                    case "huawei-unlock":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.HuaweiUnlock, _cts?.Token ?? CancellationToken.None);
                        break;
                    case "unisoc-unlock":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.UnisocUnlock, _cts?.Token ?? CancellationToken.None);
                        break;
                    default:
                        AddLog($"⚠️ No installer available for: {toolId}");
                        DebugWindow.LogWarning($"No installer implemented for tool: {toolId}");
                        return false;
                }

                if (toolItem != null)
                {
                    toolItem.IsInstalling = false;
                    toolItem.IsInstalled = result;
                    toolItem.Status = result ? "✓ Installed" : "Failed";

                    if (result)
                    {
                        MarkItemInstalled(toolItem, true, true);
                        AddLog($"✓ {toolItem.Name} installed!");
                        DebugWindow.LogInfo($"{toolItem.Name} installed successfully");
                    }
                    else
                    {
                        DebugWindow.LogError($"{toolItem.Name} installation failed");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                if (toolItem != null)
                {
                    toolItem.IsInstalling = false;
                    toolItem.Status = "Failed";
                }
                AddLog($"✗ {toolId} error: {ex.Message}");
                DebugWindow.LogException(ex, $"Tool installation: {toolId}");
                return false;
            }
        }

        // Generic internal method for installing any driver by ID
        private async Task<bool> InstallDriverInternalAsync(string driverId)
        {
            var driverItem = DriverItems.FirstOrDefault(d => d.ToolId == driverId);
            try
            {
                if (driverItem != null)
                {
                    driverItem.IsInstalling = true;
                    driverItem.Status = "Installing...";
                }

                bool result = false;

                switch (driverId)
                {
                    case "google-usb":
                        result = await _driverService.InstallGoogleUsbDriverAsync(_cts?.Token ?? CancellationToken.None);
                        break;
                    case "samsung-usb":
                        result = await _driverService.DownloadAndInstallSamsungDriverAsync(_cts?.Token ?? CancellationToken.None);
                        break;
                    case "qualcomm-qdloader":
                        result = await _driverService.DownloadAndInstallQualcommDriverAsync(_cts?.Token ?? CancellationToken.None);
                        break;
                    case "mtk-preloader":
                        result = await _driverService.DownloadAndInstallMtkDriverAsync(_cts?.Token ?? CancellationToken.None);
                        break;
                    default:
                        AddLog($"⚠️ No installer available for driver: {driverId}");
                        DebugWindow.LogWarning($"No installer implemented for driver: {driverId}");
                        return false;
                }

                if (driverItem != null)
                {
                    driverItem.IsInstalling = false;
                    driverItem.IsInstalled = result;
                    driverItem.Status = result ? "✓ Installed" : "Failed";

                    if (result)
                    {
                        MarkItemInstalled(driverItem, false, true);
                        AddLog($"✓ {driverItem.Name} installed!");
                        DebugWindow.LogInfo($"{driverItem.Name} installed successfully");
                    }
                    else
                    {
                        DebugWindow.LogError($"{driverItem.Name} installation failed");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                if (driverItem != null)
                {
                    driverItem.IsInstalling = false;
                    driverItem.Status = "Failed";
                }
                AddLog($"✗ {driverId} driver error: {ex.Message}");
                DebugWindow.LogException(ex, $"Driver installation: {driverId}");
                return false;
            }
        }

        private async Task InstallToolByIdAsync(string? toolId)
        {
            if (string.IsNullOrEmpty(toolId) || IsInstalling) return;

            try
            {
                IsInstalling = true;
                CurrentStatus = $"Installing {toolId}...";
                _cts = new CancellationTokenSource();

                DebugWindow.LogInfo($"Starting installation of tool: {toolId}");

                var toolItem = ToolItems.FirstOrDefault(t => t.ToolId == toolId);
                if (toolItem != null)
                {
                    toolItem.IsInstalling = true;
                    toolItem.Status = "Installing...";
                }

                bool result = false;

                switch (toolId)
                {
                    case "platform-tools":
                        result = await _toolsService.DownloadAndInstallPlatformToolsAsync(_cts.Token);
                        break;
                    case "heimdall":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.Heimdall, _cts.Token);
                        break;
                    case "thor":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.Thor, _cts.Token);
                        break;
                    case "scrcpy":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.Scrcpy, _cts.Token);
                        break;
                    case "edl-client":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.EdlClient, _cts.Token);
                        break;
                    case "mtk-client":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.MTKClient, _cts.Token);
                        break;
                    case "edl-ng":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.EdlNg, _cts.Token);
                        break;
                    case "xiaomi-tools":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.XiaomiAdbFastboot, _cts.Token);
                        break;
                    case "freya":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.Freya, _cts.Token);
                        break;
                    case "samfirm":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.SamFirm, _cts.Token);
                        break;
                    case "huawei-unlock":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.HuaweiUnlock, _cts.Token);
                        break;
                    case "unisoc-unlock":
                        result = await _toolsService.DownloadAndInstallToolAsync(ToolType.UnisocUnlock, _cts.Token);
                        break;
                    default:
                        AddLog($"Unknown tool: {toolId}");
                        DebugWindow.LogWarning($"Unknown tool requested: {toolId}");
                        break;
                }

                if (toolItem != null)
                {
                    toolItem.IsInstalling = false;
                    toolItem.IsInstalled = result;
                    toolItem.Status = result ? "Installed" : "Failed";

                    if (result)
                    {
                        MarkItemInstalled(toolItem, true, true);
                        AddLog($"✓ {toolItem.Name} installed successfully!");
                        DebugWindow.LogInfo($"{toolItem.Name} installed successfully");
                    }
                    else
                    {
                        AddLog($"✗ {toolItem.Name} installation failed");
                        DebugWindow.LogError($"{toolItem.Name} installation failed - check download URL or permissions");
                    }
                }

                CurrentStatus = result ? $"{toolId} installed!" : "Installation failed - see Debug panel";
            }
            catch (Exception ex)
            {
                DebugWindow.LogException(ex, $"Tool installation: {toolId}");
                AddLog($"✗ {toolId} error: {ex.Message}");
                CurrentStatus = "Installation failed - see Debug panel";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private async Task InstallDriverByIdAsync(string? driverId)
        {
            if (string.IsNullOrEmpty(driverId) || IsInstalling) return;

            try
            {
                IsInstalling = true;
                CurrentStatus = $"Installing {driverId} driver...";
                _cts = new CancellationTokenSource();

                DebugWindow.LogInfo($"Starting installation of driver: {driverId}");

                var driverItem = DriverItems.FirstOrDefault(d => d.ToolId == driverId);
                if (driverItem != null)
                {
                    driverItem.IsInstalling = true;
                    driverItem.Status = "Installing...";
                }

                bool result = false;

                switch (driverId)
                {
                    case "google-usb":
                        result = await _driverService.InstallGoogleUsbDriverAsync(_cts.Token);
                        break;
                    case "samsung-usb":
                        result = await _driverService.DownloadAndInstallSamsungDriverAsync(_cts.Token);
                        break;
                    case "qualcomm-qdloader":
                        result = await _driverService.DownloadAndInstallQualcommDriverAsync(_cts.Token);
                        break;
                    case "mtk-preloader":
                        result = await _driverService.DownloadAndInstallMtkDriverAsync(_cts.Token);
                        break;
                    default:
                        AddLog($"Unknown driver: {driverId}");
                        DebugWindow.LogWarning($"Unknown driver requested: {driverId}");
                        break;
                }

                if (driverItem != null)
                {
                    driverItem.IsInstalling = false;
                    driverItem.IsInstalled = result;
                    driverItem.Status = result ? "Installed" : "Failed";

                    if (result)
                    {
                        MarkItemInstalled(driverItem, false, true);
                        AddLog($"✓ {driverItem.Name} installed successfully!");
                        DebugWindow.LogInfo($"{driverItem.Name} installed successfully");
                    }
                    else
                    {
                        AddLog($"✗ {driverItem.Name} installation failed");
                        DebugWindow.LogError($"{driverItem.Name} installation failed - may need admin rights");
                    }
                }

                CurrentStatus = result ? $"{driverId} driver installed!" : "Installation failed - see Debug panel";
            }
            catch (Exception ex)
            {
                DebugWindow.LogException(ex, $"Driver installation: {driverId}");
                AddLog($"✗ {driverId} driver error: {ex.Message}");
                CurrentStatus = "Installation failed - see Debug panel";
            }
            finally
            {
                IsInstalling = false;
            }
        }

        private async Task UninstallToolByIdAsync(string? toolId)
        {
            if (string.IsNullOrEmpty(toolId)) return;

            try
            {
                CurrentStatus = $"Removing {toolId}...";

                var toolItem = ToolItems.FirstOrDefault(t => t.ToolId == toolId);
                if (toolItem == null) return;

                // Get tool path and delete
                var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
                var toolPath = Path.Combine(toolsPath, toolId);

                bool success = false;

                await Task.Run(() =>
                {
                    try
                    {
                        if (Directory.Exists(toolPath))
                        {
                            Directory.Delete(toolPath, true);
                            success = true;
                        }
                        else
                        {
                            // Try common variations
                            var variations = new[] { toolId, toolId.Replace("-", "_"), toolId.Replace("_", "-") };
                            foreach (var v in variations)
                            {
                                var path = Path.Combine(toolsPath, v);
                                if (Directory.Exists(path))
                                {
                                    Directory.Delete(path, true);
                                    success = true;
                                    break;
                                }
                            }
                        }

                        // Also check for specific tool folders
                        if (toolId == "platform-tools")
                        {
                            var ptPath = Path.Combine(toolsPath, "platform-tools");
                            if (Directory.Exists(ptPath))
                            {
                                Directory.Delete(ptPath, true);
                                success = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"Error removing {toolId}: {ex.Message}");
                    }
                });

                // Mark as uninstalled
                MarkItemInstalled(toolItem, true, false);
                AddLog($"🗑️ {toolItem.Name} removed");
                CurrentStatus = success ? $"{toolId} removed" : "Remove completed";
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
                CurrentStatus = "Remove failed";
            }
        }

        private async Task UninstallDriverByIdAsync(string? driverId)
        {
            if (string.IsNullOrEmpty(driverId)) return;

            try
            {
                CurrentStatus = $"Removing {driverId} driver...";

                var driverItem = DriverItems.FirstOrDefault(d => d.ToolId == driverId);
                if (driverItem == null) return;

                // For drivers, we just mark as uninstalled (actual driver removal requires system tools)
                // We can delete the downloaded driver files
                var driversPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PhoneRomFlashTool", "Drivers");

                await Task.Run(() =>
                {
                    try
                    {
                        var driverFolder = Path.Combine(driversPath, driverId.Replace("-", "_"));
                        if (Directory.Exists(driverFolder))
                        {
                            Directory.Delete(driverFolder, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddLog($"Error removing driver files: {ex.Message}");
                    }
                });

                // Mark as uninstalled
                MarkItemInstalled(driverItem, false, false);
                AddLog($"🗑️ {driverItem.Name} marked as removed");
                AddLog("Note: System driver may still be installed. Use Device Manager to fully remove.");
                CurrentStatus = $"{driverId} driver removed";
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
                CurrentStatus = "Remove failed";
            }
        }

        #endregion

        #region Helper Methods
        private void OpenToolsFolder()
        {
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            Directory.CreateDirectory(toolsPath);

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", toolsPath);
            }
            catch (Exception ex)
            {
                AddLog($"Error opening folder: {ex.Message}");
            }
        }

        private void OpenDownloadPage(string? itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return;

            if (ManualDownloadUrls.TryGetValue(itemId, out var urls) && urls.Length > 0)
            {
                try
                {
                    // Open first URL (official source)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = urls[0],
                        UseShellExecute = true
                    });
                    AddLog($"🌐 Opening download page for {itemId}...");
                    DebugWindow.LogInfo($"Opened manual download page: {urls[0]}");
                }
                catch (Exception ex)
                {
                    AddLog($"Error opening browser: {ex.Message}");
                    DebugWindow.LogError($"Failed to open URL: {ex.Message}");
                }
            }
            else
            {
                AddLog($"No download URL available for {itemId}");
            }
        }

        public static string? GetDownloadUrl(string itemId)
        {
            return ManualDownloadUrls.TryGetValue(itemId, out var urls) && urls.Length > 0 ? urls[0] : null;
        }

        private void Cancel()
        {
            _cts?.Cancel();
            StatusMessage = "Cancelled";
            IsChecking = false;
            IsInstalling = false;
        }

        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                // Add to end for chronological order
                Logs.Add($"[{timestamp}] {message}");
                if (Logs.Count > 500) Logs.RemoveAt(0); // Remove oldest
            });
        }
        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
