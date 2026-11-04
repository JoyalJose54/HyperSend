using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HyperSend.Core
{
    public static class FileIntegrity
    {
        public static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken = default)
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            byte[] hash = await sha.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public static bool CheckDiskSpace(string destinationPath, long requiredBytes, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                string? root = Path.GetPathRoot(Path.GetFullPath(destinationPath));
                if (string.IsNullOrEmpty(root)) return true;

                var driveInfo = new DriveInfo(root);
                long safetyMargin = 100L * 1024 * 1024; // 100 MB margin
                if (driveInfo.AvailableFreeSpace < requiredBytes + safetyMargin)
                {
                    errorMessage = $"Insufficient disk space on {driveInfo.Name}. Required: {(requiredBytes + safetyMargin) / (1024.0 * 1024.0):F0} MB, Available: {driveInfo.AvailableFreeSpace / (1024.0 * 1024.0):F0} MB";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }
            return true;
        }
    }
}

