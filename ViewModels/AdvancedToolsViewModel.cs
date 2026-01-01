using System;
using System.Collections.Generic;
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
    public class AdvancedToolsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<string>? LogMessage;

        private readonly ImageService _imageService;
        private MagiskService? _magiskService;
        private QualcommService? _qualcommService;
        private MtkService? _mtkService;
        private SamsungService? _samsungService;
        private FileManagerService? _fileManagerService;
        private ImeiService? _imeiService;
        private DeviceDiagnosticsService? _diagnosticsService;
        private FastbootService? _fastbootService;
        private BuildPropService? _buildPropService;
        private ScreenMirrorService? _screenMirrorService;

        private readonly string _toolsPath;

        // Magisk
        private RootStatus? _rootStatus;
        public RootStatus? RootStatus
        {
            get => _rootStatus;
            set { _rootStatus = value; OnPropertyChanged(); }
        }

        private MagiskInfo? _latestMagisk;
        public MagiskInfo? LatestMagisk
        {
            get => _latestMagisk;
            set { _latestMagisk = value; OnPropertyChanged(); }
        }

        public ObservableCollection<MagiskModule> InstalledModules { get; } = new();

        // Device Detection
        public ObservableCollection<QualcommDevice> QualcommDevices { get; } = new();
        public ObservableCollection<MtkDevice> MtkDevices { get; } = new();
        public ObservableCollection<SamsungDevice> SamsungDevices { get; } = new();

        // File Manager
        private string _currentPath = "/sdcard";
        public string CurrentPath
        {
            get => _currentPath;
            set { _currentPath = value; OnPropertyChanged(); }
        }

        public ObservableCollection<DeviceFile> CurrentFiles { get; } = new();
        public ObservableCollection<StorageInfo> StorageInfos { get; } = new();

        private DeviceFile? _selectedFile;
        public DeviceFile? SelectedFile
        {
            get => _selectedFile;
            set { _selectedFile = value; OnPropertyChanged(); }
        }

        // IMEI
        private string _imei1 = "";
        public string Imei1
        {
            get => _imei1;
            set { _imei1 = value; OnPropertyChanged(); }
        }

        private string _imei2 = "";
        public string Imei2
        {
            get => _imei2;
            set { _imei2 = value; OnPropertyChanged(); }
        }

        // Samsung
        public ObservableCollection<OdinFile> OdinFiles { get; } = new();
        private bool _odinWipeData;
        public bool OdinWipeData
        {
            get => _odinWipeData;
            set { _odinWipeData = value; OnPropertyChanged(); }
        }

        // Bootloader Status
        private BootloaderStatus? _bootloaderStatus;
        public BootloaderStatus? BootloaderStatus
        {
            get => _bootloaderStatus;
            set { _bootloaderStatus = value; OnPropertyChanged(); }
        }

        // Play Integrity
        private IntegrityStatus? _integrityStatus;
        public IntegrityStatus? IntegrityStatus
        {
            get => _integrityStatus;
            set { _integrityStatus = value; OnPropertyChanged(); }
        }

        // Battery Health
        private BatteryHealth? _batteryHealth;
        public BatteryHealth? BatteryHealth
        {
            get => _batteryHealth;
            set { _batteryHealth = value; OnPropertyChanged(); }
        }

        // Sensors
        public ObservableCollection<SensorInfo> Sensors { get; } = new();

        // Network
        private NetworkInfo? _networkInfo;
        public NetworkInfo? NetworkInfo
        {
            get => _networkInfo;
            set { _networkInfo = value; OnPropertyChanged(); }
        }

        // Apps
        public ObservableCollection<AppInfo> InstalledApps { get; } = new();
        private AppInfo? _selectedApp;
        public AppInfo? SelectedApp
        {
            get => _selectedApp;
            set { _selectedApp = value; OnPropertyChanged(); }
        }

        // Fastboot
        public ObservableCollection<FastbootDevice> FastbootDevices { get; } = new();
        public ObservableCollection<FastbootPartitionInfo> Partitions { get; } = new();
        private FastbootDevice? _selectedFastbootDevice;
        public FastbootDevice? SelectedFastbootDevice
        {
            get => _selectedFastbootDevice;
            set { _selectedFastbootDevice = value; OnPropertyChanged(); }
        }

        // Build.prop
        public ObservableCollection<BuildPropFile> PropFiles { get; } = new();
        public ObservableCollection<BuildPropEntry> PropEntries { get; } = new();
        private BuildPropFile? _selectedPropFile;
        public BuildPropFile? SelectedPropFile
        {
            get => _selectedPropFile;
            set { _selectedPropFile = value; OnPropertyChanged(); }
        }

        // Benchmark
        private DeviceBenchmark? _benchmarkResult;
        public DeviceBenchmark? BenchmarkResult
        {
            get => _benchmarkResult;
            set { _benchmarkResult = value; OnPropertyChanged(); }
        }

        // Screen Mirror
        private bool _isMirroring;
        public bool IsMirroring
        {
            get => _isMirroring;
            set { _isMirroring = value; OnPropertyChanged(); }
        }

        private bool _isRecording;
        public bool IsRecording
        {
            get => _isRecording;
            set { _isRecording = value; OnPropertyChanged(); }
        }

        // Progress
        private int _progress;
        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        private string _statusText = "Ready";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        // Commands
        public ICommand CheckRootStatusCommand { get; }
        public ICommand GetMagiskInfoCommand { get; }
        public ICommand DownloadMagiskCommand { get; }
        public ICommand InstallMagiskCommand { get; }
        public ICommand RefreshModulesCommand { get; }
        public ICommand ToggleModuleCommand { get; }
        public ICommand RemoveModuleCommand { get; }

        public ICommand DetectQualcommCommand { get; }
        public ICommand DetectMtkCommand { get; }
        public ICommand DetectSamsungCommand { get; }

        public ICommand BrowseDirectoryCommand { get; }
        public ICommand NavigateUpCommand { get; }
        public ICommand NavigateToCommand { get; }
        public ICommand PushFileCommand { get; }
        public ICommand PullFileCommand { get; }
        public ICommand DeleteFileCommand { get; }
        public ICommand RefreshFilesCommand { get; }

        public ICommand ReadImeiCommand { get; }
        public ICommand ValidateImeiCommand { get; }

        public ICommand LoadOdinFilesCommand { get; }
        public ICommand FlashOdinCommand { get; }

        public ICommand DownloadAllImagesCommand { get; }

        // New Commands
        public ICommand CheckBootloaderCommand { get; }
        public ICommand CheckPlayIntegrityCommand { get; }
        public ICommand CheckBatteryHealthCommand { get; }
        public ICommand GetSensorListCommand { get; }
        public ICommand GetNetworkInfoCommand { get; }
        public ICommand GetInstalledAppsCommand { get; }
        public ICommand InstallApkCommand { get; }
        public ICommand UninstallAppCommand { get; }
        public ICommand BackupApkCommand { get; }
        public ICommand DetectFastbootCommand { get; }
        public ICommand FlashPartitionCommand { get; }
        public ICommand RebootFastbootCommand { get; }
        public ICommand UnlockBootloaderCommand { get; }
        public ICommand GetPropFilesCommand { get; }
        public ICommand LoadPropFileCommand { get; }
        public ICommand SavePropFileCommand { get; }
        public ICommand RunBenchmarkCommand { get; }
        public ICommand StartMirrorCommand { get; }
        public ICommand StopMirrorCommand { get; }
        public ICommand StartRecordingCommand { get; }
        public ICommand StopRecordingCommand { get; }
        public ICommand TakeScreenshotCommand { get; }

        public AdvancedToolsViewModel()
        {
            _toolsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhoneRomFlashTool", "Tools");

            _imageService = new ImageService();
            _imageService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);

            InitializeServices();

            // Initialize commands
            CheckRootStatusCommand = new RelayCommand(async () => await CheckRootStatus());
            GetMagiskInfoCommand = new RelayCommand(async () => await GetMagiskInfo());
            DownloadMagiskCommand = new RelayCommand(async () => await DownloadMagisk());
            InstallMagiskCommand = new RelayCommand(async () => await InstallMagisk());
            RefreshModulesCommand = new RelayCommand(async () => await RefreshModules());
            ToggleModuleCommand = new RelayCommandWithParam<MagiskModule>(async (m) => await ToggleModule(m));
            RemoveModuleCommand = new RelayCommandWithParam<MagiskModule>(async (m) => await RemoveModule(m));

            DetectQualcommCommand = new RelayCommand(async () => await DetectQualcomm());
            DetectMtkCommand = new RelayCommand(async () => await DetectMtk());
            DetectSamsungCommand = new RelayCommand(async () => await DetectSamsung());

            BrowseDirectoryCommand = new RelayCommand(async () => await BrowseDirectory());
            NavigateUpCommand = new RelayCommand(async () => await NavigateUp());
            NavigateToCommand = new RelayCommandWithParam<string>(async (p) => await NavigateTo(p));
            PushFileCommand = new RelayCommand(async () => await PushFile());
            PullFileCommand = new RelayCommand(async () => await PullFile());
            DeleteFileCommand = new RelayCommand(async () => await DeleteFile());
            RefreshFilesCommand = new RelayCommand(async () => await RefreshFiles());

            ReadImeiCommand = new RelayCommand(async () => await ReadImei());
            ValidateImeiCommand = new RelayCommand(() => ValidateImei());

            LoadOdinFilesCommand = new RelayCommand(async () => await LoadOdinFiles());
            FlashOdinCommand = new RelayCommand(async () => await FlashOdin());

            DownloadAllImagesCommand = new RelayCommand(async () => await DownloadAllImages());

            // New commands
            CheckBootloaderCommand = new RelayCommand(async () => await CheckBootloader());
            CheckPlayIntegrityCommand = new RelayCommand(async () => await CheckPlayIntegrity());
            CheckBatteryHealthCommand = new RelayCommand(async () => await CheckBatteryHealth());
            GetSensorListCommand = new RelayCommand(async () => await GetSensorList());
            GetNetworkInfoCommand = new RelayCommand(async () => await GetNetworkInfo());
            GetInstalledAppsCommand = new RelayCommand(async () => await GetInstalledApps());
            InstallApkCommand = new RelayCommand(async () => await InstallApk());
            UninstallAppCommand = new RelayCommand(async () => await UninstallApp());
            BackupApkCommand = new RelayCommand(async () => await BackupApk());
            DetectFastbootCommand = new RelayCommand(async () => await DetectFastboot());
            FlashPartitionCommand = new RelayCommand(async () => await FlashPartition());
            RebootFastbootCommand = new RelayCommandWithParam<string>(async (m) => await RebootFastboot(m));
            UnlockBootloaderCommand = new RelayCommand(async () => await UnlockBootloader());
            GetPropFilesCommand = new RelayCommand(async () => await GetPropFiles());
            LoadPropFileCommand = new RelayCommand(async () => await LoadPropFile());
            SavePropFileCommand = new RelayCommand(async () => await SavePropFile());
            RunBenchmarkCommand = new RelayCommand(async () => await RunBenchmark());
            StartMirrorCommand = new RelayCommand(async () => await StartMirror());
            StopMirrorCommand = new RelayCommand(() => StopMirror());
            StartRecordingCommand = new RelayCommand(async () => await StartRecording());
            StopRecordingCommand = new RelayCommand(() => StopRecording());
            TakeScreenshotCommand = new RelayCommand(async () => await TakeScreenshot());
        }

        private void InitializeServices()
        {
            var adbPath = Path.Combine(_toolsPath, "platform-tools", "adb.exe");
            if (File.Exists(adbPath))
            {
                _magiskService = new MagiskService(adbPath);
                _magiskService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);

                _fileManagerService = new FileManagerService(adbPath);
                _fileManagerService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);

                _imeiService = new ImeiService(adbPath);
                _imeiService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);

                _buildPropService = new BuildPropService(adbPath);
                _buildPropService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);
            }

            _qualcommService = new QualcommService(_toolsPath);
            _qualcommService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);

            _mtkService = new MtkService(_toolsPath);
            _mtkService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);

            _samsungService = new SamsungService(_toolsPath);
            _samsungService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);

            _diagnosticsService = new DeviceDiagnosticsService(_toolsPath);
            _diagnosticsService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);

            _fastbootService = new FastbootService(_toolsPath);
            _fastbootService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);

            _screenMirrorService = new ScreenMirrorService(_toolsPath);
            _screenMirrorService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);
            _screenMirrorService.MirrorStarted += (s, e) => IsMirroring = true;
            _screenMirrorService.MirrorStopped += (s, e) => IsMirroring = false;
        }

        // Magisk Methods
        private async Task CheckRootStatus()
        {
            if (_magiskService == null)
            {
                LogMessage?.Invoke(this, "ADB not found");
                return;
            }

            IsBusy = true;
            StatusText = "Checking root status...";

            try
            {
                RootStatus = await _magiskService.CheckRootStatusAsync();

                if (RootStatus.IsRooted)
                {
                    LogMessage?.Invoke(this, $"Device is rooted via {RootStatus.RootMethod}");
                    if (!string.IsNullOrEmpty(RootStatus.MagiskVersion))
                    {
                        LogMessage?.Invoke(this, $"Magisk version: {RootStatus.MagiskVersion}");
                    }
                }
                else
                {
                    LogMessage?.Invoke(this, "Device is not rooted");
                }
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        private async Task GetMagiskInfo()
        {
            if (_magiskService == null) return;

            IsBusy = true;
            StatusText = "Getting Magisk info...";

            try
            {
                LatestMagisk = await _magiskService.GetLatestMagiskAsync();
                LogMessage?.Invoke(this, $"Latest Magisk: {LatestMagisk.Version}");
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        private async Task DownloadMagisk()
        {
            if (_magiskService == null || LatestMagisk == null) return;

            IsBusy = true;
            StatusText = "Downloading Magisk...";

            try
            {
                var progress = new Progress<int>(p => Progress = p);
                var path = await _magiskService.DownloadMagiskAsync(LatestMagisk, progress);

                if (!string.IsNullOrEmpty(path))
                {
                    LogMessage?.Invoke(this, $"Downloaded to: {path}");
                }
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        private async Task InstallMagisk()
        {
            // Implementation
            await Task.CompletedTask;
        }

        private async Task RefreshModules()
        {
            if (_magiskService == null) return;

            IsBusy = true;

            try
            {
                var modules = await _magiskService.GetInstalledModulesAsync();

                InstalledModules.Clear();
                foreach (var module in modules)
                {
                    InstalledModules.Add(module);
                }

                LogMessage?.Invoke(this, $"Found {modules.Count} modules");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ToggleModule(MagiskModule? module)
        {
            if (_magiskService == null || module == null) return;

            await _magiskService.ToggleModuleAsync(module.Id, !module.Enabled);
            await RefreshModules();
        }

        private async Task RemoveModule(MagiskModule? module)
        {
            if (_magiskService == null || module == null) return;

            await _magiskService.RemoveModuleAsync(module.Id);
            await RefreshModules();
        }

        // Device Detection
        private async Task DetectQualcomm()
        {
            if (_qualcommService == null) return;

            var devices = await _qualcommService.DetectEdlDevicesAsync();

            QualcommDevices.Clear();
            foreach (var device in devices)
            {
                QualcommDevices.Add(device);
            }
        }

        private async Task DetectMtk()
        {
            if (_mtkService == null) return;

            var devices = await _mtkService.DetectMtkDevicesAsync();

            MtkDevices.Clear();
            foreach (var device in devices)
            {
                MtkDevices.Add(device);
            }
        }

        private async Task DetectSamsung()
        {
            if (_samsungService == null) return;

            var devices = await _samsungService.DetectSamsungDevicesAsync();

            SamsungDevices.Clear();
            foreach (var device in devices)
            {
                SamsungDevices.Add(device);
            }
        }

        // File Manager
        private async Task BrowseDirectory()
        {
            if (_fileManagerService == null) return;

            var files = await _fileManagerService.ListDirectoryAsync(CurrentPath);

            CurrentFiles.Clear();
            foreach (var file in files)
            {
                CurrentFiles.Add(file);
            }
        }

        private async Task NavigateUp()
        {
            if (_fileManagerService == null) return;

            _fileManagerService.NavigateUp();
            CurrentPath = _fileManagerService.CurrentPath;
            await BrowseDirectory();
        }

        private async Task NavigateTo(string? path)
        {
            if (_fileManagerService == null || string.IsNullOrEmpty(path)) return;

            CurrentPath = path;
            await BrowseDirectory();
        }

        private async Task PushFile()
        {
            if (_fileManagerService == null) return;

            var dialog = new OpenFileDialog
            {
                Title = "Select file to push"
            };

            if (dialog.ShowDialog() == true)
            {
                var remotePath = Path.Combine(CurrentPath, Path.GetFileName(dialog.FileName)).Replace("\\", "/");
                await _fileManagerService.PushFileAsync(dialog.FileName, remotePath);
                await RefreshFiles();
            }
        }

        private async Task PullFile()
        {
            if (_fileManagerService == null || SelectedFile == null) return;

            var dialog = new SaveFileDialog
            {
                FileName = SelectedFile.Name,
                Title = "Save file as"
            };

            if (dialog.ShowDialog() == true)
            {
                await _fileManagerService.PullFileAsync(SelectedFile.Path, dialog.FileName);
            }
        }

        private async Task DeleteFile()
        {
            if (_fileManagerService == null || SelectedFile == null) return;

            await _fileManagerService.DeleteFileAsync(SelectedFile.Path, SelectedFile.IsDirectory);
            await RefreshFiles();
        }

        private async Task RefreshFiles()
        {
            await BrowseDirectory();

            if (_fileManagerService != null)
            {
                var storages = await _fileManagerService.GetStorageInfoAsync();
                StorageInfos.Clear();
                foreach (var storage in storages)
                {
                    StorageInfos.Add(storage);
                }
            }
        }

        // IMEI
        private async Task ReadImei()
        {
            if (_imeiService == null) return;

            var info = await _imeiService.ReadImeiInfoAsync();

            if (info.TryGetValue("IMEI 1", out var imei1))
                Imei1 = imei1;
            if (info.TryGetValue("IMEI 2", out var imei2))
                Imei2 = imei2;
        }

        private void ValidateImei()
        {
            if (_imeiService == null) return;

            var valid1 = _imeiService.ValidateImei(Imei1);
            var valid2 = string.IsNullOrEmpty(Imei2) || _imeiService.ValidateImei(Imei2);

            LogMessage?.Invoke(this, $"IMEI 1: {(valid1 ? "Valid" : "Invalid")}");
            if (!string.IsNullOrEmpty(Imei2))
            {
                LogMessage?.Invoke(this, $"IMEI 2: {(valid2 ? "Valid" : "Invalid")}");
            }
        }

        // Samsung Odin
        private async Task LoadOdinFiles()
        {
            if (_samsungService == null) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Samsung Firmware|*.zip|All Files|*.*",
                Title = "Select Samsung firmware package"
            };

            if (dialog.ShowDialog() == true)
            {
                IsBusy = true;
                StatusText = "Extracting firmware...";

                try
                {
                    var files = await _samsungService.ExtractOdinPackageAsync(dialog.FileName);

                    OdinFiles.Clear();
                    foreach (var file in files)
                    {
                        OdinFiles.Add(file);
                    }
                }
                finally
                {
                    IsBusy = false;
                    StatusText = "Ready";
                }
            }
        }

        private async Task FlashOdin()
        {
            if (_samsungService == null || OdinFiles.Count == 0) return;

            IsBusy = true;
            StatusText = "Flashing...";

            try
            {
                var progress = new Progress<int>(p => Progress = p);
                var files = new System.Collections.Generic.List<OdinFile>(OdinFiles);
                await _samsungService.FlashOdinAsync(files, OdinWipeData, progress);
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        // Images
        private async Task DownloadAllImages()
        {
            IsBusy = true;
            StatusText = "Downloading brand logos...";

            try
            {
                await _imageService.DownloadAllBrandLogosAsync();

                StatusText = "Downloading device images...";
                var progress = new Progress<int>(p => Progress = p);
                await _imageService.DownloadAllDeviceImagesAsync(progress);

                LogMessage?.Invoke(this, "All images downloaded!");
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        // Bootloader Check
        private async Task CheckBootloader()
        {
            if (_diagnosticsService == null) return;

            IsBusy = true;
            StatusText = "Checking bootloader status...";

            try
            {
                BootloaderStatus = await _diagnosticsService.CheckBootloaderStatusAsync();
                LogMessage?.Invoke(this, $"Bootloader: {BootloaderStatus.Status}");
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        // Play Integrity
        private async Task CheckPlayIntegrity()
        {
            if (_diagnosticsService == null) return;

            IsBusy = true;
            StatusText = "Checking Play Integrity...";

            try
            {
                IntegrityStatus = await _diagnosticsService.CheckPlayIntegrityAsync();
                LogMessage?.Invoke(this, $"Play Integrity: {IntegrityStatus.EvaluationType}");

                if (IntegrityStatus.FailureReasons.Count > 0)
                {
                    foreach (var reason in IntegrityStatus.FailureReasons)
                    {
                        LogMessage?.Invoke(this, $"  - {reason}");
                    }
                }
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        // Battery Health
        private async Task CheckBatteryHealth()
        {
            if (_diagnosticsService == null) return;

            IsBusy = true;
            StatusText = "Checking battery health...";

            try
            {
                BatteryHealth = await _diagnosticsService.GetBatteryHealthAsync();
                LogMessage?.Invoke(this, $"Battery: {BatteryHealth.Level}% - {BatteryHealth.Health}");
                if (BatteryHealth.DesignCapacity > 0)
                {
                    LogMessage?.Invoke(this, $"Health: {BatteryHealth.HealthPercent:F1}% of design capacity");
                }
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        // Sensors
        private async Task GetSensorList()
        {
            if (_diagnosticsService == null) return;

            IsBusy = true;
            StatusText = "Getting sensor list...";

            try
            {
                var sensors = await _diagnosticsService.GetSensorListAsync();

                Sensors.Clear();
                foreach (var sensor in sensors)
                {
                    Sensors.Add(sensor);
                }

                LogMessage?.Invoke(this, $"Found {sensors.Count} sensors");
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        // Network
        private async Task GetNetworkInfo()
        {
            if (_diagnosticsService == null) return;

            IsBusy = true;
            StatusText = "Getting network info...";

            try
            {
                NetworkInfo = await _diagnosticsService.GetNetworkInfoAsync();
                LogMessage?.Invoke(this, $"Network: {NetworkInfo.ConnectionType} - {NetworkInfo.Operator}");
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        // App Manager
        private async Task GetInstalledApps()
        {
            if (_diagnosticsService == null) return;

            IsBusy = true;
            StatusText = "Getting installed apps...";

            try
            {
                var apps = await _diagnosticsService.GetInstalledAppsAsync(false);

                InstalledApps.Clear();
                foreach (var app in apps)
                {
                    InstalledApps.Add(app);
                }

                LogMessage?.Invoke(this, $"Found {apps.Count} user apps");
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        private async Task InstallApk()
        {
            if (_diagnosticsService == null) return;

            var dialog = new OpenFileDialog
            {
                Filter = "APK Files|*.apk|All Files|*.*",
                Title = "Select APK to install"
            };

            if (dialog.ShowDialog() == true)
            {
                IsBusy = true;
                StatusText = "Installing APK...";

                try
                {
                    await _diagnosticsService.InstallApkAsync(dialog.FileName);
                }
                finally
                {
                    IsBusy = false;
                    StatusText = "Ready";
                }
            }
        }

        private async Task UninstallApp()
        {
            if (_diagnosticsService == null || SelectedApp == null) return;

            IsBusy = true;
            StatusText = $"Uninstalling {SelectedApp.AppName}...";

            try
            {
                await _diagnosticsService.UninstallAppAsync(SelectedApp.PackageName);
                await GetInstalledApps();
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        private async Task BackupApk()
        {
            if (_diagnosticsService == null || SelectedApp == null) return;

            var dialog = new SaveFileDialog
            {
                FileName = $"{SelectedApp.PackageName}.apk",
                Filter = "APK Files|*.apk",
                Title = "Save APK backup"
            };

            if (dialog.ShowDialog() == true)
            {
                IsBusy = true;
                StatusText = $"Backing up {SelectedApp.AppName}...";

                try
                {
                    await _diagnosticsService.BackupApkAsync(SelectedApp.PackageName, dialog.FileName);
                }
                finally
                {
                    IsBusy = false;
                    StatusText = "Ready";
                }
            }
        }

        // Fastboot
        private async Task DetectFastboot()
        {
            if (_fastbootService == null) return;

            IsBusy = true;
            StatusText = "Detecting fastboot devices...";

            try
            {
                var devices = await _fastbootService.DetectDevicesAsync();

                FastbootDevices.Clear();
                foreach (var device in devices)
                {
                    FastbootDevices.Add(device);
                }

                if (devices.Count > 0)
                {
                    SelectedFastbootDevice = devices[0];

                    var partitions = await _fastbootService.GetPartitionListAsync(devices[0].Serial);
                    Partitions.Clear();
                    foreach (var partition in partitions)
                    {
                        Partitions.Add(partition);
                    }
                }
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        private async Task FlashPartition()
        {
            if (_fastbootService == null || SelectedFastbootDevice == null) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Image Files|*.img|All Files|*.*",
                Title = "Select image to flash"
            };

            if (dialog.ShowDialog() == true)
            {
                // Ask for partition name
                var partitionName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);

                IsBusy = true;
                StatusText = $"Flashing {partitionName}...";
                Progress = 0;

                try
                {
                    var progress = new Progress<int>(p => Progress = p);
                    await _fastbootService.FlashPartitionAsync(partitionName, dialog.FileName,
                        SelectedFastbootDevice.Serial, progress);
                }
                finally
                {
                    IsBusy = false;
                    StatusText = "Ready";
                }
            }
        }

        private async Task RebootFastboot(string? mode)
        {
            if (_fastbootService == null || SelectedFastbootDevice == null) return;

            await _fastbootService.RebootAsync(mode ?? "", SelectedFastbootDevice.Serial);
        }

        private async Task UnlockBootloader()
        {
            if (_fastbootService == null || SelectedFastbootDevice == null) return;

            IsBusy = true;
            StatusText = "Unlocking bootloader...";

            try
            {
                await _fastbootService.UnlockBootloaderAsync(SelectedFastbootDevice.Serial);
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        // Build.prop
        private async Task GetPropFiles()
        {
            if (_buildPropService == null) return;

            IsBusy = true;
            StatusText = "Getting prop files...";

            try
            {
                var files = await _buildPropService.GetAvailablePropFilesAsync();

                PropFiles.Clear();
                foreach (var file in files)
                {
                    PropFiles.Add(file);
                }
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        private async Task LoadPropFile()
        {
            if (_buildPropService == null || SelectedPropFile == null) return;

            IsBusy = true;
            StatusText = $"Loading {SelectedPropFile.Name}...";

            try
            {
                var propFile = await _buildPropService.LoadPropFileAsync(SelectedPropFile.Path);
                SelectedPropFile = propFile;

                PropEntries.Clear();
                foreach (var entry in propFile.Entries)
                {
                    PropEntries.Add(entry);
                }

                LogMessage?.Invoke(this, $"Loaded {propFile.Entries.Count} entries");
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        private async Task SavePropFile()
        {
            if (_buildPropService == null || SelectedPropFile == null) return;

            IsBusy = true;
            StatusText = $"Saving {SelectedPropFile.Name}...";

            try
            {
                await _buildPropService.SavePropFileAsync(SelectedPropFile);
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        // Benchmark
        private async Task RunBenchmark()
        {
            if (_diagnosticsService == null) return;

            IsBusy = true;
            StatusText = "Running benchmark...";
            Progress = 0;

            try
            {
                var progress = new Progress<int>(p => Progress = p);
                BenchmarkResult = await _diagnosticsService.RunBenchmarkAsync(progress);
                LogMessage?.Invoke(this, $"Benchmark complete. Score: {BenchmarkResult.OverallScore}");
            }
            finally
            {
                IsBusy = false;
                StatusText = "Ready";
            }
        }

        // Screen Mirror
        private async Task StartMirror()
        {
            if (_screenMirrorService == null) return;

            if (!_screenMirrorService.IsScrcpyInstalled())
            {
                IsBusy = true;
                StatusText = "Downloading scrcpy...";

                var progress = new Progress<int>(p => Progress = p);
                await _screenMirrorService.DownloadScrcpyAsync(progress);

                IsBusy = false;
                StatusText = "Ready";
            }

            await _screenMirrorService.StartMirrorAsync();
        }

        private void StopMirror()
        {
            _screenMirrorService?.StopMirror();
        }

        private async Task StartRecording()
        {
            if (_screenMirrorService == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "MP4 Video|*.mp4|MKV Video|*.mkv",
                Title = "Save recording as"
            };

            if (dialog.ShowDialog() == true)
            {
                await _screenMirrorService.StartRecordingAsync(dialog.FileName);
                IsRecording = true;
            }
        }

        private void StopRecording()
        {
            _screenMirrorService?.StopRecording();
            IsRecording = false;
        }

        private async Task TakeScreenshot()
        {
            if (_screenMirrorService == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image|*.png",
                FileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                Title = "Save screenshot"
            };

            if (dialog.ShowDialog() == true)
            {
                await _screenMirrorService.TakeScreenshotAsync(dialog.FileName);
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
