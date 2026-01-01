using System;
using System.Collections.Generic;

namespace PhoneRomFlashTool.Models
{
    public class RomInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string Checksum { get; set; } = string.Empty;
        public RomType Type { get; set; }
        public DateTime BackupDate { get; set; }
        public string DeviceModel { get; set; } = string.Empty;
        public string AndroidVersion { get; set; } = string.Empty;
        public List<PartitionInfo> Partitions { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();

        public string FileSizeFormatted => FormatFileSize(FileSize);

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

    public class PartitionInfo
    {
        public string Name { get; set; } = string.Empty;
        public long Offset { get; set; }
        public long Size { get; set; }
        public string Type { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    public enum RomType
    {
        Unknown,
        FullRom,
        Boot,
        Recovery,
        System,
        Vendor,
        Data,
        Custom
    }
}
