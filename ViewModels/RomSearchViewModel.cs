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
    public class RomSearchViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<string>? LogMessage;

        private readonly RomSearchService _searchService;
        private CancellationTokenSource? _searchCts;

        // Search Query
        private string _searchQuery = "";
        public string SearchQuery
        {
            get => _searchQuery;
            set { _searchQuery = value; OnPropertyChanged(); }
        }

        // Search Results
        public ObservableCollection<RomSearchResult> SearchResults { get; } = new();

        // Selected ROM
        private RomSearchResult? _selectedRom;
        public RomSearchResult? SelectedRom
        {
            get => _selectedRom;
            set { _selectedRom = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedRom)); }
        }
        public bool HasSelectedRom => SelectedRom != null;

        // Downloaded ROMs
        public ObservableCollection<DownloadedRomInfo> DownloadedRoms { get; } = new();

        // Status
        private string _searchStatus = "Enter device model to search for ROMs";
        public string SearchStatus
        {
            get => _searchStatus;
            set { _searchStatus = value; OnPropertyChanged(); }
        }

        private int _searchProgress;
        public int SearchProgress
        {
            get => _searchProgress;
            set { _searchProgress = value; OnPropertyChanged(); }
        }

        private bool _isSearching;
        public bool IsSearching
        {
            get => _isSearching;
            set { _isSearching = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSearch)); }
        }

        private bool _isDownloading;
        public bool IsDownloading
        {
            get => _isDownloading;
            set { _isDownloading = value; OnPropertyChanged(); }
        }

        private int _downloadProgress;
        public int DownloadProgress
        {
            get => _downloadProgress;
            set { _downloadProgress = value; OnPropertyChanged(); }
        }

        public bool CanSearch => !IsSearching && !string.IsNullOrWhiteSpace(SearchQuery);

        // Filter options
        private bool _showStockRoms = true;
        public bool ShowStockRoms
        {
            get => _showStockRoms;
            set { _showStockRoms = value; OnPropertyChanged(); FilterResults(); }
        }

        private bool _showCustomRoms = true;
        public bool ShowCustomRoms
        {
            get => _showCustomRoms;
            set { _showCustomRoms = value; OnPropertyChanged(); FilterResults(); }
        }

        private bool _showRecovery = true;
        public bool ShowRecovery
        {
            get => _showRecovery;
            set { _showRecovery = value; OnPropertyChanged(); FilterResults(); }
        }

        // All results (unfiltered)
        private ObservableCollection<RomSearchResult> _allResults = new();

        // Commands
        public ICommand SearchCommand { get; }
        public ICommand CancelSearchCommand { get; }
        public ICommand DownloadRomCommand { get; }
        public ICommand OpenDownloadUrlCommand { get; }
        public ICommand CopyUrlCommand { get; }
        public ICommand OpenDownloadsFolderCommand { get; }
        public ICommand RefreshDownloadsCommand { get; }
        public ICommand DeleteDownloadedRomCommand { get; }
        public ICommand QuickSearchCommand { get; }

        public RomSearchViewModel()
        {
            _searchService = new RomSearchService();
            _searchService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);
            _searchService.StatusChanged += (s, status) => SearchStatus = status;
            _searchService.ProgressChanged += (s, progress) => SearchProgress = progress;
            _searchService.RomFound += (s, rom) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    if (ShouldShowRom(rom))
                    {
                        SearchResults.Add(rom);
                    }
                    _allResults.Add(rom);
                });
            };

            // Initialize commands
            SearchCommand = new RelayCommand(async () => await SearchAsync());
            CancelSearchCommand = new RelayCommand(CancelSearch);
            DownloadRomCommand = new RelayCommandWithParam<RomSearchResult>(async (rom) => await DownloadRomAsync(rom));
            OpenDownloadUrlCommand = new RelayCommandWithParam<RomSearchResult>(OpenDownloadUrl);
            CopyUrlCommand = new RelayCommandWithParam<RomSearchResult>(CopyUrl);
            OpenDownloadsFolderCommand = new RelayCommand(OpenDownloadsFolder);
            RefreshDownloadsCommand = new RelayCommand(RefreshDownloadedRoms);
            DeleteDownloadedRomCommand = new RelayCommandWithParam<DownloadedRomInfo>(DeleteDownloadedRom);
            QuickSearchCommand = new RelayCommandWithParam<string>(async (query) => await QuickSearchAsync(query));

            // Load downloaded ROMs
            RefreshDownloadedRoms();
        }

        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                SearchStatus = "Please enter a device model";
                return;
            }

            IsSearching = true;
            SearchProgress = 0;
            SearchResults.Clear();
            _allResults.Clear();

            try
            {
                _searchCts = new CancellationTokenSource();
                var results = await _searchService.SearchRomAsync(SearchQuery, _searchCts.Token);

                // Results are added via RomFound event during search
                if (SearchResults.Count == 0)
                {
                    SearchStatus = $"No ROMs found for '{SearchQuery}'. Try different keywords.";
                }
            }
            catch (OperationCanceledException)
            {
                SearchStatus = "Search cancelled";
            }
            catch (Exception ex)
            {
                SearchStatus = $"Search error: {ex.Message}";
                LogMessage?.Invoke(this, $"Search error: {ex.Message}");
            }
            finally
            {
                IsSearching = false;
                _searchCts?.Dispose();
                _searchCts = null;
            }
        }

        private async Task QuickSearchAsync(string? query)
        {
            if (string.IsNullOrEmpty(query)) return;
            SearchQuery = query;
            await SearchAsync();
        }

        private void CancelSearch()
        {
            _searchCts?.Cancel();
        }

        private async Task DownloadRomAsync(RomSearchResult? rom)
        {
            if (rom == null) return;

            IsDownloading = true;
            DownloadProgress = 0;

            try
            {
                var progress = new Progress<int>(p => DownloadProgress = p);
                var filePath = await _searchService.DownloadRomAsync(rom, progress);

                SearchStatus = $"Downloaded: {Path.GetFileName(filePath)}";
                RefreshDownloadedRoms();
            }
            catch (Exception ex)
            {
                SearchStatus = $"Download failed: {ex.Message}";
                LogMessage?.Invoke(this, $"Download error: {ex.Message}");
            }
            finally
            {
                IsDownloading = false;
            }
        }

        private void OpenDownloadUrl(RomSearchResult? rom)
        {
            if (rom == null || string.IsNullOrEmpty(rom.DownloadUrl)) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = rom.DownloadUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error opening URL: {ex.Message}");
            }
        }

        private void CopyUrl(RomSearchResult? rom)
        {
            if (rom == null || string.IsNullOrEmpty(rom.DownloadUrl)) return;

            try
            {
                System.Windows.Clipboard.SetText(rom.DownloadUrl);
                SearchStatus = "URL copied to clipboard";
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error copying URL: {ex.Message}");
            }
        }

        private void OpenDownloadsFolder()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var downloadPath = Path.Combine(appData, "PhoneRomFlashTool", "Downloads");

            Directory.CreateDirectory(downloadPath);

            try
            {
                System.Diagnostics.Process.Start("explorer.exe", downloadPath);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error opening folder: {ex.Message}");
            }
        }

        private void RefreshDownloadedRoms()
        {
            DownloadedRoms.Clear();

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var downloadPath = Path.Combine(appData, "PhoneRomFlashTool", "Downloads");

            if (!Directory.Exists(downloadPath)) return;

            foreach (var file in Directory.GetFiles(downloadPath))
            {
                if (file.EndsWith(".info.json")) continue;

                var fileInfo = new FileInfo(file);
                DownloadedRoms.Add(new DownloadedRomInfo
                {
                    FileName = fileInfo.Name,
                    FilePath = file,
                    FileSize = FormatFileSize(fileInfo.Length),
                    DownloadDate = fileInfo.CreationTime
                });
            }
        }

        private void DeleteDownloadedRom(DownloadedRomInfo? rom)
        {
            if (rom == null) return;

            try
            {
                if (File.Exists(rom.FilePath))
                {
                    File.Delete(rom.FilePath);

                    // Delete info file too
                    var infoFile = rom.FilePath + ".info.json";
                    if (File.Exists(infoFile))
                        File.Delete(infoFile);
                }

                DownloadedRoms.Remove(rom);
                SearchStatus = $"Deleted: {rom.FileName}";
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error deleting file: {ex.Message}");
            }
        }

        private void FilterResults()
        {
            SearchResults.Clear();
            foreach (var rom in _allResults)
            {
                if (ShouldShowRom(rom))
                {
                    SearchResults.Add(rom);
                }
            }
        }

        private bool ShouldShowRom(RomSearchResult rom)
        {
            return rom.RomType switch
            {
                "Stock" => ShowStockRoms,
                "Custom" => ShowCustomRoms,
                "Recovery" => ShowRecovery,
                "GApps" => ShowCustomRoms,
                _ => true
            };
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class DownloadedRomInfo
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string FileSize { get; set; } = "";
        public DateTime DownloadDate { get; set; }

        public string DateDisplay => DownloadDate.ToString("yyyy-MM-dd HH:mm");
    }
}
