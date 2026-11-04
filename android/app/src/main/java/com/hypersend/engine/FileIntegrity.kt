package com.hypersend.engine

import android.os.StatFs
import java.io.File
import java.io.FileInputStream
import java.security.MessageDigest

object FileIntegrity {

    fun computeFileSha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        val buffer = ByteArray(128 * 1024)
        FileInputStream(file).use { stream ->
            var read: Int
            while (stream.read(buffer).also { read = it } != -1) {
                digest.update(buffer, 0, read)
            }
        }
        val bytes = digest.digest()
        return bytes.joinToString("") { "%02x".format(it) }
    }

    fun checkDiskSpace(directoryPath: String, requiredBytes: Long): Boolean {
        return try {
            val stat = StatFs(directoryPath)
            val available = stat.availableBlocksLong * stat.blockSizeLong
            val safetyMargin = 100L * 1024 * 1024 // 100 MB margin
            available >= requiredBytes + safetyMargin
        } catch (_: Exception) {
            true // Allow if check fails
        }
    }
}

