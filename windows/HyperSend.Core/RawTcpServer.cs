using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace HyperSend.Core
{
    public class TransferProgress
    {
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public double CurrentMBps { get; set; }
        public double AverageMBps { get; set; }
        public double ElapsedSeconds { get; set; }
        public double Percentage => TotalBytes > 0 ? (double)BytesTransferred / TotalBytes * 100.0 : 0.0;
    }

    public class RawTcpServer : IAsyncDisposable
    {
        private readonly int _port;
        private readonly int _expectedStreams;
        private TcpListener? _listener;
        private readonly CancellationTokenSource _cts = new();

        public RawTcpServer(int port = NetworkConfig.DefaultPort, int expectedStreams = NetworkConfig.DefaultStreams)
        {
            _port = port;
            _expectedStreams = expectedStreams;
        }

        public async Task<ThroughputMeter> RunReceiverAsync(
            string? destinationFilePath = null,
            long expectedTotalBytes = -1,
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
            var token = linkedCts.Token;

            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start(16);

            var meter = new ThroughputMeter();
            var sockets = new Socket[_expectedStreams];

            try
            {
                // Accept all expected parallel data connections
                for (int i = 0; i < _expectedStreams; i++)
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    NetworkConfig.ConfigureSocket(client.Client);
                    sockets[i] = client.Client;
                }

                meter.Start();

                // Periodic progress timer
                using var progressTimer = new Timer(_ =>
                {
                    if (progress != null)
                    {
                        var summary = meter.GetSummary();
                        progress.Report(new TransferProgress
                        {
                            BytesTransferred = summary.totalBytes,
                            TotalBytes = expectedTotalBytes,
                            CurrentMBps = meter.InstantaneousMegabytesPerSecond,
                            AverageMBps = summary.avgMBps,
                            ElapsedSeconds = summary.elapsedSec
                        });
                    }
                }, null, 250, 250);

                FileStream? fileStream = null;
                if (!string.IsNullOrEmpty(destinationFilePath))
                {
                    var dir = Path.GetDirectoryName(destinationFilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    fileStream = new FileStream(
                        destinationFilePath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.ReadWrite,
                        bufferSize: 64 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);

                    if (expectedTotalBytes > 0)
                    {
                        // Pre-allocate to prevent disk fragmentation and mid-transfer allocation stalls
                        fileStream.SetLength(expectedTotalBytes);
                    }
                }

                var workerTasks = new Task[_expectedStreams];
                long bytesPerStream = expectedTotalBytes > 0 ? (expectedTotalBytes + _expectedStreams - 1) / _expectedStreams : -1;

                for (int i = 0; i < _expectedStreams; i++)
                {
                    int streamIndex = i;
                    var socket = sockets[streamIndex];
                    long streamTargetBytes = -1;
                    long streamOffset = -1;

                    if (expectedTotalBytes > 0)
                    {
                        streamOffset = streamIndex * bytesPerStream;
                        streamTargetBytes = Math.Min(bytesPerStream, Math.Max(0, expectedTotalBytes - streamOffset));
                    }

                    workerTasks[streamIndex] = Task.Run(() => StreamReceiverWorker(
                        socket,
                        streamIndex,
                        fileStream,
                        streamOffset,
                        streamTargetBytes,
                        meter,
                        token), token);
                }

                await Task.WhenAll(workerTasks);

                meter.Stop();

                if (fileStream != null)
                {
                    await fileStream.FlushAsync(token);
                    fileStream.Dispose();
                }

                return meter;
            }
            finally
            {
                foreach (var s in sockets)
                {
                    try { s?.Dispose(); } catch { }
                }
                _listener.Stop();
            }
        }

        private void StreamReceiverWorker(
            Socket socket,
            int streamIndex,
            FileStream? fileStream,
            long fileOffset,
            long targetBytes,
            ThroughputMeter meter,
            CancellationToken token)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(NetworkConfig.SocketBufferSize);
            long streamBytesReceived = 0;
            long currentOffset = fileOffset;

            try
            {
                using var networkStream = new NetworkStream(socket, ownsSocket: false);

                while (!token.IsCancellationRequested)
                {
                    int toRead = buffer.Length;
                    if (targetBytes > 0)
                    {
                        long remaining = targetBytes - streamBytesReceived;
                        if (remaining <= 0) break;
                        toRead = (int)Math.Min(buffer.Length, remaining);
                    }

                    int bytesRead = networkStream.Read(buffer, 0, toRead);
                    if (bytesRead == 0) break; // socket closed cleanly

                    if (fileStream != null && currentOffset >= 0)
                    {
                        lock (fileStream)
                        {
                            fileStream.Seek(currentOffset, SeekOrigin.Begin);
                            fileStream.Write(buffer, 0, bytesRead);
                        }
                        currentOffset += bytesRead;
                    }

                    streamBytesReceived += bytesRead;
                    meter.AddBytes(bytesRead);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener?.Stop();
            await Task.Yield();
        }
    }
}

