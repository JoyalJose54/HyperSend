package com.hypersend.engine

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.withContext
import java.io.File
import java.io.RandomAccessFile
import java.net.InetSocketAddress
import java.nio.ByteBuffer
import java.nio.channels.SocketChannel

class EncryptedChunkedSender(
    private val targetHost: String,
    private val targetPort: Int = NetworkConfig.DEFAULT_PORT,
    private val sessionMasterKey: ByteArray,
    private val sessionId: Long = 1L,
    private val streamCount: Int = NetworkConfig.DEFAULT_STREAMS,
    private val chunkSize: Int = NetworkConfig.DEFAULT_CHUNK_SIZE
) {
    suspend fun runSender(
        sourceFile: File? = null,
        fileChannel: java.nio.channels.FileChannel? = null,
        totalBytesOverride: Long? = null,
        syntheticBytes: Long = 2L * 1024 * 1024 * 1024,
        onProgress: ((ProgressReport) -> Unit)? = null
    ): ThroughputMeter = withContext(Dispatchers.IO) {
        val meter = ThroughputMeter()
        val totalBytes = totalBytesOverride ?: sourceFile?.length() ?: fileChannel?.size() ?: syntheticBytes
        val workQueue = ChunkWorkQueue(totalBytes, chunkSize)

        val raf = if (fileChannel == null && sourceFile != null) RandomAccessFile(sourceFile, "r") else null
        val effectiveChannel = fileChannel ?: raf?.channel

        val channels = mutableListOf<SocketChannel>()

        try {
            val connectJobs = (0 until streamCount).map { index ->
                val streamId = index + 1
                async(Dispatchers.IO) {
                    val ch = SocketChannel.open()
                    NetworkConfig.configureChannel(ch)
                    ch.connect(InetSocketAddress(targetHost, targetPort))

                    // 17-byte stream preamble
                    val preamble = ByteBuffer.allocate(17)
                    preamble.put("HS_DATA\u0000".toByteArray(Charsets.US_ASCII))
                    preamble.putLong(sessionId)
                    preamble.put(streamId.toByte())
                    preamble.flip()
                    while (preamble.hasRemaining()) {
                        ch.write(preamble)
                    }

                    ch
                }
            }
            channels.addAll(connectJobs.awaitAll())
            meter.start()

            val workerJobs = channels.mapIndexed { index, channel ->
                val streamId = index + 1
                val streamKey = CryptoEngine.deriveStreamKey(sessionMasterKey, streamId)

                async(Dispatchers.IO) {
                    val headerBuf = ByteBuffer.allocateDirect(ChunkFrameHeader.SIZE)
                    val plaintextBuf = ByteBuffer.allocateDirect(chunkSize)
                    val encryptedBuf = ByteBuffer.allocateDirect(chunkSize + CryptoEngine.TAG_SIZE + 4096)
                    var counter = 0L

                    if (effectiveChannel == null) {
                        for (i in 0 until chunkSize) {
                            plaintextBuf.put((i and 0xFF).toByte())
                        }
                    }

                    try {
                        while (true) {
                            val chunk = workQueue.pollNextChunk() ?: break

                            val len = chunk.length
                            plaintextBuf.clear()
                            plaintextBuf.limit(len)

                            if (effectiveChannel != null) {
                                effectiveChannel.read(plaintextBuf, chunk.offset)
                                plaintextBuf.flip()
                            } else {
                                plaintextBuf.position(0)
                            }

                            counter++
                            val nonce = CryptoEngine.formatNonce(streamId, counter)

                            val header = ChunkFrameHeader(
                                type = 0x06.toByte(), // DATA_CHUNK
                                fileId = 1L,
                                chunkIndex = chunk.chunkIndex,
                                payloadLength = len + CryptoEngine.TAG_SIZE,
                                nonceOrOffset = chunk.offset
                            )

                            headerBuf.clear()
                            ChunkFrameHeader.writeTo(header, headerBuf)
                            headerBuf.flip()

                            encryptedBuf.clear()

                            // Zero-allocation direct ByteBuffer AES-GCM encryption in Conscrypt BoringSSL
                            CryptoEngine.encryptDirect(
                                key = streamKey,
                                nonce = nonce,
                                inputPlaintext = plaintextBuf,
                                outputCiphertextAndTag = encryptedBuf,
                                associatedData = headerBuf.duplicate()
                            )

                            encryptedBuf.flip()

                            // Send Header
                            while (headerBuf.hasRemaining()) {
                                channel.write(headerBuf)
                            }

                            // Send Encrypted Payload
                            while (encryptedBuf.hasRemaining()) {
                                val written = channel.write(encryptedBuf)
                                if (written <= 0) break
                            }

                            meter.addBytes(len.toLong())

                            onProgress?.invoke(
                                ProgressReport(
                                    bytesTransferred = meter.currentTotalBytes,
                                    totalBytes = totalBytes,
                                    currentMBps = meter.getInstantaneousMegabytesPerSecond(),
                                    averageMBps = meter.averageMegabytesPerSecond,
                                    elapsedSeconds = meter.elapsedSeconds
                                )
                            )
                        }
                    } finally {
                        channel.close()
                    }
                }
            }

            workerJobs.awaitAll()

            if (channels.isNotEmpty() && channels[0].isOpen) {
                runCatching {
                    val doneHeader = ChunkFrameHeader(0x08.toByte(), 1L, workQueue.totalChunks.toLong(), 0, totalBytes)
                    val doneBuf = ByteBuffer.allocateDirect(ChunkFrameHeader.SIZE)
                    ChunkFrameHeader.writeTo(doneHeader, doneBuf)
                    doneBuf.flip()
                    while (doneBuf.hasRemaining()) {
                        channels[0].write(doneBuf)
                    }
                }
            }

            meter.stop()
            raf?.close()
            meter
        } finally {
            raf?.close()
            channels.forEach { runCatching { it.close() } }
        }
    }
}
