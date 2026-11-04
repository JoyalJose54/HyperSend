package com.hypersend.engine

import java.io.ByteArrayOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.nio.ByteBuffer
import java.nio.charset.StandardCharsets

data class BundleEntry(
    val relativePath: String,
    val modifiedTimeUtc: Long,
    val fileSize: Long,
    val absoluteFile: File
)

object BundlePacker {
    const val MAX_BUNDLE_SIZE = 4 * 1024 * 1024 // 4 MB target per small-file bundle
    const val SMALL_FILE_THRESHOLD = 1024 * 1024 // Files < 1 MB are bundled

    fun scanDirectory(sourceDir: File): Pair<List<BundleEntry>, List<List<BundleEntry>>> {
        val largeFiles = mutableListOf<BundleEntry>()
        val bundles = mutableListOf<List<BundleEntry>>()
        var currentBundle = mutableListOf<BundleEntry>()
        var currentBundleBytes = 0L

        if (!sourceDir.exists() || !sourceDir.isDirectory) {
            return Pair(largeFiles, bundles)
        }

        val allFiles = sourceDir.walkTopDown().filter { it.isFile }.toList()
        for (file in allFiles) {
            val relative = PathSanitizer.sanitizeRelativePath(file.relativeTo(sourceDir).path)
            val entry = BundleEntry(
                relativePath = relative,
                modifiedTimeUtc = file.lastModified() / 1000L,
                fileSize = file.length(),
                absoluteFile = file
            )

            if (file.length() >= SMALL_FILE_THRESHOLD) {
                largeFiles.add(entry)
            } else {
                currentBundle.add(entry)
                currentBundleBytes += file.length()

                if (currentBundleBytes >= MAX_BUNDLE_SIZE) {
                    bundles.add(currentBundle)
                    currentBundle = mutableListOf()
                    currentBundleBytes = 0L
                }
            }
        }

        if (currentBundle.isNotEmpty()) {
            bundles.add(currentBundle)
        }

        return Pair(largeFiles, bundles)
    }

    fun packBundle(entries: List<BundleEntry>): ByteArray {
        val bos = ByteArrayOutputStream()
        val dos = DataOutputStream(bos)

        // 1. Entry count (uint32)
        dos.writeInt(entries.size)

        val buffer = ByteArray(64 * 1024)
        for (entry in entries) {
            val pathBytes = entry.relativePath.toByteArray(StandardCharsets.UTF_8)
            // Path length (uint16)
            dos.writeShort(pathBytes.size)
            dos.write(pathBytes)

            // Modified time (int64)
            dos.writeLong(entry.modifiedTimeUtc)

            // File size (int64)
            dos.writeLong(entry.fileSize)

            // File payload
            FileInputStream(entry.absoluteFile).use { fis ->
                var read: Int
                while (fis.read(buffer).also { read = it } != -1) {
                    dos.write(buffer, 0, read)
                }
            }
        }
        dos.flush()
        return bos.toByteArray()
    }

    fun unpackBundle(bundleData: ByteArray, destinationRoot: File) {
        val byteBuffer = ByteBuffer.wrap(bundleData)
        val count = byteBuffer.int

        for (i in 0 until count) {
            val pathLen = byteBuffer.short.toInt() and 0xFFFF
            val pathBytes = ByteArray(pathLen)
            byteBuffer.get(pathBytes)
            val relPath = String(pathBytes, StandardCharsets.UTF_8)

            val modTime = byteBuffer.long
            val fileSize = byteBuffer.long

            val targetFile = PathSanitizer.resolveSafeDestinationPath(destinationRoot, relPath)
            targetFile.parentFile?.mkdirs()

            FileOutputStream(targetFile).use { fos ->
                var remaining = fileSize
                val chunk = ByteArray(64 * 1024)
                while (remaining > 0) {
                    val toRead = Math.min(remaining, chunk.size.toLong()).toInt()
                    byteBuffer.get(chunk, 0, toRead)
                    fos.write(chunk, 0, toRead)
                    remaining -= toRead
                }
            }

            try {
                targetFile.setLastModified(modTime * 1000L)
            } catch (_: Exception) {}
        }
    }
}
