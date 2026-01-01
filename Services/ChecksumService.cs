using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PhoneRomFlashTool.Services
{
    public class ChecksumResult
    {
        public string FileName { get; set; } = "";
        public long FileSize { get; set; }
        public string MD5 { get; set; } = "";
        public string SHA1 { get; set; } = "";
        public string SHA256 { get; set; } = "";
        public bool IsValid { get; set; }
        public string? ExpectedHash { get; set; }
        public string? MatchedAlgorithm { get; set; }
    }

    public class ChecksumService
    {
        public event EventHandler<int>? ProgressChanged;

        public async Task<ChecksumResult> CalculateChecksumsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var result = new ChecksumResult
            {
                FileName = Path.GetFileName(filePath)
            };

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("File not found", filePath);
            }

            var fileInfo = new FileInfo(filePath);
            result.FileSize = fileInfo.Length;

            // Calculate all checksums
            result.MD5 = await CalculateHashAsync<MD5>(filePath, cancellationToken);
            ProgressChanged?.Invoke(this, 33);

            result.SHA1 = await CalculateHashAsync<SHA1>(filePath, cancellationToken);
            ProgressChanged?.Invoke(this, 66);

            result.SHA256 = await CalculateHashAsync<SHA256>(filePath, cancellationToken);
            ProgressChanged?.Invoke(this, 100);

            return result;
        }

        public async Task<ChecksumResult> VerifyChecksumAsync(string filePath, string expectedHash, CancellationToken cancellationToken = default)
        {
            var result = await CalculateChecksumsAsync(filePath, cancellationToken);
            result.ExpectedHash = expectedHash.ToUpperInvariant().Replace(" ", "").Replace("-", "");

            // Check which algorithm matches
            if (result.MD5.Equals(result.ExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                result.IsValid = true;
                result.MatchedAlgorithm = "MD5";
            }
            else if (result.SHA1.Equals(result.ExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                result.IsValid = true;
                result.MatchedAlgorithm = "SHA1";
            }
            else if (result.SHA256.Equals(result.ExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                result.IsValid = true;
                result.MatchedAlgorithm = "SHA256";
            }
            else
            {
                result.IsValid = false;
            }

            return result;
        }

        private async Task<string> CalculateHashAsync<T>(string filePath, CancellationToken cancellationToken) where T : HashAlgorithm
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            using var hasher = (T)CryptoConfig.CreateFromName(typeof(T).Name)!;

            var buffer = new byte[81920];
            int bytesRead;
            long totalBytesRead = 0;
            long fileSize = stream.Length;

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
                totalBytesRead += bytesRead;

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }
            }

            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(hasher.Hash!).Replace("-", "");
        }

        public static string FormatFileSize(long bytes)
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
}
