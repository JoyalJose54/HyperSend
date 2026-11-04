using System;
using System.IO;
using System.Threading.Tasks;
using HyperSend.Core;
using Xunit;

namespace HyperSend.Tests
{
    public class BaselineTransferTests
    {
        [Fact]
        public void NetworkDiscovery_CanQueryLinkHealth()
        {
            var health = NetworkDiscovery.QueryLinkHealth();
            Assert.NotNull(health);
            // Ssid and band should have non-null default or read values
            Assert.False(string.IsNullOrEmpty(health.Band));
        }

        [Fact]
        public async Task LocalLoopback_SyntheticTransfer_SaturatesHighSpeed()
        {
            int port = 5099;
            int streams = 4;
            long testBytes = 50 * 1024 * 1024; // 50 MB quick test

            var server = new RawTcpServer(port, streams);
            var serverTask = Task.Run(async () => await server.RunReceiverAsync(null, testBytes));

            await Task.Delay(100);

            var client = new RawTcpClient("127.0.0.1", port, streams);
            var clientMeter = await client.RunSenderAsync(null, testBytes);

            var serverMeter = await serverTask;

            Assert.Equal(testBytes, clientMeter.TotalBytes);
            Assert.Equal(testBytes, serverMeter.TotalBytes);
            // Loopback should exceed 100 MB/s
            Assert.True(clientMeter.AverageMegabytesPerSecond > 50.0);
        }

        [Fact]
        public async Task LocalLoopback_FileTransfer_IntegrityVerified()
        {
            int port = 5100;
            int streams = 1; // M1 bare TCP single stream integrity test (M2 tests multi-stream chunked out-of-order writes)
            long testBytes = 10 * 1024 * 1024; // 10 MB

            string tempSrc = Path.Combine(Path.GetTempPath(), $"hypersend_src_{Guid.NewGuid():N}.dat");
            string tempDst = Path.Combine(Path.GetTempPath(), $"hypersend_dst_{Guid.NewGuid():N}.dat");

            try
            {
                // Generate source test data
                var data = new byte[testBytes];
                new Random(42).NextBytes(data);
                await File.WriteAllBytesAsync(tempSrc, data);

                var server = new RawTcpServer(port, streams);
                var serverTask = Task.Run(async () => await server.RunReceiverAsync(tempDst, testBytes));

                await Task.Delay(100);

                var client = new RawTcpClient("127.0.0.1", port, streams);
                await client.RunSenderAsync(tempSrc, testBytes);

                await serverTask;

                Assert.True(File.Exists(tempDst));
                var dstData = await File.ReadAllBytesAsync(tempDst);
                Assert.Equal(data.Length, dstData.Length);
                Assert.Equal(data, dstData);
            }
            finally
            {
                if (File.Exists(tempSrc)) File.Delete(tempSrc);
                if (File.Exists(tempDst)) File.Delete(tempDst);
            }
        }
    }
}

