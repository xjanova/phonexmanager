using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PhoneRomFlashTool.Data;
using PhoneRomFlashTool.Models;

namespace PhoneRomFlashTool.ViewModels
{
    public class RomDatabaseViewModel : INotifyPropertyChanged
    {
        private readonly RomDatabaseContext _database;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<string>? LogMessage;

        private ObservableCollection<RomCategoryModel> _categories = new();
        private ObservableCollection<RomSourceModel> _sources = new();
        private ObservableCollection<RomEntryModel> _roms = new();
        private ObservableCollection<RomEntryModel> _filteredRoms = new();
        private ObservableCollection<TipModel> _tips = new();

        private RomCategoryModel? _selectedCategory;
        private RomEntryModel? _selectedRom;
        private string _searchQuery = string.Empty;
        private bool _showRareOnly;
        private bool _showModifiedOnly;
        private int _totalRomCount;

        // Filter options
        private ObservableCollection<string> _brandFilter = new();
        private string _selectedBrand = "All";

        public ObservableCollection<RomCategoryModel> Categories
        {
            get => _categories;
            set { _categories = value; OnPropertyChanged(); }
        }

        public ObservableCollection<RomSourceModel> Sources
        {
            get => _sources;
            set { _sources = value; OnPropertyChanged(); }
        }

        public ObservableCollection<RomEntryModel> Roms
        {
            get => _roms;
            set { _roms = value; OnPropertyChanged(); }
        }

        public ObservableCollection<RomEntryModel> FilteredRoms
        {
            get => _filteredRoms;
            set { _filteredRoms = value; OnPropertyChanged(); }
        }

        public ObservableCollection<TipModel> Tips
        {
            get => _tips;
            set { _tips = value; OnPropertyChanged(); }
        }

        public RomCategoryModel? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                _selectedCategory = value;
                OnPropertyChanged();
                LoadRomsForCategory();
            }
        }

        public RomEntryModel? SelectedRom
        {
            get => _selectedRom;
            set { _selectedRom = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedRom)); }
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                _searchQuery = value;
                OnPropertyChanged();
                FilterRoms();
            }
        }

        public bool ShowRareOnly
        {
            get => _showRareOnly;
            set
            {
                _showRareOnly = value;
                OnPropertyChanged();
                LoadRomsForCategory();
            }
        }

        public bool ShowModifiedOnly
        {
            get => _showModifiedOnly;
            set
            {
                _showModifiedOnly = value;
                OnPropertyChanged();
                LoadRomsForCategory();
            }
        }

        public int TotalRomCount
        {
            get => _totalRomCount;
            set { _totalRomCount = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> BrandFilter
        {
            get => _brandFilter;
            set { _brandFilter = value; OnPropertyChanged(); }
        }

        public string SelectedBrand
        {
            get => _selectedBrand;
            set
            {
                _selectedBrand = value;
                OnPropertyChanged();
                FilterRoms();
            }
        }

        public bool HasSelectedRom => SelectedRom != null;

        // Commands
        public ICommand RefreshDatabaseCommand { get; }
        public ICommand LoadAllRomsCommand { get; }
        public ICommand ShowCategoryCommand { get; }
        public ICommand OpenDownloadUrlCommand { get; }
        public ICommand CopyDownloadUrlCommand { get; }
        public ICommand OpenSourceUrlCommand { get; }
        public ICommand OpenRomDownloadCommand { get; }

        public RomDatabaseViewModel()
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhoneRomFlashTool",
                "RomDatabase",
                "roms.db"
            );

            _database = new RomDatabaseContext(dbPath);

            RefreshDatabaseCommand = new RelayCommand(() => RefreshDatabase());
            LoadAllRomsCommand = new RelayCommand(() => LoadAllRoms());
            ShowCategoryCommand = new RelayCommandWithParam<int>(id => ShowCategory(id));
            OpenDownloadUrlCommand = new RelayCommand(() => OpenDownloadUrl());
            CopyDownloadUrlCommand = new RelayCommand(() => CopyDownloadUrl());
            OpenSourceUrlCommand = new RelayCommandWithParam<string>(url => OpenUrl(url));
            OpenRomDownloadCommand = new RelayCommandWithParam<RomEntryModel>(rom => OpenRomUrl(rom));

            LoadData();
        }

        private void LoadData()
        {
            try
            {
                // Load categories with ROM counts
                var categories = _database.GetCategories();
                foreach (var cat in categories)
                {
                    cat.RomCount = _database.GetRomCount(cat.Id);
                }

                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    Categories.Clear();
                    foreach (var cat in categories)
                    {
                        Categories.Add(cat);
                    }
                });

                // Load sources
                var sources = _database.GetSources();
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    Sources.Clear();
                    foreach (var src in sources)
                    {
                        Sources.Add(src);
                    }
                });

                // Load tips
                var tips = _database.GetTips();
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    Tips.Clear();
                    foreach (var tip in tips)
                    {
                        Tips.Add(tip);
                    }
                });

                // Load all ROMs initially
                LoadAllRoms();

                // Total count
                TotalRomCount = _database.GetRomCount();

                Log("ROM Database loaded successfully");
            }
            catch (Exception ex)
            {
                Log($"Error loading ROM database: {ex.Message}");
            }
        }

        private void LoadAllRoms()
        {
            SelectedCategory = null;
            var roms = _database.GetRoms(
                isRare: _showRareOnly ? true : null,
                isModified: _showModifiedOnly ? true : null
            );

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Roms.Clear();
                FilteredRoms.Clear();
                BrandFilter.Clear();
                BrandFilter.Add("All");

                var brands = roms.Select(r => r.Brand).Distinct().OrderBy(b => b);
                foreach (var brand in brands)
                {
                    BrandFilter.Add(brand);
                }

                foreach (var rom in roms)
                {
                    Roms.Add(rom);
                    FilteredRoms.Add(rom);
                }
            });
        }

        private void LoadRomsForCategory()
        {
            int? categoryId = SelectedCategory?.Id;

            var roms = _database.GetRoms(
                categoryId: categoryId,
                isRare: _showRareOnly ? true : null,
                isModified: _showModifiedOnly ? true : null
            );

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                Roms.Clear();
                FilteredRoms.Clear();
                BrandFilter.Clear();
                BrandFilter.Add("All");

                var brands = roms.Select(r => r.Brand).Distinct().OrderBy(b => b);
                foreach (var brand in brands)
                {
                    BrandFilter.Add(brand);
                }

                foreach (var rom in roms)
                {
                    Roms.Add(rom);
                    FilteredRoms.Add(rom);
                }
            });

            SelectedBrand = "All";
        }

        private void FilterRoms()
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                FilteredRoms.Clear();
                var query = SearchQuery?.ToLower() ?? string.Empty;

                foreach (var rom in Roms)
                {
                    bool matchesSearch = string.IsNullOrEmpty(query) ||
                        rom.Brand.ToLower().Contains(query) ||
                        rom.Model.ToLower().Contains(query) ||
                        rom.Codename.ToLower().Contains(query) ||
                        rom.RomType.ToLower().Contains(query) ||
                        rom.Description.ToLower().Contains(query);

                    bool matchesBrand = SelectedBrand == "All" || rom.Brand == SelectedBrand;

                    if (matchesSearch && matchesBrand)
                    {
                        FilteredRoms.Add(rom);
                    }
                }
            });
        }

        private void ShowCategory(int categoryId)
        {
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
        }

        private void RefreshDatabase()
        {
            LoadData();
        }

        private void OpenDownloadUrl()
        {
            if (SelectedRom != null && !string.IsNullOrEmpty(SelectedRom.DownloadUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = SelectedRom.DownloadUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Log($"Error opening URL: {ex.Message}");
                }
            }
        }

        private void CopyDownloadUrl()
        {
            if (SelectedRom != null && !string.IsNullOrEmpty(SelectedRom.DownloadUrl))
            {
                try
                {
                    System.Windows.Clipboard.SetText(SelectedRom.DownloadUrl);
                    Log("Download URL copied to clipboard");
                }
                catch (Exception ex)
                {
                    Log($"Error copying URL: {ex.Message}");
                }
            }
        }

        private void OpenUrl(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                    Log($"Opening: {url}");
                }
                catch (Exception ex)
                {
                    Log($"Error opening URL: {ex.Message}");
                }
            }
        }

        private void OpenRomUrl(RomEntryModel rom)
        {
            if (rom != null && !string.IsNullOrEmpty(rom.DownloadUrl))
            {
                OpenUrl(rom.DownloadUrl);
            }
            else if (rom != null)
            {
                // Try to find source URL
                var source = Sources.FirstOrDefault(s => s.Id == rom.SourceId);
                if (source != null && !string.IsNullOrEmpty(source.Url))
                {
                    OpenUrl(source.Url);
                }
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Log(string message)
        {
            LogMessage?.Invoke(this, message);
        }
    }
}
