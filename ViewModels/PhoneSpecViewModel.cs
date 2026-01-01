using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PhoneRomFlashTool.Services;

namespace PhoneRomFlashTool.ViewModels
{
    public class PhoneSpecViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<string>? LogMessage;

        private readonly SiamphoneService _siamphoneService;
        private CancellationTokenSource? _cancellationTokenSource;

        // Collections
        public ObservableCollection<PhoneSpecModel> Phones { get; } = new();
        public ObservableCollection<PhoneSpecModel> SearchResults { get; } = new();
        public ObservableCollection<string> AvailableBrands { get; } = new();

        // Selected Items
        private PhoneSpecModel? _selectedPhone;
        public PhoneSpecModel? SelectedPhone
        {
            get => _selectedPhone;
            set
            {
                _selectedPhone = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedPhone));
            }
        }

        private string _selectedBrand = "";
        public string SelectedBrand
        {
            get => _selectedBrand;
            set { _selectedBrand = value; OnPropertyChanged(); }
        }

        public bool HasSelectedPhone => SelectedPhone != null;

        // Search
        private string _searchQuery = "";
        public string SearchQuery
        {
            get => _searchQuery;
            set { _searchQuery = value; OnPropertyChanged(); }
        }

        // Progress
        private bool _isScraping;
        public bool IsScraping
        {
            get => _isScraping;
            set { _isScraping = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStartScrape)); }
        }

        private int _scrapeProgress;
        public int ScrapeProgress
        {
            get => _scrapeProgress;
            set { _scrapeProgress = value; OnPropertyChanged(); }
        }

        private string _scrapeStatus = "Ready";
        public string ScrapeStatus
        {
            get => _scrapeStatus;
            set { _scrapeStatus = value; OnPropertyChanged(); }
        }

        private string _currentModel = "";
        public string CurrentModel
        {
            get => _currentModel;
            set { _currentModel = value; OnPropertyChanged(); }
        }

        private int _downloadedCount;
        public int DownloadedCount
        {
            get => _downloadedCount;
            set { _downloadedCount = value; OnPropertyChanged(); }
        }

        private int _skippedCount;
        public int SkippedCount
        {
            get => _skippedCount;
            set { _skippedCount = value; OnPropertyChanged(); }
        }

        private int _totalInDb;
        public int TotalInDb
        {
            get => _totalInDb;
            set { _totalInDb = value; OnPropertyChanged(); }
        }

        public bool CanStartScrape => !IsScraping;

        // Commands
        public ICommand SearchCommand { get; }
        public ICommand LoadBrandCommand { get; }
        public ICommand ScrapeSelectedBrandCommand { get; }
        public ICommand ScrapeAllBrandsCommand { get; }
        public ICommand StopScrapeCommand { get; }
        public ICommand RefreshDatabaseCommand { get; }
        public ICommand OpenSiamphoneUrlCommand { get; }
        public ICommand ScrapeModelCommand { get; }

        public PhoneSpecViewModel()
        {
            _siamphoneService = new SiamphoneService();
            _siamphoneService.LogMessage += (s, msg) => LogMessage?.Invoke(this, msg);
            _siamphoneService.ProgressChanged += OnScrapeProgress;

            // Initialize commands
            SearchCommand = new RelayCommand(async () => await SearchAsync());
            LoadBrandCommand = new RelayCommandWithParam<string>(async (b) => await LoadBrandAsync(b));
            ScrapeSelectedBrandCommand = new RelayCommand(async () => await ScrapeSelectedBrandAsync());
            ScrapeAllBrandsCommand = new RelayCommand(async () => await ScrapeAllBrandsAsync());
            StopScrapeCommand = new RelayCommand(StopScrape);
            RefreshDatabaseCommand = new RelayCommand(RefreshDatabase);
            OpenSiamphoneUrlCommand = new RelayCommand(OpenSiamphoneUrl);
            ScrapeModelCommand = new RelayCommandWithParam<string>(async (m) => await ScrapeModelAsync(m));

            // Load brands
            foreach (var brand in SiamphoneService.Brands)
            {
                AvailableBrands.Add(brand.Value);
            }

            // Load initial stats
            RefreshDatabase();
        }

        private void OnScrapeProgress(object? sender, ScrapeProgressEventArgs e)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                ScrapeProgress = e.Percent;
                CurrentModel = e.CurrentModel;
                ScrapeStatus = $"{e.Current}/{e.Total} - {e.Status}";
                DownloadedCount = e.Downloaded;
                SkippedCount = e.Skipped;
            });
        }

        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                RefreshDatabase();
                return;
            }

            LogMessage?.Invoke(this, $"Searching for: {SearchQuery}");

            await Task.Run(() =>
            {
                var results = _siamphoneService.SearchPhones(SearchQuery);

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    SearchResults.Clear();
                    Phones.Clear();
                    foreach (var phone in results)
                    {
                        SearchResults.Add(phone);
                        Phones.Add(phone);
                    }
                });

                LogMessage?.Invoke(this, $"Found {results.Count} results");
            });
        }

        private async Task LoadBrandAsync(string? brand)
        {
            if (string.IsNullOrEmpty(brand)) return;

            LogMessage?.Invoke(this, $"Loading phones for: {brand}");

            await Task.Run(() =>
            {
                var phones = _siamphoneService.GetPhonesByBrand(brand);

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    Phones.Clear();
                    foreach (var phone in phones)
                    {
                        Phones.Add(phone);
                    }
                });

                LogMessage?.Invoke(this, $"Loaded {phones.Count} phones for {brand}");
            });
        }

        private async Task ScrapeSelectedBrandAsync()
        {
            if (string.IsNullOrEmpty(SelectedBrand)) return;

            // Find brand slug
            string? brandSlug = null;
            foreach (var brand in SiamphoneService.Brands)
            {
                if (brand.Value == SelectedBrand)
                {
                    brandSlug = brand.Key;
                    break;
                }
            }

            if (brandSlug == null) return;

            IsScraping = true;
            ScrapeProgress = 0;
            ScrapeStatus = $"Starting scrape for {SelectedBrand}...";
            DownloadedCount = 0;
            SkippedCount = 0;

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await _siamphoneService.ScrapeAllBrandModelsAsync(brandSlug, _cancellationTokenSource.Token);
                RefreshDatabase();
            }
            catch (OperationCanceledException)
            {
                LogMessage?.Invoke(this, "Scrape cancelled by user");
            }
            finally
            {
                IsScraping = false;
                ScrapeStatus = "Completed";
            }
        }

        private async Task ScrapeAllBrandsAsync()
        {
            IsScraping = true;
            ScrapeProgress = 0;
            ScrapeStatus = "Starting full scrape...";
            DownloadedCount = 0;
            SkippedCount = 0;

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await _siamphoneService.ScrapeAllBrandsAsync(_cancellationTokenSource.Token);
                RefreshDatabase();
            }
            catch (OperationCanceledException)
            {
                LogMessage?.Invoke(this, "Scrape cancelled by user");
            }
            finally
            {
                IsScraping = false;
                ScrapeStatus = "Completed";
            }
        }

        private async Task ScrapeModelAsync(string? modelQuery)
        {
            if (string.IsNullOrEmpty(modelQuery)) return;

            // Parse brand/model from query
            // User can input: "samsung galaxy s24" or just "galaxy s24"

            LogMessage?.Invoke(this, $"Searching Siamphone for: {modelQuery}");

            // For now, try to find in existing brands
            var queryLower = modelQuery.ToLower().Trim();
            string? foundBrand = null;
            string modelPart = queryLower;

            foreach (var brand in SiamphoneService.Brands)
            {
                if (queryLower.StartsWith(brand.Key) || queryLower.StartsWith(brand.Value.ToLower()))
                {
                    foundBrand = brand.Key;
                    modelPart = queryLower
                        .Replace(brand.Key, "")
                        .Replace(brand.Value.ToLower(), "")
                        .Trim();
                    break;
                }
            }

            if (foundBrand == null)
            {
                // Try Samsung as default
                foundBrand = "samsung";
            }

            // Convert model to slug format
            var modelSlug = modelPart
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace(".", "_");

            IsScraping = true;
            ScrapeStatus = $"Fetching {foundBrand}/{modelSlug}...";

            try
            {
                var phone = await _siamphoneService.ScrapeModelSpecsAsync(foundBrand, modelSlug);
                if (phone != null)
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Phones.Insert(0, phone);
                        SelectedPhone = phone;
                    });
                    LogMessage?.Invoke(this, $"Downloaded: {phone.Brand} {phone.Model}");
                }
                else
                {
                    LogMessage?.Invoke(this, $"Could not find: {modelQuery}");
                }
            }
            finally
            {
                IsScraping = false;
                ScrapeStatus = "Ready";
                RefreshDatabase();
            }
        }

        private void StopScrape()
        {
            _cancellationTokenSource?.Cancel();
            LogMessage?.Invoke(this, "Stopping scrape...");
        }

        private void RefreshDatabase()
        {
            TotalInDb = _siamphoneService.GetTotalCount();
            DownloadedCount = _siamphoneService.GetDownloadedCount();

            var phones = _siamphoneService.GetAllPhones();

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Phones.Clear();
                foreach (var phone in phones)
                {
                    Phones.Add(phone);
                }
            });

            LogMessage?.Invoke(this, $"Database: {TotalInDb} phones ({DownloadedCount} downloaded)");
        }

        private void OpenSiamphoneUrl()
        {
            if (SelectedPhone == null || string.IsNullOrEmpty(SelectedPhone.SiamphoneUrl))
                return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SelectedPhone.SiamphoneUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error opening URL: {ex.Message}");
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
