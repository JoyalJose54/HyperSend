package com.hypersend.engine

import java.util.concurrent.ConcurrentLinkedQueue

data class ChunkDescriptor(
    val chunkIndex: Long,
    val offset: Long,
    val length: Int
)

class ChunkWorkQueue(
    val totalBytes: Long,
    val chunkSize: Int = NetworkConfig.DEFAULT_CHUNK_SIZE
) {
    private val queue = ConcurrentLinkedQueue<ChunkDescriptor>()
    val totalChunks: Int

    init {
        if (totalBytes <= 0) {
            totalChunks = 0
        } else {
            val count = ((totalBytes + chunkSize - 1) / chunkSize).toInt()
            totalChunks = count
            for (i in 0 until count) {
                val offset = i.toLong() * chunkSize
                val len = minOf(chunkSize.toLong(), totalBytes - offset).toInt()
                queue.add(ChunkDescriptor(i.toLong(), offset, len))
            }
        }
    }

    fun pollNextChunk(): ChunkDescriptor? = queue.poll()

    val remainingChunks: Int
        get() = queue.size
}

