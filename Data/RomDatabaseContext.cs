using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace PhoneRomFlashTool.Data
{
    public class RomDatabaseContext : IDisposable
    {
        private readonly string _connectionString;
        private SqliteConnection? _connection;

        public RomDatabaseContext(string databasePath)
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = $"Data Source={databasePath}";
            InitializeDatabase();
        }

        private SqliteConnection GetConnection()
        {
            if (_connection == null)
            {
                _connection = new SqliteConnection(_connectionString);
                _connection.Open();
            }
            return _connection;
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Create ROM Categories table
            var createCategoriesTable = @"
                CREATE TABLE IF NOT EXISTS RomCategories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Description TEXT,
                    Icon TEXT,
                    Color TEXT DEFAULT '#1976D2',
                    SortOrder INTEGER DEFAULT 0
                )";

            // Create ROM Sources table
            var createSourcesTable = @"
                CREATE TABLE IF NOT EXISTS RomSources (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Url TEXT,
                    Description TEXT,
                    TrustLevel INTEGER DEFAULT 3,
                    IsOfficial INTEGER DEFAULT 0
                )";

            // Create ROMs table with extended info
            var createRomsTable = @"
                CREATE TABLE IF NOT EXISTS Roms (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Brand TEXT NOT NULL,
                    Model TEXT NOT NULL,
                    Codename TEXT,
                    AndroidVersion TEXT,
                    RomType TEXT DEFAULT 'Stock',
                    CategoryId INTEGER,
                    SourceId INTEGER,
                    DownloadUrl TEXT,
                    MirrorUrl1 TEXT,
                    MirrorUrl2 TEXT,
                    FileSize INTEGER DEFAULT 0,
                    Checksum TEXT,
                    ChecksumType TEXT DEFAULT 'MD5',
                    ReleaseDate TEXT,
                    Description TEXT,
                    Changelog TEXT,
                    InstallGuide TEXT,
                    Warnings TEXT,
                    Tips TEXT,
                    IsModified INTEGER DEFAULT 0,
                    IsRare INTEGER DEFAULT 0,
                    IsVerified INTEGER DEFAULT 0,
                    RequiresUnlock INTEGER DEFAULT 1,
                    RiskLevel INTEGER DEFAULT 1,
                    DownloadCount INTEGER DEFAULT 0,
                    Rating REAL DEFAULT 0,
                    Tags TEXT,
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (CategoryId) REFERENCES RomCategories(Id),
                    FOREIGN KEY (SourceId) REFERENCES RomSources(Id)
                )";

            // Create Required Drivers table
            var createDriversTable = @"
                CREATE TABLE IF NOT EXISTS RequiredDrivers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RomId INTEGER,
                    DriverName TEXT NOT NULL,
                    DriverUrl TEXT,
                    FOREIGN KEY (RomId) REFERENCES Roms(Id) ON DELETE CASCADE
                )";

            // Create Tips table
            var createTipsTable = @"
                CREATE TABLE IF NOT EXISTS Tips (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    Category TEXT DEFAULT 'General',
                    Importance INTEGER DEFAULT 1,
                    SortOrder INTEGER DEFAULT 0
                )";

            using var cmd = connection.CreateCommand();
            cmd.CommandText = createCategoriesTable;
            cmd.ExecuteNonQuery();

            cmd.CommandText = createSourcesTable;
            cmd.ExecuteNonQuery();

            cmd.CommandText = createRomsTable;
            cmd.ExecuteNonQuery();

            cmd.CommandText = createDriversTable;
            cmd.ExecuteNonQuery();

            cmd.CommandText = createTipsTable;
            cmd.ExecuteNonQuery();

            // Insert default categories if empty
            cmd.CommandText = "SELECT COUNT(*) FROM RomCategories";
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            if (count == 0)
            {
                InsertDefaultData(connection);
            }
        }

        private void InsertDefaultData(SqliteConnection connection)
        {
            // Insert categories
            var categories = new[]
            {
                ("Stock ROMs", "Official factory ROMs from manufacturers", "📱", "#4CAF50", 1),
                ("Custom ROMs", "Modified ROMs with custom features", "🔧", "#2196F3", 2),
                ("Recovery", "Custom recovery images (TWRP, CWM)", "💾", "#9C27B0", 3),
                ("Rare/Legacy", "Hard to find ROMs for old or rare devices", "💎", "#FF9800", 4),
                ("Modified/Patched", "Pre-rooted or patched ROMs - USE WITH CAUTION", "⚠️", "#F44336", 5),
                ("Firmware", "Baseband, Modem, and other firmware files", "📡", "#607D8B", 6),
                ("Unbrick", "Files to recover bricked devices", "🔥", "#E91E63", 7)
            };

            using var cmd = connection.CreateCommand();
            foreach (var (name, desc, icon, color, order) in categories)
            {
                cmd.CommandText = "INSERT INTO RomCategories (Name, Description, Icon, Color, SortOrder) VALUES (@name, @desc, @icon, @color, @order)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@desc", desc);
                cmd.Parameters.AddWithValue("@icon", icon);
                cmd.Parameters.AddWithValue("@color", color);
                cmd.Parameters.AddWithValue("@order", order);
                cmd.ExecuteNonQuery();
            }

            // Insert sources
            var sources = new[]
            {
                ("Samsung Firmware", "https://samfw.com", "Official Samsung firmware downloads", 5, 1),
                ("Xiaomi Firmware", "https://xiaomifirmwareupdater.com", "Official Xiaomi MIUI firmware", 5, 1),
                ("XDA Developers", "https://xda-developers.com", "Community-driven ROM development", 4, 0),
                ("LineageOS", "https://lineageos.org", "Popular open-source custom ROM", 5, 0),
                ("PixelExperience", "https://download.pixelexperience.org", "Pixel-like experience for all devices", 4, 0),
                ("TWRP", "https://twrp.me", "Official Team Win Recovery Project", 5, 0),
                ("4PDA", "https://4pda.to", "Russian Android community - rare ROMs", 3, 0),
                ("Needrom", "https://needrom.com", "Various firmware and ROMs", 2, 0),
                ("Android File Host", "https://androidfilehost.com", "File hosting for Android files", 3, 0),
                ("GSM Arena Firmware", "https://firmware.gem-flash.com", "Various device firmware", 2, 0)
            };

            foreach (var (name, url, desc, trust, official) in sources)
            {
                cmd.CommandText = "INSERT INTO RomSources (Name, Url, Description, TrustLevel, IsOfficial) VALUES (@name, @url, @desc, @trust, @official)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@url", url);
                cmd.Parameters.AddWithValue("@desc", desc);
                cmd.Parameters.AddWithValue("@trust", trust);
                cmd.Parameters.AddWithValue("@official", official);
                cmd.ExecuteNonQuery();
            }

            // Insert tips
            var tips = new[]
            {
                ("Always Backup First", "Before flashing any ROM, always create a full backup of your current ROM using the Backup tab. This allows you to restore if something goes wrong.", "Safety", 5, 1),
                ("Check Compatibility", "Make sure the ROM you're downloading is exactly for your device model and variant. Wrong ROM can brick your device!", "Safety", 5, 2),
                ("Battery Level", "Ensure your device has at least 50% battery before flashing. Power loss during flash can brick your device.", "Safety", 4, 3),
                ("Unlock Bootloader", "Most custom ROMs require an unlocked bootloader. This usually wipes all data - backup first!", "Prerequisites", 4, 4),
                ("Install Drivers", "Make sure you have the correct USB drivers installed before connecting your device.", "Prerequisites", 4, 5),
                ("Verify Checksums", "Always verify the MD5/SHA256 checksum of downloaded files to ensure they're not corrupted.", "Safety", 3, 6),
                ("Read Instructions", "Each ROM may have specific installation instructions. Read them carefully before flashing.", "General", 3, 7),
                ("Modified ROMs Warning", "Pre-rooted or modified ROMs may contain malware. Only use from trusted sources!", "Warning", 5, 8),
                ("EDL Mode", "Qualcomm devices can use EDL (Emergency Download) mode to flash when device is completely bricked.", "Advanced", 2, 9),
                ("MTK Flash Tool", "MediaTek devices use SP Flash Tool for flashing. Download from official MediaTek sources.", "Tools", 3, 10)
            };

            foreach (var (title, content, category, importance, order) in tips)
            {
                cmd.CommandText = "INSERT INTO Tips (Title, Content, Category, Importance, SortOrder) VALUES (@title, @content, @cat, @imp, @order)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@title", title);
                cmd.Parameters.AddWithValue("@content", content);
                cmd.Parameters.AddWithValue("@cat", category);
                cmd.Parameters.AddWithValue("@imp", importance);
                cmd.Parameters.AddWithValue("@order", order);
                cmd.ExecuteNonQuery();
            }

            // Insert sample rare/legacy ROMs
            InsertSampleRoms(connection);
        }

        private void InsertSampleRoms(SqliteConnection connection)
        {
            var roms = new List<(string brand, string model, string codename, string android, string romType,
                int categoryId, int sourceId, string desc, string warnings, string tips, int isModified, int isRare, int riskLevel)>
            {
                // Stock ROMs
                ("Samsung", "Galaxy S24 Ultra", "e3q", "14", "Stock", 1, 1,
                    "Official One UI 6.1 firmware for Galaxy S24 Ultra", "", "Use Odin for flashing", 0, 0, 1),
                ("Samsung", "Galaxy S23", "dm1q", "14", "Stock", 1, 1,
                    "Official One UI 6.0 firmware", "", "Enable OEM unlock in Developer Options", 0, 0, 1),
                ("Xiaomi", "14 Pro", "shennong", "14", "Stock", 1, 2,
                    "Official HyperOS firmware", "", "Use Mi Flash Tool", 0, 0, 1),
                ("Xiaomi", "Redmi Note 13 Pro", "garnet", "14", "Stock", 1, 2,
                    "Official MIUI 14 firmware", "", "Unlock bootloader via Mi Unlock Tool", 0, 0, 1),

                // Custom ROMs
                ("Samsung", "Galaxy S21", "o1s", "14", "LineageOS 21", 2, 4,
                    "Clean Android experience with LineageOS", "", "Requires TWRP and unlocked bootloader", 0, 0, 2),
                ("OnePlus", "9 Pro", "lemonadep", "14", "PixelExperience", 2, 5,
                    "Pixel-like experience with Google features", "", "Flash via TWRP or Fastboot", 0, 0, 2),
                ("Google", "Pixel 7", "panther", "14", "GrapheneOS", 2, 3,
                    "Privacy-focused Android distribution", "", "Use Web Installer at grapheneos.org", 0, 0, 2),

                // Recovery
                ("Samsung", "Galaxy A54", "a54x", "N/A", "TWRP 3.7.0", 3, 6,
                    "Team Win Recovery Project for Galaxy A54", "", "Flash via Odin in Download mode", 0, 0, 1),
                ("Xiaomi", "POCO F5", "marble", "N/A", "OrangeFox R11.1", 3, 3,
                    "OrangeFox Recovery with touch support", "", "Flash via Fastboot", 0, 0, 1),

                // Rare/Legacy ROMs
                ("Sony", "Xperia Z Ultra", "togari", "5.1", "Stock", 4, 7,
                    "Last official firmware for Xperia Z Ultra - RARE", "Device no longer supported", "Hard to find, keep backup!", 0, 1, 2),
                ("HTC", "One M8", "m8", "6.0", "Stock", 4, 8,
                    "Final official Sense ROM for HTC One M8", "Old device, battery may not hold charge", "Collector's item ROM", 0, 1, 2),
                ("LG", "G4", "h815", "6.0", "Stock", 4, 7,
                    "Official firmware before LG exit from mobile", "Known bootloop issue on some units", "Flash only if necessary", 0, 1, 3),
                ("Nokia", "N9", "harmattan", "MeeGo 1.2", "Stock", 4, 7,
                    "Legendary MeeGo OS - extremely rare", "Discontinued OS", "Museum piece ROM", 0, 1, 2),
                ("Samsung", "Galaxy Note 7", "grace", "6.0", "Stock", 4, 10,
                    "Original Note 7 firmware before recall", "DEVICE WAS RECALLED - DO NOT USE ORIGINAL BATTERY", "Historical purposes only", 0, 1, 5),
                ("BlackBerry", "Priv", "venice", "6.0", "Stock", 4, 7,
                    "First BlackBerry Android phone firmware", "Device no longer supported", "Rare Android BlackBerry", 0, 1, 2),

                // Modified/Patched ROMs - WITH WARNINGS
                ("Samsung", "Galaxy S10", "beyond1lte", "12", "Pre-rooted Stock", 5, 9,
                    "Stock ROM with pre-installed Magisk root",
                    "⚠️ MODIFIED ROM - May contain unwanted modifications. Only use if you trust the source!",
                    "Verify MD5 checksum before flashing", 1, 0, 4),
                ("Xiaomi", "Redmi Note 8", "ginkgo", "11", "Debloated MIUI", 5, 7,
                    "MIUI with bloatware removed and optimizations",
                    "⚠️ MODIFIED - System apps removed. Some features may not work!",
                    "Good for low storage devices", 1, 0, 3),
                ("Samsung", "Galaxy A52", "a52q", "13", "Patched Knox", 5, 9,
                    "Stock ROM with Knox disabled for root compatibility",
                    "⚠️ WARRANTY VOID - Knox will be tripped permanently!",
                    "Samsung Pay and Secure Folder will not work", 1, 0, 4),

                // Firmware
                ("Qualcomm", "Various", "sdm845", "N/A", "EDL Programmers", 6, 7,
                    "EDL firehose programmers for Snapdragon 845 devices",
                    "⚠️ Advanced users only - wrong programmer can brick device!",
                    "Used with QFIL tool for emergency recovery", 0, 1, 4),
                ("MediaTek", "MT6785", "helio_g90", "N/A", "Preloader", 6, 10,
                    "Preloader files for MediaTek Helio G90 devices",
                    "Only use with SP Flash Tool",
                    "Required for unbrick procedures", 0, 0, 3),

                // Unbrick
                ("Samsung", "Galaxy S20", "x1s", "N/A", "Unbrick Package", 7, 1,
                    "Full firmware package for S20 recovery",
                    "Use only if device is completely bricked",
                    "Flash all partitions via Odin", 0, 0, 3),
                ("Xiaomi", "Mi 11", "venus", "N/A", "EDL Unbrick", 7, 2,
                    "Emergency Download mode recovery files",
                    "⚠️ Requires authorized Mi account for EDL access",
                    "Contact Xiaomi support for EDL authorization", 0, 0, 4)
            };

            using var cmd = connection.CreateCommand();
            foreach (var rom in roms)
            {
                cmd.CommandText = @"
                    INSERT INTO Roms (Brand, Model, Codename, AndroidVersion, RomType, CategoryId, SourceId,
                        Description, Warnings, Tips, IsModified, IsRare, RiskLevel)
                    VALUES (@brand, @model, @codename, @android, @romType, @categoryId, @sourceId,
                        @desc, @warnings, @tips, @isModified, @isRare, @riskLevel)";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@brand", rom.brand);
                cmd.Parameters.AddWithValue("@model", rom.model);
                cmd.Parameters.AddWithValue("@codename", rom.codename);
                cmd.Parameters.AddWithValue("@android", rom.android);
                cmd.Parameters.AddWithValue("@romType", rom.romType);
                cmd.Parameters.AddWithValue("@categoryId", rom.categoryId);
                cmd.Parameters.AddWithValue("@sourceId", rom.sourceId);
                cmd.Parameters.AddWithValue("@desc", rom.desc);
                cmd.Parameters.AddWithValue("@warnings", rom.warnings);
                cmd.Parameters.AddWithValue("@tips", rom.tips);
                cmd.Parameters.AddWithValue("@isModified", rom.isModified);
                cmd.Parameters.AddWithValue("@isRare", rom.isRare);
                cmd.Parameters.AddWithValue("@riskLevel", rom.riskLevel);
                cmd.ExecuteNonQuery();
            }
        }

        public List<RomCategoryModel> GetCategories()
        {
            var categories = new List<RomCategoryModel>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM RomCategories ORDER BY SortOrder";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                categories.Add(new RomCategoryModel
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Icon = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Color = reader.IsDBNull(4) ? "#1976D2" : reader.GetString(4),
                    SortOrder = reader.GetInt32(5)
                });
            }
            return categories;
        }

        public List<RomSourceModel> GetSources()
        {
            var sources = new List<RomSourceModel>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM RomSources ORDER BY TrustLevel DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                sources.Add(new RomSourceModel
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Url = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    TrustLevel = reader.GetInt32(4),
                    IsOfficial = reader.GetInt32(5) == 1
                });
            }
            return sources;
        }

        public List<RomEntryModel> GetRoms(int? categoryId = null, string? searchQuery = null, bool? isRare = null, bool? isModified = null)
        {
            var roms = new List<RomEntryModel>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            var sql = @"
                SELECT r.*, c.Name as CategoryName, c.Color as CategoryColor, c.Icon as CategoryIcon,
                       s.Name as SourceName, s.TrustLevel
                FROM Roms r
                LEFT JOIN RomCategories c ON r.CategoryId = c.Id
                LEFT JOIN RomSources s ON r.SourceId = s.Id
                WHERE 1=1";

            if (categoryId.HasValue)
            {
                sql += " AND r.CategoryId = @categoryId";
                cmd.Parameters.AddWithValue("@categoryId", categoryId.Value);
            }

            if (!string.IsNullOrEmpty(searchQuery))
            {
                sql += " AND (r.Brand LIKE @search OR r.Model LIKE @search OR r.Codename LIKE @search OR r.RomType LIKE @search)";
                cmd.Parameters.AddWithValue("@search", $"%{searchQuery}%");
            }

            if (isRare.HasValue)
            {
                sql += " AND r.IsRare = @isRare";
                cmd.Parameters.AddWithValue("@isRare", isRare.Value ? 1 : 0);
            }

            if (isModified.HasValue)
            {
                sql += " AND r.IsModified = @isModified";
                cmd.Parameters.AddWithValue("@isModified", isModified.Value ? 1 : 0);
            }

            sql += " ORDER BY r.Brand, r.Model";
            cmd.CommandText = sql;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                roms.Add(new RomEntryModel
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    Brand = reader.GetString(reader.GetOrdinal("Brand")),
                    Model = reader.GetString(reader.GetOrdinal("Model")),
                    Codename = GetStringOrEmpty(reader, "Codename"),
                    AndroidVersion = GetStringOrEmpty(reader, "AndroidVersion"),
                    RomType = GetStringOrEmpty(reader, "RomType"),
                    CategoryId = GetIntOrDefault(reader, "CategoryId"),
                    SourceId = GetIntOrDefault(reader, "SourceId"),
                    DownloadUrl = GetStringOrEmpty(reader, "DownloadUrl"),
                    MirrorUrl1 = GetStringOrEmpty(reader, "MirrorUrl1"),
                    MirrorUrl2 = GetStringOrEmpty(reader, "MirrorUrl2"),
                    FileSize = GetLongOrDefault(reader, "FileSize"),
                    Description = GetStringOrEmpty(reader, "Description"),
                    Warnings = GetStringOrEmpty(reader, "Warnings"),
                    Tips = GetStringOrEmpty(reader, "Tips"),
                    IsModified = reader.GetInt32(reader.GetOrdinal("IsModified")) == 1,
                    IsRare = reader.GetInt32(reader.GetOrdinal("IsRare")) == 1,
                    IsVerified = reader.GetInt32(reader.GetOrdinal("IsVerified")) == 1,
                    RiskLevel = reader.GetInt32(reader.GetOrdinal("RiskLevel")),
                    CategoryName = GetStringOrEmpty(reader, "CategoryName"),
                    CategoryColor = GetStringOrEmpty(reader, "CategoryColor"),
                    CategoryIcon = GetStringOrEmpty(reader, "CategoryIcon"),
                    SourceName = GetStringOrEmpty(reader, "SourceName"),
                    SourceTrustLevel = GetIntOrDefault(reader, "TrustLevel")
                });
            }
            return roms;
        }

        public List<TipModel> GetTips(string? category = null)
        {
            var tips = new List<TipModel>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            var sql = "SELECT * FROM Tips";
            if (!string.IsNullOrEmpty(category))
            {
                sql += " WHERE Category = @category";
                cmd.Parameters.AddWithValue("@category", category);
            }
            sql += " ORDER BY Importance DESC, SortOrder";
            cmd.CommandText = sql;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tips.Add(new TipModel
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Content = reader.GetString(2),
                    Category = reader.GetString(3),
                    Importance = reader.GetInt32(4),
                    SortOrder = reader.GetInt32(5)
                });
            }
            return tips;
        }

        public int GetRomCount(int? categoryId = null)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();

            if (categoryId.HasValue)
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Roms WHERE CategoryId = @categoryId";
                cmd.Parameters.AddWithValue("@categoryId", categoryId.Value);
            }
            else
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Roms";
            }

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private string GetStringOrEmpty(SqliteDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
        }

        private int GetIntOrDefault(SqliteDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
        }

        private long GetLongOrDefault(SqliteDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? 0 : reader.GetInt64(ordinal);
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }

    // Models
    public class RomCategoryModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Color { get; set; } = "#1976D2";
        public int SortOrder { get; set; }
        public int RomCount { get; set; }
    }

    public class RomSourceModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int TrustLevel { get; set; }
        public bool IsOfficial { get; set; }

        public string TrustLevelText => TrustLevel switch
        {
            5 => "Highly Trusted",
            4 => "Trusted",
            3 => "Moderate",
            2 => "Use Caution",
            1 => "Risky",
            _ => "Unknown"
        };
    }

    public class RomEntryModel
    {
        public int Id { get; set; }
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Codename { get; set; } = string.Empty;
        public string AndroidVersion { get; set; } = string.Empty;
        public string RomType { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public int SourceId { get; set; }
        public string DownloadUrl { get; set; } = string.Empty;
        public string MirrorUrl1 { get; set; } = string.Empty;
        public string MirrorUrl2 { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string Description { get; set; } = string.Empty;
        public string Warnings { get; set; } = string.Empty;
        public string Tips { get; set; } = string.Empty;
        public bool IsModified { get; set; }
        public bool IsRare { get; set; }
        public bool IsVerified { get; set; }
        public int RiskLevel { get; set; }

        // Joined fields
        public string CategoryName { get; set; } = string.Empty;
        public string CategoryColor { get; set; } = "#1976D2";
        public string CategoryIcon { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public int SourceTrustLevel { get; set; }

        public string DisplayName => $"{Brand} {Model}";
        public bool HasWarning => !string.IsNullOrEmpty(Warnings);
        public string FileSizeFormatted => FormatFileSize(FileSize);

        public string RiskLevelText => RiskLevel switch
        {
            1 => "Safe",
            2 => "Low Risk",
            3 => "Moderate",
            4 => "High Risk",
            5 => "Dangerous",
            _ => "Unknown"
        };

        public string RiskLevelColor => RiskLevel switch
        {
            1 => "#4CAF50",
            2 => "#8BC34A",
            3 => "#FF9800",
            4 => "#FF5722",
            5 => "#F44336",
            _ => "#9E9E9E"
        };

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }

    public class TipModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int Importance { get; set; }
        public int SortOrder { get; set; }

        public string ImportanceIcon => Importance switch
        {
            5 => "🔴",
            4 => "🟠",
            3 => "🟡",
            2 => "🟢",
            1 => "🔵",
            _ => "⚪"
        };
    }
}
