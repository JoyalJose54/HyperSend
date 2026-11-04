package com.hypersend

import com.hypersend.engine.ChunkFrameHeader
import com.hypersend.engine.ChunkWorkQueue
import com.hypersend.engine.ThroughputMeter
import org.junit.Assert.*
import org.junit.Test
import java.nio.ByteBuffer

class BaselineEngineTest {

    @Test
    fun testThroughputMeterCalculations() {
        val meter = ThroughputMeter()
        meter.start()
        meter.addBytes(100 * 1024 * 1024) // 100 MB
        Thread.sleep(100) // 100 ms
        meter.stop()

        assertEquals(100 * 1024 * 1024L, meter.currentTotalBytes)
        assertTrue(meter.elapsedSeconds > 0.05)
        assertTrue(meter.averageMegabytesPerSecond > 0.0)
        assertTrue(meter.averageMegabitsPerSecond > 0.0)
    }

    @Test
    fun testChunkFrameHeaderSerialization() {
        val header = ChunkFrameHeader(
            type = 0x06.toByte(),
            fileId = 42L,
            chunkIndex = 7L,
            payloadLength = 2 * 1024 * 1024,
            nonceOrOffset = 14 * 1024 * 1024L
        )

        val buffer = ByteBuffer.allocate(ChunkFrameHeader.SIZE)
        ChunkFrameHeader.writeTo(header, buffer)
        buffer.flip()

        val parsed = ChunkFrameHeader.tryReadFrom(buffer)
        assertNotNull(parsed)
        assertEquals(0x06.toByte(), parsed!!.type)
        assertEquals(42L, parsed.fileId)
        assertEquals(7L, parsed.chunkIndex)
        assertEquals(2 * 1024 * 1024, parsed.payloadLength)
        assertEquals(14 * 1024 * 1024L, parsed.nonceOrOffset)
    }

    @Test
    fun testChunkWorkQueuePartitioning() {
        val totalBytes = 5 * 1024 * 1024L // 5 MB
        val chunkSize = 2 * 1024 * 1024 // 2 MB

        val queue = ChunkWorkQueue(totalBytes, chunkSize)
        assertEquals(3, queue.totalChunks)

        val c0 = queue.pollNextChunk()
        assertNotNull(c0)
        assertEquals(0L, c0!!.chunkIndex)
        assertEquals(0L, c0.offset)
        assertEquals(2 * 1024 * 1024, c0.length)

        val c1 = queue.pollNextChunk()
        assertNotNull(c1)
        assertEquals(1L, c1!!.chunkIndex)
        assertEquals(2 * 1024 * 1024L, c1.offset)
        assertEquals(2 * 1024 * 1024, c1.length)

        val c2 = queue.pollNextChunk()
        assertNotNull(c2)
        assertEquals(2L, c2!!.chunkIndex)
        assertEquals(4 * 1024 * 1024L, c2.offset)
        assertEquals(1 * 1024 * 1024, c2.length)

        assertNull(queue.pollNextChunk())
    }
}
