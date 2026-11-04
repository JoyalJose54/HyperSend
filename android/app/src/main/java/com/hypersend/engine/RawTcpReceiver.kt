package com.hypersend.engine

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.withContext
import java.io.File
import java.io.RandomAccessFile
import java.net.InetSocketAddress
import java.nio.ByteBuffer
import java.nio.channels.ServerSocketChannel
import java.nio.channels.SocketChannel

data class ProgressReport(
    val bytesTransferred: Long,
    val totalBytes: Long,
    val currentMBps: Double,
    val averageMBps: Double,
    val elapsedSeconds: Double
) {
    val percentage: Double
        get() = if (totalBytes > 0) (bytesTransferred.toDouble() / totalBytes) * 100.0 else 0.0
}

class RawTcpReceiver(
    private val port: Int = NetworkConfig.DEFAULT_PORT,
    private val expectedStreams: Int = NetworkConfig.DEFAULT_STREAMS
) {
    private var serverChannel: ServerSocketChannel? = null

    suspend fun runReceiver(
        destinationFile: File? = null,
        expectedTotalBytes: Long = -1L,
        onProgress: ((ProgressReport) -> Unit)? = null
    ): ThroughputMeter = withContext(Dispatchers.IO) {
        val meter = ThroughputMeter()
        serverChannel = ServerSocketChannel.open().apply {
            socket().reuseAddress = true
            bind(InetSocketAddress(port), 16)
        }

        val clientChannels = mutableListOf<SocketChannel>()
        try {
            // Accept all parallel streams
            for (i in 0 until expectedStreams) {
                val channel = serverChannel!!.accept()
                NetworkConfig.configureChannel(channel)
                clientChannels.add(channel)
            }

            meter.start()

            val raf = destinationFile?.let {
                it.parentFile?.mkdirs()
                val r = RandomAccessFile(it, "rw")
                if (expectedTotalBytes > 0) {
                    r.setLength(expectedTotalBytes) // Pre-allocate to prevent disk fragmentation
                }
                r
            }

            val bytesPerStream = if (expectedTotalBytes > 0) {
                (expectedTotalBytes + expectedStreams - 1) / expectedStreams
            } else -1L

            val jobs = clientChannels.mapIndexed { index, channel ->
                val streamOffset = if (expectedTotalBytes > 0) index * bytesPerStream else -1L
                val targetBytes = if (expectedTotalBytes > 0) {
                    minOf(bytesPerStream, maxOf(0L, expectedTotalBytes - streamOffset))
                } else -1L

                async(Dispatchers.IO) {
                    val directBuf = ByteBuffer.allocateDirect(NetworkConfig.SOCKET_BUFFER_SIZE)
                    var streamBytes = 0L
                    var writeOffset = streamOffset

                    try {
                        while (true) {
                            directBuf.clear()
                            if (targetBytes > 0) {
                                val remaining = targetBytes - streamBytes
                                if (remaining <= 0) break
                                if (directBuf.capacity() > remaining) {
                                    directBuf.limit(remaining.toInt())
                                }
                            }

                            val read = channel.read(directBuf)
                            if (read <= 0) break

                            directBuf.flip()

                            if (raf != null && writeOffset >= 0) {
                                synchronized(raf) {
                                    raf.seek(writeOffset)
                                    val channelDest = raf.channel
                                    channelDest.write(directBuf)
                                }
                                writeOffset += read
                            }

                            streamBytes += read
                            meter.addBytes(read.toLong())

                            onProgress?.invoke(
                                ProgressReport(
                                    bytesTransferred = meter.currentTotalBytes,
                                    totalBytes = expectedTotalBytes,
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

            jobs.awaitAll()
            meter.stop()

            raf?.close()
            meter
        } finally {
            clientChannels.forEach { runCatching { it.close() } }
            serverChannel?.close()
        }
    }

    fun stop() {
        runCatching { serverChannel?.close() }
    }
}

