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

class RawTcpSender(
    private val targetHost: String,
    private val targetPort: Int = NetworkConfig.DEFAULT_PORT,
    private val streamCount: Int = NetworkConfig.DEFAULT_STREAMS
) {
    suspend fun runSender(
        sourceFile: File? = null,
        syntheticBytes: Long = 2L * 1024 * 1024 * 1024, // 2 GB default
        onProgress: ((ProgressReport) -> Unit)? = null
    ): ThroughputMeter = withContext(Dispatchers.IO) {
        val meter = ThroughputMeter()
        val totalBytes = sourceFile?.length() ?: syntheticBytes
        val channels = mutableListOf<SocketChannel>()

        try {
            // Connect all sockets in parallel
            val connectJobs = (0 until streamCount).map {
                async(Dispatchers.IO) {
                    val channel = SocketChannel.open()
                    NetworkConfig.configureChannel(channel)
                    channel.connect(InetSocketAddress(targetHost, targetPort))
                    channel
                }
            }
            channels.addAll(connectJobs.awaitAll())

            meter.start()

            val bytesPerStream = (totalBytes + streamCount - 1) / streamCount

            val workerJobs = channels.mapIndexed { index, channel ->
                val streamOffset = index * bytesPerStream
                val targetBytes = minOf(bytesPerStream, maxOf(0L, totalBytes - streamOffset))

                async(Dispatchers.IO) {
                    val directBuf = ByteBuffer.allocateDirect(NetworkConfig.SOCKET_BUFFER_SIZE)
                    // Fill synthetic pattern once
                    if (sourceFile == null) {
                        for (i in 0 until directBuf.capacity()) {
                            directBuf.put((i and 0xFF).toByte())
                        }
                    }

                    val raf = sourceFile?.let { RandomAccessFile(it, "r") }
                    val fileChannel = raf?.channel

                    try {
                        var streamBytesSent = 0L

                        while (streamBytesSent < targetBytes) {
                            val toSend = minOf(directBuf.capacity().toLong(), targetBytes - streamBytesSent).toInt()

                            if (fileChannel != null) {
                                directBuf.clear()
                                directBuf.limit(toSend)
                                val read = fileChannel.read(directBuf, streamOffset + streamBytesSent)
                                if (read <= 0) break
                                directBuf.flip()
                            } else {
                                directBuf.clear()
                                directBuf.limit(toSend)
                            }

                            while (directBuf.hasRemaining()) {
                                val written = channel.write(directBuf)
                                if (written <= 0) break
                                streamBytesSent += written
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
                        raf?.close()
                        channel.close()
                    }
                }
            }

            workerJobs.awaitAll()
            meter.stop()
            meter
        } finally {
            channels.forEach { runCatching { it.close() } }
        }
    }
}

