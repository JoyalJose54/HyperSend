using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace HyperSend.Core
{
    public class ChunkedTcpReceiver : IAsyncDisposable
    {
        private readonly int _port;
        private readonly int _streamCount;
        private TcpListener? _listener;
        private readonly CancellationTokenSource _cts = new();

        public ChunkedTcpReceiver(int port = NetworkConfig.DefaultPort, int streamCount = NetworkConfig.DefaultStreams)
        {
            _port = port;
            _streamCount = streamCount;
        }

        public async Task<ThroughputMeter> RunReceiverAsync(
            string? destinationFilePath,
            long expectedTotalBytes,
            int chunkSize = NetworkConfig.DefaultChunkSize,
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
            var token = linkedCts.Token;

            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start(16);

            var meter = new ThroughputMeter();
            var sockets = new Socket[_streamCount];

            try
            {
                // Accept N worker streams
                for (int i = 0; i < _streamCount; i++)
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    NetworkConfig.ConfigureSocket(client.Client);
                    sockets[i] = client.Client;
                }

                meter.Start();

                // Periodic progress reporter
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
                        FileAccess.ReadWrite,
                        FileShare.ReadWrite,
                        bufferSize: 64 * 1024,
                        FileOptions.Asynchronous);

                    if (expectedTotalBytes > 0)
                    {
                        fileStream.SetLength(expectedTotalBytes);
                    }
                }

                int totalExpectedChunks = (int)((expectedTotalBytes + chunkSize - 1) / chunkSize);
                var receivedChunks = new ConcurrentDictionary<uint, byte>();
                var workerTasks = new Task[_streamCount];

                for (int i = 0; i < _streamCount; i++)
                {
                    int streamIndex = i;
                    var socket = sockets[streamIndex];

                    workerTasks[streamIndex] = Task.Run(() => WorkerReceiveLoop(
                        socket,
                        fileStream,
                        chunkSize,
                        receivedChunks,
                        totalExpectedChunks,
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

        private void WorkerReceiveLoop(
            Socket socket,
            FileStream? fileStream,
            int maxChunkSize,
            ConcurrentDictionary<uint, byte> receivedChunks,
            int totalExpectedChunks,
            ThroughputMeter meter,
            CancellationToken token)
        {
            var headerBuffer = new byte[ChunkFrameHeader.Size];
            var payloadBuffer = ArrayPool<byte>.Shared.Rent(maxChunkSize + 4096);

            try
            {
                using var netStream = new NetworkStream(socket, ownsSocket: false);

                while (!token.IsCancellationRequested)
                {
                    if (totalExpectedChunks > 0 && receivedChunks.Count >= totalExpectedChunks)
                        break;

                    // Read 24-byte Frame Header
                    if (!ReadExact(netStream, headerBuffer, 0, ChunkFrameHeader.Size))
                        break;

                    if (!ChunkFrameHeader.TryReadFrom(headerBuffer, out var header))
                        break;

                    if (header.Type == NetworkConfig.TypeFileDone)
                        break;

                    if (header.Type == NetworkConfig.TypeDataChunk)
                    {
                        int payloadLength = header.PayloadLength;
                        if (payloadLength > payloadBuffer.Length)
                        {
                            ArrayPool<byte>.Shared.Return(payloadBuffer);
                            payloadBuffer = ArrayPool<byte>.Shared.Rent(payloadLength);
                        }

                        // Read full payload
                        if (!ReadExact(netStream, payloadBuffer, 0, payloadLength))
                            break;

                        // Out-of-order write directly to target file offset without lock contention
                        if (fileStream != null)
                        {
                            RandomAccess.Write(
                                fileStream.SafeFileHandle,
                                payloadBuffer.AsSpan(0, payloadLength),
                                (long)header.NonceOrOffset);
                        }

                        receivedChunks.TryAdd(header.ChunkIndex, 0);
                        meter.AddBytes(payloadLength);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payloadBuffer);
            }
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = stream.Read(buffer, offset + totalRead, count - totalRead);
                if (read == 0) return false;
                totalRead += read;
            }
            return true;
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener?.Stop();
            await Task.Yield();
        }
    }
}

