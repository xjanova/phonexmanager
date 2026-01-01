using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PhoneRomFlashTool.Models
{
    public class HexData
    {
        public byte[] RawData { get; set; } = Array.Empty<byte>();
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public bool IsModified { get; set; }
        public List<HexModification> Modifications { get; set; } = new();
        public ObservableCollection<HexLine> Lines { get; set; } = new();
    }

    public class HexLine
    {
        public long Offset { get; set; }
        public byte[] Bytes { get; set; } = new byte[16];
        public int ValidBytes { get; set; } = 16;

        public string OffsetHex => Offset.ToString("X8");

        public string HexString
        {
            get
            {
                var parts = new List<string>();
                for (int i = 0; i < ValidBytes; i++)
                {
                    parts.Add(Bytes[i].ToString("X2"));
                }
                return string.Join(" ", parts);
            }
        }

        public string AsciiString
        {
            get
            {
                var chars = new char[ValidBytes];
                for (int i = 0; i < ValidBytes; i++)
                {
                    var b = Bytes[i];
                    chars[i] = (b >= 32 && b < 127) ? (char)b : '.';
                }
                return new string(chars);
            }
        }
    }

    public class HexModification
    {
        public long Offset { get; set; }
        public byte OriginalValue { get; set; }
        public byte NewValue { get; set; }
        public DateTime ModifiedTime { get; set; }
    }

    public class HexSearchResult
    {
        public long Offset { get; set; }
        public byte[] MatchedBytes { get; set; } = Array.Empty<byte>();
        public string Context { get; set; } = string.Empty;
    }

    public class RomAnalysisResult
    {
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileType { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public List<string> DetectedPatterns { get; set; } = new();
        public List<PartitionInfo> Partitions { get; set; } = new();
        public Dictionary<string, string> Properties { get; set; } = new();
        public List<StringMatch> Strings { get; set; } = new();
    }

    public class StringMatch
    {
        public long Offset { get; set; }
        public string Value { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }
}
