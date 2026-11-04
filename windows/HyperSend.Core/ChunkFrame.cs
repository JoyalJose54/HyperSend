using System;
using System.Buffers.Binary;
using System.IO;

namespace HyperSend.Core
{
    public readonly struct ChunkFrameHeader
    {
        public const int Size = 24;
        public static readonly byte[] ExpectedMagic = new byte[] { 0x48, 0x53 }; // "HS"
        public const byte CurrentVersion = 1;

        public byte Type { get; }
        public uint FileId { get; }
        public uint ChunkIndex { get; }
        public int PayloadLength { get; }
        public ulong NonceOrOffset { get; }

        public ChunkFrameHeader(byte type, uint fileId, uint chunkIndex, int payloadLength, ulong nonceOrOffset)
        {
            Type = type;
            FileId = fileId;
            ChunkIndex = chunkIndex;
            PayloadLength = payloadLength;
            NonceOrOffset = nonceOrOffset;
        }

        public void WriteTo(Span<byte> destination)
        {
            if (destination.Length < Size)
                throw new ArgumentException($"Destination span too small. Required {Size} bytes.");

            destination[0] = ExpectedMagic[0];
            destination[1] = ExpectedMagic[1];
            destination[2] = CurrentVersion;
            destination[3] = Type;
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), FileId);
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8, 4), ChunkIndex);
            BinaryPrimitives.WriteInt32BigEndian(destination.Slice(12, 4), PayloadLength);
            BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(16, 8), NonceOrOffset);
        }

        public static bool TryReadFrom(ReadOnlySpan<byte> source, out ChunkFrameHeader header)
        {
            header = default;
            if (source.Length < Size)
                return false;

            if (source[0] != ExpectedMagic[0] || source[1] != ExpectedMagic[1])
                return false;

            byte version = source[2];
            byte type = source[3];
            uint fileId = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(4, 4));
            uint chunkIndex = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(8, 4));
            int payloadLength = BinaryPrimitives.ReadInt32BigEndian(source.Slice(12, 4));
            ulong nonceOrOffset = BinaryPrimitives.ReadUInt64BigEndian(source.Slice(16, 8));

            header = new ChunkFrameHeader(type, fileId, chunkIndex, payloadLength, nonceOrOffset);
            return true;
        }
    }
}

