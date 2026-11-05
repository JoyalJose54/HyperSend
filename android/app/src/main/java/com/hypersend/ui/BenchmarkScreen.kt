package com.hypersend.ui

import android.content.ContentValues
import android.media.MediaScannerConnection
import android.os.Build
import android.os.Environment
import android.os.ParcelFileDescriptor
import android.provider.MediaStore
import androidx.compose.runtime.*
import androidx.compose.ui.platform.LocalContext
import com.hypersend.engine.CryptoEngine
import com.hypersend.engine.EncryptedChunkedReceiver
import com.hypersend.engine.EncryptedChunkedSender
import com.hypersend.engine.NetworkConfig
import com.hypersend.service.TransferForegroundService
import com.hypersend.ui.theme.HyperSendTheme
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import java.io.File
import java.io.RandomAccessFile
import java.net.Inet4Address
import java.net.NetworkInterface
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

fun getLocalIp(): String {
    try {
        val interfaces = NetworkInterface.getNetworkInterfaces()?.toList() ?: return "10.15.70.174"
        // 1. Prioritize Wi-Fi and Hotspot interfaces (wlan0, ap0, swlan0, rndis)
        for (intf in interfaces) {
            val name = intf.name.lowercase()
            if (name.contains("wlan") || name.contains("ap") || name.contains("rndis")) {
                for (addr in intf.inetAddresses) {
                    if (!addr.isLoopbackAddress && addr is Inet4Address) {
                        val host = addr.hostAddress ?: continue
                        if (!host.startsWith("127.")) return host
                    }
                }
            }
        }
        // 2. Fallback: any private IPv4
        for (intf in interfaces) {
            for (addr in intf.inetAddresses) {
                if (!addr.isLoopbackAddress && addr is Inet4Address) {
                    val host = addr.hostAddress ?: continue
                    if (host.startsWith("10.") || host.startsWith("192.168.") || host.startsWith("172.")) {
                        return host
                    }
                }
            }
        }
    } catch (_: Exception) {}
    return "10.15.70.174"
}

@Composable
fun HyperSendApp() {
    var screen by remember { mutableStateOf("home") }
    var isSender by remember { mutableStateOf(false) }
    var pin by remember { mutableStateOf(CryptoEngine.generateRandomPin()) }
    val localIp = remember { getLocalIp() }
    var targetIp by remember { mutableStateOf("10.15.70.247") }
    var currentMBps by remember { mutableStateOf(0.0) }
    var averageMBps by remember { mutableStateOf(0.0) }
    var bytesTransferred by remember { mutableStateOf(0L) }
    var totalBytes by remember { mutableStateOf(2L * 1024 * 1024 * 1024) }
    var activeFileName by remember { mutableStateOf("High-Speed Stream (2 GB)") }
    var savedFilePath by remember { mutableStateOf("") }
    var isComplete by remember { mutableStateOf(false) }

    val context = LocalContext.current
    val coroutineScope = rememberCoroutineScope()
    var activeJob by remember { mutableStateOf<Job?>(null) }

    HyperSendTheme {
        when (screen) {
            "home" -> HomeScreen(
                onSendClicked = {
                    isSender = true
                    screen = "pairing_send"
                },
                onReceiveClicked = {
                    isSender = false
                    val newPin = CryptoEngine.generateRandomPin()
                    pin = newPin
                    screen = "pairing_receive"
                    isComplete = false
                    currentMBps = 0.0
                    averageMBps = 0.0
                    bytesTransferred = 0L
                    savedFilePath = ""
                    activeFileName = "Incoming Transfer"
                    totalBytes = 2L * 1024 * 1024 * 1024

                    // START LISTENING ON PORT 5000 IMMEDIATELY!
                    TransferForegroundService.start(context)
                    activeJob?.cancel()
                    activeJob = coroutineScope.launch {
                        var pfd: ParcelFileDescriptor? = null
                        var destChannel: java.nio.channels.FileChannel? = null
                        var finalSavedPath = ""
                        var receiverInstance: EncryptedChunkedReceiver? = null
                        try {
                            val timeStamp = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(Date())
                            val fileName = "HyperSend_$timeStamp.dat"

                            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                                val values = ContentValues().apply {
                                    put(MediaStore.MediaColumns.DISPLAY_NAME, fileName)
                                    put(MediaStore.MediaColumns.MIME_TYPE, "application/octet-stream")
                                    put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/HyperSend")
                                }
                                val destUri = context.contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)
                                if (destUri != null) {
                                    pfd = context.contentResolver.openFileDescriptor(destUri, "rwt")
                                    destChannel = pfd?.let { java.io.FileOutputStream(it.fileDescriptor).channel }
                                    finalSavedPath = "Downloads/HyperSend/$fileName"
                                }
                            }

                            if (destChannel == null) {
                                val pubDir = File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), "HyperSend")
                                pubDir.mkdirs()
                                val targetFile = File(pubDir, fileName)
                                finalSavedPath = targetFile.absolutePath
                                val raf = RandomAccessFile(targetFile, "rw")
                                destChannel = raf.channel
                            }

                            savedFilePath = finalSavedPath

                            val fixedSalt = ByteArray(16) { 0x5A.toByte() }
                            val sessionKey = CryptoEngine.derivePinKey(newPin, fixedSalt)
                            val receiver = EncryptedChunkedReceiver(
                                sessionMasterKey = sessionKey,
                                expectedSessionId = 1L,
                                port = NetworkConfig.DEFAULT_PORT,
                                streamCount = 6
                            )
                            receiverInstance = receiver

                            val meter = receiver.runReceiver(
                                destinationFile = null,
                                fileChannelOverride = destChannel,
                                expectedTotalBytes = 0L,
                                chunkSize = NetworkConfig.DEFAULT_CHUNK_SIZE
                            ) { report ->
                                if (screen != "transfer") {
                                    screen = "transfer"
                                }
                                currentMBps = report.currentMBps
                                averageMBps = report.averageMBps
                                bytesTransferred = report.bytesTransferred
                            }
                            averageMBps = meter.averageMegabytesPerSecond
                            isComplete = true
                            screen = "complete"

                            try {
                                if (finalSavedPath.startsWith("/")) {
                                    MediaScannerConnection.scanFile(context, arrayOf(finalSavedPath), null, null)
                                }
                            } catch (_: Exception) {}
                        } catch (e: Exception) {
                            if (screen == "transfer" || screen == "pairing_receive") {
                                screen = "home"
                            }
                        } finally {
                            receiverInstance?.stop()
                            runCatching { destChannel?.close() }
                            runCatching { pfd?.close() }
                            TransferForegroundService.stop(context)
                        }
                    }
                }
            )

            "pairing_send" -> PairingScreen(
                isSender = true,
                pin = pin,
                localIp = localIp,
                targetIp = targetIp,
                onTargetIpChanged = { targetIp = it },
                onConnect = { enteredPin, files ->
                    val actualPin = if (enteredPin.isNotBlank()) enteredPin else pin
                    pin = actualPin
                    screen = "transfer"
                    isComplete = false
                    currentMBps = 0.0
                    averageMBps = 0.0
                    bytesTransferred = 0L
                    savedFilePath = ""

                    val isBenchmark = files.isEmpty()
                    if (isBenchmark) {
                        activeFileName = "Synthetic Benchmark (2 GB)"
                        totalBytes = 2L * 1024 * 1024 * 1024
                    } else {
                        activeFileName = if (files.size == 1) files[0].name else "${files.size} Files"
                        totalBytes = files.sumOf { it.size }
                    }

                    TransferForegroundService.start(context)
                    activeJob?.cancel()
                    activeJob = coroutineScope.launch {
                        try {
                            val fixedSalt = ByteArray(16) { 0x5A.toByte() }
                            val sessionKey = CryptoEngine.derivePinKey(actualPin, fixedSalt)
                            val sender = EncryptedChunkedSender(
                                targetHost = targetIp,
                                targetPort = NetworkConfig.DEFAULT_PORT,
                                sessionMasterKey = sessionKey,
                                sessionId = 1L,
                                streamCount = 6,
                                chunkSize = NetworkConfig.DEFAULT_CHUNK_SIZE
                            )

                            if (isBenchmark) {
                                val meter = sender.runSender(null, null, null, totalBytes) { report ->
                                    currentMBps = report.currentMBps
                                    averageMBps = report.averageMBps
                                    bytesTransferred = report.bytesTransferred
                                }
                                averageMBps = meter.averageMegabytesPerSecond
                            } else {
                                var cumulativeTransferred = 0L
                                for (fileInfo in files) {
                                    activeFileName = fileInfo.name
                                    val pfd = context.contentResolver.openFileDescriptor(fileInfo.uri, "r")
                                    if (pfd != null) {
                                        java.io.FileInputStream(pfd.fileDescriptor).use { fis ->
                                            val channel = fis.channel
                                            sender.runSender(
                                                fileChannel = channel,
                                                totalBytesOverride = fileInfo.size
                                            ) { report ->
                                                currentMBps = report.currentMBps
                                                averageMBps = report.averageMBps
                                                bytesTransferred = cumulativeTransferred + report.bytesTransferred
                                            }
                                            cumulativeTransferred += fileInfo.size
                                        }
                                        pfd.close()
                                    }
                                }
                            }
                            isComplete = true
                            screen = "complete"
                        } catch (e: Exception) {
                            screen = "home"
                        } finally {
                            TransferForegroundService.stop(context)
                        }
                    }
                },
                onBack = { screen = "home" }
            )

            "pairing_receive" -> PairingScreen(
                isSender = false,
                pin = pin,
                localIp = localIp,
                targetIp = "",
                onTargetIpChanged = {},
                onConnect = { _, _ -> },
                onBack = {
                    activeJob?.cancel()
                    TransferForegroundService.stop(context)
                    screen = "home"
                }
            )

            "transfer" -> TransferScreen(
                isSender = isSender,
                deviceName = android.os.Build.MODEL,
                peerDeviceName = "Windows PC",
                fileName = activeFileName,
                totalBytes = totalBytes,
                currentMBps = currentMBps,
                averageMBps = averageMBps,
                bytesTransferred = bytesTransferred,
                isComplete = isComplete,
                onCancel = {
                    activeJob?.cancel()
                    TransferForegroundService.stop(context)
                    screen = "home"
                }
            )

            "complete" -> CompletionScreen(
                averageMBps = averageMBps,
                totalBytes = bytesTransferred,
                isSender = isSender,
                savedPath = savedFilePath,
                onDone = {
                    screen = "home"
                    isComplete = false
                    savedFilePath = ""
                }
            )
        }
    }
}
