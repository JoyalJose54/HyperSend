using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HyperSend.Core
{
    public static class CryptoEngine
    {
        public const int KeySize = 32;       // 256-bit key
        public const int NonceSize = 12;     // 96-bit nonce
        public const int TagSize = 16;       // 128-bit authentication tag
        public const int SaltSize = 16;      // 128-bit random salt
        public const int NonceRandomSize = 16;
        public const int Pbkdf2Iterations = 10000;

        /// <summary>
        /// Derives a 256-bit secret from the 6-digit PIN and salt using PBKDF2-HMAC-SHA256.
        /// </summary>
        public static byte[] DerivePinKey(string pin, byte[] salt)
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                password: Encoding.UTF8.GetBytes(pin),
                salt: salt,
                iterations: Pbkdf2Iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: KeySize);
        }

        /// <summary>
        /// Generates a unique 12-byte nonce from stream ID and monotonically increasing counter.
        /// [ StreamID (4 bytes Big-Endian) ] || [ Counter (8 bytes Big-Endian) ]
        /// Guaranteed never-repeating per stream.
        /// </summary>
        public static void FormatNonce(int streamId, ulong counter, Span<byte> destination)
        {
            if (destination.Length < NonceSize)
                throw new ArgumentException($"Nonce destination must be at least {NonceSize} bytes");

            BinaryPrimitives.WriteInt32BigEndian(destination.Slice(0, 4), streamId);
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(4, 8), counter);
        }

        /// <summary>
        /// Derives an independent 256-bit AEAD key for each parallel data stream using HKDF-Expand.
        /// </summary>
        public static byte[] DeriveStreamKey(byte[] masterDataKey, int streamId)
        {
            byte[] info = Encoding.UTF8.GetBytes($"HyperSend-Stream-{streamId}");
            byte[] streamKey = new byte[KeySize];
            HKDF.Expand(HashAlgorithmName.SHA256, masterDataKey, streamKey, info);
            return streamKey;
        }

        /// <summary>
        /// Hardware-accelerated AES-256-GCM encryption.
        /// </summary>
        public static void Encrypt(
            byte[] key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> plaintext,
            Span<byte> ciphertext,
            Span<byte> tag,
            ReadOnlySpan<byte> associatedData = default)
        {
            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }

        /// <summary>
        /// Hardware-accelerated AES-256-GCM decryption and authentication.
        /// Throws AuthenticationTagMismatchException on tampering or corrupt chunks.
        /// </summary>
        public static void Decrypt(
            byte[] key,
            ReadOnlySpan<byte> nonce,
            ReadOnlySpan<byte> ciphertext,
            ReadOnlySpan<byte> tag,
            Span<byte> plaintext,
            ReadOnlySpan<byte> associatedData = default)
        {
            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        }

        /// <summary>
        /// Computes HMAC-SHA256 authentication confirmation token.
        /// </summary>
        public static byte[] ComputeHmac(byte[] key, byte[] data)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(data);
        }
    }
}

