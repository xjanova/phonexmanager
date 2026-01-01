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
    public class SetupItem : INotifyPropertyChanged
    {
        private string _name = "";
        private string _description = "";
        private string _status = "Not Checked";
        private bool _isInstalled;
        private bool _isChecking;
        private bool _isInstalling;
        private int _progress;

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

        public string StatusIcon => IsInstalled ? "✓" : "✗";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class SetupViewModel : INotifyPropertyChanged
    {
        private readonly ToolsDownloadService _toolsService;
        private readonly DriverInstallerService _driverService;

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
                foreach (var item in ToolItems)
                    if (!item.IsInstalled) return false;
                return ToolItems.Count > 0;
            }
        }
        #endregion

        #region Commands
        public ICommand CheckAllCommand { get; }
        public ICommand InstallAllToolsCommand { get; }
        public ICommand InstallAllDriversCommand { get; }
        public ICommand InstallPlatformToolsCommand { get; }
        public ICommand InstallGoogleDriverCommand { get; }
        public ICommand OpenToolsFolderCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ClearLogsCommand { get; }
        #endregion

        public SetupViewModel()
        {
            _toolsService = new ToolsDownloadService();
            _driverService = new DriverInstallerService();

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
            InstallPlatformToolsCommand = new RelayCommand(async () => await InstallPlatformToolsAsync());
            InstallGoogleDriverCommand = new RelayCommand(async () => await InstallGoogleDriverAsync());
            OpenToolsFolderCommand = new RelayCommand(OpenToolsFolder);
            CancelCommand = new RelayCommand(Cancel);
            ClearLogsCommand = new RelayCommand(() => Logs.Clear());

            // Auto-check on startup
            _ = CheckAllAsync();
        }

        private void InitializeItems()
        {
            // Tools
            ToolItems.Add(new SetupItem
            {
                Name = "Android Platform Tools",
                Description = "ADB and Fastboot command-line tools (Required)"
            });

            ToolItems.Add(new SetupItem
            {
                Name = "Heimdall",
                Description = "Open-source Samsung flashing tool"
            });

            ToolItems.Add(new SetupItem
            {
                Name = "EDL Client",
                Description = "Qualcomm Emergency Download mode tool (Requires Python)"
            });

            ToolItems.Add(new SetupItem
            {
                Name = "MTK Client",
                Description = "MediaTek BROM/Preloader tool (Requires Python)"
            });

            // Drivers
            DriverItems.Add(new SetupItem
            {
                Name = "Google USB Driver",
                Description = "Universal Android ADB/Fastboot driver"
            });

            DriverItems.Add(new SetupItem
            {
                Name = "Qualcomm HS-USB QDLoader",
                Description = "Qualcomm EDL 9008 mode driver"
            });

            DriverItems.Add(new SetupItem
            {
                Name = "MediaTek Preloader",
                Description = "MTK download mode driver"
            });

            DriverItems.Add(new SetupItem
            {
                Name = "Samsung USB Driver",
                Description = "Samsung Odin/Download mode driver"
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

                // Check tools
                AddLog("Checking tools...");
                await CheckToolsAsync(_cts.Token);

                OverallProgress = 50;

                // Check drivers
                AddLog("Checking drivers...");
                await CheckDriversAsync(_cts.Token);

                OverallProgress = 100;

                // Summary
                int toolsInstalled = 0;
                foreach (var item in ToolItems)
                    if (item.IsInstalled) toolsInstalled++;

                int driversInstalled = 0;
                foreach (var item in DriverItems)
                    if (item.IsInstalled) driversInstalled++;

                StatusMessage = $"Tools: {toolsInstalled}/{ToolItems.Count} installed, Drivers: {driversInstalled}/{DriverItems.Count} installed";
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

            // Map to UI items
            foreach (var item in ToolItems)
            {
                item.IsChecking = true;
            }

            // ADB/Fastboot
            if (ToolItems.Count > 0)
            {
                var adbInstalled = _toolsService.IsAdbInstalled();
                ToolItems[0].IsInstalled = adbInstalled;
                ToolItems[0].Status = adbInstalled ? "Installed" : "Not Found";
                ToolItems[0].IsChecking = false;
            }

            // Heimdall
            if (ToolItems.Count > 1)
            {
                var tool = tools.Find(t => t.Type == ToolType.Heimdall);
                ToolItems[1].IsInstalled = tool?.IsInstalled ?? false;
                ToolItems[1].Status = tool?.IsInstalled == true ? "Installed" : "Not Found";
                ToolItems[1].IsChecking = false;
            }

            // EDL Client
            if (ToolItems.Count > 2)
            {
                var tool = tools.Find(t => t.Type == ToolType.EdlClient);
                ToolItems[2].IsInstalled = tool?.IsInstalled ?? false;
                ToolItems[2].Status = tool?.IsInstalled == true ? "Installed" : "Not Found";
                ToolItems[2].IsChecking = false;
            }

            // MTK Client
            if (ToolItems.Count > 3)
            {
                var tool = tools.Find(t => t.Type == ToolType.MTKClient);
                ToolItems[3].IsInstalled = tool?.IsInstalled ?? false;
                ToolItems[3].Status = tool?.IsInstalled == true ? "Installed" : "Not Found";
                ToolItems[3].IsChecking = false;
            }
        }

        private async Task CheckDriversAsync(CancellationToken ct)
        {
            var drivers = await _driverService.GetInstalledDriversAsync(ct);

            // Map to UI items
            foreach (var item in DriverItems)
            {
                item.IsChecking = true;
            }

            // Google USB
            if (DriverItems.Count > 0)
            {
                var driver = drivers.Find(d => d.Type == DriverType.GoogleUSB);
                DriverItems[0].IsInstalled = driver?.IsInstalled ?? false;
                DriverItems[0].Status = driver?.IsInstalled == true ? "Installed" : "Not Found";
                DriverItems[0].IsChecking = false;
            }

            // Qualcomm
            if (DriverItems.Count > 1)
            {
                var driver = drivers.Find(d => d.Type == DriverType.QualcommQDLoader);
                DriverItems[1].IsInstalled = driver?.IsInstalled ?? false;
                DriverItems[1].Status = driver?.IsInstalled == true ? "Installed" : "Not Found";
                DriverItems[1].IsChecking = false;
            }

            // MediaTek
            if (DriverItems.Count > 2)
            {
                var driver = drivers.Find(d => d.Type == DriverType.MediaTekPreloader);
                DriverItems[2].IsInstalled = driver?.IsInstalled ?? false;
                DriverItems[2].Status = driver?.IsInstalled == true ? "Installed" : "Not Found";
                DriverItems[2].IsChecking = false;
            }

            // Samsung
            if (DriverItems.Count > 3)
            {
                var driver = drivers.Find(d => d.Type == DriverType.SamsungUSB);
                DriverItems[3].IsInstalled = driver?.IsInstalled ?? false;
                DriverItems[3].Status = driver?.IsInstalled == true ? "Installed" : "Not Found";
                DriverItems[3].IsChecking = false;
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
                _cts = new CancellationTokenSource();

                if (ToolItems.Count > 0)
                {
                    ToolItems[0].IsInstalling = true;
                    ToolItems[0].Status = "Installing...";
                }

                var result = await _toolsService.DownloadAndInstallPlatformToolsAsync(_cts.Token);

                if (ToolItems.Count > 0)
                {
                    ToolItems[0].IsInstalling = false;
                    ToolItems[0].IsInstalled = result;
                    ToolItems[0].Status = result ? "Installed" : "Failed";
                }

                // Start ADB server
                if (result)
                {
                    await _toolsService.StartAdbServerAsync(_cts.Token);
                }

                OnPropertyChanged(nameof(AllToolsInstalled));
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
                _cts = new CancellationTokenSource();

                if (DriverItems.Count > 0)
                {
                    DriverItems[0].IsInstalling = true;
                    DriverItems[0].Status = "Installing...";
                }

                var result = await _driverService.InstallGoogleUsbDriverAsync(_cts.Token);

                if (DriverItems.Count > 0)
                {
                    DriverItems[0].IsInstalling = false;
                    DriverItems[0].IsInstalled = result;
                    DriverItems[0].Status = result ? "Installed" : "Failed";
                }
            }
            finally
            {
                IsInstalling = false;
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
                Logs.Insert(0, $"[{timestamp}] {message}");
                if (Logs.Count > 500) Logs.RemoveAt(Logs.Count - 1);
            });
        }
        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
