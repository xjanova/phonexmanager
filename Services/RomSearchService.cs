using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace PhoneRomFlashTool.Services
{
    public class RomSearchResult
    {
        public string Name { get; set; } = "";
        public string Model { get; set; } = "";
        public string Version { get; set; } = "";
        public string AndroidVersion { get; set; } = "";
        public string Size { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string Source { get; set; } = "";
        public string SourceIcon { get; set; } = "🌐";
        public string RomType { get; set; } = "Stock"; // Stock, Custom, Recovery
        public string Region { get; set; } = "";
        public DateTime? ReleaseDate { get; set; }
        public bool IsVerified { get; set; }
        public string Checksum { get; set; } = "";
        public string Description { get; set; } = "";

        public string DisplayName => $"{Name} ({Version})";
        public string SourceDisplay => $"{SourceIcon} {Source}";
    }

    public class RomSearchService
    {
        private readonly HttpClient _httpClient;
        private readonly string _romCachePath;
        private readonly string _downloadPath;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<int>? ProgressChanged;
        public event EventHandler<RomSearchResult>? RomFound;

        // Search sources configuration
        private readonly List<IRomSearchSource> _searchSources;

        public RomSearchService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PhoneXManager/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _romCachePath = Path.Combine(appData, "PhoneRomFlashTool", "RomCache");
            _downloadPath = Path.Combine(appData, "PhoneRomFlashTool", "Downloads");

            Directory.CreateDirectory(_romCachePath);
            Directory.CreateDirectory(_downloadPath);

            // Initialize search sources
            _searchSources = new List<IRomSearchSource>
            {
                new SamFwSearchSource(_httpClient),
                new XiaomiFirmwareSearchSource(_httpClient),
                new LineageOsSearchSource(_httpClient),
                new PixelExperienceSearchSource(_httpClient),
                new TwrpSearchSource(_httpClient),
                new SamMobileSearchSource(_httpClient),
                new XdaSearchSource(_httpClient),
                new ApkMirrorRecoverySource(_httpClient)
            };
        }

        public async Task<List<RomSearchResult>> SearchRomAsync(string query, CancellationToken ct = default)
        {
            var results = new List<RomSearchResult>();
            var normalizedQuery = NormalizeQuery(query);

            Log($"Searching for: {query}");
            StatusChanged?.Invoke(this, $"Searching for '{query}'...");
            ProgressChanged?.Invoke(this, 0);

            int completed = 0;
            int total = _searchSources.Count;

            // Search all sources in parallel
            var searchTasks = _searchSources.Select(async source =>
            {
                try
                {
                    var sourceResults = await source.SearchAsync(normalizedQuery, ct);
                    lock (results)
                    {
                        results.AddRange(sourceResults);
                        foreach (var result in sourceResults)
                        {
                            RomFound?.Invoke(this, result);
                        }
                    }
                    Log($"Found {sourceResults.Count} results from {source.Name}");
                }
                catch (Exception ex)
                {
                    Log($"Error searching {source.Name}: {ex.Message}");
                }
                finally
                {
                    Interlocked.Increment(ref completed);
                    ProgressChanged?.Invoke(this, (completed * 100) / total);
                }
            });

            await Task.WhenAll(searchTasks);

            // Sort by relevance
            results = results
                .OrderByDescending(r => CalculateRelevance(r, normalizedQuery))
                .ThenByDescending(r => r.ReleaseDate)
                .ToList();

            StatusChanged?.Invoke(this, $"Found {results.Count} ROMs");
            ProgressChanged?.Invoke(this, 100);

            // Cache results
            await CacheResultsAsync(query, results);

            return results;
        }

        public async Task<string> DownloadRomAsync(RomSearchResult rom, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            Log($"Downloading: {rom.Name}");
            StatusChanged?.Invoke(this, $"Downloading {rom.Name}...");

            try
            {
                var fileName = SanitizeFileName($"{rom.Model}_{rom.Version}_{rom.Source}");
                var extension = GetExtensionFromUrl(rom.DownloadUrl);
                var filePath = Path.Combine(_downloadPath, $"{fileName}{extension}");

                // Check if already downloaded
                if (File.Exists(filePath))
                {
                    Log($"ROM already downloaded: {filePath}");
                    StatusChanged?.Invoke(this, "ROM already downloaded");
                    return filePath;
                }

                using var response = await _httpClient.GetAsync(rom.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progressPercent = (int)((downloadedBytes * 100) / totalBytes);
                        progress?.Report(progressPercent);
                        ProgressChanged?.Invoke(this, progressPercent);
                    }
                }

                Log($"Downloaded: {filePath}");
                StatusChanged?.Invoke(this, "Download complete");
                ProgressChanged?.Invoke(this, 100);

                // Save ROM info
                await SaveRomInfoAsync(rom, filePath);

                return filePath;
            }
            catch (Exception ex)
            {
                Log($"Download error: {ex.Message}");
                StatusChanged?.Invoke(this, "Download failed");
                throw;
            }
        }

        public List<RomSearchResult> GetCachedResults(string query)
        {
            var cacheFile = Path.Combine(_romCachePath, $"{SanitizeFileName(query)}.json");
            if (File.Exists(cacheFile))
            {
                try
                {
                    var json = File.ReadAllText(cacheFile);
                    return JsonSerializer.Deserialize<List<RomSearchResult>>(json) ?? new List<RomSearchResult>();
                }
                catch { }
            }
            return new List<RomSearchResult>();
        }

        public List<string> GetDownloadedRoms()
        {
            if (!Directory.Exists(_downloadPath))
                return new List<string>();

            return Directory.GetFiles(_downloadPath, "*.*")
                .Where(f => f.EndsWith(".zip") || f.EndsWith(".img") || f.EndsWith(".tar") || f.EndsWith(".md5"))
                .ToList();
        }

        private string NormalizeQuery(string query)
        {
            // Extract model info from various formats
            query = query.Trim().ToLower();

            // Common patterns
            query = Regex.Replace(query, @"\s+", " ");

            return query;
        }

        private int CalculateRelevance(RomSearchResult result, string query)
        {
            int score = 0;
            var lowerName = result.Name.ToLower();
            var lowerModel = result.Model.ToLower();

            if (lowerModel.Contains(query)) score += 100;
            if (lowerName.Contains(query)) score += 50;
            if (result.IsVerified) score += 30;
            if (!string.IsNullOrEmpty(result.Checksum)) score += 20;
            if (result.RomType == "Stock") score += 10;

            return score;
        }

        private async Task CacheResultsAsync(string query, List<RomSearchResult> results)
        {
            try
            {
                var cacheFile = Path.Combine(_romCachePath, $"{SanitizeFileName(query)}.json");
                var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(cacheFile, json);
            }
            catch { }
        }

        private async Task SaveRomInfoAsync(RomSearchResult rom, string filePath)
        {
            try
            {
                var infoFile = filePath + ".info.json";
                var json = JsonSerializer.Serialize(rom, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(infoFile, json);
            }
            catch { }
        }

        private string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }

        private string GetExtensionFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath;
                var ext = Path.GetExtension(path);
                if (!string.IsNullOrEmpty(ext)) return ext;
            }
            catch { }
            return ".zip";
        }

        private void Log(string message) => LogMessage?.Invoke(this, $"[RomSearch] {message}");
    }

    // Interface for ROM search sources
    public interface IRomSearchSource
    {
        string Name { get; }
        string Icon { get; }
        Task<List<RomSearchResult>> SearchAsync(string query, CancellationToken ct);
    }

    // Samsung Firmware - SamFW
    public class SamFwSearchSource : IRomSearchSource
    {
        private readonly HttpClient _http;
        public string Name => "SamFW";
        public string Icon => "📱";

        public SamFwSearchSource(HttpClient http) => _http = http;

        public async Task<List<RomSearchResult>> SearchAsync(string query, CancellationToken ct)
        {
            var results = new List<RomSearchResult>();

            try
            {
                // SamFW search API
                var searchUrl = $"https://samfw.com/firmware/{HttpUtility.UrlEncode(query.ToUpper())}";
                var html = await _http.GetStringAsync(searchUrl, ct);

                // Parse firmware listings
                var modelPattern = @"<a[^>]*href=""(/firmware/[^""]+)""[^>]*>([^<]+)</a>";
                var matches = Regex.Matches(html, modelPattern);

                foreach (Match match in matches.Take(10))
                {
                    var link = match.Groups[1].Value;
                    var name = match.Groups[2].Value.Trim();

                    if (name.ToLower().Contains(query.ToLower()) ||
                        link.ToLower().Contains(query.ToLower()))
                    {
                        results.Add(new RomSearchResult
                        {
                            Name = name,
                            Model = ExtractModel(name),
                            Source = Name,
                            SourceIcon = Icon,
                            RomType = "Stock",
                            DownloadUrl = $"https://samfw.com{link}",
                            IsVerified = true,
                            Description = "Official Samsung Firmware"
                        });
                    }
                }
            }
            catch { }

            return results;
        }

        private string ExtractModel(string name)
        {
            var match = Regex.Match(name, @"(SM-[A-Z]\d+[A-Z]?)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : name;
        }
    }

    // Xiaomi Firmware
    public class XiaomiFirmwareSearchSource : IRomSearchSource
    {
        private readonly HttpClient _http;
        public string Name => "Xiaomi Firmware";
        public string Icon => "🍊";

        public XiaomiFirmwareSearchSource(HttpClient http) => _http = http;

        public async Task<List<RomSearchResult>> SearchAsync(string query, CancellationToken ct)
        {
            var results = new List<RomSearchResult>();

            try
            {
                var searchUrl = $"https://xiaomifirmwareupdater.com/miui/{HttpUtility.UrlEncode(query)}/";
                var html = await _http.GetStringAsync(searchUrl, ct);

                // Parse MIUI listings
                var pattern = @"<td[^>]*>([^<]+)</td>\s*<td[^>]*>([^<]+)</td>\s*<td[^>]*><a[^>]*href=""([^""]+)""";
                var matches = Regex.Matches(html, pattern);

                foreach (Match match in matches.Take(10))
                {
                    results.Add(new RomSearchResult
                    {
                        Name = $"MIUI {match.Groups[1].Value}",
                        Model = query,
                        Version = match.Groups[2].Value,
                        Source = Name,
                        SourceIcon = Icon,
                        RomType = "Stock",
                        DownloadUrl = match.Groups[3].Value,
                        IsVerified = true,
                        Description = "Official Xiaomi MIUI/HyperOS"
                    });
                }
            }
            catch { }

            return results;
        }
    }

    // LineageOS
    public class LineageOsSearchSource : IRomSearchSource
    {
        private readonly HttpClient _http;
        public string Name => "LineageOS";
        public string Icon => "🌿";

        public LineageOsSearchSource(HttpClient http) => _http = http;

        public async Task<List<RomSearchResult>> SearchAsync(string query, CancellationToken ct)
        {
            var results = new List<RomSearchResult>();

            try
            {
                // LineageOS wiki API for device search
                var apiUrl = $"https://download.lineageos.org/api/v2/devices";
                var json = await _http.GetStringAsync(apiUrl, ct);

                using var doc = JsonDocument.Parse(json);
                foreach (var device in doc.RootElement.EnumerateArray())
                {
                    var codename = device.GetProperty("codename").GetString() ?? "";
                    var name = device.GetProperty("name").GetString() ?? "";
                    var vendor = device.TryGetProperty("vendor", out var v) ? v.GetString() ?? "" : "";

                    if (codename.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        vendor.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new RomSearchResult
                        {
                            Name = $"LineageOS for {vendor} {name}",
                            Model = codename,
                            Source = Name,
                            SourceIcon = Icon,
                            RomType = "Custom",
                            DownloadUrl = $"https://download.lineageos.org/{codename}",
                            IsVerified = true,
                            Description = "Open-source Android distribution"
                        });
                    }
                }
            }
            catch { }

            return results;
        }
    }

    // Pixel Experience
    public class PixelExperienceSearchSource : IRomSearchSource
    {
        private readonly HttpClient _http;
        public string Name => "Pixel Experience";
        public string Icon => "🔵";

        public PixelExperienceSearchSource(HttpClient http) => _http = http;

        public async Task<List<RomSearchResult>> SearchAsync(string query, CancellationToken ct)
        {
            var results = new List<RomSearchResult>();

            try
            {
                var apiUrl = "https://download.pixelexperience.org/devices";
                var html = await _http.GetStringAsync(apiUrl, ct);

                // Parse device list
                var pattern = @"<a[^>]*href=""/([^""]+)""[^>]*>([^<]+)</a>";
                var matches = Regex.Matches(html, pattern);

                foreach (Match match in matches)
                {
                    var codename = match.Groups[1].Value;
                    var name = match.Groups[2].Value;

                    if (codename.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new RomSearchResult
                        {
                            Name = $"Pixel Experience - {name}",
                            Model = codename,
                            Source = Name,
                            SourceIcon = Icon,
                            RomType = "Custom",
                            DownloadUrl = $"https://download.pixelexperience.org/{codename}",
                            IsVerified = true,
                            Description = "Pixel-like experience for your device"
                        });
                    }
                }
            }
            catch { }

            return results;
        }
    }

    // TWRP Recovery
    public class TwrpSearchSource : IRomSearchSource
    {
        private readonly HttpClient _http;
        public string Name => "TWRP";
        public string Icon => "🔧";

        public TwrpSearchSource(HttpClient http) => _http = http;

        public async Task<List<RomSearchResult>> SearchAsync(string query, CancellationToken ct)
        {
            var results = new List<RomSearchResult>();

            try
            {
                var searchUrl = $"https://twrp.me/Devices/";
                var html = await _http.GetStringAsync(searchUrl, ct);

                var pattern = @"<a[^>]*href=""(/[^""]+)""[^>]*>([^<]*" + Regex.Escape(query) + @"[^<]*)</a>";
                var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);

                foreach (Match match in matches.Take(10))
                {
                    var link = match.Groups[1].Value;
                    var name = match.Groups[2].Value.Trim();

                    results.Add(new RomSearchResult
                    {
                        Name = $"TWRP for {name}",
                        Model = name,
                        Source = Name,
                        SourceIcon = Icon,
                        RomType = "Recovery",
                        DownloadUrl = $"https://twrp.me{link}",
                        IsVerified = true,
                        Description = "Team Win Recovery Project"
                    });
                }
            }
            catch { }

            return results;
        }
    }

    // SamMobile
    public class SamMobileSearchSource : IRomSearchSource
    {
        private readonly HttpClient _http;
        public string Name => "SamMobile";
        public string Icon => "📲";

        public SamMobileSearchSource(HttpClient http) => _http = http;

        public async Task<List<RomSearchResult>> SearchAsync(string query, CancellationToken ct)
        {
            var results = new List<RomSearchResult>();

            try
            {
                var searchUrl = $"https://www.sammobile.com/firmwares/database/{HttpUtility.UrlEncode(query.ToUpper())}/";
                var html = await _http.GetStringAsync(searchUrl, ct);

                var pattern = @"<a[^>]*href=""(/firmwares/[^""]+)""[^>]*>\s*<span[^>]*>([^<]+)</span>";
                var matches = Regex.Matches(html, pattern);

                foreach (Match match in matches.Take(10))
                {
                    var link = match.Groups[1].Value;
                    var version = match.Groups[2].Value.Trim();

                    results.Add(new RomSearchResult
                    {
                        Name = $"Samsung {query.ToUpper()}",
                        Model = query.ToUpper(),
                        Version = version,
                        Source = Name,
                        SourceIcon = Icon,
                        RomType = "Stock",
                        DownloadUrl = $"https://www.sammobile.com{link}",
                        Description = "Samsung firmware from SamMobile"
                    });
                }
            }
            catch { }

            return results;
        }
    }

    // XDA Developers
    public class XdaSearchSource : IRomSearchSource
    {
        private readonly HttpClient _http;
        public string Name => "XDA Forums";
        public string Icon => "💬";

        public XdaSearchSource(HttpClient http) => _http = http;

        public async Task<List<RomSearchResult>> SearchAsync(string query, CancellationToken ct)
        {
            var results = new List<RomSearchResult>();

            try
            {
                // XDA search for ROMs
                var searchUrl = $"https://xdaforums.com/search/?q={HttpUtility.UrlEncode(query + " rom")}&t=post&c[node]=422";

                // Note: XDA may require session, return generic search link
                results.Add(new RomSearchResult
                {
                    Name = $"Search XDA for {query} ROMs",
                    Model = query,
                    Source = Name,
                    SourceIcon = Icon,
                    RomType = "Custom",
                    DownloadUrl = searchUrl,
                    Description = "Community ROMs and mods from XDA Developers"
                });
            }
            catch { }

            return results;
        }
    }

    // APKMirror for GApps and Recovery tools
    public class ApkMirrorRecoverySource : IRomSearchSource
    {
        private readonly HttpClient _http;
        public string Name => "APKMirror";
        public string Icon => "📦";

        public ApkMirrorRecoverySource(HttpClient http) => _http = http;

        public async Task<List<RomSearchResult>> SearchAsync(string query, CancellationToken ct)
        {
            var results = new List<RomSearchResult>();

            // Common GApps packages
            if (query.Contains("gapps", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new RomSearchResult
                {
                    Name = "Open GApps",
                    Model = "All Android",
                    Source = Name,
                    SourceIcon = Icon,
                    RomType = "GApps",
                    DownloadUrl = "https://opengapps.org/",
                    IsVerified = true,
                    Description = "Google Apps package for custom ROMs"
                });

                results.Add(new RomSearchResult
                {
                    Name = "NikGApps",
                    Model = "All Android",
                    Source = Name,
                    SourceIcon = Icon,
                    RomType = "GApps",
                    DownloadUrl = "https://nikgapps.com/",
                    IsVerified = true,
                    Description = "Customizable Google Apps package"
                });

                results.Add(new RomSearchResult
                {
                    Name = "MindTheGapps",
                    Model = "All Android",
                    Source = Name,
                    SourceIcon = Icon,
                    RomType = "GApps",
                    DownloadUrl = "https://github.com/AdrainWei/Android-Blob-Utility/",
                    IsVerified = true,
                    Description = "Minimal Google Apps package"
                });
            }

            return results;
        }
    }
}
