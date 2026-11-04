package com.hypersend.engine

import java.nio.ByteBuffer

data class ChunkFrameHeader(
    val type: Byte,
    val fileId: Long,
    val chunkIndex: Long,
    val payloadLength: Int,
    val nonceOrOffset: Long
) {
    companion object {
        const val SIZE = 24
        val EXPECTED_MAGIC = byteArrayOf(0x48.toByte(), 0x53.toByte()) // "HS"
        const val CURRENT_VERSION: Byte = 1

        fun writeTo(header: ChunkFrameHeader, buffer: ByteBuffer) {
            buffer.put(EXPECTED_MAGIC[0])
            buffer.put(EXPECTED_MAGIC[1])
            buffer.put(CURRENT_VERSION)
            buffer.put(header.type)
            buffer.putInt(header.fileId.toInt())
            buffer.putInt(header.chunkIndex.toInt())
            buffer.putInt(header.payloadLength)
            buffer.putLong(header.nonceOrOffset)
        }

        fun tryReadFrom(buffer: ByteBuffer): ChunkFrameHeader? {
            if (buffer.remaining() < SIZE) return null
            val m0 = buffer.get()
            val m1 = buffer.get()
            if (m0 != EXPECTED_MAGIC[0] || m1 != EXPECTED_MAGIC[1]) return null

            val version = buffer.get()
            val type = buffer.get()
            val fileId = buffer.int.toLong() and 0xFFFFFFFFL
            val chunkIndex = buffer.int.toLong() and 0xFFFFFFFFL
            val payloadLength = buffer.int
            val nonceOrOffset = buffer.long

            return ChunkFrameHeader(type, fileId, chunkIndex, payloadLength, nonceOrOffset)
        }
    }
}

