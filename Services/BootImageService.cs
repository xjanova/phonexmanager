using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class BootImageHeader
    {
        public string Magic { get; set; } = "";
        public uint KernelSize { get; set; }
        public uint KernelAddress { get; set; }
        public uint RamdiskSize { get; set; }
        public uint RamdiskAddress { get; set; }
        public uint SecondSize { get; set; }
        public uint SecondAddress { get; set; }
        public uint TagsAddress { get; set; }
        public uint PageSize { get; set; }
        public uint HeaderVersion { get; set; }
        public uint OsVersion { get; set; }
        public string Name { get; set; } = "";
        public string Cmdline { get; set; } = "";
        public byte[] Id { get; set; } = new byte[32];
        public string ExtraCmdline { get; set; } = "";

        // V1+ fields
        public uint RecoveryDtboSize { get; set; }
        public ulong RecoveryDtboOffset { get; set; }
        public uint HeaderSize { get; set; }

        // V2+ fields
        public uint DtbSize { get; set; }
        public ulong DtbAddress { get; set; }

        // V3+ fields (new format)
        public uint VendorRamdiskSize { get; set; }

        public string OsVersionFormatted
        {
            get
            {
                var a = (OsVersion >> 25) & 0x7f;
                var b = (OsVersion >> 18) & 0x7f;
                var c = (OsVersion >> 11) & 0x7f;
                var y = ((OsVersion >> 4) & 0x7f) + 2000;
                var m = OsVersion & 0xf;
                return $"Android {a}.{b}.{c} ({y}-{m:D2})";
            }
        }
    }

    public class BootImageComponents
    {
        public string KernelPath { get; set; } = "";
        public string RamdiskPath { get; set; } = "";
        public string SecondPath { get; set; } = "";
        public string RecoveryDtboPath { get; set; } = "";
        public string DtbPath { get; set; } = "";
        public BootImageHeader Header { get; set; } = new();
    }

    public class BootImageService
    {
        private readonly string _toolsPath;
        private readonly HttpClient _httpClient;
        private string _magiskbootPath = "";

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        private const string BOOT_MAGIC = "ANDROID!";
        private const int BOOT_MAGIC_SIZE = 8;

        public BootImageService(string toolsPath)
        {
            _toolsPath = toolsPath;
            _httpClient = new HttpClient();
            _magiskbootPath = FindMagiskboot();
        }

        private string FindMagiskboot()
        {
            var localPath = Path.Combine(_toolsPath, "magiskboot.exe");
            if (File.Exists(localPath)) return localPath;

            // Check PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(';'))
            {
                var fullPath = Path.Combine(dir, "magiskboot.exe");
                if (File.Exists(fullPath)) return fullPath;
            }

            return "";
        }

        public bool IsMagiskbootInstalled()
        {
            return !string.IsNullOrEmpty(_magiskbootPath) && File.Exists(_magiskbootPath);
        }

        public async Task<bool> DownloadMagiskbootAsync(IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Downloading magiskboot...");

                // Download from Magisk releases
                var downloadUrl = "https://github.com/topjohnwu/Magisk/releases/latest/download/Magisk-v27.0.apk";

                var tempZip = Path.Combine(_toolsPath, "magisk.apk");

                using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    var buffer = new byte[8192];
                    long bytesRead = 0;

                    using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var fileStream = File.Create(tempZip);

                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read, ct);
                        bytesRead += read;

                        if (totalBytes > 0)
                        {
                            progress?.Report((int)((bytesRead * 50) / totalBytes)); // 0-50%
                        }
                    }
                }

                // Extract magiskboot from APK
                LogMessage?.Invoke(this, "Extracting magiskboot from Magisk APK...");

                using (var archive = ZipFile.OpenRead(tempZip))
                {
                    // Look for magiskboot in lib/x86_64 or lib/x86
                    var entry = archive.GetEntry("lib/x86_64/libmagiskboot.so") ??
                                archive.GetEntry("lib/x86/libmagiskboot.so") ??
                                archive.GetEntry("lib/armeabi-v7a/libmagiskboot.so");

                    if (entry != null)
                    {
                        var outputPath = Path.Combine(_toolsPath, "magiskboot.exe");
                        entry.ExtractToFile(outputPath, true);
                        _magiskbootPath = outputPath;
                        LogMessage?.Invoke(this, $"Extracted magiskboot: {_magiskbootPath}");
                    }
                    else
                    {
                        LogMessage?.Invoke(this, "magiskboot not found in APK. Using built-in parser.");
                    }
                }

                File.Delete(tempZip);
                progress?.Report(100);

                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Download error: {ex.Message}");
                return false;
            }
        }

        public async Task<BootImageHeader?> ReadBootImageHeaderAsync(string bootImagePath)
        {
            try
            {
                if (!File.Exists(bootImagePath))
                {
                    LogMessage?.Invoke(this, $"File not found: {bootImagePath}");
                    return null;
                }

                using var stream = File.OpenRead(bootImagePath);
                using var reader = new BinaryReader(stream);

                var header = new BootImageHeader();

                // Read magic
                var magicBytes = reader.ReadBytes(BOOT_MAGIC_SIZE);
                header.Magic = Encoding.ASCII.GetString(magicBytes);

                if (header.Magic != BOOT_MAGIC)
                {
                    LogMessage?.Invoke(this, $"Invalid boot image magic: {header.Magic}");
                    return null;
                }

                // Read v0/v1/v2 header fields
                header.KernelSize = reader.ReadUInt32();
                header.KernelAddress = reader.ReadUInt32();
                header.RamdiskSize = reader.ReadUInt32();
                header.RamdiskAddress = reader.ReadUInt32();
                header.SecondSize = reader.ReadUInt32();
                header.SecondAddress = reader.ReadUInt32();
                header.TagsAddress = reader.ReadUInt32();
                header.PageSize = reader.ReadUInt32();

                // Header version at offset 40
                header.HeaderVersion = reader.ReadUInt32();
                header.OsVersion = reader.ReadUInt32();

                // Name (16 bytes)
                var nameBytes = reader.ReadBytes(16);
                header.Name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                // Cmdline (512 bytes)
                var cmdlineBytes = reader.ReadBytes(512);
                header.Cmdline = Encoding.ASCII.GetString(cmdlineBytes).TrimEnd('\0');

                // ID (32 bytes)
                header.Id = reader.ReadBytes(32);

                // Extra cmdline (1024 bytes)
                var extraCmdlineBytes = reader.ReadBytes(1024);
                header.ExtraCmdline = Encoding.ASCII.GetString(extraCmdlineBytes).TrimEnd('\0');

                // V1+ fields
                if (header.HeaderVersion >= 1)
                {
                    header.RecoveryDtboSize = reader.ReadUInt32();
                    header.RecoveryDtboOffset = reader.ReadUInt64();
                    header.HeaderSize = reader.ReadUInt32();
                }

                // V2+ fields
                if (header.HeaderVersion >= 2)
                {
                    header.DtbSize = reader.ReadUInt32();
                    header.DtbAddress = reader.ReadUInt64();
                }

                LogMessage?.Invoke(this, $"Boot Image: v{header.HeaderVersion}, Page Size: {header.PageSize}");
                LogMessage?.Invoke(this, $"  Kernel: {header.KernelSize} bytes @ 0x{header.KernelAddress:X}");
                LogMessage?.Invoke(this, $"  Ramdisk: {header.RamdiskSize} bytes @ 0x{header.RamdiskAddress:X}");
                LogMessage?.Invoke(this, $"  OS: {header.OsVersionFormatted}");
                if (!string.IsNullOrEmpty(header.Cmdline))
                    LogMessage?.Invoke(this, $"  Cmdline: {header.Cmdline.Substring(0, Math.Min(50, header.Cmdline.Length))}...");

                return header;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Error reading header: {ex.Message}");
                return null;
            }
        }

        public async Task<BootImageComponents?> UnpackBootImageAsync(string bootImagePath, string outputDir,
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                var header = await ReadBootImageHeaderAsync(bootImagePath);
                if (header == null) return null;

                Directory.CreateDirectory(outputDir);

                LogMessage?.Invoke(this, $"Unpacking boot image to: {outputDir}");

                var components = new BootImageComponents { Header = header };

                using var stream = File.OpenRead(bootImagePath);

                // Calculate offsets based on page size
                var pageSize = header.PageSize;
                var kernelOffset = pageSize; // After header
                var kernelPages = (header.KernelSize + pageSize - 1) / pageSize;
                var ramdiskOffset = kernelOffset + kernelPages * pageSize;
                var ramdiskPages = (header.RamdiskSize + pageSize - 1) / pageSize;
                var secondOffset = ramdiskOffset + ramdiskPages * pageSize;

                // Extract kernel
                if (header.KernelSize > 0)
                {
                    components.KernelPath = Path.Combine(outputDir, "kernel");
                    await ExtractComponentAsync(stream, kernelOffset, header.KernelSize,
                        components.KernelPath, ct);
                    LogMessage?.Invoke(this, $"Extracted kernel: {header.KernelSize} bytes");
                    progress?.Report(25);
                }

                // Extract ramdisk
                if (header.RamdiskSize > 0)
                {
                    components.RamdiskPath = Path.Combine(outputDir, "ramdisk.cpio");
                    await ExtractComponentAsync(stream, ramdiskOffset, header.RamdiskSize,
                        components.RamdiskPath, ct);
                    LogMessage?.Invoke(this, $"Extracted ramdisk: {header.RamdiskSize} bytes");

                    // Decompress ramdisk if compressed
                    await DecompressRamdiskAsync(components.RamdiskPath, outputDir, ct);
                    progress?.Report(50);
                }

                // Extract second stage
                if (header.SecondSize > 0)
                {
                    components.SecondPath = Path.Combine(outputDir, "second");
                    await ExtractComponentAsync(stream, secondOffset, header.SecondSize,
                        components.SecondPath, ct);
                    LogMessage?.Invoke(this, $"Extracted second: {header.SecondSize} bytes");
                    progress?.Report(60);
                }

                // Extract recovery dtbo (v1+)
                if (header.HeaderVersion >= 1 && header.RecoveryDtboSize > 0)
                {
                    components.RecoveryDtboPath = Path.Combine(outputDir, "recovery_dtbo");
                    await ExtractComponentAsync(stream, (long)header.RecoveryDtboOffset,
                        header.RecoveryDtboSize, components.RecoveryDtboPath, ct);
                    LogMessage?.Invoke(this, $"Extracted recovery_dtbo: {header.RecoveryDtboSize} bytes");
                    progress?.Report(80);
                }

                // Extract DTB (v2+)
                if (header.HeaderVersion >= 2 && header.DtbSize > 0)
                {
                    var secondPages = (header.SecondSize + pageSize - 1) / pageSize;
                    long dtbOffset = secondOffset + secondPages * pageSize;

                    if (header.HeaderVersion >= 1 && header.RecoveryDtboSize > 0)
                    {
                        var recoveryDtboPages = (header.RecoveryDtboSize + pageSize - 1) / pageSize;
                        dtbOffset = (long)header.RecoveryDtboOffset + recoveryDtboPages * pageSize;
                    }

                    components.DtbPath = Path.Combine(outputDir, "dtb");
                    await ExtractComponentAsync(stream, dtbOffset, header.DtbSize,
                        components.DtbPath, ct);
                    LogMessage?.Invoke(this, $"Extracted dtb: {header.DtbSize} bytes");
                }

                // Save header info
                var headerInfoPath = Path.Combine(outputDir, "header_info.txt");
                await SaveHeaderInfoAsync(header, headerInfoPath);

                progress?.Report(100);
                LogMessage?.Invoke(this, "Boot image unpacked successfully!");

                return components;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Unpack error: {ex.Message}");
                return null;
            }
        }

        private async Task ExtractComponentAsync(FileStream stream, long offset, uint size,
            string outputPath, CancellationToken ct)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[size];
            await stream.ReadAsync(buffer, 0, (int)size, ct);
            await File.WriteAllBytesAsync(outputPath, buffer, ct);
        }

        private async Task DecompressRamdiskAsync(string ramdiskPath, string outputDir, CancellationToken ct)
        {
            try
            {
                using var stream = File.OpenRead(ramdiskPath);
                var magic = new byte[4];
                await stream.ReadAsync(magic, 0, 4, ct);
                stream.Seek(0, SeekOrigin.Begin);

                string compression;
                if (magic[0] == 0x1F && magic[1] == 0x8B)
                    compression = "gzip";
                else if (magic[0] == 0x5D && magic[1] == 0x00)
                    compression = "lzma";
                else if (magic[0] == 0xFD && magic[1] == 0x37 && magic[2] == 0x7A && magic[3] == 0x58)
                    compression = "xz";
                else if (magic[0] == 0x28 && magic[1] == 0xB5 && magic[2] == 0x2F && magic[3] == 0xFD)
                    compression = "zstd";
                else if (magic[0] == 0x89 && magic[1] == 0x4C && magic[2] == 0x5A)
                    compression = "lz4";
                else
                    compression = "none";

                LogMessage?.Invoke(this, $"Ramdisk compression: {compression}");

                if (compression == "gzip")
                {
                    var decompressedPath = Path.Combine(outputDir, "ramdisk.cpio.decompressed");
                    using var gzipStream = new GZipStream(stream, CompressionMode.Decompress);
                    using var outputStream = File.Create(decompressedPath);
                    await gzipStream.CopyToAsync(outputStream, ct);

                    // Replace original with decompressed
                    File.Delete(ramdiskPath);
                    File.Move(decompressedPath, ramdiskPath);
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Decompress warning: {ex.Message}");
            }
        }

        public async Task<bool> RepackBootImageAsync(string outputPath, BootImageComponents components,
            IProgress<int>? progress = null, CancellationToken ct = default)
        {
            try
            {
                var header = components.Header;
                var pageSize = header.PageSize > 0 ? header.PageSize : 4096u;

                LogMessage?.Invoke(this, $"Repacking boot image (v{header.HeaderVersion})...");

                using var stream = File.Create(outputPath);

                // Write header
                await WriteBootHeaderAsync(stream, header, pageSize, ct);
                progress?.Report(10);

                // Pad to page boundary
                await PadToPageAsync(stream, pageSize, ct);

                // Write kernel
                if (!string.IsNullOrEmpty(components.KernelPath) && File.Exists(components.KernelPath))
                {
                    var kernel = await File.ReadAllBytesAsync(components.KernelPath, ct);
                    header.KernelSize = (uint)kernel.Length;
                    await stream.WriteAsync(kernel, ct);
                    await PadToPageAsync(stream, pageSize, ct);
                    LogMessage?.Invoke(this, $"Written kernel: {kernel.Length} bytes");
                }
                progress?.Report(30);

                // Write ramdisk
                if (!string.IsNullOrEmpty(components.RamdiskPath) && File.Exists(components.RamdiskPath))
                {
                    var ramdisk = await File.ReadAllBytesAsync(components.RamdiskPath, ct);

                    // Compress ramdisk with gzip
                    using var compressedStream = new MemoryStream();
                    using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal, true))
                    {
                        await gzipStream.WriteAsync(ramdisk, ct);
                    }

                    var compressedRamdisk = compressedStream.ToArray();
                    header.RamdiskSize = (uint)compressedRamdisk.Length;
                    await stream.WriteAsync(compressedRamdisk, ct);
                    await PadToPageAsync(stream, pageSize, ct);
                    LogMessage?.Invoke(this, $"Written ramdisk: {compressedRamdisk.Length} bytes (compressed)");
                }
                progress?.Report(50);

                // Write second
                if (!string.IsNullOrEmpty(components.SecondPath) && File.Exists(components.SecondPath))
                {
                    var second = await File.ReadAllBytesAsync(components.SecondPath, ct);
                    header.SecondSize = (uint)second.Length;
                    await stream.WriteAsync(second, ct);
                    await PadToPageAsync(stream, pageSize, ct);
                }
                progress?.Report(60);

                // Write recovery_dtbo (v1+)
                if (header.HeaderVersion >= 1 && !string.IsNullOrEmpty(components.RecoveryDtboPath)
                    && File.Exists(components.RecoveryDtboPath))
                {
                    var recoveryDtbo = await File.ReadAllBytesAsync(components.RecoveryDtboPath, ct);
                    header.RecoveryDtboSize = (uint)recoveryDtbo.Length;
                    await stream.WriteAsync(recoveryDtbo, ct);
                    await PadToPageAsync(stream, pageSize, ct);
                }
                progress?.Report(80);

                // Write DTB (v2+)
                if (header.HeaderVersion >= 2 && !string.IsNullOrEmpty(components.DtbPath)
                    && File.Exists(components.DtbPath))
                {
                    var dtb = await File.ReadAllBytesAsync(components.DtbPath, ct);
                    header.DtbSize = (uint)dtb.Length;
                    await stream.WriteAsync(dtb, ct);
                    await PadToPageAsync(stream, pageSize, ct);
                }

                // Update header with new sizes
                stream.Seek(0, SeekOrigin.Begin);
                await WriteBootHeaderAsync(stream, header, pageSize, ct);

                progress?.Report(100);
                LogMessage?.Invoke(this, $"Boot image repacked: {outputPath}");

                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Repack error: {ex.Message}");
                return false;
            }
        }

        private async Task WriteBootHeaderAsync(FileStream stream, BootImageHeader header,
            uint pageSize, CancellationToken ct)
        {
            using var writer = new BinaryWriter(stream, Encoding.ASCII, true);

            // Magic
            writer.Write(Encoding.ASCII.GetBytes(BOOT_MAGIC));

            // Basic fields
            writer.Write(header.KernelSize);
            writer.Write(header.KernelAddress);
            writer.Write(header.RamdiskSize);
            writer.Write(header.RamdiskAddress);
            writer.Write(header.SecondSize);
            writer.Write(header.SecondAddress);
            writer.Write(header.TagsAddress);
            writer.Write(pageSize);
            writer.Write(header.HeaderVersion);
            writer.Write(header.OsVersion);

            // Name (16 bytes)
            var nameBytes = new byte[16];
            Encoding.ASCII.GetBytes(header.Name ?? "").CopyTo(nameBytes, 0);
            writer.Write(nameBytes);

            // Cmdline (512 bytes)
            var cmdlineBytes = new byte[512];
            Encoding.ASCII.GetBytes(header.Cmdline ?? "").CopyTo(cmdlineBytes, 0);
            writer.Write(cmdlineBytes);

            // ID (32 bytes)
            writer.Write(header.Id);

            // Extra cmdline (1024 bytes)
            var extraCmdlineBytes = new byte[1024];
            Encoding.ASCII.GetBytes(header.ExtraCmdline ?? "").CopyTo(extraCmdlineBytes, 0);
            writer.Write(extraCmdlineBytes);

            // V1+ fields
            if (header.HeaderVersion >= 1)
            {
                writer.Write(header.RecoveryDtboSize);
                writer.Write(header.RecoveryDtboOffset);
                writer.Write(header.HeaderSize > 0 ? header.HeaderSize : 1648u);
            }

            // V2+ fields
            if (header.HeaderVersion >= 2)
            {
                writer.Write(header.DtbSize);
                writer.Write(header.DtbAddress);
            }
        }

        private async Task PadToPageAsync(FileStream stream, uint pageSize, CancellationToken ct)
        {
            var position = stream.Position;
            var remainder = position % pageSize;
            if (remainder > 0)
            {
                var padding = pageSize - remainder;
                var zeros = new byte[padding];
                await stream.WriteAsync(zeros, ct);
            }
        }

        private async Task SaveHeaderInfoAsync(BootImageHeader header, string path)
        {
            var info = new StringBuilder();
            info.AppendLine($"# Boot Image Header Info");
            info.AppendLine($"header_version={header.HeaderVersion}");
            info.AppendLine($"page_size={header.PageSize}");
            info.AppendLine($"kernel_size={header.KernelSize}");
            info.AppendLine($"kernel_addr=0x{header.KernelAddress:X}");
            info.AppendLine($"ramdisk_size={header.RamdiskSize}");
            info.AppendLine($"ramdisk_addr=0x{header.RamdiskAddress:X}");
            info.AppendLine($"second_size={header.SecondSize}");
            info.AppendLine($"second_addr=0x{header.SecondAddress:X}");
            info.AppendLine($"tags_addr=0x{header.TagsAddress:X}");
            info.AppendLine($"os_version={header.OsVersion}");
            info.AppendLine($"os_version_fmt={header.OsVersionFormatted}");
            info.AppendLine($"name={header.Name}");
            info.AppendLine($"cmdline={header.Cmdline}");
            info.AppendLine($"extra_cmdline={header.ExtraCmdline}");

            if (header.HeaderVersion >= 1)
            {
                info.AppendLine($"recovery_dtbo_size={header.RecoveryDtboSize}");
                info.AppendLine($"recovery_dtbo_offset={header.RecoveryDtboOffset}");
            }

            if (header.HeaderVersion >= 2)
            {
                info.AppendLine($"dtb_size={header.DtbSize}");
                info.AppendLine($"dtb_addr=0x{header.DtbAddress:X}");
            }

            await File.WriteAllTextAsync(path, info.ToString());
        }

        public async Task<bool> PatchRamdiskForRootAsync(string ramdiskDir, CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Patching ramdisk for root access...");

                // Common root patches:
                // 1. Modify default.prop / prop.default
                var defaultPropPath = Path.Combine(ramdiskDir, "default.prop");
                if (!File.Exists(defaultPropPath))
                    defaultPropPath = Path.Combine(ramdiskDir, "prop.default");

                if (File.Exists(defaultPropPath))
                {
                    var content = await File.ReadAllTextAsync(defaultPropPath, ct);

                    // Enable adb root
                    content = content.Replace("ro.debuggable=0", "ro.debuggable=1");
                    content = content.Replace("ro.secure=1", "ro.secure=0");
                    content = content.Replace("ro.adb.secure=1", "ro.adb.secure=0");

                    if (!content.Contains("ro.debuggable=1"))
                        content += "\nro.debuggable=1";
                    if (!content.Contains("ro.secure=0"))
                        content += "\nro.secure=0";

                    await File.WriteAllTextAsync(defaultPropPath, content, ct);
                    LogMessage?.Invoke(this, "Patched default.prop for debuggable and insecure");
                }

                // 2. Disable dm-verity in fstab
                var fstabFiles = Directory.GetFiles(ramdiskDir, "fstab.*", SearchOption.AllDirectories);
                foreach (var fstabPath in fstabFiles)
                {
                    var content = await File.ReadAllTextAsync(fstabPath, ct);
                    content = content.Replace(",verify", "");
                    content = content.Replace(",avb", "");
                    content = content.Replace(",avb_keys", "");
                    await File.WriteAllTextAsync(fstabPath, content, ct);
                    LogMessage?.Invoke(this, $"Removed dm-verity from {Path.GetFileName(fstabPath)}");
                }

                // 3. Add su binary placeholder
                var suPath = Path.Combine(ramdiskDir, "system", "xbin");
                Directory.CreateDirectory(suPath);

                LogMessage?.Invoke(this, "Ramdisk patched for root (placeholder)");
                LogMessage?.Invoke(this, "Note: For full root, use Magisk patching instead");

                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Patch error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ExtractRamdiskCpioAsync(string cpioPath, string outputDir,
            CancellationToken ct = default)
        {
            try
            {
                LogMessage?.Invoke(this, "Extracting CPIO ramdisk...");

                // Simple CPIO extractor for newc format
                Directory.CreateDirectory(outputDir);

                using var stream = File.OpenRead(cpioPath);
                using var reader = new BinaryReader(stream);

                while (stream.Position < stream.Length - 6)
                {
                    ct.ThrowIfCancellationRequested();

                    // Read magic (6 bytes: "070701" or "070702")
                    var magic = Encoding.ASCII.GetString(reader.ReadBytes(6));
                    if (magic != "070701" && magic != "070702")
                    {
                        LogMessage?.Invoke(this, "Unsupported CPIO format or end of archive");
                        break;
                    }

                    // Read header fields (all hex strings)
                    var ino = ReadHexField(reader, 8);
                    var mode = ReadHexField(reader, 8);
                    var uid = ReadHexField(reader, 8);
                    var gid = ReadHexField(reader, 8);
                    var nlink = ReadHexField(reader, 8);
                    var mtime = ReadHexField(reader, 8);
                    var filesize = ReadHexField(reader, 8);
                    var devmajor = ReadHexField(reader, 8);
                    var devminor = ReadHexField(reader, 8);
                    var rdevmajor = ReadHexField(reader, 8);
                    var rdevminor = ReadHexField(reader, 8);
                    var namesize = ReadHexField(reader, 8);
                    var check = ReadHexField(reader, 8);

                    // Read filename
                    var filenameBytes = reader.ReadBytes((int)namesize);
                    var filename = Encoding.ASCII.GetString(filenameBytes).TrimEnd('\0');

                    // Align to 4 bytes
                    var headerSize = 110 + namesize;
                    var padding = (4 - (headerSize % 4)) % 4;
                    if (padding > 0) reader.ReadBytes((int)padding);

                    // Check for trailer
                    if (filename == "TRAILER!!!")
                        break;

                    // Read file data
                    var data = reader.ReadBytes((int)filesize);

                    // Align to 4 bytes
                    var dataPadding = (4 - (filesize % 4)) % 4;
                    if (dataPadding > 0) reader.ReadBytes((int)dataPadding);

                    // Create file or directory
                    var outputPath = Path.Combine(outputDir, filename);
                    var isDir = (mode & 0xF000) == 0x4000;

                    if (isDir)
                    {
                        Directory.CreateDirectory(outputPath);
                    }
                    else
                    {
                        var dir = Path.GetDirectoryName(outputPath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);

                        await File.WriteAllBytesAsync(outputPath, data, ct);
                    }
                }

                LogMessage?.Invoke(this, $"Ramdisk extracted to: {outputDir}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"Extract CPIO error: {ex.Message}");
                return false;
            }
        }

        private uint ReadHexField(BinaryReader reader, int length)
        {
            var hex = Encoding.ASCII.GetString(reader.ReadBytes(length));
            return Convert.ToUInt32(hex, 16);
        }

        public async Task<string> GetKernelVersionAsync(string kernelPath)
        {
            try
            {
                using var stream = File.OpenRead(kernelPath);
                var buffer = new byte[Math.Min(65536, stream.Length)];
                await stream.ReadAsync(buffer, 0, buffer.Length);

                // Search for Linux version string
                var content = Encoding.ASCII.GetString(buffer);
                var linuxIndex = content.IndexOf("Linux version ");
                if (linuxIndex >= 0)
                {
                    var endIndex = content.IndexOf('\0', linuxIndex);
                    if (endIndex < 0) endIndex = linuxIndex + 100;
                    return content.Substring(linuxIndex, Math.Min(100, endIndex - linuxIndex));
                }

                return "Unknown";
            }
            catch
            {
                return "Error reading kernel";
            }
        }
    }
}
