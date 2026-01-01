using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class GptHeader
    {
        public string Signature { get; set; } = "";
        public uint Revision { get; set; }
        public uint HeaderSize { get; set; }
        public uint Crc32 { get; set; }
        public ulong CurrentLba { get; set; }
        public ulong BackupLba { get; set; }
        public ulong FirstUsableLba { get; set; }
        public ulong LastUsableLba { get; set; }
        public Guid DiskGuid { get; set; }
        public ulong PartitionEntriesLba { get; set; }
        public uint PartitionCount { get; set; }
        public uint PartitionEntrySize { get; set; }
        public uint PartitionEntriesCrc32 { get; set; }
    }

    public class GptPartition
    {
        public Guid TypeGuid { get; set; }
        public Guid UniqueGuid { get; set; }
        public ulong FirstLba { get; set; }
        public ulong LastLba { get; set; }
        public ulong Attributes { get; set; }
        public string Name { get; set; } = "";
        public int Index { get; set; }

        public ulong SizeInSectors => LastLba - FirstLba + 1;
        public ulong SizeInBytes => SizeInSectors * 512;
        public string SizeFormatted => FormatSize((long)SizeInBytes);

        // Common partition type GUIDs
        public string TypeName
        {
            get
            {
                var knownTypes = new Dictionary<string, string>
                {
                    { "0FC63DAF-8483-4772-8E79-3D69D8477DE4", "Linux filesystem" },
                    { "EBD0A0A2-B9E5-4433-87C0-68B6B72699C7", "Windows Basic Data" },
                    { "C12A7328-F81F-11D2-BA4B-00A0C93EC93B", "EFI System" },
                    { "21686148-6449-6E6F-744E-656564454649", "BIOS Boot" },
                    { "E3C9E316-0B5C-4DB8-817D-F92DF00215AE", "Microsoft Reserved" },
                    { "DE94BBA4-06D1-4D40-A16A-BFD50179D6AC", "Windows Recovery" },
                    { "9E1A2D38-C612-4316-AA26-8B49521E5A8B", "Android Boot" },
                    { "38F428E6-D326-425D-9140-6E0EA133647C", "Android Recovery" },
                    { "A893EF21-E428-470A-9E55-0668FD91A2D9", "Android System" },
                    { "DC76DDA9-5AC1-491C-AF42-A82591580C0D", "Android Data" },
                    { "EF32A33B-A409-486C-9141-9FFB711F6266", "Android Metadata" },
                    { "20AC26BE-20B7-11E3-84C5-6CFDB94711E9", "Android Misc" },
                    { "57C83E7B-7E61-11E3-8C54-FC5D3FA27E8E", "Android Vendor" },
                    { "193D1EA4-B3CA-11E4-B075-10604B889DCF", "Android Cache" }
                };

                var typeStr = TypeGuid.ToString().ToUpper();
                return knownTypes.TryGetValue(typeStr, out var name) ? name : "Unknown";
            }
        }

        private static string FormatSize(long bytes)
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

    public class Ext4SuperBlock
    {
        public uint InodesCount { get; set; }
        public uint BlocksCountLo { get; set; }
        public uint BlocksCountHi { get; set; }
        public uint FreeBlocksCountLo { get; set; }
        public uint FreeInodesCount { get; set; }
        public uint FirstDataBlock { get; set; }
        public uint LogBlockSize { get; set; }
        public uint BlocksPerGroup { get; set; }
        public uint InodesPerGroup { get; set; }
        public uint MountTime { get; set; }
        public uint WriteTime { get; set; }
        public ushort MagicNumber { get; set; }
        public ushort State { get; set; }
        public string VolumeName { get; set; } = "";
        public Guid Uuid { get; set; }

        public ulong TotalBlocks => ((ulong)BlocksCountHi << 32) | BlocksCountLo;
        public uint BlockSize => 1024u << (int)LogBlockSize;
        public ulong TotalSize => TotalBlocks * BlockSize;
        public string TotalSizeFormatted => FormatSize((long)TotalSize);

        public bool IsValid => MagicNumber == 0xEF53;

        private static string FormatSize(long bytes)
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

    public class F2fsSuperBlock
    {
        public uint Magic { get; set; }
        public ushort MajorVersion { get; set; }
        public ushort MinorVersion { get; set; }
        public uint LogSectorSize { get; set; }
        public uint LogSectorsPerBlock { get; set; }
        public uint LogBlockSize { get; set; }
        public uint LogBlocksPerSeg { get; set; }
        public uint SegsPerSec { get; set; }
        public uint SecsPerZone { get; set; }
        public ulong BlockCount { get; set; }
        public ulong SectionCount { get; set; }
        public ulong SegmentCount { get; set; }
        public string VolumeName { get; set; } = "";
        public Guid Uuid { get; set; }

        public uint BlockSize => 1u << (int)LogBlockSize;
        public ulong TotalSize => BlockCount * BlockSize;
        public string TotalSizeFormatted => FormatSize((long)TotalSize);

        public bool IsValid => Magic == 0xF2F52010;

        private static string FormatSize(long bytes)
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

    public class PartitionToolService
    {
        private readonly string _adbPath;
        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        private const string GPT_SIGNATURE = "EFI PART";
        private const int SECTOR_SIZE = 512;
        private const int GPT_HEADER_SIZE = 92;
        private const int GPT_ENTRY_SIZE = 128;

        public PartitionToolService(string adbPath)
        {
            _adbPath = adbPath;
        }

        public async Task<GptHeader?> ReadGptHeaderAsync(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    LogMessage?.Invoke(this, $"File not found: {imagePath}");
                    return null;
                }

                using var stream = File.OpenRead(imagePath);
                using var reader = new BinaryReader(stream);

                // Skip MBR (first sector) and read GPT header
                stream.Seek(SECTOR_SIZE, SeekOrigin.Begin);

                var signatureBytes = reader.ReadBytes(8);
                var signature = Encoding.ASCII.GetString(signatureBytes);

                if (signature != GPT_SIGNATURE)
                {
                    LogMessage?.Invoke(this, "Invalid GPT signature");
                    return null;
                }

                var header = new GptHeader
                {
                    Signature = signature,
                    Revision = reader.ReadUInt32(),
                    HeaderSize = reader.ReadUInt32(),
                    Crc32 = reader.ReadUInt32()
                };

                reader.ReadUInt32(); // Reserved

                header.CurrentLba = reader.ReadUInt64();
                header.BackupLba = reader.ReadUInt64();
                header.FirstUsableLba = reader.ReadUInt64();
                header.LastUsableLba = reader.ReadUInt64();

                var guidBytes = reader.ReadBytes(16);
                header.DiskGuid = new Guid(guidBytes);

                header.PartitionEntriesLba = reader.ReadUInt64();
                header.PartitionCount = reader.ReadUInt32();
                header.PartitionEntrySize = reader.ReadUInt32();
                header.PartitionEntriesCrc32 = reader.ReadUInt32();

                LogMessage?.Invoke(this, $"GPT Header: {header.PartitionCount} partitions, Disk GUID: {header.DiskGuid}");

                return header;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error reading GPT header: {ex.Message}");
                return null;
            }
        }

        public async Task<List<GptPartition>> ReadGptPartitionsAsync(string imagePath)
        {
            var partitions = new List<GptPartition>();

            try
            {
                var header = await ReadGptHeaderAsync(imagePath);
                if (header == null) return partitions;

                using var stream = File.OpenRead(imagePath);
                using var reader = new BinaryReader(stream);

                // Seek to partition entries (usually LBA 2)
                stream.Seek((long)(header.PartitionEntriesLba * SECTOR_SIZE), SeekOrigin.Begin);

                for (int i = 0; i < header.PartitionCount; i++)
                {
                    var typeGuidBytes = reader.ReadBytes(16);
                    var typeGuid = new Guid(typeGuidBytes);

                    // Skip empty entries
                    if (typeGuid == Guid.Empty)
                    {
                        reader.ReadBytes((int)header.PartitionEntrySize - 16);
                        continue;
                    }

                    var partition = new GptPartition
                    {
                        Index = i + 1,
                        TypeGuid = typeGuid
                    };

                    var uniqueGuidBytes = reader.ReadBytes(16);
                    partition.UniqueGuid = new Guid(uniqueGuidBytes);

                    partition.FirstLba = reader.ReadUInt64();
                    partition.LastLba = reader.ReadUInt64();
                    partition.Attributes = reader.ReadUInt64();

                    var nameBytes = reader.ReadBytes(72);
                    partition.Name = Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');

                    // Skip remaining bytes in entry
                    var remaining = (int)header.PartitionEntrySize - 128;
                    if (remaining > 0) reader.ReadBytes(remaining);

                    partitions.Add(partition);
                    LogMessage?.Invoke(this, $"  {partition.Index}: {partition.Name} ({partition.TypeName}) - {partition.SizeFormatted}");
                }

                LogMessage?.Invoke(this, $"Found {partitions.Count} partitions");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error reading partitions: {ex.Message}");
            }

            return partitions;
        }

        public async Task<bool> ExtractPartitionAsync(string imagePath, string partitionName, string outputPath,
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                var partitions = await ReadGptPartitionsAsync(imagePath);
                var partition = partitions.FirstOrDefault(p =>
                    p.Name.Equals(partitionName, StringComparison.OrdinalIgnoreCase));

                if (partition == null)
                {
                    LogMessage?.Invoke(this, $"Partition not found: {partitionName}");
                    return false;
                }

                LogMessage?.Invoke(this, $"Extracting {partition.Name} ({partition.SizeFormatted})...");

                using var input = File.OpenRead(imagePath);
                using var output = File.Create(outputPath);

                input.Seek((long)(partition.FirstLba * SECTOR_SIZE), SeekOrigin.Begin);

                var totalBytes = (long)partition.SizeInBytes;
                long bytesWritten = 0;
                var buffer = new byte[1024 * 1024]; // 1MB buffer

                while (bytesWritten < totalBytes)
                {
                    ct.ThrowIfCancellationRequested();

                    var toRead = (int)Math.Min(buffer.Length, totalBytes - bytesWritten);
                    var read = await input.ReadAsync(buffer.AsMemory(0, toRead), ct);
                    if (read == 0) break;

                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    bytesWritten += read;

                    var pct = (int)((bytesWritten * 100) / totalBytes);
                    progress?.Report(pct);
                    ProgressChanged?.Invoke(this, pct);
                }

                LogMessage?.Invoke(this, $"Extracted to: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Extract error: {ex.Message}");
                return false;
            }
        }

        public async Task<Ext4SuperBlock?> ReadExt4SuperBlockAsync(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    LogMessage?.Invoke(this, $"File not found: {imagePath}");
                    return null;
                }

                using var stream = File.OpenRead(imagePath);
                using var reader = new BinaryReader(stream);

                // EXT4 superblock is at offset 1024
                stream.Seek(1024, SeekOrigin.Begin);

                var sb = new Ext4SuperBlock
                {
                    InodesCount = reader.ReadUInt32(),
                    BlocksCountLo = reader.ReadUInt32()
                };

                reader.ReadUInt32(); // Reserved blocks count
                sb.FreeBlocksCountLo = reader.ReadUInt32();
                sb.FreeInodesCount = reader.ReadUInt32();
                sb.FirstDataBlock = reader.ReadUInt32();
                sb.LogBlockSize = reader.ReadUInt32();

                reader.ReadUInt32(); // Log fragment size
                sb.BlocksPerGroup = reader.ReadUInt32();
                reader.ReadUInt32(); // Fragments per group
                sb.InodesPerGroup = reader.ReadUInt32();
                sb.MountTime = reader.ReadUInt32();
                sb.WriteTime = reader.ReadUInt32();

                reader.ReadUInt16(); // Mount count
                reader.ReadUInt16(); // Max mount count
                sb.MagicNumber = reader.ReadUInt16();
                sb.State = reader.ReadUInt16();

                if (!sb.IsValid)
                {
                    LogMessage?.Invoke(this, "Invalid EXT4 magic number");
                    return null;
                }

                // Read more fields...
                stream.Seek(1024 + 120, SeekOrigin.Begin); // Volume name offset
                var volumeNameBytes = reader.ReadBytes(16);
                sb.VolumeName = Encoding.ASCII.GetString(volumeNameBytes).TrimEnd('\0');

                // UUID at offset 104 from superblock start
                stream.Seek(1024 + 104, SeekOrigin.Begin);
                var uuidBytes = reader.ReadBytes(16);
                sb.Uuid = new Guid(uuidBytes);

                // Blocks count high bits at offset 336
                stream.Seek(1024 + 336, SeekOrigin.Begin);
                sb.BlocksCountHi = reader.ReadUInt32();

                LogMessage?.Invoke(this, $"EXT4: {sb.VolumeName}, Size: {sb.TotalSizeFormatted}, Block Size: {sb.BlockSize}");

                return sb;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error reading EXT4 superblock: {ex.Message}");
                return null;
            }
        }

        public async Task<F2fsSuperBlock?> ReadF2fsSuperBlockAsync(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    LogMessage?.Invoke(this, $"File not found: {imagePath}");
                    return null;
                }

                using var stream = File.OpenRead(imagePath);
                using var reader = new BinaryReader(stream);

                // F2FS superblock is at offset 1024
                stream.Seek(1024, SeekOrigin.Begin);

                var sb = new F2fsSuperBlock
                {
                    Magic = reader.ReadUInt32()
                };

                if (!sb.IsValid)
                {
                    LogMessage?.Invoke(this, "Invalid F2FS magic number");
                    return null;
                }

                sb.MajorVersion = reader.ReadUInt16();
                sb.MinorVersion = reader.ReadUInt16();

                sb.LogSectorSize = reader.ReadUInt32();
                sb.LogSectorsPerBlock = reader.ReadUInt32();
                sb.LogBlockSize = reader.ReadUInt32();
                sb.LogBlocksPerSeg = reader.ReadUInt32();
                sb.SegsPerSec = reader.ReadUInt32();
                sb.SecsPerZone = reader.ReadUInt32();

                reader.ReadUInt32(); // checksum_offset
                sb.BlockCount = reader.ReadUInt64();
                sb.SectionCount = reader.ReadUInt64();
                sb.SegmentCount = reader.ReadUInt64();

                // Skip to volume name (offset 1110 from start of superblock)
                stream.Seek(1024 + 1110, SeekOrigin.Begin);
                var volumeNameBytes = reader.ReadBytes(512);
                sb.VolumeName = Encoding.Unicode.GetString(volumeNameBytes).TrimEnd('\0');

                // UUID is at offset 1622
                stream.Seek(1024 + 1622, SeekOrigin.Begin);
                var uuidBytes = reader.ReadBytes(16);
                sb.Uuid = new Guid(uuidBytes);

                LogMessage?.Invoke(this, $"F2FS v{sb.MajorVersion}.{sb.MinorVersion}: {sb.VolumeName}, Size: {sb.TotalSizeFormatted}");

                return sb;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error reading F2FS superblock: {ex.Message}");
                return null;
            }
        }

        public async Task<string> DetectFileSystemAsync(string imagePath)
        {
            try
            {
                using var stream = File.OpenRead(imagePath);
                using var reader = new BinaryReader(stream);

                // Check for EXT4 (magic at offset 1024+56)
                stream.Seek(1024 + 56, SeekOrigin.Begin);
                var ext4Magic = reader.ReadUInt16();
                if (ext4Magic == 0xEF53)
                {
                    return "EXT4";
                }

                // Check for F2FS (magic at offset 1024)
                stream.Seek(1024, SeekOrigin.Begin);
                var f2fsMagic = reader.ReadUInt32();
                if (f2fsMagic == 0xF2F52010)
                {
                    return "F2FS";
                }

                // Check for EROFS (magic at offset 1024)
                stream.Seek(1024, SeekOrigin.Begin);
                var erofsMagic = reader.ReadUInt32();
                if (erofsMagic == 0xE0F5E1E2)
                {
                    return "EROFS";
                }

                // Check for sparse image
                stream.Seek(0, SeekOrigin.Begin);
                var sparseHeader = reader.ReadUInt32();
                if (sparseHeader == 0xED26FF3A)
                {
                    return "SPARSE";
                }

                // Check GPT
                stream.Seek(512, SeekOrigin.Begin);
                var gptSignature = reader.ReadBytes(8);
                if (Encoding.ASCII.GetString(gptSignature) == "EFI PART")
                {
                    return "GPT";
                }

                return "UNKNOWN";
            }
            catch
            {
                return "ERROR";
            }
        }

        public async Task<bool> ConvertSparseToRawAsync(string sparsePath, string rawPath,
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                if (!File.Exists(sparsePath))
                {
                    LogMessage?.Invoke(this, $"File not found: {sparsePath}");
                    return false;
                }

                using var input = File.OpenRead(sparsePath);
                using var reader = new BinaryReader(input);

                // Read sparse header
                var magic = reader.ReadUInt32();
                if (magic != 0xED26FF3A)
                {
                    LogMessage?.Invoke(this, "Not a sparse image");
                    return false;
                }

                var majorVersion = reader.ReadUInt16();
                var minorVersion = reader.ReadUInt16();
                var fileHeaderSize = reader.ReadUInt16();
                var chunkHeaderSize = reader.ReadUInt16();
                var blockSize = reader.ReadUInt32();
                var totalBlocks = reader.ReadUInt32();
                var totalChunks = reader.ReadUInt32();

                LogMessage?.Invoke(this, $"Sparse image: {totalBlocks} blocks, {totalChunks} chunks, block size: {blockSize}");

                using var output = File.Create(rawPath);

                var zeroBuffer = new byte[blockSize];
                var dataBuffer = new byte[blockSize];

                for (uint chunk = 0; chunk < totalChunks; chunk++)
                {
                    ct.ThrowIfCancellationRequested();

                    var chunkType = reader.ReadUInt16();
                    reader.ReadUInt16(); // Reserved
                    var chunkBlocks = reader.ReadUInt32();
                    var totalBytes = reader.ReadUInt32();

                    switch (chunkType)
                    {
                        case 0xCAC1: // RAW
                            for (uint b = 0; b < chunkBlocks; b++)
                            {
                                var read = await input.ReadAsync(dataBuffer, ct);
                                await output.WriteAsync(dataBuffer.AsMemory(0, read), ct);
                            }
                            break;

                        case 0xCAC2: // FILL
                            var fillValue = reader.ReadBytes(4);
                            for (uint b = 0; b < chunkBlocks; b++)
                            {
                                for (int i = 0; i < blockSize; i += 4)
                                {
                                    await output.WriteAsync(fillValue, ct);
                                }
                            }
                            break;

                        case 0xCAC3: // DONT_CARE (holes)
                            output.Seek((long)(chunkBlocks * blockSize), SeekOrigin.Current);
                            break;

                        case 0xCAC4: // CRC32
                            reader.ReadUInt32(); // CRC value
                            break;
                    }

                    progress?.Report((int)((chunk * 100) / totalChunks));
                }

                LogMessage?.Invoke(this, $"Converted to raw: {rawPath}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Convert error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CreateGptImageAsync(string outputPath, long sizeInBytes,
            List<(string name, long size, Guid typeGuid)> partitions,
            CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, $"Creating GPT image: {sizeInBytes / (1024 * 1024)} MB");

                using var stream = File.Create(outputPath);

                // Calculate total sectors
                var totalSectors = sizeInBytes / SECTOR_SIZE;
                var partitionEntryCount = 128u;
                var partitionEntrySectors = (partitionEntryCount * GPT_ENTRY_SIZE) / SECTOR_SIZE;

                // Write protective MBR
                stream.SetLength(sizeInBytes);
                stream.Seek(0, SeekOrigin.Begin);

                var mbr = new byte[SECTOR_SIZE];
                // Boot signature
                mbr[510] = 0x55;
                mbr[511] = 0xAA;

                // Protective MBR partition entry at offset 446
                mbr[446 + 0] = 0x00; // Not bootable
                mbr[446 + 4] = 0xEE; // GPT protective
                // Start LBA
                mbr[446 + 8] = 0x01;
                mbr[446 + 9] = 0x00;
                mbr[446 + 10] = 0x00;
                mbr[446 + 11] = 0x00;
                // Sectors
                var protectiveSectors = Math.Min((uint)totalSectors - 1, 0xFFFFFFFF);
                BitConverter.GetBytes(protectiveSectors).CopyTo(mbr, 446 + 12);

                await stream.WriteAsync(mbr, ct);

                // Write GPT header
                var diskGuid = Guid.NewGuid();
                var firstUsableLba = 34ul; // After GPT header + entries
                var lastUsableLba = (ulong)totalSectors - 34;

                var gptHeader = new byte[SECTOR_SIZE];
                Encoding.ASCII.GetBytes(GPT_SIGNATURE).CopyTo(gptHeader, 0);
                BitConverter.GetBytes(0x00010000u).CopyTo(gptHeader, 8); // Revision
                BitConverter.GetBytes((uint)GPT_HEADER_SIZE).CopyTo(gptHeader, 12); // Header size
                // CRC32 placeholder at offset 16
                BitConverter.GetBytes(1ul).CopyTo(gptHeader, 24); // Current LBA
                BitConverter.GetBytes((ulong)totalSectors - 1).CopyTo(gptHeader, 32); // Backup LBA
                BitConverter.GetBytes(firstUsableLba).CopyTo(gptHeader, 40); // First usable LBA
                BitConverter.GetBytes(lastUsableLba).CopyTo(gptHeader, 48); // Last usable LBA
                diskGuid.ToByteArray().CopyTo(gptHeader, 56); // Disk GUID
                BitConverter.GetBytes(2ul).CopyTo(gptHeader, 72); // Partition entries LBA
                BitConverter.GetBytes(partitionEntryCount).CopyTo(gptHeader, 80); // Number of entries
                BitConverter.GetBytes((uint)GPT_ENTRY_SIZE).CopyTo(gptHeader, 84); // Entry size

                stream.Seek(SECTOR_SIZE, SeekOrigin.Begin);
                await stream.WriteAsync(gptHeader, ct);

                // Write partition entries
                stream.Seek(2 * SECTOR_SIZE, SeekOrigin.Begin);
                var currentLba = firstUsableLba;

                foreach (var (name, size, typeGuid) in partitions)
                {
                    var entry = new byte[GPT_ENTRY_SIZE];
                    var partitionGuid = Guid.NewGuid();
                    var partitionSectors = (ulong)(size / SECTOR_SIZE);

                    typeGuid.ToByteArray().CopyTo(entry, 0);
                    partitionGuid.ToByteArray().CopyTo(entry, 16);
                    BitConverter.GetBytes(currentLba).CopyTo(entry, 32);
                    BitConverter.GetBytes(currentLba + partitionSectors - 1).CopyTo(entry, 40);
                    // Attributes at 48
                    Encoding.Unicode.GetBytes(name).CopyTo(entry, 56);

                    await stream.WriteAsync(entry, ct);

                    currentLba += partitionSectors;
                    LogMessage?.Invoke(this, $"Added partition: {name} ({size / (1024 * 1024)} MB)");
                }

                LogMessage?.Invoke(this, $"Created GPT image: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Create GPT error: {ex.Message}");
                return false;
            }
        }

        public async Task<List<GptPartition>> ReadDevicePartitionsViaAdbAsync(string blockDevice = "/dev/block/sda",
            CancellationToken ct = default)
        {
            var partitions = new List<GptPartition>();

            try
            {
                LogMessage?.Invoke(this, $"Reading partitions from device: {blockDevice}");

                // Pull GPT from device
                var tempFile = Path.GetTempFileName();

                try
                {
                    // Read first 34 sectors (MBR + GPT header + partition entries)
                    var result = await RunAdbCommandAsync(
                        $"shell su -c 'dd if={blockDevice} bs=512 count=34 2>/dev/null' > \"{tempFile}\"",
                        TimeSpan.FromMinutes(1), ct);

                    if (File.Exists(tempFile) && new FileInfo(tempFile).Length > 0)
                    {
                        partitions = await ReadGptPartitionsAsync(tempFile);
                    }
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error reading device partitions: {ex.Message}");
            }

            return partitions;
        }

        private async Task<string> RunAdbCommandAsync(string command, TimeSpan timeout, CancellationToken ct)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return "";

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);

                var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);

                return output;
            }
            catch
            {
                return "";
            }
        }
    }
}
