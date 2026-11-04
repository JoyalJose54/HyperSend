using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HyperSend.Core;
using Xunit;

namespace HyperSend.Tests
{
    public class EncryptedTransferTests
    {
        [Fact]
        public async Task EncryptedTransfer_Loopback_TransfersWithIntegrityAndAEAD()
        {
            int port = 5300;
            int streams = 4;
            int chunkSize = 1 * 1024 * 1024; // 1 MB chunks
            long testBytes = 20 * 1024 * 1024; // 20 MB

            byte[] masterKey = new byte[32];
            RandomNumberGenerator.Fill(masterKey);

            string tempSrc = Path.Combine(Path.GetTempPath(), $"enc_src_{Guid.NewGuid():N}.dat");
            string tempDst = Path.Combine(Path.GetTempPath(), $"enc_dst_{Guid.NewGuid():N}.dat");

            try
            {
                var rand = new Random(42);
                var data = new byte[testBytes];
                rand.NextBytes(data);
                await File.WriteAllBytesAsync(tempSrc, data);

                var receiver = new EncryptedChunkedReceiver(masterKey, expectedSessionId: 1, port: port, streamCount: streams);
                var receiverTask = Task.Run(async () => await receiver.RunReceiverAsync(tempDst, testBytes, chunkSize));

                await Task.Delay(150);

                var sender = new EncryptedChunkedSender("127.0.0.1", port, masterKey, sessionId: 1, streamCount: streams, chunkSize: chunkSize);
                var senderMeter = await sender.RunSenderAsync(tempSrc, testBytes);

                var receiverMeter = await receiverTask;

                Assert.True(File.Exists(tempDst));
                var dstData = await File.ReadAllBytesAsync(tempDst);
                Assert.Equal(data.Length, dstData.Length);
                Assert.Equal(data, dstData);

                // Hardware-accelerated AES-256-GCM should be very fast (>50 MB/s on loopback)
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
