package com.hypersend.engine

import java.io.File
import java.util.Locale

object PathSanitizer {
    private val RESERVED_NAMES = setOf(
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    )

    private val ILLEGAL_CHARS = charArrayOf('<', '>', ':', '"', '|', '?', '*')

    fun sanitizeRelativePath(relativePath: String): String {
        require(relativePath.isNotBlank()) { "Path cannot be blank" }

        val trimmed = relativePath.trim()
        if (trimmed.startsWith('/') || trimmed.startsWith('\\')) {
            throw IllegalArgumentException("Absolute or root paths are rejected: $relativePath")
        }

        val normalized = trimmed.replace('\\', '/')
        if (normalized.contains(':') || normalized.startsWith("//")) {
            throw IllegalArgumentException("Absolute or UNC paths are rejected: $relativePath")
        }

        val segments = normalized.split('/').filter { it.isNotEmpty() }
        if (segments.isEmpty()) {
            throw IllegalArgumentException("Path contains no valid segments")
        }

        for (segment in segments) {
            if (segment == ".." || segment == ".") {
                throw IllegalArgumentException("Directory traversal sequence detected: $relativePath")
            }

            for (ch in ILLEGAL_CHARS) {
                if (segment.indexOf(ch) >= 0) {
                    throw IllegalArgumentException("Path segment contains illegal character '$ch': $segment")
                }
            }

            val baseName = segment.substringBefore('.').uppercase(Locale.US)
            if (RESERVED_NAMES.contains(baseName)) {
                throw IllegalArgumentException("Reserved Windows device name rejected: $segment")
            }
        }

        return segments.joinToString("/")
    }

    fun resolveSafeDestinationPath(destinationDirectory: File, relativePath: String): File {
        val cleanRelative = sanitizeRelativePath(relativePath)
        val canonicalDest = destinationDirectory.canonicalFile
        val target = File(canonicalDest, cleanRelative.replace('/', File.separatorChar)).canonicalFile

        val destPathWithSep = if (canonicalDest.path.endsWith(File.separator)) {
            canonicalDest.path
        } else {
            canonicalDest.path + File.separator
        }

        if (!target.path.startsWith(destPathWithSep) && target.path != canonicalDest.path) {
            throw SecurityException("Path traversal escape attempt detected: $relativePath")
        }

        return target
    }
}
