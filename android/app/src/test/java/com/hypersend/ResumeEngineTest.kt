package com.hypersend

import com.hypersend.engine.FileIntegrity
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

class ResumeEngineTest {

    @Test
    fun testFileSha256Computation() {
        val temp = File.createTempFile("sha_test", ".dat")
        try {
            temp.writeText("HyperSend Resume and Integrity Test Payload")
            val hash = FileIntegrity.computeFileSha256(temp)
            assertEquals(64, hash.length)
            assertTrue(hash.all { it.isDigit() || it in 'a'..'f' })
        } finally {
            temp.delete()
        }
    }
}

