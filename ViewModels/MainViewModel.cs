using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using PhoneRomFlashTool.Models;
using PhoneRomFlashTool.Services;
using HexSearchResultModel = PhoneRomFlashTool.Models.HexSearchResult;

namespace PhoneRomFlashTool.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly AdbService _adbService;
        private readonly RomBackupService _backupService;
        private readonly HexEditorService _hexEditorService;
        private readonly FlashService _flashService;
        private readonly DownloadService _downloadService;
        private readonly AppSettings _settings;
        private readonly RomDatabaseViewModel _romDbViewModel;
        private readonly GuidesViewModel _guidesViewModel;
        private readonly ToolsViewModel _toolsViewModel;
        private readonly AdvancedToolsViewModel _advancedToolsViewModel;
        private readonly PhoneSpecViewModel _phoneSpecViewModel;
        private readonly EngineerToolsViewModel _engineerToolsViewModel;
        private readonly SetupViewModel _setupViewModel;
        private readonly RomSearchViewModel _romSearchViewModel;

        public event PropertyChangedEventHandler? PropertyChanged;

        // ViewModels
        public RomDatabaseViewModel RomDbViewModel => _romDbViewModel;
        public GuidesViewModel GuidesViewModel => _guidesViewModel;
        public ToolsViewModel ToolsViewModel => _toolsViewModel;
        public AdvancedToolsViewModel AdvancedToolsViewModel => _advancedToolsViewModel;
        public PhoneSpecViewModel PhoneSpecViewModel => _phoneSpecViewModel;
        public EngineerToolsViewModel EngineerToolsViewModel => _engineerToolsViewModel;
        public SetupViewModel SetupViewModel => _setupViewModel;
        public RomSearchViewModel RomSearchViewModel => _romSearchViewModel;

        private ObservableCollection<DeviceInfo> _connectedDevices = new();
        private DeviceInfo? _selectedDevice;
        private ObservableCollection<string> _logMessages = new();
        private int _progressValue;
        private string _progressText = "Ready";
        private bool _isOperationRunning;
        private ObservableCollection<PartitionInfo> _devicePartitions = new();
        private PartitionInfo? _selectedPartition;
        private ObservableCollection<RomInfo> _backupHistory = new();
        private HexData? _currentHexData;
        private ObservableCollection<HexLine> _hexLines = new();
        private string _searchHexPattern = string.Empty;
        private string _searchStringPattern = string.Empty;
        private ObservableCollection<HexSearchResultModel> _searchResults = new();
        private HexSearchResultModel? _selectedSearchResult;
        private string _currentRomPath = string.Empty;
        private RomAnalysisResult? _romAnalysis;

        // Downloads & Updates
        private ObservableCollection<DownloadableItem> _availableTools = new();
        private ObservableCollection<DownloadableItem> _availableDrivers = new();
        private ObservableCollection<RomDatabaseEntry> _romDatabase = new();
        private ObservableCollection<UpdateInfo> _availableUpdates = new();
        private string _romSearchBrand = string.Empty;
        private string _romSearchModel = string.Empty;
        private string _romSearchQuery = string.Empty;
        private bool _toolsInstalled;
        private DateTime _lastUpdateCheck = DateTime.MinValue;
        private ObservableCollection<RomDatabaseEntry> _filteredRomDatabase = new();

        public ObservableCollection<DeviceInfo> ConnectedDevices
        {
            get => _connectedDevices;
            set { _connectedDevices = value; OnPropertyChanged(); }
        }

        public DeviceInfo? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                OnPropertyChanged();
                _engineerToolsViewModel?.SetDevice(value?.SerialNumber);

                // Update status bar
                IsDeviceConnected = value != null;
                ConnectionStatus = value != null
                    ? $"Connected: {value.Brand} {value.Model}"
                    : "Disconnected";
            }
        }

        public ObservableCollection<string> LogMessages
        {
            get => _logMessages;
            set { _logMessages = value; OnPropertyChanged(); }
        }

        public int ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(); }
        }

        public bool IsOperationRunning
        {
            get => _isOperationRunning;
            set { _isOperationRunning = value; OnPropertyChanged(); }
        }

        // Status bar properties
        private string _connectionStatus = "Disconnected";
        private bool _isDeviceConnected;
        private string _currentOperation = "Ready";
        private string _dataSent = "0 KB";
        private string _dataReceived = "0 KB";
        private string _appVersion = "v1.0.0";

        // First run / Setup state
        private int _selectedTabIndex;
        private bool _isFirstRun;
        private bool _showSetupPrompt;

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set { _connectionStatus = value; OnPropertyChanged(); }
        }

        public bool IsDeviceConnected
        {
            get => _isDeviceConnected;
            set { _isDeviceConnected = value; OnPropertyChanged(); }
        }

        public string CurrentOperation
        {
            get => _currentOperation;
            set { _currentOperation = value; OnPropertyChanged(); }
        }

        public string DataSent
        {
            get => _dataSent;
            set { _dataSent = value; OnPropertyChanged(); }
        }

        public string DataReceived
        {
            get => _dataReceived;
            set { _dataReceived = value; OnPropertyChanged(); }
        }

        public string AppVersion
        {
            get => _appVersion;
            set { _appVersion = value; OnPropertyChanged(); }
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { _selectedTabIndex = value; OnPropertyChanged(); }
        }

        public bool IsFirstRun
        {
            get => _isFirstRun;
            set { _isFirstRun = value; OnPropertyChanged(); }
        }

        public bool ShowSetupPrompt
        {
            get => _showSetupPrompt;
            set { _showSetupPrompt = value; OnPropertyChanged(); }
        }

        public ObservableCollection<PartitionInfo> DevicePartitions
        {
            get => _devicePartitions;
            set { _devicePartitions = value; OnPropertyChanged(); }
        }

        public PartitionInfo? SelectedPartition
        {
            get => _selectedPartition;
            set { _selectedPartition = value; OnPropertyChanged(); }
        }

        public ObservableCollection<RomInfo> BackupHistory
        {
            get => _backupHistory;
            set { _backupHistory = value; OnPropertyChanged(); }
        }

        public HexData? CurrentHexData
        {
            get => _currentHexData;
            set { _currentHexData = value; OnPropertyChanged(); }
        }

        public ObservableCollection<HexLine> HexLines
        {
            get => _hexLines;
            set { _hexLines = value; OnPropertyChanged(); }
        }

        public string SearchHexPattern
        {
            get => _searchHexPattern;
            set { _searchHexPattern = value; OnPropertyChanged(); }
        }

        public string SearchStringPattern
        {
            get => _searchStringPattern;
            set { _searchStringPattern = value; OnPropertyChanged(); }
        }

        public ObservableCollection<HexSearchResultModel> SearchResults
        {
            get => _searchResults;
            set { _searchResults = value; OnPropertyChanged(); }
        }

        public HexSearchResultModel? SelectedSearchResult
        {
            get => _selectedSearchResult;
            set { _selectedSearchResult = value; OnPropertyChanged(); }
        }

        public string CurrentRomPath
        {
            get => _currentRomPath;
            set { _currentRomPath = value; OnPropertyChanged(); }
        }

        public RomAnalysisResult? RomAnalysis
        {
            get => _romAnalysis;
            set { _romAnalysis = value; OnPropertyChanged(); }
        }

        // Downloads & Settings Properties
        public ObservableCollection<DownloadableItem> AvailableTools
        {
            get => _availableTools;
            set { _availableTools = value; OnPropertyChanged(); }
        }

        public ObservableCollection<DownloadableItem> AvailableDrivers
        {
            get => _availableDrivers;
            set { _availableDrivers = value; OnPropertyChanged(); }
        }

        public ObservableCollection<RomDatabaseEntry> RomDatabase
        {
            get => _romDatabase;
            set { _romDatabase = value; OnPropertyChanged(); }
        }

        public ObservableCollection<UpdateInfo> AvailableUpdates
        {
            get => _availableUpdates;
            set { _availableUpdates = value; OnPropertyChanged(); }
        }

        public string RomSearchBrand
        {
            get => _romSearchBrand;
            set { _romSearchBrand = value; OnPropertyChanged(); SearchRomsInDatabase(); }
        }

        public string RomSearchModel
        {
            get => _romSearchModel;
            set { _romSearchModel = value; OnPropertyChanged(); SearchRomsInDatabase(); }
        }

        public string RomSearchQuery
        {
            get => _romSearchQuery;
            set { _romSearchQuery = value; OnPropertyChanged(); FilterRomDatabase(); }
        }

        public bool ToolsInstalled
        {
            get => _toolsInstalled;
            set { _toolsInstalled = value; OnPropertyChanged(); }
        }

        public DateTime LastUpdateCheck
        {
            get => _lastUpdateCheck;
            set { _lastUpdateCheck = value; OnPropertyChanged(); }
        }

        public ObservableCollection<RomDatabaseEntry> FilteredRomDatabase
        {
            get => _filteredRomDatabase;
            set { _filteredRomDatabase = value; OnPropertyChanged(); }
        }

        public AppSettings Settings => _settings;

        // Commands
        public ICommand RefreshDevicesCommand { get; }
        public ICommand LoadPartitionsCommand { get; }
        public ICommand BackupPartitionCommand { get; }
        public ICommand BackupFullRomCommand { get; }
        public ICommand RebootToModeCommand { get; }
        public ICommand OpenRomFileCommand { get; }
        public ICommand SaveRomFileCommand { get; }
        public ICommand ExportAnalysisCommand { get; }
        public ICommand ExportHexDumpCommand { get; }
        public ICommand SearchHexCommand { get; }
        public ICommand SearchStringCommand { get; }
        public ICommand GoToOffsetCommand { get; }
        public ICommand FlashPartitionCommand { get; }
        public ICommand UndoHexChangeCommand { get; }
        public ICommand UndoAllHexChangesCommand { get; }
        public ICommand OpenHexEditorWindowCommand { get; }

        // Download Commands
        public ICommand DownloadPlatformToolsCommand { get; }
        public ICommand DownloadAllEssentialsCommand { get; }
        public ICommand DownloadDriverCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand InstallUpdateCommand { get; }
        public ICommand OpenSettingsFolderCommand { get; }
        public ICommand SearchRomDatabaseCommand { get; }

        public MainViewModel()
        {
            _settings = AppSettings.Load();
            _adbService = new AdbService();
            _backupService = new RomBackupService(_adbService);
            _hexEditorService = new HexEditorService();
            _flashService = new FlashService(_adbService);
            _downloadService = new DownloadService(_settings);
            _romDbViewModel = new RomDatabaseViewModel();
            _romDbViewModel.LogMessage += (s, msg) => AddLog(msg);

            _guidesViewModel = new GuidesViewModel();
            _guidesViewModel.LogMessage += (s, msg) => AddLog(msg);

            _toolsViewModel = new ToolsViewModel();
            _toolsViewModel.LogMessage += (s, msg) => AddLog(msg);

            _advancedToolsViewModel = new AdvancedToolsViewModel();
            _advancedToolsViewModel.LogMessage += (s, msg) => AddLog(msg);

            _phoneSpecViewModel = new PhoneSpecViewModel();
            _phoneSpecViewModel.LogMessage += (s, msg) => AddLog(msg);

            _engineerToolsViewModel = new EngineerToolsViewModel(_adbService);

            _setupViewModel = new SetupViewModel();

            _romSearchViewModel = new RomSearchViewModel();
            _romSearchViewModel.LogMessage += (s, msg) => AddLog(msg);

            // Subscribe to setup completion events
            _setupViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SetupViewModel.AllToolsInstalled) && _setupViewModel.AllToolsInstalled)
                {
                    _settings.SetupCompleted = true;
                    _settings.PlatformToolsInstalled = true;
                    _settings.IsFirstRun = false;
                    _settings.Save();
                    ShowSetupPrompt = false;
                    AddLog("Setup completed! All required tools are installed.");
                }
            };

            // Check if first run or tools not installed
            IsFirstRun = _settings.IsFirstRun;
            ShowSetupPrompt = !_settings.SetupCompleted || !_downloadService.IsToolInstalled("platform-tools");

            // If setup not complete, select Setup tab
            if (ShowSetupPrompt)
            {
                SelectedTabIndex = 0; // Setup tab is first
            }

            // Initialize commands
            RefreshDevicesCommand = new RelayCommand(async () => await RefreshDevicesAsync());
            LoadPartitionsCommand = new RelayCommand(async () => await LoadPartitionsAsync());
            BackupPartitionCommand = new RelayCommand(async () => await BackupPartitionAsync());
            BackupFullRomCommand = new RelayCommand(async () => await BackupFullRomAsync());
            RebootToModeCommand = new RelayCommandWithParam<string>(async mode => await RebootToModeAsync(mode));
            OpenRomFileCommand = new RelayCommand(async () => await OpenRomFileAsync());
            SaveRomFileCommand = new RelayCommand(async () => await SaveRomFileAsync());
            ExportAnalysisCommand = new RelayCommand(async () => await ExportAnalysisAsync());
            ExportHexDumpCommand = new RelayCommand(async () => await ExportHexDumpAsync());
            SearchHexCommand = new RelayCommand(() => SearchHex());
            SearchStringCommand = new RelayCommand(() => SearchString());
            GoToOffsetCommand = new RelayCommand(() => GoToOffset());
            FlashPartitionCommand = new RelayCommand(async () => await FlashPartitionAsync());
            UndoHexChangeCommand = new RelayCommand(() => UndoHexChange());
            UndoAllHexChangesCommand = new RelayCommand(() => UndoAllHexChanges());
            OpenHexEditorWindowCommand = new RelayCommand(() => OpenHexEditorWindow());

            // Download commands
            DownloadPlatformToolsCommand = new RelayCommand(async () => await DownloadPlatformToolsAsync());
            DownloadAllEssentialsCommand = new RelayCommand(async () => await DownloadAllEssentialsAsync());
            DownloadDriverCommand = new RelayCommandWithParam<string>(async id => await DownloadDriverAsync(id));
            CheckForUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesAsync());
            InstallUpdateCommand = new RelayCommandWithParam<string>(async id => await InstallUpdateAsync(id));
            OpenSettingsFolderCommand = new RelayCommandWithParam<string>(type => OpenSettingsFolder(type));
            SearchRomDatabaseCommand = new RelayCommand(() => FilterRomDatabase());

            // Subscribe to events
            _adbService.DeviceConnected += OnDeviceConnected;
            _adbService.DeviceDisconnected += OnDeviceDisconnected;
            _adbService.LogMessage += OnLogMessage;
            _backupService.LogMessage += OnLogMessage;
            _hexEditorService.LogMessage += OnLogMessage;
            _flashService.LogMessage += OnLogMessage;
            _downloadService.LogMessage += OnLogMessage;

            // Start monitoring
            _adbService.StartDeviceMonitoring();

            AddLog("PhoneX Manager v1.0 - Xman Studio initialized");
            AddLog($"Tools path: {_settings.ToolsPath}");
            AddLog($"Drivers path: {_settings.DriversPath}");

            // Check if tools are installed
            ToolsInstalled = _downloadService.IsToolInstalled("platform-tools");
            if (!ToolsInstalled)
            {
                AddLog("Platform Tools not installed! Please go to Downloads tab to install.");
            }

            // Update last check time
            if (_settings.LastUpdateCheck != DateTime.MinValue)
            {
                LastUpdateCheck = _settings.LastUpdateCheck;
            }

            // Load ROM database
            LoadRomDatabase();

            // Load tools and drivers list
            LoadAvailableToolsAndDrivers();

            // Always check for updates on startup
            _ = CheckForUpdatesAsync();
        }

        private void LoadRomDatabase()
        {
            var roms = _downloadService.GetRomDatabase();
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                RomDatabase.Clear();
                FilteredRomDatabase.Clear();
                foreach (var rom in roms)
                {
                    RomDatabase.Add(rom);
                    FilteredRomDatabase.Add(rom);
                }
            });
        }

        private void SearchRomsInDatabase()
        {
            var results = _downloadService.SearchRoms(RomSearchBrand, RomSearchModel);
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                RomDatabase.Clear();
                foreach (var rom in results)
                {
                    RomDatabase.Add(rom);
                }
            });
        }

        private void FilterRomDatabase()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                FilteredRomDatabase.Clear();
                var query = RomSearchQuery?.ToLower() ?? string.Empty;
                foreach (var rom in RomDatabase)
                {
                    if (string.IsNullOrEmpty(query) ||
                        rom.Brand.ToLower().Contains(query) ||
                        rom.Model.ToLower().Contains(query) ||
                        rom.Codename.ToLower().Contains(query) ||
                        rom.RomType.ToLower().Contains(query))
                    {
                        FilteredRomDatabase.Add(rom);
                    }
                }
            });
        }

        private void LoadAvailableToolsAndDrivers()
        {
            var tools = _downloadService.GetEssentialTools();
            var drivers = _downloadService.GetAvailableDrivers();

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                AvailableTools.Clear();
                foreach (var tool in tools)
                {
                    AvailableTools.Add(tool);
                }

                AvailableDrivers.Clear();
                foreach (var driver in drivers)
                {
                    AvailableDrivers.Add(driver);
                }
            });
        }

        private async Task DownloadPlatformToolsAsync()
        {
            IsOperationRunning = true;
            ProgressValue = 0;
            ProgressText = "Downloading Platform Tools...";

            try
            {
                var progress = new Progress<DownloadProgressEventArgs>(p =>
                {
                    ProgressValue = p.PercentComplete;
                    ProgressText = $"{p.ItemName}: {p.Status}";
                });

                var success = await _downloadService.DownloadPlatformToolsAsync(progress);

                if (success)
                {
                    ToolsInstalled = true;
                    AddLog("Platform Tools installed successfully!");
                }
                else
                {
                    AddLog("Failed to install Platform Tools");
                }
            }
            finally
            {
                IsOperationRunning = false;
                ProgressText = "Ready";
            }
        }

        private async Task DownloadAllEssentialsAsync()
        {
            IsOperationRunning = true;
            ProgressValue = 0;
            ProgressText = "Downloading all essentials...";

            try
            {
                var progress = new Progress<DownloadProgressEventArgs>(p =>
                {
                    ProgressValue = p.PercentComplete;
                    ProgressText = $"{p.ItemName}: {p.Status}";
                });

                var success = await _downloadService.DownloadAllEssentialsAsync(progress);

                if (success)
                {
                    ToolsInstalled = true;
                    AddLog("All essential tools and drivers installed!");
                }
                else
                {
                    AddLog("Some downloads failed. Please retry.");
                }
            }
            finally
            {
                IsOperationRunning = false;
                ProgressText = "Ready";
            }
        }

        private async Task DownloadDriverAsync(string driverId)
        {
            IsOperationRunning = true;
            ProgressText = $"Downloading {driverId}...";

            try
            {
                var progress = new Progress<DownloadProgressEventArgs>(p =>
                {
                    ProgressValue = p.PercentComplete;
                    ProgressText = $"{p.ItemName}: {p.Status}";
                });

                var success = await _downloadService.DownloadDriverAsync(driverId, progress);

                if (success)
                {
                    AddLog($"{driverId} installed successfully!");
                }
                else
                {
                    AddLog($"Failed to install {driverId}");
                }
            }
            finally
            {
                IsOperationRunning = false;
                ProgressText = "Ready";
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            AddLog("Checking for updates...");

            try
            {
                var updates = await _downloadService.CheckForUpdatesAsync();

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    AvailableUpdates.Clear();
                    foreach (var update in updates)
                    {
                        AvailableUpdates.Add(update);
                    }
                });

                LastUpdateCheck = DateTime.Now;

                if (updates.Count > 0)
                {
                    AddLog($"Found {updates.Count} updates available");
                }
                else
                {
                    AddLog("All tools are up to date");
                }
            }
            catch (Exception ex)
            {
                AddLog($"Error checking updates: {ex.Message}");
            }
        }

        private async Task InstallUpdateAsync(string itemId)
        {
            var update = AvailableUpdates.FirstOrDefault(u => u.ItemId == itemId);
            if (update == null) return;

            if (update.ItemType == "Tool" && itemId == "platform-tools")
            {
                await DownloadPlatformToolsAsync();
            }
            else
            {
                await DownloadDriverAsync(itemId);
            }

            // Remove from updates list
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                AvailableUpdates.Remove(update);
            });
        }

        private void OpenSettingsFolder(string folderType)
        {
            try
            {
                var path = folderType switch
                {
                    "tools" => _settings.ToolsPath,
                    "drivers" => _settings.DriversPath,
                    "roms" => _settings.RomsPath,
                    "backups" => _settings.BackupsPath,
                    _ => Path.GetDirectoryName(_settings.ToolsPath)
                };

                if (!string.IsNullOrEmpty(path))
                {
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }
                    System.Diagnostics.Process.Start("explorer.exe", path);
                }
            }
            catch (Exception ex)
            {
                AddLog($"Error opening folder: {ex.Message}");
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnDeviceConnected(object? sender, DeviceInfo device)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (!ConnectedDevices.Any(d => d.SerialNumber == device.SerialNumber))
                {
                    ConnectedDevices.Add(device);
                    if (SelectedDevice == null)
                    {
                        SelectedDevice = device;
                    }
                }
            });
        }

        private void OnDeviceDisconnected(object? sender, DeviceInfo device)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                var existing = ConnectedDevices.FirstOrDefault(d => d.SerialNumber == device.SerialNumber);
                if (existing != null)
                {
                    ConnectedDevices.Remove(existing);
                    if (SelectedDevice?.SerialNumber == device.SerialNumber)
                    {
                        SelectedDevice = ConnectedDevices.FirstOrDefault();
                    }
                }
            });
        }

        private void OnLogMessage(object? sender, string message)
        {
            AddLog(message);
        }

        private void AddLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                // Add to end for chronological order (auto-scroll will scroll to bottom)
                LogMessages.Add($"[{timestamp}] {message}");
                if (LogMessages.Count > 1000)
                {
                    LogMessages.RemoveAt(0); // Remove oldest
                }
            });
        }

        private async Task RefreshDevicesAsync()
        {
            AddLog("Refreshing device list...");
            var devices = await _adbService.GetConnectedDevicesAsync();

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                ConnectedDevices.Clear();
                foreach (var device in devices)
                {
                    ConnectedDevices.Add(device);
                }

                if (ConnectedDevices.Count > 0 && SelectedDevice == null)
                {
                    SelectedDevice = ConnectedDevices[0];
                }
            });

            AddLog($"Found {devices.Count} device(s)");
        }

        private async Task LoadPartitionsAsync()
        {
            if (SelectedDevice == null) return;

            AddLog($"Loading partitions for {SelectedDevice.DisplayName}...");
            var partitions = await _backupService.GetDevicePartitionsAsync(SelectedDevice.SerialNumber);

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                DevicePartitions.Clear();
                foreach (var partition in partitions)
                {
                    DevicePartitions.Add(partition);
                }
            });

            AddLog($"Found {partitions.Count} partitions");
        }

        private async Task BackupPartitionAsync()
        {
            if (SelectedDevice == null || SelectedPartition == null) return;

            IsOperationRunning = true;
            ProgressValue = 0;
            ProgressText = "Starting backup...";

            try
            {
                var progress = new Progress<ProgressEventArgs>(p =>
                {
                    ProgressValue = p.Percentage;
                    ProgressText = p.Message;
                });

                var result = await _backupService.BackupPartitionAsync(
                    SelectedDevice.SerialNumber,
                    SelectedPartition.Name,
                    progress);

                if (result != null)
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        BackupHistory.Insert(0, result);
                    });
                    AddLog($"Backup completed: {result.FilePath}");
                }
                else
                {
                    AddLog("Backup failed");
                }
            }
            finally
            {
                IsOperationRunning = false;
                ProgressText = "Ready";
            }
        }

        private async Task BackupFullRomAsync()
        {
            if (SelectedDevice == null) return;

            IsOperationRunning = true;
            ProgressValue = 0;
            ProgressText = "Starting full backup...";

            try
            {
                var progress = new Progress<ProgressEventArgs>(p =>
                {
                    ProgressValue = p.Percentage;
                    ProgressText = p.Message;
                });

                var result = await _backupService.BackupFullRomAsync(
                    SelectedDevice.SerialNumber,
                    progress);

                if (result != null)
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        BackupHistory.Insert(0, result);
                    });
                    AddLog($"Full backup completed: {result.FilePath}");
                }
            }
            finally
            {
                IsOperationRunning = false;
                ProgressText = "Ready";
            }
        }

        private async Task RebootToModeAsync(string mode)
        {
            if (SelectedDevice == null) return;

            var deviceMode = mode switch
            {
                "recovery" => DeviceMode.Recovery,
                "fastboot" => DeviceMode.Fastboot,
                "download" => DeviceMode.Download,
                "normal" => DeviceMode.Normal,
                _ => DeviceMode.Normal
            };

            await _adbService.RebootToModeAsync(SelectedDevice.SerialNumber, deviceMode);
        }

        private async Task OpenRomFileAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Open ROM File",
                Filter = "ROM Files (*.img;*.bin;*.mbn)|*.img;*.bin;*.mbn|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                await LoadRomFileAsync(dialog.FileName);
            }
        }

        private async Task LoadRomFileAsync(string filePath)
        {
            IsOperationRunning = true;
            ProgressText = "Loading ROM file...";

            try
            {
                CurrentRomPath = filePath;
                CurrentHexData = await _hexEditorService.LoadFileAsync(filePath);

                if (CurrentHexData != null)
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        HexLines = CurrentHexData.Lines;
                    });
                    AddLog($"ROM file loaded: {filePath}");

                    ProgressText = "Analyzing ROM...";
                    RomAnalysis = await _hexEditorService.AnalyzeRomAsync(filePath);
                    AddLog($"ROM type detected: {RomAnalysis.FileType}");
                }
            }
            finally
            {
                IsOperationRunning = false;
                ProgressText = "Ready";
            }
        }

        private void SearchHex()
        {
            if (string.IsNullOrEmpty(SearchHexPattern)) return;

            var results = _hexEditorService.SearchHexString(SearchHexPattern);
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                SearchResults.Clear();
                foreach (var result in results)
                {
                    SearchResults.Add(result);
                }
            });

            AddLog($"Found {results.Count} matches for hex pattern");
        }

        private void SearchString()
        {
            if (string.IsNullOrEmpty(SearchStringPattern)) return;

            var results = _hexEditorService.SearchString(SearchStringPattern);
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                SearchResults.Clear();
                foreach (var result in results)
                {
                    SearchResults.Add(result);
                }
            });

            AddLog($"Found {results.Count} matches for string pattern");
        }

        private void GoToOffset()
        {
            if (SelectedSearchResult != null)
            {
                var lines = _hexEditorService.GetLinesInRange(
                    SelectedSearchResult.Offset - 256,
                    SelectedSearchResult.Offset + 512);

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    HexLines = lines;
                });
            }
        }

        private async Task SaveRomFileAsync()
        {
            if (CurrentHexData == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save ROM File",
                Filter = "ROM Files (*.img)|*.img|All Files (*.*)|*.*",
                FileName = Path.GetFileName(CurrentRomPath)
            };

            if (dialog.ShowDialog() == true)
            {
                await _hexEditorService.SaveFileAsync(dialog.FileName);
                AddLog($"ROM file saved: {dialog.FileName}");
            }
        }

        private async Task ExportAnalysisAsync()
        {
            if (string.IsNullOrEmpty(CurrentRomPath)) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export ROM Analysis",
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = Path.GetFileNameWithoutExtension(CurrentRomPath) + "_analysis.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                await _hexEditorService.ExportAnalysisAsync(CurrentRomPath, dialog.FileName);
                AddLog($"Analysis exported: {dialog.FileName}");
            }
        }

        private async Task ExportHexDumpAsync()
        {
            if (string.IsNullOrEmpty(CurrentRomPath)) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Hex Dump",
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = Path.GetFileNameWithoutExtension(CurrentRomPath) + "_hexdump.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                await _hexEditorService.ExportHexDumpAsync(dialog.FileName);
                AddLog($"Hex dump exported: {dialog.FileName}");
            }
        }

        private async Task FlashPartitionAsync()
        {
            if (SelectedDevice == null || string.IsNullOrEmpty(CurrentRomPath)) return;

            if (SelectedPartition == null)
            {
                AddLog("Please select a partition to flash");
                return;
            }

            IsOperationRunning = true;
            ProgressValue = 0;
            ProgressText = "Starting flash...";

            try
            {
                var progress = new Progress<ProgressEventArgs>(p =>
                {
                    ProgressValue = p.Percentage;
                    ProgressText = p.Message;
                });

                var result = await _flashService.FlashPartitionAsync(
                    SelectedDevice.SerialNumber,
                    SelectedPartition.Name,
                    CurrentRomPath,
                    progress);

                if (result)
                {
                    AddLog("Flash completed successfully");
                }
                else
                {
                    AddLog("Flash failed");
                }
            }
            finally
            {
                IsOperationRunning = false;
                ProgressText = "Ready";
            }
        }

        private void UndoHexChange()
        {
            _hexEditorService.UndoLastModification();
            RefreshHexView();
        }

        private void UndoAllHexChanges()
        {
            _hexEditorService.UndoAllModifications();
            RefreshHexView();
        }

        private void OpenHexEditorWindow()
        {
            var hexWindow = new Views.HexEditorWindow();
            hexWindow.Show();
            AddLog("Opened full Hex Editor window");
        }

        private void RefreshHexView()
        {
            if (CurrentHexData != null)
            {
                var currentOffset = HexLines.Count > 0 ? HexLines[0].Offset : 0;
                var lines = _hexEditorService.GetLinesInRange(currentOffset, currentOffset + 10000);
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    HexLines = lines;
                });
            }
        }

        public void ModifyByte(long offset, byte newValue)
        {
            if (_hexEditorService.ModifyByte(offset, newValue))
            {
                RefreshHexView();
            }
        }

        public void Cleanup()
        {
            _adbService.StopDeviceMonitoring();
            _adbService.Dispose();
            _settings.Save();
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();
    }

    public class RelayCommandWithParam<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool>? _canExecute;

        public RelayCommandWithParam(Action<T> execute, Func<T, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke((T)parameter!) ?? true;
        public void Execute(object? parameter) => _execute((T)parameter!);
    }
}
