package com.hypersend.engine

import java.nio.ByteBuffer
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.PBEKeySpec
import javax.crypto.spec.SecretKeySpec

object CryptoEngine {
    const val KEY_SIZE = 32       // 256-bit AES
    const val NONCE_SIZE = 12     // 96-bit nonce
    const val TAG_SIZE = 16       // 128-bit authentication tag
    const val SALT_SIZE = 16      // 128-bit random salt
    const val PBKDF2_ITERATIONS = 10000

    private val secureRandom = SecureRandom()

    fun generateSalt(): ByteArray {
        val salt = ByteArray(SALT_SIZE)
        secureRandom.nextBytes(salt)
        return salt
    }

    fun generateRandomNonce(): ByteArray {
        val nonce = ByteArray(NONCE_SIZE)
        secureRandom.nextBytes(nonce)
        return nonce
    }

    fun generateRandomPin(): String {
        val code = secureRandom.nextInt(900000) + 100000
        return code.toString()
    }

    fun derivePinKey(pin: String, salt: ByteArray): ByteArray {
        val spec = PBEKeySpec(pin.toCharArray(), salt, PBKDF2_ITERATIONS, KEY_SIZE * 8)
        val skf = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256")
        return skf.generateSecret(spec).encoded
    }

    fun formatNonce(streamId: Int, counter: Long): ByteArray {
        val buffer = ByteBuffer.allocate(NONCE_SIZE)
        buffer.putInt(streamId)
        buffer.putLong(counter)
        return buffer.array()
    }

    fun deriveStreamKey(masterDataKey: ByteArray, streamId: Int): ByteArray {
        val info = "HyperSend-Stream-$streamId".toByteArray(Charsets.UTF_8)
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(masterDataKey, "HmacSHA256"))
        mac.update(info)
        mac.update(0x01.toByte())
        val full = mac.doFinal()
        return full.copyOf(KEY_SIZE)
    }

    fun encrypt(
        key: ByteArray,
        nonce: ByteArray,
        plaintext: ByteArray,
        offset: Int = 0,
        length: Int = plaintext.size,
        associatedData: ByteArray? = null
    ): ByteArray {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        val keySpec = SecretKeySpec(key, "AES")
        val gcmSpec = GCMParameterSpec(TAG_SIZE * 8, nonce)
        cipher.init(Cipher.ENCRYPT_MODE, keySpec, gcmSpec)
        if (associatedData != null) {
            cipher.updateAAD(associatedData)
        }
        return cipher.doFinal(plaintext, offset, length)
    }

    fun decrypt(
        key: ByteArray,
        nonce: ByteArray,
        ciphertextWithTag: ByteArray,
        offset: Int = 0,
        length: Int = ciphertextWithTag.size,
        associatedData: ByteArray? = null
    ): ByteArray {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        val keySpec = SecretKeySpec(key, "AES")
        val gcmSpec = GCMParameterSpec(TAG_SIZE * 8, nonce)
        cipher.init(Cipher.DECRYPT_MODE, keySpec, gcmSpec)
        if (associatedData != null) {
            cipher.updateAAD(associatedData)
        }
        return cipher.doFinal(ciphertextWithTag, offset, length)
    }

    /**
     * Zero-allocation direct ByteBuffer AES-GCM encryption.
     * Operates directly on native memory pointers in Conscrypt / BoringSSL.
     */
    fun encryptDirect(
        key: ByteArray,
        nonce: ByteArray,
        inputPlaintext: ByteBuffer,
        outputCiphertextAndTag: ByteBuffer,
        associatedData: ByteBuffer? = null
    ) {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        val keySpec = SecretKeySpec(key, "AES")
        val gcmSpec = GCMParameterSpec(TAG_SIZE * 8, nonce)
        cipher.init(Cipher.ENCRYPT_MODE, keySpec, gcmSpec)
        if (associatedData != null) {
            cipher.updateAAD(associatedData)
        }
        cipher.doFinal(inputPlaintext, outputCiphertextAndTag)
    }

    /**
     * Zero-allocation direct ByteBuffer AES-GCM decryption.
     * Operates directly on native memory pointers in Conscrypt / BoringSSL.
     */
    fun decryptDirect(
        key: ByteArray,
        nonce: ByteArray,
        inputCiphertextAndTag: ByteBuffer,
        outputPlaintext: ByteBuffer,
        associatedData: ByteBuffer? = null
    ) {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        val keySpec = SecretKeySpec(key, "AES")
        val gcmSpec = GCMParameterSpec(TAG_SIZE * 8, nonce)
        cipher.init(Cipher.DECRYPT_MODE, keySpec, gcmSpec)
        if (associatedData != null) {
            cipher.updateAAD(associatedData)
        }
        cipher.doFinal(inputCiphertextAndTag, outputPlaintext)
    }

    fun computeHmac(key: ByteArray, data: ByteArray): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(key, "HmacSHA256"))
        return mac.doFinal(data)
    }
}
