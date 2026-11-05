package com.hypersend.engine

import java.net.Socket
import java.nio.channels.SocketChannel

object NetworkConfig {
    const val DEFAULT_PORT = 5000
    const val DEFAULT_STREAMS = 4
    const val SOCKET_BUFFER_SIZE = 4 * 1024 * 1024 // 4 MB buffer for 802.11ax
    const val DEFAULT_CHUNK_SIZE = 2 * 1024 * 1024 // 2 MB chunk

    fun configureSocket(socket: Socket) {
        socket.tcpNoDelay = true
        socket.sendBufferSize = SOCKET_BUFFER_SIZE
        socket.receiveBufferSize = SOCKET_BUFFER_SIZE
        socket.setSoLinger(false, 0)
        socket.soTimeout = 30000 // 30s timeout protects against hanging connection DoS
        try {
            socket.keepAlive = true
        } catch (_: Exception) {}
    }

    fun configureChannel(channel: SocketChannel) {
        val socket = channel.socket()
        configureSocket(socket)
        channel.configureBlocking(true)
    }
}
