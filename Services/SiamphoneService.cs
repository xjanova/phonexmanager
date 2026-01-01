using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace PhoneRomFlashTool.Services
{
    public class PhoneSpecModel
    {
        public int Id { get; set; }
        public string Brand { get; set; } = "";
        public string Model { get; set; } = "";
        public string ModelSlug { get; set; } = "";
        public string SiamphoneId { get; set; } = "";
        public string SiamphoneUrl { get; set; } = "";

        // Display
        public string DisplaySize { get; set; } = "";
        public string DisplayType { get; set; } = "";
        public string DisplayResolution { get; set; } = "";

        // Hardware
        public string Processor { get; set; } = "";
        public string Ram { get; set; } = "";
        public string Storage { get; set; } = "";
        public string ExpandableStorage { get; set; } = "";

        // Camera
        public string RearCamera { get; set; } = "";
        public string FrontCamera { get; set; } = "";

        // Battery
        public string Battery { get; set; } = "";
        public string Charging { get; set; } = "";

        // Connectivity
        public string Network { get; set; } = "";
        public string Wifi { get; set; } = "";
        public string Bluetooth { get; set; } = "";
        public string Usb { get; set; } = "";
        public string Nfc { get; set; } = "";

        // Physical
        public string Dimensions { get; set; } = "";
        public string Weight { get; set; } = "";
        public string Colors { get; set; } = "";
        public string Os { get; set; } = "";

        // Price & Release
        public string ReleaseDate { get; set; } = "";
        public decimal Price { get; set; }
        public string PriceText { get; set; } = "";

        // Images
        public string ImageUrl { get; set; } = "";
        public string LocalImagePath { get; set; } = "";

        // Metadata
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public bool IsDownloaded { get; set; }
    }

    public class SiamphoneService
    {
        private readonly HttpClient _httpClient;
        private readonly string _dbPath;
        private readonly string _imageCachePath;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<ScrapeProgressEventArgs>? ProgressChanged;

        // Known brands on Siamphone
        public static readonly Dictionary<string, string> Brands = new()
        {
            { "samsung", "Samsung" },
            { "apple", "Apple" },
            { "xiaomi", "Xiaomi" },
            { "oppo", "OPPO" },
            { "vivo", "Vivo" },
            { "realme", "Realme" },
            { "huawei", "Huawei" },
            { "honor", "Honor" },
            { "oneplus", "OnePlus" },
            { "google", "Google" },
            { "sony", "Sony" },
            { "motorola", "Motorola" },
            { "nokia", "Nokia" },
            { "asus", "ASUS" },
            { "lg", "LG" },
            { "lenovo", "Lenovo" },
            { "zte", "ZTE" },
            { "nothing", "Nothing" },
            { "infinix", "Infinix" },
            { "tecno", "Tecno" },
            { "poco", "POCO" },
            { "redmi", "Redmi" },
            { "iqoo", "iQOO" },
            { "nubia", "Nubia" },
            { "blackshark", "Black Shark" },
            { "meizu", "Meizu" }
        };

        public SiamphoneService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "th-TH,th;q=0.9,en;q=0.8");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhoneRomFlashTool");

            _dbPath = Path.Combine(appDataPath, "phonespec.db");
            _imageCachePath = Path.Combine(appDataPath, "PhoneImages");

            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);

            if (!Directory.Exists(_imageCachePath))
                Directory.CreateDirectory(_imageCachePath);

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS PhoneSpecs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Brand TEXT NOT NULL,
                    Model TEXT NOT NULL,
                    ModelSlug TEXT,
                    SiamphoneId TEXT,
                    SiamphoneUrl TEXT,
                    DisplaySize TEXT,
                    DisplayType TEXT,
                    DisplayResolution TEXT,
                    Processor TEXT,
                    Ram TEXT,
                    Storage TEXT,
                    ExpandableStorage TEXT,
                    RearCamera TEXT,
                    FrontCamera TEXT,
                    Battery TEXT,
                    Charging TEXT,
                    Network TEXT,
                    Wifi TEXT,
                    Bluetooth TEXT,
                    Usb TEXT,
                    Nfc TEXT,
                    Dimensions TEXT,
                    Weight TEXT,
                    Colors TEXT,
                    Os TEXT,
                    ReleaseDate TEXT,
                    Price REAL,
                    PriceText TEXT,
                    ImageUrl TEXT,
                    LocalImagePath TEXT,
                    CreatedAt TEXT,
                    UpdatedAt TEXT,
                    IsDownloaded INTEGER DEFAULT 0,
                    UNIQUE(Brand, Model)
                );

                CREATE INDEX IF NOT EXISTS idx_brand ON PhoneSpecs(Brand);
                CREATE INDEX IF NOT EXISTS idx_model ON PhoneSpecs(Model);
                CREATE INDEX IF NOT EXISTS idx_siamphone_id ON PhoneSpecs(SiamphoneId);
            ";
            command.ExecuteNonQuery();

            LogMessage?.Invoke(this, "Phone database initialized");
        }

        public async Task<List<string>> GetBrandModelsAsync(string brandSlug, CancellationToken cancellationToken = default)
        {
            var models = new List<string>();

            try
            {
                var url = $"https://www.siamphone.com/spec/{brandSlug}/";
                LogMessage?.Invoke(this, $"Fetching models from: {url}");

                var html = await _httpClient.GetStringAsync(url, cancellationToken);

                // Parse model links from HTML
                // Pattern: /spec/en/samsung/galaxy_s24_ultra.htm or /spec/th/...
                var pattern = $@"/spec/(?:en|th)/{brandSlug}/([a-z0-9_\-]+)\.htm";
                var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);

                foreach (Match match in matches)
                {
                    var modelSlug = match.Groups[1].Value;
                    if (!models.Contains(modelSlug))
                    {
                        models.Add(modelSlug);
                    }
                }

                LogMessage?.Invoke(this, $"Found {models.Count} models for {brandSlug}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error fetching brand models: {ex.Message}");
            }

            return models;
        }

        public async Task<PhoneSpecModel?> ScrapeModelSpecsAsync(string brandSlug, string modelSlug,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Check if already in database
                var existing = GetPhoneBySlug(brandSlug, modelSlug);
                if (existing != null && existing.IsDownloaded)
                {
                    LogMessage?.Invoke(this, $"Skipping {modelSlug} - already downloaded");
                    return existing;
                }

                var url = $"https://www.siamphone.com/spec/en/{brandSlug}/{modelSlug}.htm";
                LogMessage?.Invoke(this, $"Scraping: {url}");

                var html = await _httpClient.GetStringAsync(url, cancellationToken);

                var phone = ParsePhoneSpec(html, brandSlug, modelSlug, url);
                if (phone != null)
                {
                    // Download image
                    if (!string.IsNullOrEmpty(phone.ImageUrl))
                    {
                        phone.LocalImagePath = await DownloadImageAsync(phone.ImageUrl, brandSlug, modelSlug, cancellationToken);
                    }

                    phone.IsDownloaded = true;
                    SavePhone(phone);

                    LogMessage?.Invoke(this, $"Saved: {phone.Brand} {phone.Model}");
                }

                return phone;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404"))
            {
                // Try Thai version
                try
                {
                    var urlTh = $"https://www.siamphone.com/spec/th/{brandSlug}/{modelSlug}.htm";
                    var htmlTh = await _httpClient.GetStringAsync(urlTh, cancellationToken);
                    var phone = ParsePhoneSpec(htmlTh, brandSlug, modelSlug, urlTh);
                    if (phone != null)
                    {
                        phone.IsDownloaded = true;
                        SavePhone(phone);
                    }
                    return phone;
                }
                catch
                {
                    LogMessage?.Invoke(this, $"Model not found: {modelSlug}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error scraping {modelSlug}: {ex.Message}");
                return null;
            }
        }

        private PhoneSpecModel? ParsePhoneSpec(string html, string brandSlug, string modelSlug, string url)
        {
            try
            {
                var phone = new PhoneSpecModel
                {
                    Brand = Brands.GetValueOrDefault(brandSlug, brandSlug),
                    ModelSlug = modelSlug,
                    SiamphoneUrl = url
                };

                // Extract Model Name from script variable
                var modelMatch = Regex.Match(html, @"modelname_toscript\s*=\s*""([^""]+)""");
                if (modelMatch.Success)
                {
                    phone.Model = modelMatch.Groups[1].Value;
                }
                else
                {
                    // Try from title
                    var titleMatch = Regex.Match(html, @"<h1[^>]*>([^<]+)</h1>");
                    if (titleMatch.Success)
                    {
                        phone.Model = titleMatch.Groups[1].Value.Trim();
                    }
                    else
                    {
                        phone.Model = modelSlug.Replace("_", " ");
                    }
                }

                // Extract Siamphone ID
                var idMatch = Regex.Match(html, @"modelid_toscript\s*=\s*""(\d+)""");
                if (idMatch.Success)
                {
                    phone.SiamphoneId = idMatch.Groups[1].Value;
                }

                // Extract Price
                var priceMatch = Regex.Match(html, @"(\d{1,3}(?:,\d{3})*)\s*(?:฿|บาท|THB)", RegexOptions.IgnoreCase);
                if (priceMatch.Success)
                {
                    var priceStr = priceMatch.Groups[1].Value.Replace(",", "");
                    if (decimal.TryParse(priceStr, out var price))
                    {
                        phone.Price = price;
                        phone.PriceText = $"{price:N0} บาท";
                    }
                }

                // Extract Image URL
                var imgMatch = Regex.Match(html,
                    $@"https://cdn\.siamphone\.com/spec/{brandSlug}/images/{modelSlug}/[^""'\s]+\.(jpg|png|webp)",
                    RegexOptions.IgnoreCase);
                if (imgMatch.Success)
                {
                    phone.ImageUrl = imgMatch.Value;
                }

                // Extract specifications using common patterns
                phone.DisplaySize = ExtractSpec(html, @"(?:Display|หน้าจอ)[:\s]*(\d+\.?\d*\s*(?:นิ้ว|Inch|""))", 1);
                phone.DisplayType = ExtractSpec(html, @"(?:AMOLED|IPS|LCD|OLED|Dynamic AMOLED)[^<]*", 0);
                phone.DisplayResolution = ExtractSpec(html, @"(\d+\s*x\s*\d+)\s*(?:pixels|พิกเซล)", 1);

                phone.Processor = ExtractSpec(html, @"(?:Qualcomm|MediaTek|Apple|Samsung|Exynos|Snapdragon|Dimensity|Helio)[^<,]+", 0);
                phone.Ram = ExtractSpec(html, @"(?:RAM|แรม)[:\s]*(\d+\s*GB)", 1);
                phone.Storage = ExtractSpec(html, @"(?:ROM|Storage|ความจุ)[:\s]*(\d+\s*(?:GB|TB))", 1);

                phone.RearCamera = ExtractSpec(html, @"(?:Rear|หลัง)[^<]*?(\d+\s*MP)", 1);
                phone.FrontCamera = ExtractSpec(html, @"(?:Front|หน้า)[^<]*?(\d+\s*MP)", 1);

                phone.Battery = ExtractSpec(html, @"(\d{1,2},?\d{3}\s*mAh)", 1);
                phone.Charging = ExtractSpec(html, @"(\d+W\s*(?:Fast|Quick)?[^<]*Charge)", 1);

                phone.Network = ExtractSpec(html, @"(5G|4G LTE|4G|3G)", 1);
                phone.Wifi = ExtractSpec(html, @"Wi-?Fi\s*([^<,]+)", 1);
                phone.Bluetooth = ExtractSpec(html, @"Bluetooth\s*(\d+\.?\d*)", 1);
                phone.Nfc = html.ToLower().Contains("nfc") ? "Yes" : "No";
                phone.Usb = ExtractSpec(html, @"(USB Type-C|USB-C|Lightning|Micro USB)", 1);

                phone.Dimensions = ExtractSpec(html, @"(\d+\.?\d*\s*x\s*\d+\.?\d*\s*x\s*\d+\.?\d*)\s*mm", 1);
                phone.Weight = ExtractSpec(html, @"(\d+\.?\d*)\s*(?:grams|g|กรัม)", 1);
                phone.Os = ExtractSpec(html, @"(Android\s*\d+|iOS\s*\d+|HarmonyOS)[^<]*", 0);

                phone.Colors = ExtractSpec(html, @"(?:Color|สี)[:\s]*([^<]+)", 1);

                return phone;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Parse error: {ex.Message}");
                return null;
            }
        }

        private string ExtractSpec(string html, string pattern, int group)
        {
            try
            {
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return group == 0 ? match.Value.Trim() : match.Groups[group].Value.Trim();
                }
            }
            catch { }
            return "";
        }

        private async Task<string> DownloadImageAsync(string imageUrl, string brand, string model,
            CancellationToken cancellationToken)
        {
            try
            {
                var fileName = $"{brand}_{model}.jpg".Replace(" ", "_");
                var localPath = Path.Combine(_imageCachePath, brand);

                if (!Directory.Exists(localPath))
                    Directory.CreateDirectory(localPath);

                var filePath = Path.Combine(localPath, fileName);

                if (File.Exists(filePath))
                    return filePath;

                var imageData = await _httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
                await File.WriteAllBytesAsync(filePath, imageData, cancellationToken);

                return filePath;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Image download error: {ex.Message}");
                return "";
            }
        }

        public void SavePhone(PhoneSpecModel phone)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO PhoneSpecs (
                    Brand, Model, ModelSlug, SiamphoneId, SiamphoneUrl,
                    DisplaySize, DisplayType, DisplayResolution,
                    Processor, Ram, Storage, ExpandableStorage,
                    RearCamera, FrontCamera, Battery, Charging,
                    Network, Wifi, Bluetooth, Usb, Nfc,
                    Dimensions, Weight, Colors, Os,
                    ReleaseDate, Price, PriceText,
                    ImageUrl, LocalImagePath,
                    CreatedAt, UpdatedAt, IsDownloaded
                ) VALUES (
                    @Brand, @Model, @ModelSlug, @SiamphoneId, @SiamphoneUrl,
                    @DisplaySize, @DisplayType, @DisplayResolution,
                    @Processor, @Ram, @Storage, @ExpandableStorage,
                    @RearCamera, @FrontCamera, @Battery, @Charging,
                    @Network, @Wifi, @Bluetooth, @Usb, @Nfc,
                    @Dimensions, @Weight, @Colors, @Os,
                    @ReleaseDate, @Price, @PriceText,
                    @ImageUrl, @LocalImagePath,
                    @CreatedAt, @UpdatedAt, @IsDownloaded
                )";

            command.Parameters.AddWithValue("@Brand", phone.Brand);
            command.Parameters.AddWithValue("@Model", phone.Model);
            command.Parameters.AddWithValue("@ModelSlug", phone.ModelSlug);
            command.Parameters.AddWithValue("@SiamphoneId", phone.SiamphoneId);
            command.Parameters.AddWithValue("@SiamphoneUrl", phone.SiamphoneUrl);
            command.Parameters.AddWithValue("@DisplaySize", phone.DisplaySize);
            command.Parameters.AddWithValue("@DisplayType", phone.DisplayType);
            command.Parameters.AddWithValue("@DisplayResolution", phone.DisplayResolution);
            command.Parameters.AddWithValue("@Processor", phone.Processor);
            command.Parameters.AddWithValue("@Ram", phone.Ram);
            command.Parameters.AddWithValue("@Storage", phone.Storage);
            command.Parameters.AddWithValue("@ExpandableStorage", phone.ExpandableStorage);
            command.Parameters.AddWithValue("@RearCamera", phone.RearCamera);
            command.Parameters.AddWithValue("@FrontCamera", phone.FrontCamera);
            command.Parameters.AddWithValue("@Battery", phone.Battery);
            command.Parameters.AddWithValue("@Charging", phone.Charging);
            command.Parameters.AddWithValue("@Network", phone.Network);
            command.Parameters.AddWithValue("@Wifi", phone.Wifi);
            command.Parameters.AddWithValue("@Bluetooth", phone.Bluetooth);
            command.Parameters.AddWithValue("@Usb", phone.Usb);
            command.Parameters.AddWithValue("@Nfc", phone.Nfc);
            command.Parameters.AddWithValue("@Dimensions", phone.Dimensions);
            command.Parameters.AddWithValue("@Weight", phone.Weight);
            command.Parameters.AddWithValue("@Colors", phone.Colors);
            command.Parameters.AddWithValue("@Os", phone.Os);
            command.Parameters.AddWithValue("@ReleaseDate", phone.ReleaseDate);
            command.Parameters.AddWithValue("@Price", phone.Price);
            command.Parameters.AddWithValue("@PriceText", phone.PriceText);
            command.Parameters.AddWithValue("@ImageUrl", phone.ImageUrl);
            command.Parameters.AddWithValue("@LocalImagePath", phone.LocalImagePath);
            command.Parameters.AddWithValue("@CreatedAt", phone.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("@UpdatedAt", DateTime.Now.ToString("O"));
            command.Parameters.AddWithValue("@IsDownloaded", phone.IsDownloaded ? 1 : 0);

            command.ExecuteNonQuery();
        }

        public PhoneSpecModel? GetPhoneBySlug(string brand, string modelSlug)
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM PhoneSpecs WHERE Brand = @Brand AND ModelSlug = @ModelSlug";
            command.Parameters.AddWithValue("@Brand", Brands.GetValueOrDefault(brand, brand));
            command.Parameters.AddWithValue("@ModelSlug", modelSlug);

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                return MapReaderToPhone(reader);
            }

            return null;
        }

        public List<PhoneSpecModel> SearchPhones(string query)
        {
            var phones = new List<PhoneSpecModel>();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT * FROM PhoneSpecs
                WHERE Brand LIKE @Query
                   OR Model LIKE @Query
                   OR Processor LIKE @Query
                ORDER BY Brand, Model
                LIMIT 100";
            command.Parameters.AddWithValue("@Query", $"%{query}%");

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                phones.Add(MapReaderToPhone(reader));
            }

            return phones;
        }

        public List<PhoneSpecModel> GetPhonesByBrand(string brand)
        {
            var phones = new List<PhoneSpecModel>();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM PhoneSpecs WHERE Brand = @Brand ORDER BY Model";
            command.Parameters.AddWithValue("@Brand", brand);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                phones.Add(MapReaderToPhone(reader));
            }

            return phones;
        }

        public List<PhoneSpecModel> GetAllPhones()
        {
            var phones = new List<PhoneSpecModel>();

            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM PhoneSpecs ORDER BY Brand, Model";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                phones.Add(MapReaderToPhone(reader));
            }

            return phones;
        }

        public int GetTotalCount()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM PhoneSpecs";

            return Convert.ToInt32(command.ExecuteScalar());
        }

        public int GetDownloadedCount()
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM PhoneSpecs WHERE IsDownloaded = 1";

            return Convert.ToInt32(command.ExecuteScalar());
        }

        private PhoneSpecModel MapReaderToPhone(SqliteDataReader reader)
        {
            return new PhoneSpecModel
            {
                Id = reader.GetInt32(0),
                Brand = reader.GetString(1),
                Model = reader.GetString(2),
                ModelSlug = reader.IsDBNull(3) ? "" : reader.GetString(3),
                SiamphoneId = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SiamphoneUrl = reader.IsDBNull(5) ? "" : reader.GetString(5),
                DisplaySize = reader.IsDBNull(6) ? "" : reader.GetString(6),
                DisplayType = reader.IsDBNull(7) ? "" : reader.GetString(7),
                DisplayResolution = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Processor = reader.IsDBNull(9) ? "" : reader.GetString(9),
                Ram = reader.IsDBNull(10) ? "" : reader.GetString(10),
                Storage = reader.IsDBNull(11) ? "" : reader.GetString(11),
                ExpandableStorage = reader.IsDBNull(12) ? "" : reader.GetString(12),
                RearCamera = reader.IsDBNull(13) ? "" : reader.GetString(13),
                FrontCamera = reader.IsDBNull(14) ? "" : reader.GetString(14),
                Battery = reader.IsDBNull(15) ? "" : reader.GetString(15),
                Charging = reader.IsDBNull(16) ? "" : reader.GetString(16),
                Network = reader.IsDBNull(17) ? "" : reader.GetString(17),
                Wifi = reader.IsDBNull(18) ? "" : reader.GetString(18),
                Bluetooth = reader.IsDBNull(19) ? "" : reader.GetString(19),
                Usb = reader.IsDBNull(20) ? "" : reader.GetString(20),
                Nfc = reader.IsDBNull(21) ? "" : reader.GetString(21),
                Dimensions = reader.IsDBNull(22) ? "" : reader.GetString(22),
                Weight = reader.IsDBNull(23) ? "" : reader.GetString(23),
                Colors = reader.IsDBNull(24) ? "" : reader.GetString(24),
                Os = reader.IsDBNull(25) ? "" : reader.GetString(25),
                ReleaseDate = reader.IsDBNull(26) ? "" : reader.GetString(26),
                Price = reader.IsDBNull(27) ? 0 : (decimal)reader.GetDouble(27),
                PriceText = reader.IsDBNull(28) ? "" : reader.GetString(28),
                ImageUrl = reader.IsDBNull(29) ? "" : reader.GetString(29),
                LocalImagePath = reader.IsDBNull(30) ? "" : reader.GetString(30),
                IsDownloaded = reader.GetInt32(33) == 1
            };
        }

        public async Task ScrapeAllBrandModelsAsync(string brandSlug, CancellationToken cancellationToken = default)
        {
            LogMessage?.Invoke(this, $"Starting scrape for brand: {brandSlug}");

            var models = await GetBrandModelsAsync(brandSlug, cancellationToken);
            int total = models.Count;
            int current = 0;
            int skipped = 0;
            int downloaded = 0;

            foreach (var modelSlug in models)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    LogMessage?.Invoke(this, "Scraping cancelled");
                    break;
                }

                current++;

                // Check if already downloaded
                var existing = GetPhoneBySlug(brandSlug, modelSlug);
                if (existing != null && existing.IsDownloaded)
                {
                    skipped++;
                    ProgressChanged?.Invoke(this, new ScrapeProgressEventArgs
                    {
                        Current = current,
                        Total = total,
                        CurrentModel = $"{brandSlug}/{modelSlug}",
                        Status = "Skipped (already exists)",
                        Downloaded = downloaded,
                        Skipped = skipped
                    });
                    continue;
                }

                var phone = await ScrapeModelSpecsAsync(brandSlug, modelSlug, cancellationToken);
                if (phone != null)
                {
                    downloaded++;
                }

                ProgressChanged?.Invoke(this, new ScrapeProgressEventArgs
                {
                    Current = current,
                    Total = total,
                    CurrentModel = $"{brandSlug}/{modelSlug}",
                    Status = phone != null ? "Downloaded" : "Failed",
                    Downloaded = downloaded,
                    Skipped = skipped
                });

                // Rate limiting - be nice to the server
                await Task.Delay(500, cancellationToken);
            }

            LogMessage?.Invoke(this, $"Completed: {downloaded} downloaded, {skipped} skipped, {total - downloaded - skipped} failed");
        }

        public async Task ScrapeAllBrandsAsync(CancellationToken cancellationToken = default)
        {
            LogMessage?.Invoke(this, "Starting full scrape of all brands...");

            foreach (var brand in Brands)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                LogMessage?.Invoke(this, $"Processing brand: {brand.Value}");
                await ScrapeAllBrandModelsAsync(brand.Key, cancellationToken);

                // Delay between brands
                await Task.Delay(1000, cancellationToken);
            }

            LogMessage?.Invoke(this, "Full scrape completed!");
        }
    }

    public class ScrapeProgressEventArgs : EventArgs
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string CurrentModel { get; set; } = "";
        public string Status { get; set; } = "";
        public int Downloaded { get; set; }
        public int Skipped { get; set; }
        public int Percent => Total > 0 ? (Current * 100) / Total : 0;
    }
}
