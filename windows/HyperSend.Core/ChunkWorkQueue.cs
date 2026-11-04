using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HyperSend.Core
{
    public class ChunkDescriptor
    {
        public uint ChunkIndex { get; }
        public long Offset { get; }
        public int Length { get; }

        public ChunkDescriptor(uint chunkIndex, long offset, int length)
        {
            ChunkIndex = chunkIndex;
            Offset = offset;
            Length = length;
        }
    }

    public class ChunkWorkQueue
    {
        private readonly ConcurrentQueue<ChunkDescriptor> _queue = new();
        public int TotalChunks { get; }
        public long TotalBytes { get; }

        public ChunkWorkQueue(long totalBytes, int chunkSize = NetworkConfig.DefaultChunkSize)
        {
            TotalBytes = totalBytes;
            if (totalBytes <= 0)
            {
                TotalChunks = 0;
                return;
            }

            int count = (int)((totalBytes + chunkSize - 1) / chunkSize);
            TotalChunks = count;

            for (uint i = 0; i < count; i++)
            {
                long offset = (long)i * chunkSize;
                int len = (int)Math.Min((long)chunkSize, totalBytes - offset);
                _queue.Enqueue(new ChunkDescriptor(i, offset, len));
            }
        }

        public bool TryGetNextChunk(out ChunkDescriptor? chunk)
        {
            return _queue.TryDequeue(out chunk);
        }

        public int RemainingChunks => _queue.Count;
    }
}

