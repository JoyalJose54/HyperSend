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

class ChunkedTcpSender(
    private val targetHost: String,
    private val targetPort: Int = NetworkConfig.DEFAULT_PORT,
    private val streamCount: Int = NetworkConfig.DEFAULT_STREAMS,
    private val chunkSize: Int = NetworkConfig.DEFAULT_CHUNK_SIZE
) {
    suspend fun runSender(
        sourceFile: File? = null,
        syntheticBytes: Long = 2L * 1024 * 1024 * 1024,
        onProgress: ((ProgressReport) -> Unit)? = null
    ): ThroughputMeter = withContext(Dispatchers.IO) {
        val meter = ThroughputMeter()
        val totalBytes = sourceFile?.length() ?: syntheticBytes
        val workQueue = ChunkWorkQueue(totalBytes, chunkSize)

        val raf = sourceFile?.let { RandomAccessFile(it, "r") }
        val fileChannel = raf?.channel

        val channels = mutableListOf<SocketChannel>()

        try {
            val connectJobs = (0 until streamCount).map {
                async(Dispatchers.IO) {
                    val ch = SocketChannel.open()
                    NetworkConfig.configureChannel(ch)
                    ch.connect(InetSocketAddress(targetHost, targetPort))
                    ch
                }
            }
            channels.addAll(connectJobs.awaitAll())
            meter.start()

            val workerJobs = channels.map { channel ->
                async(Dispatchers.IO) {
                    val headerBuf = ByteBuffer.allocateDirect(ChunkFrameHeader.SIZE)
                    val payloadBuf = ByteBuffer.allocateDirect(chunkSize)

                    if (fileChannel == null) {
                        for (i in 0 until payloadBuf.capacity()) {
                            payloadBuf.put((i and 0xFF).toByte())
                        }
                    }

                    try {
                        while (true) {
                            val chunk = workQueue.pollNextChunk() ?: break

                            val len = chunk.length
                            if (fileChannel != null) {
                                payloadBuf.clear()
                                payloadBuf.limit(len)
                                fileChannel.read(payloadBuf, chunk.offset)
                                payloadBuf.flip()
                            } else {
                                payloadBuf.clear()
                                payloadBuf.limit(len)
                            }

                            val header = ChunkFrameHeader(
                                type = 0x06.toByte(), // DATA_CHUNK
                                fileId = 1L,
                                chunkIndex = chunk.chunkIndex,
                                payloadLength = len,
                                nonceOrOffset = chunk.offset
                            )

                            headerBuf.clear()
                            ChunkFrameHeader.writeTo(header, headerBuf)
                            headerBuf.flip()

                            // Write header
                            while (headerBuf.hasRemaining()) {
                                channel.write(headerBuf)
                            }

                            // Write payload
                            while (payloadBuf.hasRemaining()) {
                                val written = channel.write(payloadBuf)
                                if (written <= 0) break
                                meter.addBytes(written.toLong())
                            }

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

            // Send FILE_DONE frame on channel 0 if open
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

