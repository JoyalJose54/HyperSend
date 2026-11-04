using System;
using System.IO;
using System.Threading.Tasks;
using HyperSend.Core;
using Xunit;

namespace HyperSend.Tests
{
    public class ChunkEngineTests
    {
        [Fact]
        public void ChunkFrameHeader_RoundTripSerialization()
        {
            var header = new ChunkFrameHeader(
                type: NetworkConfig.TypeDataChunk,
                fileId: 42,
                chunkIndex: 100,
                payloadLength: 2 * 1024 * 1024,
                nonceOrOffset: 209715200UL);

            var buffer = new byte[ChunkFrameHeader.Size];
            header.WriteTo(buffer);

            bool success = ChunkFrameHeader.TryReadFrom(buffer, out var parsed);
            Assert.True(success);
            Assert.Equal(NetworkConfig.TypeDataChunk, parsed.Type);
            Assert.Equal(42u, parsed.FileId);
            Assert.Equal(100u, parsed.ChunkIndex);
            Assert.Equal(2 * 1024 * 1024, parsed.PayloadLength);
            Assert.Equal(209715200UL, parsed.NonceOrOffset);
        }

        [Fact]
        public void ChunkWorkQueue_CorrectPartitioning()
        {
            long totalBytes = 5 * 1024 * 1024; // 5 MB
            int chunkSize = 2 * 1024 * 1024;   // 2 MB

            var queue = new ChunkWorkQueue(totalBytes, chunkSize);
            Assert.Equal(3, queue.TotalChunks);

            Assert.True(queue.TryGetNextChunk(out var c0));
            Assert.Equal(0u, c0!.ChunkIndex);
            Assert.Equal(0L, c0.Offset);
            Assert.Equal(2 * 1024 * 1024, c0.Length);

            Assert.True(queue.TryGetNextChunk(out var c1));
            Assert.Equal(1u, c1!.ChunkIndex);
            Assert.Equal(2 * 1024 * 1024L, c1.Offset);
            Assert.Equal(2 * 1024 * 1024, c1.Length);

            Assert.True(queue.TryGetNextChunk(out var c2));
            Assert.Equal(2u, c2!.ChunkIndex);
            Assert.Equal(4 * 1024 * 1024L, c2.Offset);
            Assert.Equal(1 * 1024 * 1024, c2.Length); // remaining 1 MB

            Assert.False(queue.TryGetNextChunk(out _));
        }

        [Fact]
        public async Task ChunkEngine_Loopback_TransfersFileWithIntegrity()
        {
            int port = 5200;
            int streams = 4;
            int chunkSize = 1 * 1024 * 1024; // 1 MB chunks
            long testBytes = 25 * 1024 * 1024; // 25 MB

            string tempSrc = Path.Combine(Path.GetTempPath(), $"chunk_src_{Guid.NewGuid():N}.dat");
            string tempDst = Path.Combine(Path.GetTempPath(), $"chunk_dst_{Guid.NewGuid():N}.dat");

            try
            {
                var rand = new Random(1337);
                var data = new byte[testBytes];
                rand.NextBytes(data);
                await File.WriteAllBytesAsync(tempSrc, data);

                var receiver = new ChunkedTcpReceiver(port, streams);
                var receiverTask = Task.Run(async () => await receiver.RunReceiverAsync(tempDst, testBytes, chunkSize));

                await Task.Delay(100);

                var sender = new ChunkedTcpSender("127.0.0.1", port, streams, chunkSize);
                var senderMeter = await sender.RunSenderAsync(tempSrc, testBytes);

                var receiverMeter = await receiverTask;

                Assert.True(File.Exists(tempDst));
                var dstData = await File.ReadAllBytesAsync(tempDst);
                Assert.Equal(data.Length, dstData.Length);
                Assert.Equal(data, dstData);
                Assert.True(senderMeter.AverageMegabytesPerSecond > 50.0);
            }
            finally
            {
                if (File.Exists(tempSrc)) File.Delete(tempSrc);
                if (File.Exists(tempDst)) File.Delete(tempDst);
            }
        }
    }
}

