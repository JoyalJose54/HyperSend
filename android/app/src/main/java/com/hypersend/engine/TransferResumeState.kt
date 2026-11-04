package com.hypersend.engine

import android.util.Base64
import org.json.JSONObject
import java.io.File
import java.util.BitSet

class TransferResumeState(
    val filePath: String,
    val totalBytes: Long,
    val chunkSize: Int,
    val transferId: String = ""
) {
    val stateFile = File("$filePath.hypersend-state")
    val partFile = File("$filePath.part")
    val totalChunks = ((totalBytes + chunkSize - 1) / chunkSize).toInt()
    val completedChunks = BitSet(totalChunks)

    init {
        loadStateIfExists()
    }

    @Synchronized
    fun markChunkCompleted(chunkIndex: Int) {
        if (chunkIndex in 0 until totalChunks) {
            completedChunks.set(chunkIndex)
        }
    }

    @Synchronized
    fun isChunkCompleted(chunkIndex: Int): Boolean {
        return if (chunkIndex in 0 until totalChunks) completedChunks.get(chunkIndex) else false
    }

    val completedCount: Int
        @Synchronized get() = completedChunks.cardinality()

    val isComplete: Boolean
        get() = completedCount >= totalChunks

    @Synchronized
    fun saveState() {
        try {
            val bytes = completedChunks.toByteArray()
            val base64 = Base64.encodeToString(bytes, Base64.NO_WRAP)

            val json = JSONObject().apply {
                put("transferId", transferId)
                put("filePath", filePath)
                put("totalBytes", totalBytes)
                put("chunkSize", chunkSize)
                put("totalChunks", totalChunks)
                put("completedChunksBase64", base64)
            }
            stateFile.writeText(json.toString())
        } catch (_: Exception) {}
    }

    fun cleanupStateOnCompletion() {
        runCatching {
            if (stateFile.exists()) stateFile.delete()
            if (partFile.exists()) {
                val dest = File(filePath)
                if (dest.exists()) dest.delete()
                partFile.renameTo(dest)
            }
        }
    }

    private fun loadStateIfExists() {
        try {
            if (stateFile.exists()) {
                val json = JSONObject(stateFile.readText())
                if (json.optLong("totalBytes") == totalBytes && json.optInt("totalChunks") == totalChunks) {
                    val base64 = json.getString("completedChunksBase64")
                    val bytes = Base64.decode(base64, Base64.NO_WRAP)
                    val loadedBits = BitSet.valueOf(bytes)
                    synchronized(this) {
                        completedChunks.clear()
                        completedChunks.or(loadedBits)
                    }
                }
            }
        } catch (_: Exception) {}
    }
}

