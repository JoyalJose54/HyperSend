package com.hypersend

import com.hypersend.engine.BundlePacker
import com.hypersend.engine.PathSanitizer
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.nio.file.Files

class PathSanitizerAndBundleTest {

    @Test
    fun testValidRelativePaths() {
        assertEquals("valid/path/file.txt", PathSanitizer.sanitizeRelativePath("valid/path/file.txt"))
        assertEquals("valid/path/file.txt", PathSanitizer.sanitizeRelativePath("valid\\path\\file.txt"))
        assertEquals("single_file.bin", PathSanitizer.sanitizeRelativePath("single_file.bin"))
        assertEquals("nested/deep/directory/file.jpg", PathSanitizer.sanitizeRelativePath("nested/deep/directory/file.jpg"))
    }

    @Test
    fun testRejectsMaliciousPaths() {
        val badPaths = listOf(
            "../evil.txt",
            "foo/../../evil.txt",
            "..",
            "/etc/passwd",
            "C:\\Windows\\System32\\cmd.exe",
            "D:/secret/file.txt",
            "//192.168.1.1/share/evil",
            "CON",
            "con.txt",
            "sub/AUX.dat",
            "NUL",
            "COM1",
            "sub/LPT1.log",
            "invalid<name>.txt",
            "invalid|name.txt",
            "invalid?name.txt",
            "invalid*name.txt"
        )

        for (bad in badPaths) {
            assertThrows(Exception::class.java) {
                PathSanitizer.sanitizeRelativePath(bad)
            }
        }
    }

    @Test
    fun testBundlePackAndUnpackRoundtrip() {
        val srcDir = Files.createTempDirectory("hs_src_test").toFile()
        val dstDir = Files.createTempDirectory("hs_dst_test").toFile()

        try {
            val file1 = File(srcDir, "doc.txt")
            val subDir = File(srcDir, "subfolder").also { it.mkdirs() }
            val file2 = File(subDir, "payload.bin")

            val bytes1 = "HyperSend Bundle Unit Test".toByteArray()
            val bytes2 = ByteArray(5000) { (it % 256).toByte() }

            file1.writeBytes(bytes1)
            file2.writeBytes(bytes2)

            val (large, bundles) = BundlePacker.scanDirectory(srcDir)
            assertEquals(0, large.size)
            assertEquals(1, bundles.size)
            assertEquals(2, bundles[0].size)

            val packed = BundlePacker.packBundle(bundles[0])
            assertTrue(packed.isNotEmpty())

            BundlePacker.unpackBundle(packed, dstDir)

            val dstFile1 = File(dstDir, "doc.txt")
            val dstFile2 = File(dstDir, "subfolder/payload.bin")

            assertTrue(dstFile1.exists())
            assertTrue(dstFile2.exists())

            assertArrayEquals(bytes1, dstFile1.readBytes())
            assertArrayEquals(bytes2, dstFile2.readBytes())
        } finally {
            srcDir.deleteRecursively()
            dstDir.deleteRecursively()
        }
    }
}
