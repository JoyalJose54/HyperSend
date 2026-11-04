using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HyperSend.Core
{
    public static class ControlProtocol
    {
        public static readonly byte[] TagCtrl = Encoding.ASCII.GetBytes("HS_CTRL\0"); // 8 bytes
        public static readonly byte[] TagData = Encoding.ASCII.GetBytes("HS_DATA\0"); // 8 bytes

        // Message types for control connection
        public const byte MsgHello = 0x01;
        public const byte MsgAuthChallenge = 0x02;
        public const byte MsgAuthResponse = 0x03;
        public const byte MsgAuthSuccess = 0x04;
        public const byte MsgAuthFailed = 0x05;
        public const byte MsgOffer = 0x06;
        public const byte MsgAccept = 0x07;
        public const byte MsgFileDone = 0x08;

        public static byte[] GenerateRandomBytes(int length)
        {
            byte[] bytes = new byte[length];
            RandomNumberGenerator.Fill(bytes);
            return bytes;
        }

        public static ulong GenerateSessionId()
        {
            byte[] bytes = new byte[8];
            RandomNumberGenerator.Fill(bytes);
            return BinaryPrimitives.ReadUInt64BigEndian(bytes);
        }

        public static byte[] DeriveSessionMasterKey(byte[] pinKey, byte[] clientNonce, byte[] serverNonce)
        {
            byte[] salt = new byte[clientNonce.Length + serverNonce.Length];
            Buffer.BlockCopy(clientNonce, 0, salt, 0, clientNonce.Length);
            Buffer.BlockCopy(serverNonce, 0, salt, clientNonce.Length, serverNonce.Length);

            byte[] info = Encoding.UTF8.GetBytes("HyperSend-Session-Master-v1");
            byte[] okm = new byte[CryptoEngine.KeySize];
            HKDF.DeriveKey(HashAlgorithmName.SHA256, pinKey, okm, salt, info);
            return okm;
        }
    }
}

