using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using HyperSend.Core;
using Xunit;

namespace HyperSend.Tests
{
    public class FuzzingAndHardeningTests
    {
        private static readonly byte[] TestKey = new byte[32] {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
            17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32
        };

        [Fact]
        public void Fuzzer_CorruptedAuthTag_RejectsPayloadImmediately()
        {
            byte[] plaintext = new byte[1024];
            Random.Shared.NextBytes(plaintext);

            byte[] nonce = new byte[12];
            CryptoEngine.FormatNonce(1, 42, nonce);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            CryptoEngine.Encrypt(TestKey, nonce, plaintext, ciphertext, tag);

            // Corrupt authentication tag
            tag[0] ^= 0xFF;

            byte[] decrypted = new byte[plaintext.Length];
            Assert.ThrowsAny<Exception>(() =>
            {
                CryptoEngine.Decrypt(TestKey, nonce, ciphertext, tag, decrypted);
            });
        }

        [Fact]
        public void Fuzzer_BitFlippedCiphertext_RejectsPayloadImmediately()
        {
            byte[] plaintext = new byte[2048];
            Random.Shared.NextBytes(plaintext);

            byte[] nonce = new byte[12];
            CryptoEngine.FormatNonce(2, 100, nonce);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            CryptoEngine.Encrypt(TestKey, nonce, plaintext, ciphertext, tag);

            // Flip bit in ciphertext
            ciphertext[15] ^= 0x01;

            byte[] decrypted = new byte[plaintext.Length];
            Assert.ThrowsAny<Exception>(() =>
            {
                CryptoEngine.Decrypt(TestKey, nonce, ciphertext, tag, decrypted);
            });
        }

        [Fact]
        public void Fuzzer_InvalidNonceCounter_FailsDecryption()
        {
            byte[] plaintext = new byte[512];
            Random.Shared.NextBytes(plaintext);

            byte[] validNonce = new byte[12];
            CryptoEngine.FormatNonce(3, 50, validNonce);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            CryptoEngine.Encrypt(TestKey, validNonce, plaintext, ciphertext, tag);

            // Format mismatched nonce (51 instead of 50)
            byte[] wrongNonce = new byte[12];
            CryptoEngine.FormatNonce(3, 51, wrongNonce);

            byte[] decrypted = new byte[plaintext.Length];
            Assert.ThrowsAny<Exception>(() =>
            {
                CryptoEngine.Decrypt(TestKey, wrongNonce, ciphertext, tag, decrypted);
            });
        }

        [Fact]
        public void Fuzzer_StreamIdMismatch_FailsDecryption()
        {
            byte[] plaintext = new byte[512];
            Random.Shared.NextBytes(plaintext);

            byte[] stream1Nonce = new byte[12];
            CryptoEngine.FormatNonce(1, 1, stream1Nonce);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];

            CryptoEngine.Encrypt(TestKey, stream1Nonce, plaintext, ciphertext, tag);

            // Mismatched stream id nonce (stream 2 instead of 1)
            byte[] stream2Nonce = new byte[12];
            CryptoEngine.FormatNonce(2, 1, stream2Nonce);

            byte[] decrypted = new byte[plaintext.Length];
            Assert.ThrowsAny<Exception>(() =>
            {
                CryptoEngine.Decrypt(TestKey, stream2Nonce, ciphertext, tag, decrypted);
            });
        }
    }
}
