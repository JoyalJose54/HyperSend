using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HyperSend.Core
{
    public class BundleEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public long ModifiedTimeUtc { get; set; }
        public long FileSize { get; set; }
        public string AbsoluteSourcePath { get; set; } = string.Empty;
    }

    public static class BundlePacker
    {
        public const int MaxBundleSize = 4 * 1024 * 1024; // 4 MB target per small-file bundle
        public const int SmallFileThreshold = 1024 * 1024; // Files < 1 MB are bundled

        /// <summary>
        /// Scans directory recursively and segregates into individual large files vs bundled small files.
        /// </summary>
        public static (List<BundleEntry> largeFiles, List<List<BundleEntry>> smallFileBundles) ScanDirectory(string sourceDir)
        {
            var largeFiles = new List<BundleEntry>();
            var bundles = new List<List<BundleEntry>>();
            var currentBundle = new List<BundleEntry>();
            long currentBundleBytes = 0;

            var dirInfo = new DirectoryInfo(sourceDir);
            if (!dirInfo.Exists) return (largeFiles, bundles);

            foreach (var fi in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                string rel = PathSanitizer.SanitizeRelativePath(Path.GetRelativePath(sourceDir, fi.FullName));
                var entry = new BundleEntry
                {
                    RelativePath = rel,
                    ModifiedTimeUtc = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds(),
                    FileSize = fi.Length,
                    AbsoluteSourcePath = fi.FullName
                };

                if (fi.Length >= SmallFileThreshold)
                {
                    largeFiles.Add(entry);
                }
                else
                {
                    currentBundle.Add(entry);
                    currentBundleBytes += fi.Length;

                    if (currentBundleBytes >= MaxBundleSize)
                    {
                        bundles.Add(currentBundle);
                        currentBundle = new List<BundleEntry>();
                        currentBundleBytes = 0;
                    }
                }
            }

            if (currentBundle.Count > 0)
            {
                bundles.Add(currentBundle);
            }

            return (largeFiles, bundles);
        }

        /// <summary>
        /// Serializes a batch of small files into a contiguous binary memory stream.
        /// </summary>
        public static async Task<byte[]> PackBundleAsync(List<BundleEntry> entries, CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            byte[] intBuf = new byte[8];

            // 1. Write Entry Count (uint32)
            BinaryPrimitives.WriteUInt32BigEndian(intBuf.AsSpan(0, 4), (uint)entries.Count);
            ms.Write(intBuf, 0, 4);

            foreach (var entry in entries)
            {
                byte[] pathBytes = Encoding.UTF8.GetBytes(entry.RelativePath);
                // Path length (uint16)
                BinaryPrimitives.WriteUInt16BigEndian(intBuf.AsSpan(0, 2), (ushort)pathBytes.Length);
                ms.Write(intBuf, 0, 2);
                ms.Write(pathBytes, 0, pathBytes.Length);

                // Modified time (int64)
                BinaryPrimitives.WriteInt64BigEndian(intBuf.AsSpan(0, 8), entry.ModifiedTimeUtc);
                ms.Write(intBuf, 0, 8);

                // File size (int64)
                BinaryPrimitives.WriteInt64BigEndian(intBuf.AsSpan(0, 8), entry.FileSize);
                ms.Write(intBuf, 0, 8);

                // File payload
                using var fs = new FileStream(entry.AbsoluteSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await fs.CopyToAsync(ms, cancellationToken);
            }

            return ms.ToArray();
        }

        private struct UnpackedFileItem
        {
            public string RelativePath;
            public long ModifiedTimeUtc;
            public int DataOffset;
            public int DataLength;
        }

        private static List<UnpackedFileItem> ParseBundleEntries(ReadOnlyMemory<byte> bundleData)
        {
            var items = new List<UnpackedFileItem>();
            var span = bundleData.Span;
            int offset = 0;

            uint count = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset, 4));
            offset += 4;

            for (uint i = 0; i < count; i++)
            {
                ushort pathLen = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset, 2));
                offset += 2;

                string relPath = Encoding.UTF8.GetString(span.Slice(offset, pathLen));
                offset += pathLen;

                long modTime = BinaryPrimitives.ReadInt64BigEndian(span.Slice(offset, 8));
                offset += 8;

                long fileSize = BinaryPrimitives.ReadInt64BigEndian(span.Slice(offset, 8));
                offset += 8;

                items.Add(new UnpackedFileItem
                {
                    RelativePath = relPath,
                    ModifiedTimeUtc = modTime,
                    DataOffset = offset,
                    DataLength = (int)fileSize
                });

                offset += (int)fileSize;
            }

            return items;
        }

        /// <summary>
        /// Unpacks a contiguous binary bundle directly into the destination directory structure.
        /// </summary>
        public static async Task UnpackBundleAsync(ReadOnlyMemory<byte> bundleData, string destinationRoot, CancellationToken cancellationToken = default)
        {
            var items = ParseBundleEntries(bundleData);

            // Write files asynchronously
            foreach (var item in items)
            {
                string targetPath = PathSanitizer.ResolveSafeDestinationPath(destinationRoot, item.RelativePath);
                string? targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

                var filePayload = bundleData.Slice(item.DataOffset, item.DataLength);

                using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
                {
                    await fs.WriteAsync(filePayload, cancellationToken);
                }

                try
                {
                    File.SetLastWriteTimeUtc(targetPath, DateTimeOffset.FromUnixTimeSeconds(item.ModifiedTimeUtc).UtcDateTime);
                }
                catch {}
            }
        }
    }
}
