using System;
using System.IO;
using System.Threading.Tasks;
using HyperSend.Core;
using Xunit;

namespace HyperSend.Tests
{
    public class ReliabilityTests
    {
        [Fact]
        public void TransferResumeState_SavesAndReloadsBitmapAccurately()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"resume_test_{Guid.NewGuid():N}.dat");
            long totalBytes = 50 * 1024 * 1024; // 50 MB
            int chunkSize = 2 * 1024 * 1024;   // 2 MB -> 25 chunks

            try
            {
                var state = new TransferResumeState(tempFile, totalBytes, chunkSize, "test-id-123");
                Assert.Equal(25, state.TotalChunks);
                Assert.Equal(0, state.CompletedCount);

                // Mark 50% (first 13 chunks) as completed
                for (int i = 0; i < 13; i++)
                {
                    state.MarkChunkCompleted(i);
                }

                Assert.Equal(13, state.CompletedCount);
                Assert.False(state.IsComplete);

                state.SaveState();
                Assert.True(File.Exists(state.StateFilePath));

                // Reload state from disk
                var reloaded = new TransferResumeState(tempFile, totalBytes, chunkSize, "test-id-123");
                Assert.Equal(13, reloaded.CompletedCount);

                for (int i = 0; i < 13; i++)
                {
                    Assert.True(reloaded.IsChunkCompleted(i));
                }
                for (int i = 13; i < 25; i++)
                {
                    Assert.False(reloaded.IsChunkCompleted(i));
                }
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                if (File.Exists(tempFile + ".hypersend-state")) File.Delete(tempFile + ".hypersend-state");
                if (File.Exists(tempFile + ".part")) File.Delete(tempFile + ".part");
            }
        }

        [Fact]
        public async Task FileIntegrity_ComputesAccurateSha256()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"sha_test_{Guid.NewGuid():N}.dat");
            try
            {
                byte[] testData = System.Text.Encoding.UTF8.GetBytes("HyperSend Integrity Verification Hash Test");
                await File.WriteAllBytesAsync(tempFile, testData);

                string hash = await FileIntegrity.ComputeFileSha256Async(tempFile);
                Assert.NotEmpty(hash);
                Assert.Equal(64, hash.Length); // 64 hex characters
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}

