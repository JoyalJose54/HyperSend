package com.hypersend

import com.hypersend.engine.CryptoEngine
import org.junit.Assert.*
import org.junit.Test
import javax.crypto.AEADBadTagException

class CryptoEngineTest {

    @Test
    fun testAesGcmEncryptionDecryptionRoundtrip() {
        val key = ByteArray(CryptoEngine.KEY_SIZE) { it.toByte() }
        val nonce = CryptoEngine.formatNonce(1, 42L)
        val plaintext = "HyperSend Secure Wireless Transfer Test 1234567890".toByteArray(Charsets.UTF_8)
        val aad = byteArrayOf(0x48, 0x53, 0x01, 0x06)

        val encrypted = CryptoEngine.encrypt(key, nonce, plaintext, associatedData = aad)
        assertTrue(encrypted.size == plaintext.size + CryptoEngine.TAG_SIZE)

        val decrypted = CryptoEngine.decrypt(key, nonce, encrypted, associatedData = aad)
        assertArrayEquals(plaintext, decrypted)
    }

    @Test(expected = AEADBadTagException::class)
    fun testAesGcmCorruptTagThrowsException() {
        val key = ByteArray(CryptoEngine.KEY_SIZE) { it.toByte() }
        val nonce = CryptoEngine.formatNonce(2, 99L)
        val plaintext = "Tamper check payload".toByteArray(Charsets.UTF_8)

        val encrypted = CryptoEngine.encrypt(key, nonce, plaintext)
        // Corrupt one byte of tag at the end
        encrypted[encrypted.size - 1] = (encrypted[encrypted.size - 1].toInt() xor 0xFF).toByte()

        CryptoEngine.decrypt(key, nonce, encrypted)
    }

    @Test
    fun testDerivePinKeyMatchesExpected() {
        val pin = "482910"
        val salt = ByteArray(16) { (it + 1).toByte() }

        val keyA = CryptoEngine.derivePinKey(pin, salt)
        val keyB = CryptoEngine.derivePinKey(pin, salt)

        assertEquals(32, keyA.size)
        assertArrayEquals(keyA, keyB)
    }

    @Test
    fun testStreamKeyDerivationUniqueness() {
        val masterKey = ByteArray(32) { (it * 3).toByte() }
        val k1 = CryptoEngine.deriveStreamKey(masterKey, 1)
        val k2 = CryptoEngine.deriveStreamKey(masterKey, 2)

        assertEquals(32, k1.size)
        assertEquals(32, k2.size)
        assertFalse(k1.contentEquals(k2))
    }
}
