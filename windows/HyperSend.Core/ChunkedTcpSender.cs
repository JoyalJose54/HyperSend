using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace HyperSend.Core
{
    public class ChunkedTcpSender
    {
        private readonly string _targetHost;
        private readonly int _targetPort;
        private readonly int _streamCount;
        private readonly int _chunkSize;

        public ChunkedTcpSender(
            string targetHost,
            int targetPort = NetworkConfig.DefaultPort,
            int streamCount = NetworkConfig.DefaultStreams,
            int chunkSize = NetworkConfig.DefaultChunkSize)
        {
            _targetHost = targetHost;
            _targetPort = targetPort;
            _streamCount = streamCount;
            _chunkSize = chunkSize;
        }

        public async Task<ThroughputMeter> RunSenderAsync(
            string? sourceFilePath = null,
            long syntheticBytes = 2L * 1024 * 1024 * 1024,
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var meter = new ThroughputMeter();
            var sockets = new Socket[_streamCount];
            long totalBytesToSend = syntheticBytes;

            FileStream? fileStream = null;
            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                var fi = new FileInfo(sourceFilePath);
                if (!fi.Exists) throw new FileNotFoundException("Source file not found", sourceFilePath);
                totalBytesToSend = fi.Length;
                fileStream = new FileStream(
                    sourceFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }

            var workQueue = new ChunkWorkQueue(totalBytesToSend, _chunkSize);

            try
            {
                // Connect N sockets in parallel
                var connectTasks = new Task[_streamCount];
                for (int i = 0; i < _streamCount; i++)
                {
                    int streamIndex = i;
                    connectTasks[streamIndex] = Task.Run(async () =>
                    {
                        var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        NetworkConfig.ConfigureSocket(s);
                        await s.ConnectAsync(_targetHost, _targetPort, cancellationToken);
                        sockets[streamIndex] = s;
                    }, cancellationToken);
                }

                await Task.WhenAll(connectTasks);
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
                            TotalBytes = totalBytesToSend,
                            CurrentMBps = meter.InstantaneousMegabytesPerSecond,
                            AverageMBps = summary.avgMBps,
                            ElapsedSeconds = summary.elapsedSec
                        });
                    }
                }, null, 250, 250);

                var senderTasks = new Task[_streamCount];
                for (int i = 0; i < _streamCount; i++)
                {
                    int streamIndex = i;
                    var socket = sockets[streamIndex];

                    senderTasks[streamIndex] = Task.Run(() => WorkerSendLoop(
                        socket,
                        fileStream,
                        workQueue,
                        meter,
                        cancellationToken), cancellationToken);
                }

                await Task.WhenAll(senderTasks);

                // Send FILE_DONE frame on stream 0 to signal receiver
                if (sockets.Length > 0 && sockets[0].Connected)
                {
                    var doneHeader = new ChunkFrameHeader(NetworkConfig.TypeFileDone, 1, (uint)workQueue.TotalChunks, 0, (ulong)totalBytesToSend);
                    var headerBytes = new byte[ChunkFrameHeader.Size];
                    doneHeader.WriteTo(headerBytes);
                    await sockets[0].SendAsync(headerBytes, SocketFlags.None, cancellationToken);
                }

                meter.Stop();
                return meter;
            }
            finally
            {
                fileStream?.Dispose();
                foreach (var s in sockets)
                {
                    try { s?.Dispose(); } catch { }
                }
            }
        }

        private void WorkerSendLoop(
            Socket socket,
            FileStream? fileStream,
            ChunkWorkQueue workQueue,
            ThroughputMeter meter,
            CancellationToken token)
        {
            var headerBytes = new byte[ChunkFrameHeader.Size];
            var payloadBuffer = ArrayPool<byte>.Shared.Rent(_chunkSize);

            // If synthetic, fill pattern once
            if (fileStream == null)
            {
                for (int i = 0; i < payloadBuffer.Length; i++)
                {
                    payloadBuffer[i] = (byte)(i & 0xFF);
                }
            }

            try
            {
                using var netStream = new NetworkStream(socket, ownsSocket: false);

                while (!token.IsCancellationRequested && workQueue.TryGetNextChunk(out var chunk))
                {
                    if (chunk == null) break;

                    int len = chunk.Length;
                    if (fileStream != null)
                    {
                        // Concurrent non-blocking read from file at exact offset
                        RandomAccess.Read(
                            fileStream.SafeFileHandle,
                            payloadBuffer.AsSpan(0, len),
                            chunk.Offset);
                    }

                    // Format 24-byte header
                    var header = new ChunkFrameHeader(
                        NetworkConfig.TypeDataChunk,
                        1,
                        chunk.ChunkIndex,
                        len,
                        (ulong)chunk.Offset);

                    header.WriteTo(headerBytes);

                    // Send Header + Payload
                    netStream.Write(headerBytes, 0, ChunkFrameHeader.Size);
                    netStream.Write(payloadBuffer, 0, len);

                    meter.AddBytes(len);
                }

                netStream.Flush();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payloadBuffer);
            }
        }
    }
}

