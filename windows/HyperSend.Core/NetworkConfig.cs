using System;
using System.Net.Sockets;

namespace HyperSend.Core
{
    public static class NetworkConfig
    {
        public const int DefaultPort = 5000;
        public const int DefaultStreams = 4;
        
        // Socket buffers: 2 MB default (up to 4 MB for high-bandwidth Wi-Fi 6 links)
        public const int SocketBufferSize = 2 * 1024 * 1024;
        
        // Default chunk size: 2 MB
        public const int DefaultChunkSize = 2 * 1024 * 1024;
        
        // Frame Magic: "HS" (0x48, 0x53)
        public static readonly byte[] MagicBytes = new byte[] { 0x48, 0x53 };
        public const byte ProtocolVersion = 1;
        
        // Frame Types
        public const byte TypeHello = 0x01;
        public const byte TypeAuthChallenge = 0x02;
        public const byte TypeAuthResponse = 0x03;
        public const byte TypeOffer = 0x04;
        public const byte TypeAccept = 0x05;
        public const byte TypeDataChunk = 0x06;
        public const byte TypeChunkAck = 0x07;
        public const byte TypeFileDone = 0x08;
        public const byte TypeCancel = 0x0A;
        public const byte TypeError = 0xFF;
        
        public const int HeaderSize = 24; // Magic(2) + Ver(1) + Type(1) + FileId(4) + ChunkIndex(4) + Length(4) + Nonce(8)

        public static void ConfigureSocket(Socket socket)
        {
            socket.NoDelay = true; // TCP_NODELAY disabled Nagle's algorithm
            socket.SendBufferSize = SocketBufferSize;
            socket.ReceiveBufferSize = SocketBufferSize;
            socket.LingerState = new LingerOption(false, 0);
            
            // Try enabling dual-mode / keep-alive where applicable
            try
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
            catch
            {
                // Ignored if platform doesn't permit
            }
        }
    }
}

