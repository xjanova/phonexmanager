using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PhoneRomFlashTool.Services;

namespace PhoneRomFlashTool.ViewModels
{
    public class EngineerToolsViewModel : INotifyPropertyChanged
    {
        private readonly AdbService _adbService;
        private readonly FrpBypassService _frpService;
        private readonly ScreenLockBypassService _lockBypassService;
        private readonly DebloatService _debloatService;
        private readonly NetworkUnlockService _networkUnlockService;
        private readonly RecoveryManagerService _recoveryService;
        private readonly XiaomiAuthService _xiaomiService;
        private readonly ImeiToolService _imeiService;

        private CancellationTokenSource? _cts;

        public event PropertyChangedEventHandler? PropertyChanged;

        #region Properties
        private string? _selectedDeviceId;
        public string? SelectedDeviceId
        {
            get => _selectedDeviceId;
            set { _selectedDeviceId = value; OnPropertyChanged(); }
        }

        private ObservableCollection<string> _logs = new();
        public ObservableCollection<string> Logs
        {
            get => _logs;
            set { _logs = value; OnPropertyChanged(); }
        }

        private int _progress;
        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); }
        }

        // FRP
        private string _frpStatus = "Unknown";
        public string FrpStatus
        {
            get => _frpStatus;
            set { _frpStatus = value; OnPropertyChanged(); }
        }

        private bool _hasFrp;
        public bool HasFrp
        {
            get => _hasFrp;
            set { _hasFrp = value; OnPropertyChanged(); }
        }

        // Screen Lock
        private string _lockType = "Unknown";
        public string LockType
        {
            get => _lockType;
            set { _lockType = value; OnPropertyChanged(); }
        }

        private bool _isEncrypted;
        public bool IsEncrypted
        {
            get => _isEncrypted;
            set { _isEncrypted = value; OnPropertyChanged(); }
        }

        // Network Lock
        private string _networkLockStatus = "Unknown";
        public string NetworkLockStatus
        {
            get => _networkLockStatus;
            set { _networkLockStatus = value; OnPropertyChanged(); }
        }

        private string _carrier = "";
        public string Carrier
        {
            get => _carrier;
            set { _carrier = value; OnPropertyChanged(); }
        }

        private string _unlockCode = "";
        public string UnlockCode
        {
            get => _unlockCode;
            set { _unlockCode = value; OnPropertyChanged(); }
        }

        // Debloat
        private ObservableCollection<BloatwareAppItem> _bloatwareApps = new();
        public ObservableCollection<BloatwareAppItem> BloatwareApps
        {
            get => _bloatwareApps;
            set { _bloatwareApps = value; OnPropertyChanged(); }
        }

        private string _manufacturer = "";
        public string Manufacturer
        {
            get => _manufacturer;
            set { _manufacturer = value; OnPropertyChanged(); }
        }

        // Recovery
        private string _currentRecovery = "Unknown";
        public string CurrentRecovery
        {
            get => _currentRecovery;
            set { _currentRecovery = value; OnPropertyChanged(); }
        }

        private string _recoveryPath = "";
        public string RecoveryPath
        {
            get => _recoveryPath;
            set { _recoveryPath = value; OnPropertyChanged(); }
        }

        // Xiaomi
        private string _miAccountStatus = "Unknown";
        public string MiAccountStatus
        {
            get => _miAccountStatus;
            set { _miAccountStatus = value; OnPropertyChanged(); }
        }

        private string _bootloaderStatus = "Unknown";
        public string BootloaderStatus
        {
            get => _bootloaderStatus;
            set { _bootloaderStatus = value; OnPropertyChanged(); }
        }

        private bool _isXiaomiDevice;
        public bool IsXiaomiDevice
        {
            get => _isXiaomiDevice;
            set { _isXiaomiDevice = value; OnPropertyChanged(); }
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

        private string _serialNumber = "";
        public string SerialNumber
        {
            get => _serialNumber;
            set { _serialNumber = value; OnPropertyChanged(); }
        }

        private bool _imei1Valid;
        public bool Imei1Valid
        {
            get => _imei1Valid;
            set { _imei1Valid = value; OnPropertyChanged(); }
        }
        #endregion

        #region Commands
        // FRP Commands
        public ICommand CheckFrpCommand { get; }
        public ICommand BypassFrpAdbCommand { get; }
        public ICommand BypassFrpRecoveryCommand { get; }

        // Lock Commands
        public ICommand DetectLockCommand { get; }
        public ICommand BypassLockAdbCommand { get; }
        public ICommand BypassLockRecoveryCommand { get; }
        public ICommand BypassLockRootCommand { get; }

        // Network Commands
        public ICommand CheckNetworkLockCommand { get; }
        public ICommand UnlockNetworkCommand { get; }
        public ICommand ResetApnCommand { get; }

        // Debloat Commands
        public ICommand ScanBloatwareCommand { get; }
        public ICommand DisableSelectedCommand { get; }
        public ICommand UninstallSelectedCommand { get; }
        public ICommand EnableAllCommand { get; }

        // Recovery Commands
        public ICommand DetectRecoveryCommand { get; }
        public ICommand FlashRecoveryCommand { get; }
        public ICommand BootRecoveryCommand { get; }
        public ICommand BrowseRecoveryCommand { get; }
        public ICommand RebootRecoveryCommand { get; }

        // Xiaomi Commands
        public ICommand CheckMiAccountCommand { get; }
        public ICommand CheckXiaomiBootloaderCommand { get; }
        public ICommand RemoveMiAccountCommand { get; }
        public ICommand DisableFindDeviceCommand { get; }

        // IMEI Commands
        public ICommand ReadImeiCommand { get; }
        public ICommand BackupEfsCommand { get; }
        public ICommand RestoreEfsCommand { get; }

        // General
        public ICommand CancelCommand { get; }
        public ICommand ClearLogsCommand { get; }
        #endregion

        public EngineerToolsViewModel(AdbService adbService)
        {
            _adbService = adbService;

            // Get adb path for services that need it
            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
            var adbPath = Path.Combine(toolsPath, "adb.exe");
            if (!File.Exists(adbPath)) adbPath = "adb"; // Fallback to PATH

            _frpService = new FrpBypassService(adbPath);
            _lockBypassService = new ScreenLockBypassService(adbService);
            _debloatService = new DebloatService(adbService);
            _networkUnlockService = new NetworkUnlockService(adbService);
            _recoveryService = new RecoveryManagerService(adbService);
            _xiaomiService = new XiaomiAuthService(adbService);
            _imeiService = new ImeiToolService(adbService);

            // Wire up log events
            _frpService.LogMessage += (s, msg) => AddLog(msg);
            _frpService.ProgressChanged += (s, p) => Progress = p;
            _lockBypassService.LogMessage += (s, msg) => AddLog(msg);
            _lockBypassService.ProgressChanged += (s, p) => Progress = p;
            _debloatService.LogMessage += (s, msg) => AddLog(msg);
            _debloatService.ProgressChanged += (s, p) => Progress = p;
            _networkUnlockService.LogMessage += (s, msg) => AddLog(msg);
            _networkUnlockService.ProgressChanged += (s, p) => Progress = p;
            _recoveryService.LogMessage += (s, msg) => AddLog(msg);
            _recoveryService.ProgressChanged += (s, p) => Progress = p;
            _xiaomiService.LogMessage += (s, msg) => AddLog(msg);
            _xiaomiService.ProgressChanged += (s, p) => Progress = p;
            _imeiService.LogMessage += (s, msg) => AddLog(msg);
            _imeiService.ProgressChanged += (s, p) => Progress = p;

            // FRP Commands
            CheckFrpCommand = new RelayCommand(async () => await CheckFrpAsync());
            BypassFrpAdbCommand = new RelayCommand(async () => await BypassFrpAdbAsync());
            BypassFrpRecoveryCommand = new RelayCommand(async () => await BypassFrpRecoveryAsync());

            // Lock Commands
            DetectLockCommand = new RelayCommand(async () => await DetectLockAsync());
            BypassLockAdbCommand = new RelayCommand(async () => await BypassLockAdbAsync());
            BypassLockRecoveryCommand = new RelayCommand(async () => await BypassLockRecoveryAsync());
            BypassLockRootCommand = new RelayCommand(async () => await BypassLockRootAsync());

            // Network Commands
            CheckNetworkLockCommand = new RelayCommand(async () => await CheckNetworkLockAsync());
            UnlockNetworkCommand = new RelayCommand(async () => await UnlockNetworkAsync());
            ResetApnCommand = new RelayCommand(async () => await ResetApnAsync());

            // Debloat Commands
            ScanBloatwareCommand = new RelayCommand(async () => await ScanBloatwareAsync());
            DisableSelectedCommand = new RelayCommand(async () => await DisableSelectedAsync());
            UninstallSelectedCommand = new RelayCommand(async () => await UninstallSelectedAsync());
            EnableAllCommand = new RelayCommand(async () => await EnableAllAsync());

            // Recovery Commands
            DetectRecoveryCommand = new RelayCommand(async () => await DetectRecoveryAsync());
            FlashRecoveryCommand = new RelayCommand(async () => await FlashRecoveryAsync());
            BootRecoveryCommand = new RelayCommand(async () => await BootRecoveryAsync());
            BrowseRecoveryCommand = new RelayCommand(() => BrowseRecovery());
            RebootRecoveryCommand = new RelayCommand(async () => await RebootRecoveryAsync());

            // Xiaomi Commands
            CheckMiAccountCommand = new RelayCommand(async () => await CheckMiAccountAsync());
            CheckXiaomiBootloaderCommand = new RelayCommand(async () => await CheckXiaomiBootloaderAsync());
            RemoveMiAccountCommand = new RelayCommand(async () => await RemoveMiAccountAsync());
            DisableFindDeviceCommand = new RelayCommand(async () => await DisableFindDeviceAsync());

            // IMEI Commands
            ReadImeiCommand = new RelayCommand(async () => await ReadImeiAsync());
            BackupEfsCommand = new RelayCommand(async () => await BackupEfsAsync());
            RestoreEfsCommand = new RelayCommand(async () => await RestoreEfsAsync());

            // General
            CancelCommand = new RelayCommand(() => Cancel());
            ClearLogsCommand = new RelayCommand(() => Logs.Clear());
        }

        public void SetDevice(string? deviceId)
        {
            SelectedDeviceId = deviceId;
            if (!string.IsNullOrEmpty(deviceId))
            {
                AddLog($"Device selected: {deviceId}");
                _ = DetectDeviceTypeAsync();
            }
        }

        private async Task DetectDeviceTypeAsync()
        {
            if (string.IsNullOrEmpty(SelectedDeviceId)) return;

            try
            {
                var brand = await _adbService.ExecuteAdbCommandAsync(SelectedDeviceId, "shell getprop ro.product.brand");
                Manufacturer = brand?.Trim() ?? "";

                IsXiaomiDevice = Manufacturer.ToLowerInvariant() switch
                {
                    "xiaomi" or "redmi" or "poco" => true,
                    _ => false
                };

                AddLog($"Detected: {Manufacturer} device");
            }
            catch { }
        }

        #region FRP Methods
        private async Task CheckFrpAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var status = await _frpService.CheckFrpStatusAsync(_cts.Token);
                HasFrp = status.IsLocked;
                FrpStatus = status.IsLocked ? $"FRP Active (Account: {status.GoogleAccount})" : "No FRP Lock";
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task BypassFrpAdbAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                AddLog("Starting FRP bypass via ADB...");
                var result = await _frpService.BypassViaAdbAsync(_cts.Token);
                AddLog(result ? "FRP bypass completed" : "FRP bypass failed");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task BypassFrpRecoveryAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                AddLog("Starting FRP bypass via Recovery...");
                var result = await _frpService.EnableAdbFromRecoveryAsync(_cts.Token);
                AddLog(result ? "FRP bypass completed" : "FRP bypass failed");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }
        #endregion

        #region Lock Bypass Methods
        private async Task DetectLockAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var info = await _lockBypassService.DetectLockTypeAsync(SelectedDeviceId!, _cts.Token);
                LockType = info.PasswordType.ToString();
                IsEncrypted = info.IsEncrypted;

                AddLog($"Lock Type: {LockType}, Encrypted: {IsEncrypted}");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task BypassLockAdbAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                AddLog("Attempting lock bypass via ADB...");
                var result = await _lockBypassService.BypassViaAdbAsync(SelectedDeviceId!, _cts.Token);
                AddLog(result ? "Lock bypass completed - device rebooting" : "Lock bypass failed");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task BypassLockRecoveryAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                AddLog("Attempting lock bypass via Recovery...");
                var result = await _lockBypassService.BypassViaRecoveryAsync(SelectedDeviceId!, _cts.Token);
                AddLog(result ? "Lock bypass completed" : "Lock bypass failed");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task BypassLockRootAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                AddLog("Attempting lock bypass via Root...");
                var result = await _lockBypassService.BypassViaRootAsync(SelectedDeviceId!, _cts.Token);
                AddLog(result ? "Lock bypass completed" : "Lock bypass failed - root may be required");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }
        #endregion

        #region Network Unlock Methods
        private async Task CheckNetworkLockAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var status = await _networkUnlockService.CheckNetworkLockAsync(SelectedDeviceId!, _cts.Token);
                NetworkLockStatus = status.IsLocked ? "Locked" : "Unlocked";
                Carrier = status.CurrentOperator;

                AddLog($"Network: {NetworkLockStatus}, Carrier: {Carrier}");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task UnlockNetworkAsync()
        {
            if (!ValidateDevice()) return;
            if (string.IsNullOrEmpty(UnlockCode))
            {
                AddLog("Please enter unlock code");
                return;
            }

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                AddLog("Attempting network unlock...");
                var result = await _networkUnlockService.UnlockWithNckAsync(SelectedDeviceId!, UnlockCode, _cts.Token);
                AddLog(result ? "Unlock code applied - check device" : "Network unlock failed");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task ResetApnAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var result = await _networkUnlockService.ResetApnSettingsAsync(SelectedDeviceId!, _cts.Token);
                AddLog(result ? "APN settings reset" : "Failed to reset APN");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }
        #endregion

        #region Debloat Methods
        private async Task ScanBloatwareAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                BloatwareApps.Clear();
                var apps = await _debloatService.ScanForBloatwareAsync(SelectedDeviceId!, _cts.Token);

                foreach (var app in apps)
                {
                    BloatwareApps.Add(new BloatwareAppItem
                    {
                        PackageName = app.PackageName,
                        Name = app.Name,
                        RiskLevel = app.RiskLevel,
                        IsSelected = false
                    });
                }

                AddLog($"Found {apps.Count} bloatware apps");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task DisableSelectedAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var selected = new System.Collections.Generic.List<string>();
                foreach (var app in BloatwareApps)
                {
                    if (app.IsSelected)
                        selected.Add(app.PackageName);
                }

                if (selected.Count == 0)
                {
                    AddLog("No apps selected");
                    return;
                }

                var result = await _debloatService.DebloatBatchAsync(SelectedDeviceId!, selected, DebloatAction.Disable, _cts.Token);
                AddLog($"Disabled: {result.Succeeded.Count}, Failed: {result.Failed.Count}");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task UninstallSelectedAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var selected = new System.Collections.Generic.List<string>();
                foreach (var app in BloatwareApps)
                {
                    if (app.IsSelected)
                        selected.Add(app.PackageName);
                }

                if (selected.Count == 0)
                {
                    AddLog("No apps selected");
                    return;
                }

                var result = await _debloatService.DebloatBatchAsync(SelectedDeviceId!, selected, DebloatAction.Uninstall, _cts.Token);
                AddLog($"Uninstalled: {result.Succeeded.Count}, Failed: {result.Failed.Count}");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task EnableAllAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var packages = new System.Collections.Generic.List<string>();
                foreach (var app in BloatwareApps)
                {
                    packages.Add(app.PackageName);
                }

                var result = await _debloatService.DebloatBatchAsync(SelectedDeviceId!, packages, DebloatAction.Enable, _cts.Token);
                AddLog($"Enabled: {result.Succeeded.Count}, Failed: {result.Failed.Count}");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }
        #endregion

        #region Recovery Methods
        private async Task DetectRecoveryAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var info = await _recoveryService.DetectRecoveryAsync(SelectedDeviceId!, _cts.Token);
                CurrentRecovery = $"{info.RecoveryType} {info.Version}".Trim();

                AddLog($"Recovery: {CurrentRecovery}");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task FlashRecoveryAsync()
        {
            if (!ValidateDevice()) return;
            if (string.IsNullOrEmpty(RecoveryPath))
            {
                AddLog("Please select a recovery image");
                return;
            }

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var result = await _recoveryService.FlashRecoveryAsync(SelectedDeviceId!, RecoveryPath, _cts.Token);
                AddLog(result ? "Recovery flashed successfully" : "Failed to flash recovery");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task BootRecoveryAsync()
        {
            if (!ValidateDevice()) return;
            if (string.IsNullOrEmpty(RecoveryPath))
            {
                AddLog("Please select a recovery image");
                return;
            }

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var result = await _recoveryService.BootRecoveryAsync(SelectedDeviceId!, RecoveryPath, _cts.Token);
                AddLog(result ? "Recovery booted" : "Failed to boot recovery");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private void BrowseRecovery()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Recovery Images|*.img|All Files|*.*",
                Title = "Select Recovery Image"
            };

            if (dialog.ShowDialog() == true)
            {
                RecoveryPath = dialog.FileName;
                AddLog($"Selected: {System.IO.Path.GetFileName(RecoveryPath)}");
            }
        }

        private async Task RebootRecoveryAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                await _recoveryService.RebootToRecoveryAsync(SelectedDeviceId!);
                AddLog("Rebooting to recovery...");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
        }
        #endregion

        #region Xiaomi Methods
        private async Task CheckMiAccountAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var status = await _xiaomiService.CheckMiAccountStatusAsync(SelectedDeviceId!, _cts.Token);
                MiAccountStatus = status.HasMiAccount ? "Mi Account Logged In" : "No Mi Account";

                if (status.FindDeviceEnabled)
                    MiAccountStatus += " (Find Device ON)";

                AddLog($"Mi Account: {MiAccountStatus}");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task CheckXiaomiBootloaderAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var status = await _xiaomiService.CheckBootloaderStatusAsync(SelectedDeviceId!, _cts.Token);
                BootloaderStatus = status.IsUnlocked ? "Unlocked" : "Locked";

                if (status.OemUnlockAllowed)
                    BootloaderStatus += " (OEM Unlock Allowed)";

                AddLog($"Bootloader: {BootloaderStatus}");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task RemoveMiAccountAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                AddLog("Attempting to remove Mi Account...");
                var result = await _xiaomiService.TryRemoveMiAccountAdbAsync(SelectedDeviceId!, _cts.Token);
                AddLog(result ? "Mi Account removal attempted - device rebooting" : "Failed to remove Mi Account");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task DisableFindDeviceAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var result = await _xiaomiService.DisableFindDeviceAsync(SelectedDeviceId!, _cts.Token);
                AddLog(result ? "Find Device disabled" : "Failed to disable Find Device");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }
        #endregion

        #region IMEI Methods
        private async Task ReadImeiAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var ids = await _imeiService.ReadIdentifiersAsync(SelectedDeviceId!, _cts.Token);
                Imei1 = ids.Imei1;
                Imei2 = ids.Imei2;
                SerialNumber = ids.SerialNumber;
                Imei1Valid = ids.Imei1Valid;

                AddLog($"IMEI1: {Imei1} ({(Imei1Valid ? "Valid" : "Invalid")})");
                if (!string.IsNullOrEmpty(Imei2))
                    AddLog($"IMEI2: {Imei2}");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task BackupEfsAsync()
        {
            if (!ValidateDevice()) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                AddLog("Backing up EFS (requires root)...");
                var result = await _imeiService.BackupEfsAsync(SelectedDeviceId!, _cts.Token);
                AddLog(result ? "EFS backup completed" : "EFS backup failed - root required");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task RestoreEfsAsync()
        {
            if (!ValidateDevice()) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "EFS Backup|efs.img|All Files|*.*",
                Title = "Select EFS Backup Folder"
            };

            // Use FolderBrowserDialog would be better, but for simplicity use file dialog
            if (dialog.ShowDialog() != true) return;

            try
            {
                IsRunning = true;
                _cts = new CancellationTokenSource();

                var backupDir = System.IO.Path.GetDirectoryName(dialog.FileName) ?? "";
                AddLog("Restoring EFS (requires root)...");
                var result = await _imeiService.RestoreEfsAsync(SelectedDeviceId!, backupDir, _cts.Token);
                AddLog(result ? "EFS restore completed - reboot required" : "EFS restore failed");
            }
            catch (Exception ex)
            {
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }
        #endregion

        #region Utility Methods
        private bool ValidateDevice()
        {
            if (string.IsNullOrEmpty(SelectedDeviceId))
            {
                AddLog("No device selected");
                return false;
            }
            return true;
        }

        private void AddLog(string message)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            });
        }

        private void Cancel()
        {
            _cts?.Cancel();
            AddLog("Operation cancelled");
            IsRunning = false;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }

    public class BloatwareAppItem : INotifyPropertyChanged
    {
        public string PackageName { get; set; } = "";
        public string Name { get; set; } = "";
        public string RiskLevel { get; set; } = "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
