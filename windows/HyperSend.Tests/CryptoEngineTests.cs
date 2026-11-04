using System;
using System.Security.Cryptography;
using System.Text;
using HyperSend.Core;
using Xunit;

namespace HyperSend.Tests
{
    public class CryptoEngineTests
    {
        [Fact]
        public void AesGcm_EncryptDecryptRoundTrip_Succeeds()
        {
            byte[] key = new byte[CryptoEngine.KeySize];
            RandomNumberGenerator.Fill(key);

            byte[] nonce = new byte[CryptoEngine.NonceSize];
            CryptoEngine.FormatNonce(1, 100UL, nonce);

            byte[] plaintext = Encoding.UTF8.GetBytes("HyperSend Zero-Cloud Fast Wireless File Transfer Test 1234567890!");
            byte[] associatedData = new byte[] { 0x48, 0x53, 0x01, 0x06 };

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[CryptoEngine.TagSize];

            CryptoEngine.Encrypt(key, nonce, plaintext, ciphertext, tag, associatedData);

            byte[] decrypted = new byte[plaintext.Length];
            CryptoEngine.Decrypt(key, nonce, ciphertext, tag, decrypted, associatedData);

            Assert.Equal(plaintext, decrypted);
        }

        [Fact]
        public void AesGcm_CorruptCiphertext_ThrowsCryptographicException()
        {
            byte[] key = new byte[CryptoEngine.KeySize];
            RandomNumberGenerator.Fill(key);

            byte[] nonce = new byte[CryptoEngine.NonceSize];
            CryptoEngine.FormatNonce(1, 1UL, nonce);

            byte[] plaintext = Encoding.UTF8.GetBytes("Integrity Check Data Payload");
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[CryptoEngine.TagSize];

            CryptoEngine.Encrypt(key, nonce, plaintext, ciphertext, tag);

            // Corrupt one single bit
            ciphertext[0] ^= 0x01;

            byte[] decrypted = new byte[plaintext.Length];
            Assert.Throws<AuthenticationTagMismatchException>(() =>
            {
                CryptoEngine.Decrypt(key, nonce, ciphertext, tag, decrypted);
            });
        }

        [Fact]
        public void AesGcm_CorruptTag_ThrowsCryptographicException()
        {
            byte[] key = new byte[CryptoEngine.KeySize];
            RandomNumberGenerator.Fill(key);

            byte[] nonce = new byte[CryptoEngine.NonceSize];
            CryptoEngine.FormatNonce(2, 55UL, nonce);

            byte[] plaintext = Encoding.UTF8.GetBytes("Authentication Tag Validation Payload");
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[CryptoEngine.TagSize];

            CryptoEngine.Encrypt(key, nonce, plaintext, ciphertext, tag);

            // Corrupt the auth tag
            tag[0] ^= 0xFF;

            byte[] decrypted = new byte[plaintext.Length];
            Assert.Throws<AuthenticationTagMismatchException>(() =>
            {
                CryptoEngine.Decrypt(key, nonce, ciphertext, tag, decrypted);
            });
        }

        [Fact]
        public void DeriveStreamKey_ProducesUniqueKeyPerStream()
        {
            byte[] masterKey = new byte[CryptoEngine.KeySize];
            RandomNumberGenerator.Fill(masterKey);

            byte[] key1 = CryptoEngine.DeriveStreamKey(masterKey, 1);
            byte[] key2 = CryptoEngine.DeriveStreamKey(masterKey, 2);
            byte[] key3 = CryptoEngine.DeriveStreamKey(masterKey, 3);

            Assert.Equal(32, key1.Length);
            Assert.Equal(32, key2.Length);
            Assert.NotEqual(key1, key2);
            Assert.NotEqual(key2, key3);
        }

        [Fact]
        public void DerivePinKey_DeterministicOutput()
        {
            string pin = "482910";
            byte[] salt = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

            byte[] keyA = CryptoEngine.DerivePinKey(pin, salt);
            byte[] keyB = CryptoEngine.DerivePinKey(pin, salt);

            Assert.Equal(32, keyA.Length);
            Assert.Equal(keyA, keyB);
        }
    }
}
