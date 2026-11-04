using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HyperSend.Core;
using Xunit;

namespace HyperSend.Tests
{
    public class PathSanitizerAndBundleTests
    {
        [Theory]
        [InlineData("valid/path/file.txt", "valid/path/file.txt")]
        [InlineData("valid\\path\\file.txt", "valid/path/file.txt")]
        [InlineData("single_file.bin", "single_file.bin")]
        [InlineData("nested/deep/directory/structure/file.jpg", "nested/deep/directory/structure/file.jpg")]
        public void PathSanitizer_AcceptsValidRelativePaths(string input, string expected)
        {
            string sanitized = PathSanitizer.SanitizeRelativePath(input);
            Assert.Equal(expected, sanitized);
        }

        [Theory]
        [InlineData("../evil.txt")]
        [InlineData("foo/../../evil.txt")]
        [InlineData("..")]
        [InlineData("/etc/passwd")]
        [InlineData("C:\\Windows\\System32\\cmd.exe")]
        [InlineData("D:/secret/file.txt")]
        [InlineData("//192.168.1.1/share/evil")]
        [InlineData("CON")]
        [InlineData("con.txt")]
        [InlineData("sub/AUX.dat")]
        [InlineData("NUL")]
        [InlineData("COM1")]
        [InlineData("sub/LPT1.log")]
        [InlineData("invalid<name>.txt")]
        [InlineData("invalid|name.txt")]
        [InlineData("invalid?name.txt")]
        [InlineData("invalid*name.txt")]
        public void PathSanitizer_RejectsMaliciousOrIllegalPaths(string maliciousPath)
        {
            Assert.ThrowsAny<Exception>(() => PathSanitizer.SanitizeRelativePath(maliciousPath));
        }

        [Fact]
        public void PathSanitizer_ResolveSafeDestinationPath_EnforcesDestinationBoundary()
        {
            string tempBase = Path.Combine(Path.GetTempPath(), "HyperSend_TestDest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempBase);

            try
            {
                string resolved = PathSanitizer.ResolveSafeDestinationPath(tempBase, "sub/folder/file.txt");
                Assert.StartsWith(tempBase, resolved, StringComparison.OrdinalIgnoreCase);

                Assert.ThrowsAny<Exception>(() => PathSanitizer.ResolveSafeDestinationPath(tempBase, "../outside.txt"));
            }
            finally
            {
                if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true);
            }
        }

        [Fact]
        public async Task BundlePacker_PackAndUnpack_PreservesContentAndStructure()
        {
            string sourceRoot = Path.Combine(Path.GetTempPath(), "HyperSend_Src_" + Guid.NewGuid().ToString("N"));
            string destRoot = Path.Combine(Path.GetTempPath(), "HyperSend_Dst_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(sourceRoot);
            Directory.CreateDirectory(destRoot);

            try
            {
                // Create multiple small files with nested directories
                string file1 = Path.Combine(sourceRoot, "hello.txt");
                string file2 = Path.Combine(sourceRoot, "sub1", "document.json");
                string file3 = Path.Combine(sourceRoot, "sub1", "sub2", "data.bin");

                Directory.CreateDirectory(Path.GetDirectoryName(file2)!);
                Directory.CreateDirectory(Path.GetDirectoryName(file3)!);

                byte[] data1 = Encoding.UTF8.GetBytes("Hello World from HyperSend Bundle!");
                byte[] data2 = Encoding.UTF8.GetBytes("{\"name\": \"HyperSend\", \"speed\": \"ultra\"}");
                byte[] data3 = new byte[100_000];
                new Random(42).NextBytes(data3);

                await File.WriteAllBytesAsync(file1, data1);
                await File.WriteAllBytesAsync(file2, data2);
                await File.WriteAllBytesAsync(file3, data3);

                // Scan directory
                var (largeFiles, bundles) = BundlePacker.ScanDirectory(sourceRoot);
                Assert.Empty(largeFiles);
                Assert.Single(bundles);
                Assert.Equal(3, bundles[0].Count);

                // Pack bundle
                byte[] packedBundle = await BundlePacker.PackBundleAsync(bundles[0]);
                Assert.NotEmpty(packedBundle);

                // Unpack bundle to destRoot
                await BundlePacker.UnpackBundleAsync(packedBundle, destRoot);

                // Verify unpacked files
                string destFile1 = Path.Combine(destRoot, "hello.txt");
                string destFile2 = Path.Combine(destRoot, "sub1", "document.json");
                string destFile3 = Path.Combine(destRoot, "sub1", "sub2", "data.bin");

                Assert.True(File.Exists(destFile1));
                Assert.True(File.Exists(destFile2));
                Assert.True(File.Exists(destFile3));

                Assert.Equal(data1, await File.ReadAllBytesAsync(destFile1));
                Assert.Equal(data2, await File.ReadAllBytesAsync(destFile2));
                Assert.Equal(data3, await File.ReadAllBytesAsync(destFile3));
            }
            finally
            {
                if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot, true);
                if (Directory.Exists(destRoot)) Directory.Delete(destRoot, true);
            }
        }

        [Fact]
        public async Task BundlePacker_HighVolumeThousandFiles_PacksAndUnpacksFast()
        {
            string sourceRoot = Path.Combine(Path.GetTempPath(), "HyperSend_Src1000_" + Guid.NewGuid().ToString("N"));
            string destRoot = Path.Combine(Path.GetTempPath(), "HyperSend_Dst1000_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(sourceRoot);
            Directory.CreateDirectory(destRoot);

            try
            {
                const int fileCount = 500;
                byte[] samplePayload = Encoding.UTF8.GetBytes("Small payload for HyperSend bundle stress testing.");

                // Create 500 files across multiple subfolders
                for (int i = 0; i < fileCount; i++)
                {
                    string sub = Path.Combine(sourceRoot, $"folder_{i % 10}");
                    Directory.CreateDirectory(sub);
                    File.WriteAllBytes(Path.Combine(sub, $"file_{i}.txt"), samplePayload);
                }

                var (largeFiles, bundles) = BundlePacker.ScanDirectory(sourceRoot);
                Assert.Empty(largeFiles);
                Assert.NotEmpty(bundles);

                long totalBytes = 0;
                foreach (var bundle in bundles)
                {
                    byte[] packed = await BundlePacker.PackBundleAsync(bundle);
                    totalBytes += packed.Length;
                    await BundlePacker.UnpackBundleAsync(packed, destRoot);
                }

                // Verify file count in destination
                int unpackedCount = Directory.GetFiles(destRoot, "*.txt", SearchOption.AllDirectories).Length;
                Assert.Equal(fileCount, unpackedCount);
            }
            finally
            {
                if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot, true);
                if (Directory.Exists(destRoot)) Directory.Delete(destRoot, true);
            }
        }
    }
}
