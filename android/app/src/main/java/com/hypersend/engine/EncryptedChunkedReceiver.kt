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
import java.util.concurrent.ConcurrentHashMap

class EncryptedChunkedReceiver(
    private val sessionMasterKey: ByteArray,
    private val expectedSessionId: Long = 1L,
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
            socket().receiveBufferSize = NetworkConfig.SOCKET_BUFFER_SIZE
            bind(InetSocketAddress(port), 32)
        }

        val clientChannels = mutableListOf<SocketChannel>()
        try {
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

            // Accept streams and read 17-byte preamble to route by StreamId
            val workerJobs = (0 until streamCount).map {
                val channel = serverChannel!!.accept()
                NetworkConfig.configureChannel(channel)
                clientChannels.add(channel)

                val preambleBuf = ByteBuffer.allocate(17)
                while (preambleBuf.hasRemaining()) {
                    channel.read(preambleBuf)
                }
                preambleBuf.flip()
                val tag = ByteArray(8)
                preambleBuf.get(tag)
                val sessionId = preambleBuf.long
                val streamId = preambleBuf.get().toInt() and 0xFF

                val streamKey = CryptoEngine.deriveStreamKey(sessionMasterKey, streamId)

                async(Dispatchers.IO) {
                    val headerBuf = ByteBuffer.allocateDirect(ChunkFrameHeader.SIZE)
                    var payloadBuf = ByteBuffer.allocateDirect(chunkSize + CryptoEngine.TAG_SIZE + 4096)
                    var plainBuf = ByteBuffer.allocateDirect(chunkSize + 4096)
                    var counter = 0L

                    try {
                        while (true) {
                            if (totalExpectedChunks > 0 && receivedChunks.size >= totalExpectedChunks)
                                break

                            headerBuf.clear()
                            while (headerBuf.hasRemaining()) {
                                val r = channel.read(headerBuf)
                                if (r <= 0) break
                            }
                            if (headerBuf.hasRemaining()) break

                            headerBuf.flip()
                            val header = ChunkFrameHeader.tryReadFrom(headerBuf) ?: break

                            if (header.type == 0x08.toByte()) { // FILE_DONE
                                break
                            }

                            if (header.type == 0x06.toByte()) { // DATA_CHUNK
                                val len = header.payloadLength
                                if (len < CryptoEngine.TAG_SIZE) break
                                val plaintextLen = len - CryptoEngine.TAG_SIZE

                                if (len > payloadBuf.capacity()) {
                                    payloadBuf = ByteBuffer.allocateDirect(len)
                                    plainBuf = ByteBuffer.allocateDirect(plaintextLen)
                                }
                                payloadBuf.clear()
                                payloadBuf.limit(len)

                                while (payloadBuf.hasRemaining()) {
                                    val r = channel.read(payloadBuf)
                                    if (r <= 0) break
                                }
                                if (payloadBuf.hasRemaining()) break

                                payloadBuf.flip()
                                plainBuf.clear()

                                counter++
                                val nonce = CryptoEngine.formatNonce(streamId, counter)

                                // Direct off-heap zero-allocation AES-GCM Decryption
                                headerBuf.position(0)
                                CryptoEngine.decryptDirect(
                                    key = streamKey,
                                    nonce = nonce,
                                    inputCiphertextAndTag = payloadBuf,
                                    outputPlaintext = plainBuf,
                                    associatedData = headerBuf
                                )

                                plainBuf.flip()

                                if (fileChannel != null) {
                                    fileChannel.write(plainBuf, header.nonceOrOffset)
                                }

                                receivedChunks[header.chunkIndex] = true
                                meter.addBytes(plaintextLen.toLong())

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
