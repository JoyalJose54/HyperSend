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
    public class EncryptedChunkedReceiver : IAsyncDisposable
    {
        private readonly int _port;
        private readonly int _streamCount;
        private readonly byte[] _sessionMasterKey;
        private readonly ulong _expectedSessionId;
        private TcpListener? _listener;
        private readonly CancellationTokenSource _cts = new();

        public EncryptedChunkedReceiver(
            byte[] sessionMasterKey,
            ulong expectedSessionId = 1,
            int port = NetworkConfig.DefaultPort,
            int streamCount = NetworkConfig.DefaultStreams)
        {
            _sessionMasterKey = sessionMasterKey;
            _expectedSessionId = expectedSessionId;
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
            var sockets = new ConcurrentBag<Socket>();

            try
            {
                meter.Start();

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

                // Accept streams and read their 17-byte preamble to route by StreamId
                for (int i = 0; i < _streamCount; i++)
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    NetworkConfig.ConfigureSocket(client.Client);
                    sockets.Add(client.Client);

                    // Read 17-byte preamble: [Tag (8)][SessionId (8)][StreamId (1)]
                    var stream = client.GetStream();
                    byte[] preamble = new byte[17];
                    await stream.ReadExactlyAsync(preamble, 0, 17, token);

                    ulong sessionId = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(preamble.AsSpan(8, 8));
                    int streamId = preamble[16];
                    byte[] streamKey = CryptoEngine.DeriveStreamKey(_sessionMasterKey, streamId);

                    workerTasks[i] = Task.Run(() => WorkerReceiveLoop(
                        client.Client,
                        streamId,
                        streamKey,
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
            int streamId,
            byte[] streamKey,
            FileStream? fileStream,
            int maxChunkSize,
            ConcurrentDictionary<uint, byte> receivedChunks,
            int totalExpectedChunks,
            ThroughputMeter meter,
            CancellationToken token)
        {
            var headerBuffer = new byte[ChunkFrameHeader.Size];
            int maxPayloadWithTag = maxChunkSize + CryptoEngine.TagSize + 4096;
            var payloadBuffer = ArrayPool<byte>.Shared.Rent(maxPayloadWithTag);
            var plaintextBuffer = ArrayPool<byte>.Shared.Rent(maxChunkSize + 4096);
            var nonce = new byte[CryptoEngine.NonceSize];
            ulong counter = 0;

            try
            {
                using var netStream = new NetworkStream(socket, ownsSocket: false);

                while (!token.IsCancellationRequested)
                {
                    if (totalExpectedChunks > 0 && receivedChunks.Count >= totalExpectedChunks)
                        break;

                    if (!ReadExact(netStream, headerBuffer, 0, ChunkFrameHeader.Size))
                        break;

                    if (!ChunkFrameHeader.TryReadFrom(headerBuffer, out var header))
                        break;

                    if (header.Type == NetworkConfig.TypeFileDone)
                        break;

                    if (header.Type == NetworkConfig.TypeDataChunk)
                    {
                        int payloadLength = header.PayloadLength;
                        if (payloadLength < CryptoEngine.TagSize) break;

                        int plaintextLength = payloadLength - CryptoEngine.TagSize;

                        if (!ReadExact(netStream, payloadBuffer, 0, payloadLength))
                            break;

                        counter++;
                        CryptoEngine.FormatNonce(streamId, counter, nonce);

                        var ciphertextSpan = payloadBuffer.AsSpan(0, plaintextLength);
                        var tagSpan = payloadBuffer.AsSpan(plaintextLength, CryptoEngine.TagSize);
                        var plaintextSpan = plaintextBuffer.AsSpan(0, plaintextLength);
                        var aadSpan = headerBuffer.AsSpan(0, ChunkFrameHeader.Size);

                        CryptoEngine.Decrypt(streamKey, nonce, ciphertextSpan, tagSpan, plaintextSpan, aadSpan);

                        if (fileStream != null)
                        {
                            RandomAccess.Write(
                                fileStream.SafeFileHandle,
                                plaintextSpan,
                                (long)header.NonceOrOffset);
                        }

                        receivedChunks.TryAdd(header.ChunkIndex, 0);
                        meter.AddBytes(plaintextLength);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payloadBuffer);
                ArrayPool<byte>.Shared.Return(plaintextBuffer);
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
