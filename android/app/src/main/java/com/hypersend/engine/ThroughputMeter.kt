package com.hypersend.engine

import java.util.concurrent.atomic.AtomicLong

class ThroughputMeter {
    private val totalBytes = AtomicLong(0)
    private var startTimeNs = 0L
    private var stopTimeNs = 0L
    private var isRunning = false

    private var lastBytes = 0L
    private var lastTimeNs = 0L

    @Synchronized
    fun start() {
        totalBytes.set(0)
        lastBytes = 0
        startTimeNs = System.nanoTime()
        lastTimeNs = startTimeNs
        stopTimeNs = 0L
        isRunning = true
    }

    @Synchronized
    fun stop() {
        stopTimeNs = System.nanoTime()
        isRunning = false
    }

    fun addBytes(bytes: Long) {
        totalBytes.addAndGet(bytes)
    }

    val currentTotalBytes: Long
        get() = totalBytes.get()

    val elapsedSeconds: Double
        get() {
            val end = if (isRunning) System.nanoTime() else stopTimeNs
            val durationNs = if (startTimeNs > 0) end - startTimeNs else 0L
            return durationNs / 1_000_000_000.0
        }

    val averageMegabytesPerSecond: Double
        get() {
            val sec = elapsedSeconds
            if (sec <= 0.001) return 0.0
            return (currentTotalBytes / (1024.0 * 1024.0)) / sec
        }

    @Synchronized
    fun getInstantaneousMegabytesPerSecond(): Double {
        if (!isRunning) return averageMegabytesPerSecond
        val now = System.nanoTime()
        val currentBytes = currentTotalBytes
        val deltaSec = (now - lastTimeNs) / 1_000_000_000.0

        if (deltaSec < 0.2) return averageMegabytesPerSecond

        val deltaBytes = currentBytes - lastBytes
        lastTimeNs = now
        lastBytes = currentBytes

        return (deltaBytes / (1024.0 * 1024.0)) / deltaSec
    }

    val averageMegabitsPerSecond: Double
        get() = averageMegabytesPerSecond * 8.0
}

