package com.hypersend.engine

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.withContext
import java.io.File
import java.io.RandomAccessFile
import java.net.InetSocketAddress
import java.nio.ByteBuffer
import java.nio.channels.FileChannel
import java.nio.channels.ServerSocketChannel
import java.nio.channels.SocketChannel
import java.util.concurrent.ConcurrentHashMap

class ChunkedTcpReceiver(
    private val port: Int = NetworkConfig.DEFAULT_PORT,
    private val streamCount: Int = NetworkConfig.DEFAULT_STREAMS
) {
    private var serverChannel: ServerSocketChannel? = null

    suspend fun runReceiver(
        destinationFile: File? = null,
        expectedTotalBytes: Long,
        chunkSize: Int = NetworkConfig.DEFAULT_CHUNK_SIZE,
        onProgress: ((ProgressReport) -> Unit)? = null
    ): ThroughputMeter = withContext(Dispatchers.IO) {
        val meter = ThroughputMeter()
        serverChannel = ServerSocketChannel.open().apply {
            socket().reuseAddress = true
            bind(InetSocketAddress(port), 16)
        }

        val clientChannels = mutableListOf<SocketChannel>()
        try {
            for (i in 0 until streamCount) {
                val channel = serverChannel!!.accept()
                NetworkConfig.configureChannel(channel)
                clientChannels.add(channel)
            }

            meter.start()

            val raf = destinationFile?.let {
                it.parentFile?.mkdirs()
                val r = RandomAccessFile(it, "rw")
                if (expectedTotalBytes > 0) {
                    r.setLength(expectedTotalBytes)
                }
                r
            }
            val fileChannel = raf?.channel

            val totalExpectedChunks = ((expectedTotalBytes + chunkSize - 1) / chunkSize).toInt()
            val receivedChunks = ConcurrentHashMap<Long, Boolean>()

            val workerJobs = clientChannels.map { channel ->
                async(Dispatchers.IO) {
                    val headerBuf = ByteBuffer.allocateDirect(ChunkFrameHeader.SIZE)
                    var payloadBuf = ByteBuffer.allocateDirect(chunkSize + 4096)

                    try {
                        while (true) {
                            if (totalExpectedChunks > 0 && receivedChunks.size >= totalExpectedChunks)
                                break

                            headerBuf.clear()
                            while (headerBuf.hasRemaining()) {
                                val r = channel.read(headerBuf)
                                if (r <= 0) break
                            }
                            if (headerBuf.hasRemaining()) break // stream ended prematurely

                            headerBuf.flip()
                            val header = ChunkFrameHeader.tryReadFrom(headerBuf) ?: break

                            if (header.type == 0x08.toByte()) { // FILE_DONE
                                break
                            }

                            if (header.type == 0x06.toByte()) { // DATA_CHUNK
                                val len = header.payloadLength
                                if (len > payloadBuf.capacity()) {
                                    payloadBuf = ByteBuffer.allocateDirect(len)
                                }
                                payloadBuf.clear()
                                payloadBuf.limit(len)

                                while (payloadBuf.hasRemaining()) {
                                    val r = channel.read(payloadBuf)
                                    if (r <= 0) break
                                }
                                if (payloadBuf.hasRemaining()) break

                                payloadBuf.flip()

                                if (fileChannel != null) {
                                    // Positional thread-safe write directly to chunk offset
                                    fileChannel.write(payloadBuf, header.nonceOrOffset)
                                }

                                receivedChunks[header.chunkIndex] = true
                                meter.addBytes(len.toLong())

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
                        }
                    } finally {
                        channel.close()
                    }
                }
            }

            workerJobs.awaitAll()
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

