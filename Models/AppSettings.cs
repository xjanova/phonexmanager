using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace PhoneRomFlashTool.Models
{
    public class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhoneRomFlashTool",
            "settings.json"
        );

        // Paths
        public string ToolsPath { get; set; } = string.Empty;
        public string DriversPath { get; set; } = string.Empty;
        public string RomDatabasePath { get; set; } = string.Empty;
        public string RomsPath { get; set; } = string.Empty;
        public string BackupsPath { get; set; } = string.Empty;
        public string DownloadsPath { get; set; } = string.Empty;

        // Tool paths (computed)
        public string AdbPath => System.IO.Path.Combine(ToolsPath, "adb.exe");
        public string FastbootPath => System.IO.Path.Combine(ToolsPath, "fastboot.exe");

        // Tool versions
        public Dictionary<string, ToolInfo> InstalledTools { get; set; } = new();
        public Dictionary<string, DriverInfo> InstalledDrivers { get; set; } = new();

        // Update settings
        public DateTime LastUpdateCheck { get; set; }
        public bool AutoCheckUpdates { get; set; } = true;
        public int UpdateCheckIntervalHours { get; set; } = 24;

        // Download sources
        public string ToolsManifestUrl { get; set; } = "https://raw.githubusercontent.com/nicholast/android-tools/main/manifest.json";
        public string DriversManifestUrl { get; set; } = "https://raw.githubusercontent.com/nicholast/phone-drivers/main/manifest.json";
        public string RomDatabaseUrl { get; set; } = "https://raw.githubusercontent.com/nicholast/rom-database/main/database.json";

        // UI Settings
        public bool DarkMode { get; set; } = true;
        public string Language { get; set; } = "en";

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                    settings.EnsurePathsExist();
                    return settings;
                }
            }
            catch { }

            var newSettings = new AppSettings();
            newSettings.InitializeDefaults();
            newSettings.Save();
            return newSettings;
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(directory);
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        public void InitializeDefaults()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PhoneRomFlashTool"
            );

            ToolsPath = Path.Combine(appDataPath, "Tools");
            DriversPath = Path.Combine(appDataPath, "Drivers");
            RomDatabasePath = Path.Combine(appDataPath, "RomDatabase");
            RomsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PhoneRomFlashTool", "ROMs");
            BackupsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PhoneRomFlashTool", "Backups");
            DownloadsPath = Path.Combine(appDataPath, "Downloads");

            EnsurePathsExist();
        }

        public void EnsurePathsExist()
        {
            Directory.CreateDirectory(ToolsPath);
            Directory.CreateDirectory(DriversPath);
            Directory.CreateDirectory(RomDatabasePath);
            Directory.CreateDirectory(RomsPath);
            Directory.CreateDirectory(BackupsPath);
            Directory.CreateDirectory(DownloadsPath);
        }
    }

    public class ToolInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public DateTime InstalledDate { get; set; }
        public string Checksum { get; set; } = string.Empty;
        public bool IsInstalled { get; set; }
    }

    public class DriverInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public DateTime InstalledDate { get; set; }
        public bool IsInstalled { get; set; }
        public List<string> SupportedDevices { get; set; } = new();
    }

    public class DownloadManifest
    {
        public string Version { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
        public List<DownloadableItem> Tools { get; set; } = new();
        public List<DownloadableItem> Drivers { get; set; } = new();
        public List<RomDatabaseEntry> Roms { get; set; } = new();
    }

    public class DownloadableItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string Checksum { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public List<string> SupportedPlatforms { get; set; } = new();
        public bool IsRequired { get; set; }
        public bool IsInstalled { get; set; }
        public string Path { get; set; } = string.Empty;
    }

    public class RomDatabaseEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Codename { get; set; } = string.Empty;
        public string AndroidVersion { get; set; } = string.Empty;
        public string RomType { get; set; } = string.Empty; // Stock, Custom, Recovery
        public string DownloadUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string Checksum { get; set; } = string.Empty;
        public DateTime ReleaseDate { get; set; }
        public string Changelog { get; set; } = string.Empty;
        public List<string> RequiredDrivers { get; set; } = new();
    }
}
