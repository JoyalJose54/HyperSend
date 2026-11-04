using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace HyperSend.Core
{
    public class EncryptedChunkedSender
    {
        private readonly string _targetHost;
        private readonly int _targetPort;
        private readonly int _streamCount;
        private readonly int _chunkSize;
        private readonly byte[] _sessionMasterKey;
        private readonly ulong _sessionId;

        public EncryptedChunkedSender(
            string targetHost,
            int targetPort,
            byte[] sessionMasterKey,
            ulong sessionId = 1,
            int streamCount = NetworkConfig.DefaultStreams,
            int chunkSize = NetworkConfig.DefaultChunkSize)
        {
            _targetHost = targetHost;
            _targetPort = targetPort;
            _sessionMasterKey = sessionMasterKey;
            _sessionId = sessionId;
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
                var connectTasks = new Task[_streamCount];
                for (int i = 0; i < _streamCount; i++)
                {
                    int streamIndex = i;
                    int streamId = streamIndex + 1;

                    connectTasks[streamIndex] = Task.Run(async () =>
                    {
                        var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        NetworkConfig.ConfigureSocket(s);
                        await s.ConnectAsync(_targetHost, _targetPort, cancellationToken);

                        // Send 17-byte stream preamble: [HS_DATA\0 (8)][SessionId (8)][StreamId (1)]
                        byte[] preamble = new byte[17];
                        Buffer.BlockCopy(ControlProtocol.TagData, 0, preamble, 0, 8);
                        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(preamble.AsSpan(8, 8), _sessionId);
                        preamble[16] = (byte)streamId;

                        await s.SendAsync(preamble, SocketFlags.None, cancellationToken);
                        sockets[streamIndex] = s;
                    }, cancellationToken);
                }

                await Task.WhenAll(connectTasks);
                meter.Start();

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
                    int streamId = streamIndex + 1;
                    byte[] streamKey = CryptoEngine.DeriveStreamKey(_sessionMasterKey, streamId);

                    senderTasks[streamIndex] = Task.Run(() => WorkerSendLoop(
                        socket,
                        streamId,
                        streamKey,
                        fileStream,
                        workQueue,
                        meter,
                        cancellationToken), cancellationToken);
                }

                await Task.WhenAll(senderTasks);

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
            int streamId,
            byte[] streamKey,
            FileStream? fileStream,
            ChunkWorkQueue workQueue,
            ThroughputMeter meter,
            CancellationToken token)
        {
            int maxPayloadWithTag = _chunkSize + CryptoEngine.TagSize;
            int totalFrameSize = ChunkFrameHeader.Size + maxPayloadWithTag;

            var frameBuffer = ArrayPool<byte>.Shared.Rent(totalFrameSize);
            var plaintextBuffer = ArrayPool<byte>.Shared.Rent(_chunkSize);
            var nonce = new byte[CryptoEngine.NonceSize];
            ulong counter = 0;

            if (fileStream == null)
            {
                for (int i = 0; i < plaintextBuffer.Length; i++)
                    plaintextBuffer[i] = (byte)(i & 0xFF);
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
                        RandomAccess.Read(fileStream.SafeFileHandle, plaintextBuffer.AsSpan(0, len), chunk.Offset);
                    }

                    counter++;
                    CryptoEngine.FormatNonce(streamId, counter, nonce);

                    var header = new ChunkFrameHeader(
                        NetworkConfig.TypeDataChunk,
                        1,
                        chunk.ChunkIndex,
                        len + CryptoEngine.TagSize,
                        (ulong)chunk.Offset);

                    header.WriteTo(frameBuffer.AsSpan(0, ChunkFrameHeader.Size));

                    var ciphertextSpan = frameBuffer.AsSpan(ChunkFrameHeader.Size, len);
                    var tagSpan = frameBuffer.AsSpan(ChunkFrameHeader.Size + len, CryptoEngine.TagSize);
                    var aadSpan = frameBuffer.AsSpan(0, ChunkFrameHeader.Size);

                    CryptoEngine.Encrypt(streamKey, nonce, plaintextBuffer.AsSpan(0, len), ciphertextSpan, tagSpan, aadSpan);

                    int totalSendBytes = ChunkFrameHeader.Size + len + CryptoEngine.TagSize;
                    netStream.Write(frameBuffer, 0, totalSendBytes);

                    meter.AddBytes(len);
                }

                netStream.Flush();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(frameBuffer);
                ArrayPool<byte>.Shared.Return(plaintextBuffer);
            }
        }
    }
}
