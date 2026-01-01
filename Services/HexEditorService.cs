using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class HexSearchResult
    {
        public long Offset { get; set; }
        public byte[] MatchedBytes { get; set; } = Array.Empty<byte>();
        public string HexString => BitConverter.ToString(MatchedBytes).Replace("-", " ");
        public string AsciiString => Encoding.ASCII.GetString(
            MatchedBytes.Select(b => b >= 32 && b < 127 ? b : (byte)'.').ToArray());
    }

    public class HexPatch
    {
        public long Offset { get; set; }
        public byte[] OriginalBytes { get; set; } = Array.Empty<byte>();
        public byte[] NewBytes { get; set; } = Array.Empty<byte>();
        public string Description { get; set; } = "";
    }

    public class FileRegion
    {
        public long StartOffset { get; set; }
        public long EndOffset { get; set; }
        public string Description { get; set; } = "";
        public string Type { get; set; } = "";
    }

    public class HexEditorService
    {
        private readonly int _bufferSize;

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        public HexEditorService(int bufferSize = 1024 * 1024)
        {
            _bufferSize = bufferSize;
        }

        public async Task<byte[]> ReadBytesAsync(string filePath, long offset, int count,
            CancellationToken ct = default)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                stream.Seek(offset, SeekOrigin.Begin);

                var buffer = new byte[count];
                var bytesRead = await stream.ReadAsync(buffer, 0, count, ct);

                if (bytesRead < count)
                {
                    Array.Resize(ref buffer, bytesRead);
                }

                return buffer;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Read error: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        public async Task<bool> WriteBytesAsync(string filePath, long offset, byte[] data,
            CancellationToken ct = default)
        {
            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Write, FileShare.Read);
                stream.Seek(offset, SeekOrigin.Begin);
                await stream.WriteAsync(data, ct);
                await stream.FlushAsync(ct);

                LogMessage?.Invoke(this, $"Written {data.Length} bytes at offset 0x{offset:X}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Write error: {ex.Message}");
                return false;
            }
        }

        public async Task<List<HexSearchResult>> SearchBytesAsync(string filePath, byte[] pattern,
            int maxResults = 100, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            var results = new List<HexSearchResult>();

            try
            {
                using var stream = File.OpenRead(filePath);
                var fileSize = stream.Length;
                var buffer = new byte[_bufferSize + pattern.Length - 1];
                long position = 0;

                while (position < fileSize && results.Count < maxResults)
                {
                    ct.ThrowIfCancellationRequested();

                    stream.Seek(position, SeekOrigin.Begin);
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);

                    if (bytesRead < pattern.Length) break;

                    for (int i = 0; i <= bytesRead - pattern.Length && results.Count < maxResults; i++)
                    {
                        bool match = true;
                        for (int j = 0; j < pattern.Length; j++)
                        {
                            if (buffer[i + j] != pattern[j])
                            {
                                match = false;
                                break;
                            }
                        }

                        if (match)
                        {
                            results.Add(new HexSearchResult
                            {
                                Offset = position + i,
                                MatchedBytes = buffer.Skip(i).Take(pattern.Length).ToArray()
                            });
                        }
                    }

                    position += _bufferSize;
                    progress?.Report((int)((position * 100) / fileSize));
                }

                LogMessage?.Invoke(this, $"Found {results.Count} matches");
            }
            catch (OperationCanceledException)
            {
                LogMessage?.Invoke(this, "Search cancelled");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Search error: {ex.Message}");
            }

            return results;
        }

        public async Task<List<HexSearchResult>> SearchStringAsync(string filePath, string searchText,
            bool caseSensitive = false, Encoding? encoding = null,
            int maxResults = 100, CancellationToken ct = default)
        {
            encoding ??= Encoding.UTF8;
            var pattern = encoding.GetBytes(caseSensitive ? searchText : searchText.ToLower());

            if (!caseSensitive)
            {
                var results = new List<HexSearchResult>();
                var searchLower = searchText.ToLower();

                using var stream = File.OpenRead(filePath);
                var fileSize = stream.Length;
                var buffer = new byte[_bufferSize + searchText.Length - 1];
                long position = 0;

                while (position < fileSize && results.Count < maxResults)
                {
                    ct.ThrowIfCancellationRequested();

                    stream.Seek(position, SeekOrigin.Begin);
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);

                    if (bytesRead < searchText.Length) break;

                    var text = encoding.GetString(buffer, 0, bytesRead).ToLower();
                    var idx = 0;
                    while ((idx = text.IndexOf(searchLower, idx, StringComparison.Ordinal)) >= 0
                           && results.Count < maxResults)
                    {
                        results.Add(new HexSearchResult
                        {
                            Offset = position + encoding.GetByteCount(text.Substring(0, idx)),
                            MatchedBytes = buffer.Skip(idx).Take(searchText.Length).ToArray()
                        });
                        idx++;
                    }

                    position += _bufferSize;
                }

                return results;
            }

            return await SearchBytesAsync(filePath, pattern, maxResults, null, ct);
        }

        public async Task<List<HexSearchResult>> SearchHexPatternAsync(string filePath, string hexPattern,
            int maxResults = 100, CancellationToken ct = default)
        {
            hexPattern = hexPattern.Replace(" ", "").Replace("-", "");

            var pattern = new List<byte?>();
            for (int i = 0; i < hexPattern.Length; i += 2)
            {
                var byteStr = hexPattern.Substring(i, 2);
                if (byteStr == "??" || byteStr == "**")
                {
                    pattern.Add(null);
                }
                else
                {
                    pattern.Add(Convert.ToByte(byteStr, 16));
                }
            }

            return await SearchWithWildcardAsync(filePath, pattern.ToArray(), maxResults, ct);
        }

        private async Task<List<HexSearchResult>> SearchWithWildcardAsync(string filePath, byte?[] pattern,
            int maxResults, CancellationToken ct)
        {
            var results = new List<HexSearchResult>();

            using var stream = File.OpenRead(filePath);
            var fileSize = stream.Length;
            var buffer = new byte[_bufferSize + pattern.Length - 1];
            long position = 0;

            while (position < fileSize && results.Count < maxResults)
            {
                ct.ThrowIfCancellationRequested();

                stream.Seek(position, SeekOrigin.Begin);
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);

                if (bytesRead < pattern.Length) break;

                for (int i = 0; i <= bytesRead - pattern.Length && results.Count < maxResults; i++)
                {
                    bool match = true;
                    for (int j = 0; j < pattern.Length; j++)
                    {
                        if (pattern[j].HasValue && buffer[i + j] != pattern[j].Value)
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        results.Add(new HexSearchResult
                        {
                            Offset = position + i,
                            MatchedBytes = buffer.Skip(i).Take(pattern.Length).ToArray()
                        });
                    }
                }

                position += _bufferSize;
            }

            return results;
        }

        public async Task<int> ReplaceAllAsync(string filePath, byte[] search, byte[] replace,
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            if (search.Length != replace.Length)
            {
                LogMessage?.Invoke(this, "Warning: Search and replace patterns have different lengths");
            }

            var results = await SearchBytesAsync(filePath, search, int.MaxValue, null, ct);
            var count = 0;

            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Write, FileShare.Read);

            foreach (var result in results)
            {
                ct.ThrowIfCancellationRequested();

                stream.Seek(result.Offset, SeekOrigin.Begin);
                await stream.WriteAsync(replace, ct);
                count++;

                if (results.Count > 0)
                    progress?.Report((count * 100) / results.Count);
            }

            LogMessage?.Invoke(this, $"Replaced {count} occurrences");
            return count;
        }

        public async Task<bool> ApplyPatchAsync(string filePath, HexPatch patch, bool backup = true,
            CancellationToken ct = default)
        {
            try
            {
                if (backup)
                {
                    var backupPath = filePath + ".backup";
                    File.Copy(filePath, backupPath, true);
                    LogMessage?.Invoke(this, $"Backup created: {backupPath}");
                }

                var currentBytes = await ReadBytesAsync(filePath, patch.Offset, patch.OriginalBytes.Length, ct);
                if (!currentBytes.SequenceEqual(patch.OriginalBytes))
                {
                    LogMessage?.Invoke(this, $"Warning: Original bytes don't match at offset 0x{patch.Offset:X}");
                    LogMessage?.Invoke(this, $"Expected: {BitConverter.ToString(patch.OriginalBytes)}");
                    LogMessage?.Invoke(this, $"Found: {BitConverter.ToString(currentBytes)}");
                    return false;
                }

                await WriteBytesAsync(filePath, patch.Offset, patch.NewBytes, ct);

                LogMessage?.Invoke(this, $"Applied patch: {patch.Description}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Patch error: {ex.Message}");
                return false;
            }
        }

        public async Task<List<HexPatch>> LoadPatchFileAsync(string patchFilePath,
            CancellationToken ct = default)
        {
            var patches = new List<HexPatch>();

            try
            {
                var lines = await File.ReadAllLinesAsync(patchFilePath, ct);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//"))
                        continue;

                    var parts = line.Split(':');
                    if (parts.Length >= 3)
                    {
                        var patch = new HexPatch
                        {
                            Offset = Convert.ToInt64(parts[0].Trim(), 16),
                            OriginalBytes = HexStringToBytes(parts[1].Trim()),
                            NewBytes = HexStringToBytes(parts[2].Trim()),
                            Description = parts.Length > 3 ? parts[3].Trim() : ""
                        };
                        patches.Add(patch);
                    }
                }

                LogMessage?.Invoke(this, $"Loaded {patches.Count} patches from {patchFilePath}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Load patch file error: {ex.Message}");
            }

            return patches;
        }

        public async Task<bool> SavePatchFileAsync(string patchFilePath, List<HexPatch> patches,
            CancellationToken ct = default)
        {
            try
            {
                var lines = new List<string>
                {
                    "# Hex Patch File",
                    "# Format: OFFSET:ORIGINAL:NEW:DESCRIPTION",
                    ""
                };

                foreach (var patch in patches)
                {
                    lines.Add($"0x{patch.Offset:X}:{BytesToHexString(patch.OriginalBytes)}:{BytesToHexString(patch.NewBytes)}:{patch.Description}");
                }

                await File.WriteAllLinesAsync(patchFilePath, lines, ct);
                LogMessage?.Invoke(this, $"Saved {patches.Count} patches to {patchFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Save patch file error: {ex.Message}");
                return false;
            }
        }

        public string FormatHexDump(byte[] data, long baseOffset = 0, int bytesPerLine = 16)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < data.Length; i += bytesPerLine)
            {
                sb.Append($"{baseOffset + i:X8}  ");

                for (int j = 0; j < bytesPerLine; j++)
                {
                    if (i + j < data.Length)
                    {
                        sb.Append($"{data[i + j]:X2} ");
                    }
                    else
                    {
                        sb.Append("   ");
                    }

                    if (j == 7) sb.Append(' ');
                }

                sb.Append(' ');

                for (int j = 0; j < bytesPerLine && i + j < data.Length; j++)
                {
                    var b = data[i + j];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        public async Task<string> GetHexDumpAsync(string filePath, long offset, int length,
            CancellationToken ct = default)
        {
            var data = await ReadBytesAsync(filePath, offset, length, ct);
            return FormatHexDump(data, offset);
        }

        public async Task<List<FileRegion>> AnalyzeFileAsync(string filePath,
            CancellationToken ct = default)
        {
            var regions = new List<FileRegion>();

            try
            {
                using var stream = File.OpenRead(filePath);
                var fileSize = stream.Length;

                var header = new byte[Math.Min(1536, fileSize)];
                await stream.ReadAsync(header, 0, header.Length, ct);

                var fileType = DetectFileType(header);
                regions.Add(new FileRegion
                {
                    StartOffset = 0,
                    EndOffset = fileSize,
                    Description = $"File: {Path.GetFileName(filePath)}",
                    Type = fileType
                });

                switch (fileType)
                {
                    case "Android Boot Image":
                        regions.AddRange(AnalyzeBootImage(header, fileSize));
                        break;
                    case "EXT4 Filesystem":
                        regions.AddRange(AnalyzeExt4(header, fileSize));
                        break;
                    case "GPT Disk":
                        regions.AddRange(AnalyzeGpt(header, fileSize));
                        break;
                }

                LogMessage?.Invoke(this, $"Analyzed {Path.GetFileName(filePath)}: {fileType}");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Analyze error: {ex.Message}");
            }

            return regions;
        }

        private string DetectFileType(byte[] header)
        {
            if (header.Length < 8) return "Unknown";

            // Android boot image
            if (header.Length >= 8 && Encoding.ASCII.GetString(header, 0, 8) == "ANDROID!")
                return "Android Boot Image";

            // EXT4 (magic at offset 1080)
            if (header.Length >= 1082 && header[1080] == 0x53 && header[1081] == 0xEF)
                return "EXT4 Filesystem";

            // GPT (signature at offset 512)
            if (header.Length >= 520)
            {
                try
                {
                    var sig = Encoding.ASCII.GetString(header, 512, 8);
                    if (sig == "EFI PART")
                        return "GPT Disk";
                }
                catch { }
            }

            // Sparse image
            if (header.Length >= 4 && BitConverter.ToUInt32(header, 0) == 0xED26FF3A)
                return "Sparse Image";

            // ZIP
            if (header[0] == 0x50 && header[1] == 0x4B)
                return "ZIP Archive";

            // TAR
            if (header.Length >= 262)
            {
                try
                {
                    var tarMagic = Encoding.ASCII.GetString(header, 257, 5);
                    if (tarMagic == "ustar")
                        return "TAR Archive";
                }
                catch { }
            }

            // LZ4
            if (header.Length >= 4 && header[0] == 0x04 && header[1] == 0x22 && header[2] == 0x4D && header[3] == 0x18)
                return "LZ4 Compressed";

            // GZIP
            if (header[0] == 0x1F && header[1] == 0x8B)
                return "GZIP Compressed";

            // F2FS (magic at offset 1024)
            if (header.Length >= 1028 && BitConverter.ToUInt32(header, 1024) == 0xF2F52010)
                return "F2FS Filesystem";

            // EROFS
            if (header.Length >= 1028 && BitConverter.ToUInt32(header, 1024) == 0xE0F5E1E2)
                return "EROFS Filesystem";

            return "Unknown";
        }

        private List<FileRegion> AnalyzeBootImage(byte[] header, long fileSize)
        {
            var regions = new List<FileRegion>();

            if (header.Length < 64) return regions;

            var kernelSize = BitConverter.ToUInt32(header, 8);
            var ramdiskSize = BitConverter.ToUInt32(header, 16);
            var pageSize = BitConverter.ToUInt32(header, 36);

            if (pageSize == 0) pageSize = 4096;

            regions.Add(new FileRegion
            {
                StartOffset = 0,
                EndOffset = pageSize,
                Description = "Boot Header",
                Type = "Header"
            });

            var kernelOffset = pageSize;
            regions.Add(new FileRegion
            {
                StartOffset = kernelOffset,
                EndOffset = kernelOffset + kernelSize,
                Description = $"Kernel ({kernelSize} bytes)",
                Type = "Kernel"
            });

            var kernelPages = (kernelSize + pageSize - 1) / pageSize;
            var ramdiskOffset = (long)(kernelOffset + kernelPages * pageSize);
            regions.Add(new FileRegion
            {
                StartOffset = ramdiskOffset,
                EndOffset = ramdiskOffset + ramdiskSize,
                Description = $"Ramdisk ({ramdiskSize} bytes)",
                Type = "Ramdisk"
            });

            return regions;
        }

        private List<FileRegion> AnalyzeExt4(byte[] header, long fileSize)
        {
            return new List<FileRegion>
            {
                new FileRegion
                {
                    StartOffset = 0,
                    EndOffset = 1024,
                    Description = "Boot Sector (Reserved)",
                    Type = "Reserved"
                },
                new FileRegion
                {
                    StartOffset = 1024,
                    EndOffset = 2048,
                    Description = "Superblock",
                    Type = "Superblock"
                },
                new FileRegion
                {
                    StartOffset = 2048,
                    EndOffset = 4096,
                    Description = "Group Descriptors",
                    Type = "Metadata"
                }
            };
        }

        private List<FileRegion> AnalyzeGpt(byte[] header, long fileSize)
        {
            return new List<FileRegion>
            {
                new FileRegion
                {
                    StartOffset = 0,
                    EndOffset = 512,
                    Description = "Protective MBR",
                    Type = "MBR"
                },
                new FileRegion
                {
                    StartOffset = 512,
                    EndOffset = 1024,
                    Description = "GPT Header",
                    Type = "GPT Header"
                },
                new FileRegion
                {
                    StartOffset = 1024,
                    EndOffset = 17408,
                    Description = "Partition Entries (128 x 128 bytes)",
                    Type = "GPT Entries"
                }
            };
        }

        public long GetFileSize(string filePath)
        {
            return new FileInfo(filePath).Length;
        }

        public async Task<byte[]> CalculateChecksumAsync(string filePath, string algorithm = "MD5",
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            using var hashAlgorithm = algorithm.ToUpper() switch
            {
                "SHA1" => (System.Security.Cryptography.HashAlgorithm)
                    System.Security.Cryptography.SHA1.Create(),
                "SHA256" => System.Security.Cryptography.SHA256.Create(),
                "SHA512" => System.Security.Cryptography.SHA512.Create(),
                _ => System.Security.Cryptography.MD5.Create()
            };

            using var stream = File.OpenRead(filePath);
            var fileSize = stream.Length;
            var buffer = new byte[_bufferSize];
            long bytesRead = 0;

            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                hashAlgorithm.TransformBlock(buffer, 0, read, buffer, 0);
                bytesRead += read;
                progress?.Report((int)((bytesRead * 100) / fileSize));
            }

            hashAlgorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return hashAlgorithm.Hash ?? Array.Empty<byte>();
        }

        public async Task<bool> CompareFilesAsync(string file1, string file2,
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                var info1 = new FileInfo(file1);
                var info2 = new FileInfo(file2);

                if (info1.Length != info2.Length)
                {
                    LogMessage?.Invoke(this, $"Files have different sizes: {info1.Length} vs {info2.Length}");
                    return false;
                }

                using var stream1 = File.OpenRead(file1);
                using var stream2 = File.OpenRead(file2);

                var buffer1 = new byte[_bufferSize];
                var buffer2 = new byte[_bufferSize];
                long position = 0;

                while (position < info1.Length)
                {
                    ct.ThrowIfCancellationRequested();

                    var read1 = await stream1.ReadAsync(buffer1, 0, buffer1.Length, ct);
                    var read2 = await stream2.ReadAsync(buffer2, 0, buffer2.Length, ct);

                    if (read1 != read2)
                    {
                        LogMessage?.Invoke(this, $"Read mismatch at position {position}");
                        return false;
                    }

                    for (int i = 0; i < read1; i++)
                    {
                        if (buffer1[i] != buffer2[i])
                        {
                            LogMessage?.Invoke(this, $"Difference at offset 0x{position + i:X}: 0x{buffer1[i]:X2} vs 0x{buffer2[i]:X2}");
                            return false;
                        }
                    }

                    position += read1;
                    progress?.Report((int)((position * 100) / info1.Length));
                }

                LogMessage?.Invoke(this, "Files are identical");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Compare error: {ex.Message}");
                return false;
            }
        }

        public async Task<List<long>> FindDifferencesAsync(string file1, string file2,
            int maxDifferences = 100, CancellationToken ct = default)
        {
            var differences = new List<long>();

            try
            {
                using var stream1 = File.OpenRead(file1);
                using var stream2 = File.OpenRead(file2);

                var minLength = Math.Min(stream1.Length, stream2.Length);
                var buffer1 = new byte[_bufferSize];
                var buffer2 = new byte[_bufferSize];
                long position = 0;

                while (position < minLength && differences.Count < maxDifferences)
                {
                    ct.ThrowIfCancellationRequested();

                    var toRead = (int)Math.Min(_bufferSize, minLength - position);
                    var read1 = await stream1.ReadAsync(buffer1, 0, toRead, ct);
                    var read2 = await stream2.ReadAsync(buffer2, 0, toRead, ct);

                    for (int i = 0; i < read1 && differences.Count < maxDifferences; i++)
                    {
                        if (buffer1[i] != buffer2[i])
                        {
                            differences.Add(position + i);
                        }
                    }

                    position += read1;
                }

                LogMessage?.Invoke(this, $"Found {differences.Count} differences");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Find differences error: {ex.Message}");
            }

            return differences;
        }

        private byte[] HexStringToBytes(string hex)
        {
            hex = hex.Replace(" ", "").Replace("-", "");
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        private string BytesToHexString(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        // Compatibility methods for MainViewModel
        private byte[]? _loadedData;
        private string _loadedFilePath = "";
        private List<(long offset, byte oldValue, byte newValue)> _modifications = new();

        public async Task<Models.HexData?> LoadFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                _loadedFilePath = filePath;
                var fileInfo = new FileInfo(filePath);

                // Read first chunk for display
                var chunkSize = Math.Min(1024 * 1024, fileInfo.Length); // 1MB max initially
                _loadedData = await ReadBytesAsync(filePath, 0, (int)chunkSize);

                var hexData = new Models.HexData
                {
                    FilePath = filePath,
                    FileSize = fileInfo.Length,
                    RawData = _loadedData
                };

                // Generate hex lines
                for (long offset = 0; offset < _loadedData.Length; offset += 16)
                {
                    var lineBytes = new byte[16];
                    var validBytes = Math.Min(16, _loadedData.Length - (int)offset);
                    Array.Copy(_loadedData, offset, lineBytes, 0, validBytes);

                    hexData.Lines.Add(new Models.HexLine
                    {
                        Offset = offset,
                        Bytes = lineBytes,
                        ValidBytes = validBytes
                    });
                }

                LogMessage?.Invoke(this, $"Loaded file: {filePath} ({fileInfo.Length} bytes)");
                return hexData;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Load error: {ex.Message}");
                return null;
            }
        }

        public async Task<Models.RomAnalysisResult> AnalyzeRomAsync(string filePath)
        {
            var result = new Models.RomAnalysisResult
            {
                FileName = Path.GetFileName(filePath),
                FileSize = new FileInfo(filePath).Length
            };

            var regions = await AnalyzeFileAsync(filePath);
            if (regions.Count > 0)
            {
                result.FileType = regions[0].Type;
            }

            // Calculate checksum
            var hash = await CalculateChecksumAsync(filePath, "MD5");
            result.Checksum = BitConverter.ToString(hash).Replace("-", "");

            return result;
        }

        public List<Models.HexSearchResult> SearchHexString(string hexPattern)
        {
            if (string.IsNullOrEmpty(_loadedFilePath) || _loadedData == null)
                return new List<Models.HexSearchResult>();

            var results = new List<Models.HexSearchResult>();
            var pattern = HexStringToBytes(hexPattern.Replace(" ", ""));

            for (int i = 0; i <= _loadedData.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (_loadedData[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    results.Add(new Models.HexSearchResult
                    {
                        Offset = i,
                        MatchedBytes = _loadedData.Skip(i).Take(pattern.Length).ToArray()
                    });
                }
            }

            return results;
        }

        public List<Models.HexSearchResult> SearchString(string searchText)
        {
            if (string.IsNullOrEmpty(_loadedFilePath) || _loadedData == null)
                return new List<Models.HexSearchResult>();

            var results = new List<Models.HexSearchResult>();
            var pattern = Encoding.UTF8.GetBytes(searchText);

            for (int i = 0; i <= _loadedData.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (_loadedData[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    results.Add(new Models.HexSearchResult
                    {
                        Offset = i,
                        MatchedBytes = _loadedData.Skip(i).Take(pattern.Length).ToArray()
                    });
                }
            }

            return results;
        }

        public System.Collections.ObjectModel.ObservableCollection<Models.HexLine> GetLinesInRange(long startOffset, long endOffset)
        {
            var lines = new System.Collections.ObjectModel.ObservableCollection<Models.HexLine>();

            if (_loadedData == null) return lines;

            startOffset = Math.Max(0, startOffset);
            endOffset = Math.Min(_loadedData.Length, endOffset);

            for (long offset = startOffset - (startOffset % 16); offset < endOffset; offset += 16)
            {
                if (offset < 0 || offset >= _loadedData.Length) continue;

                var lineBytes = new byte[16];
                var validBytes = (int)Math.Min(16, _loadedData.Length - offset);
                Array.Copy(_loadedData, offset, lineBytes, 0, validBytes);

                lines.Add(new Models.HexLine
                {
                    Offset = offset,
                    Bytes = lineBytes,
                    ValidBytes = validBytes
                });
            }

            return lines;
        }

        public async Task SaveFileAsync(string outputPath)
        {
            if (_loadedData == null) return;

            await File.WriteAllBytesAsync(outputPath, _loadedData);
            LogMessage?.Invoke(this, $"File saved: {outputPath}");
        }

        public async Task ExportAnalysisAsync(string inputPath, string outputPath)
        {
            var analysis = await AnalyzeRomAsync(inputPath);

            var sb = new StringBuilder();
            sb.AppendLine("ROM Analysis Report");
            sb.AppendLine("==================");
            sb.AppendLine($"File: {analysis.FileName}");
            sb.AppendLine($"Size: {analysis.FileSize} bytes");
            sb.AppendLine($"Type: {analysis.FileType}");
            sb.AppendLine($"MD5: {analysis.Checksum}");

            await File.WriteAllTextAsync(outputPath, sb.ToString());
            LogMessage?.Invoke(this, $"Analysis exported: {outputPath}");
        }

        public async Task ExportHexDumpAsync(string outputPath)
        {
            if (_loadedData == null) return;

            var hexDump = FormatHexDump(_loadedData, 0);
            await File.WriteAllTextAsync(outputPath, hexDump);
            LogMessage?.Invoke(this, $"Hex dump exported: {outputPath}");
        }

        public void UndoLastModification()
        {
            if (_modifications.Count == 0 || _loadedData == null) return;

            var last = _modifications[^1];
            _loadedData[last.offset] = last.oldValue;
            _modifications.RemoveAt(_modifications.Count - 1);

            LogMessage?.Invoke(this, $"Undid modification at offset 0x{last.offset:X}");
        }

        public void UndoAllModifications()
        {
            if (_loadedData == null) return;

            foreach (var mod in _modifications)
            {
                _loadedData[mod.offset] = mod.oldValue;
            }
            _modifications.Clear();

            LogMessage?.Invoke(this, "Undid all modifications");
        }

        public bool ModifyByte(long offset, byte newValue)
        {
            if (_loadedData == null || offset < 0 || offset >= _loadedData.Length)
                return false;

            var oldValue = _loadedData[offset];
            _modifications.Add((offset, oldValue, newValue));
            _loadedData[offset] = newValue;

            LogMessage?.Invoke(this, $"Modified byte at 0x{offset:X}: 0x{oldValue:X2} -> 0x{newValue:X2}");
            return true;
        }
    }
}
