using System;
using System.Collections;
using System.IO;
using System.Text.Json;

namespace HyperSend.Core
{
    public class TransferResumeMetadata
    {
        public string TransferId { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public int ChunkSize { get; set; }
        public int TotalChunks { get; set; }
        public string ExpectedHash { get; set; } = string.Empty;
        public string CompletedChunksBitsetBase64 { get; set; } = string.Empty;
    }

    public class TransferResumeState
    {
        public string TransferId { get; }
        public string FilePath { get; }
        public string StateFilePath => FilePath + ".hypersend-state";
        public string PartFilePath => FilePath + ".part";
        public long TotalBytes { get; }
        public int ChunkSize { get; }
        public int TotalChunks { get; }
        public BitArray CompletedChunks { get; }

        public TransferResumeState(string filePath, long totalBytes, int chunkSize, string transferId = "")
        {
            FilePath = filePath;
            TotalBytes = totalBytes;
            ChunkSize = chunkSize;
            TransferId = string.IsNullOrEmpty(transferId) ? Guid.NewGuid().ToString("N") : transferId;
            TotalChunks = (int)((totalBytes + chunkSize - 1) / chunkSize);
            CompletedChunks = new BitArray(TotalChunks, false);

            LoadStateIfExists();
        }

        public void MarkChunkCompleted(int chunkIndex)
        {
            if (chunkIndex >= 0 && chunkIndex < TotalChunks)
            {
                lock (CompletedChunks)
                {
                    CompletedChunks.Set(chunkIndex, true);
                }
            }
        }

        public bool IsChunkCompleted(int chunkIndex)
        {
            if (chunkIndex < 0 || chunkIndex >= TotalChunks) return false;
            lock (CompletedChunks)
            {
                return CompletedChunks.Get(chunkIndex);
            }
        }

        public int CompletedCount
        {
            get
            {
                lock (CompletedChunks)
                {
                    int count = 0;
                    for (int i = 0; i < CompletedChunks.Length; i++)
                    {
                        if (CompletedChunks.Get(i)) count++;
                    }
                    return count;
                }
            }
        }

        public bool IsComplete => CompletedCount >= TotalChunks;

        public void SaveState()
        {
            try
            {
                byte[] bytes = new byte[(TotalChunks + 7) / 8];
                lock (CompletedChunks)
                {
                    CompletedChunks.CopyTo(bytes, 0);
                }

                var meta = new TransferResumeMetadata
                {
                    TransferId = TransferId,
                    RelativePath = FilePath,
                    TotalBytes = TotalBytes,
                    ChunkSize = ChunkSize,
                    TotalChunks = TotalChunks,
                    CompletedChunksBitsetBase64 = Convert.ToBase64String(bytes)
                };

                string json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(StateFilePath, json);
            }
            catch {}
        }

        public void CleanupStateOnCompletion()
        {
            try
            {
                if (File.Exists(StateFilePath)) File.Delete(StateFilePath);
                if (File.Exists(PartFilePath))
                {
                    if (File.Exists(FilePath)) File.Delete(FilePath);
                    File.Move(PartFilePath, FilePath);
                }
            }
            catch {}
        }

        private void LoadStateIfExists()
        {
            try
            {
                if (File.Exists(StateFilePath))
                {
                    string json = File.ReadAllText(StateFilePath);
                    var meta = JsonSerializer.Deserialize<TransferResumeMetadata>(json);
                    if (meta != null && meta.TotalBytes == TotalBytes && meta.TotalChunks == TotalChunks)
                    {
                        byte[] bytes = Convert.FromBase64String(meta.CompletedChunksBitsetBase64);
                        var loadedBits = new BitArray(bytes);
                        lock (CompletedChunks)
                        {
                            for (int i = 0; i < TotalChunks && i < loadedBits.Length; i++)
                            {
                                CompletedChunks.Set(i, loadedBits.Get(i));
                            }
                        }
                    }
                }
            }
            catch {}
        }
    }
}

