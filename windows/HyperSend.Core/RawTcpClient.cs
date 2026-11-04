using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace HyperSend.Core
{
    public class RawTcpClient
    {
        private readonly string _targetHost;
        private readonly int _targetPort;
        private readonly int _streamCount;

        public RawTcpClient(string targetHost, int targetPort = NetworkConfig.DefaultPort, int streamCount = NetworkConfig.DefaultStreams)
        {
            _targetHost = targetHost;
            _targetPort = targetPort;
            _streamCount = streamCount;
        }

        public async Task<ThroughputMeter> RunSenderAsync(
            string? sourceFilePath = null,
            long syntheticBytes = 1024L * 1024L * 1024L * 2L, // 2 GB default
            IProgress<TransferProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var meter = new ThroughputMeter();
            var sockets = new Socket[_streamCount];
            long totalBytesToSend = syntheticBytes;

            if (!string.IsNullOrEmpty(sourceFilePath))
            {
                var fileInfo = new FileInfo(sourceFilePath);
                if (!fileInfo.Exists)
                {
                    throw new FileNotFoundException("Source file does not exist", sourceFilePath);
                }
                totalBytesToSend = fileInfo.Length;
            }

            try
            {
                // Connect all streams in parallel
                var connectTasks = new Task[_streamCount];
                for (int i = 0; i < _streamCount; i++)
                {
                    int streamIndex = i;
                    connectTasks[streamIndex] = Task.Run(async () =>
                    {
                        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        NetworkConfig.ConfigureSocket(socket);
                        await socket.ConnectAsync(_targetHost, _targetPort, cancellationToken);
                        sockets[streamIndex] = socket;
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
                long bytesPerStream = (totalBytesToSend + _streamCount - 1) / _streamCount;

                for (int i = 0; i < _streamCount; i++)
                {
                    int streamIndex = i;
                    var socket = sockets[streamIndex];
                    long streamOffset = streamIndex * bytesPerStream;
                    long streamTargetBytes = Math.Min(bytesPerStream, Math.Max(0, totalBytesToSend - streamOffset));

                    senderTasks[streamIndex] = Task.Run(() => StreamSenderWorker(
                        socket,
                        streamIndex,
                        sourceFilePath,
                        streamOffset,
                        streamTargetBytes,
                        meter,
                        cancellationToken), cancellationToken);
                }

                await Task.WhenAll(senderTasks);
                meter.Stop();

                return meter;
            }
            finally
            {
                foreach (var s in sockets)
                {
                    try { s?.Dispose(); } catch { }
                }
            }
        }

        private void StreamSenderWorker(
            Socket socket,
            int streamIndex,
            string? sourceFilePath,
            long fileOffset,
            long targetBytes,
            ThroughputMeter meter,
            CancellationToken token)
        {
            if (targetBytes <= 0) return;

            var buffer = ArrayPool<byte>.Shared.Rent(NetworkConfig.SocketBufferSize);
            long streamBytesSent = 0;

            try
            {
                using var networkStream = new NetworkStream(socket, ownsSocket: false);

                if (string.IsNullOrEmpty(sourceFilePath))
                {
                    // Synthetic in-memory stream: fill buffer with fixed test pattern once
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        buffer[i] = (byte)(i & 0xFF);
                    }

                    while (streamBytesSent < targetBytes && !token.IsCancellationRequested)
                    {
                        int toSend = (int)Math.Min(buffer.Length, targetBytes - streamBytesSent);
                        networkStream.Write(buffer, 0, toSend);
                        streamBytesSent += toSend;
                        meter.AddBytes(toSend);
                    }
                }
                else
                {
                    // Real file stream: open independent handle for this stream to avoid seek contention
                    using var fileStream = new FileStream(
                        sourceFilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 64 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);

                    fileStream.Seek(fileOffset, SeekOrigin.Begin);

                    while (streamBytesSent < targetBytes && !token.IsCancellationRequested)
                    {
                        int toRead = (int)Math.Min(buffer.Length, targetBytes - streamBytesSent);
                        int bytesRead = fileStream.Read(buffer, 0, toRead);
                        if (bytesRead == 0) break;

                        networkStream.Write(buffer, 0, bytesRead);
                        streamBytesSent += bytesRead;
                        meter.AddBytes(bytesRead);
                    }
                }

                networkStream.Flush();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}

