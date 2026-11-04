package com.hypersend

import com.hypersend.engine.ThroughputMeter
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

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
}

