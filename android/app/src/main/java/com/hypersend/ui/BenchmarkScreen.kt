package com.hypersend.ui

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
import java.net.Inet4Address
import java.net.NetworkInterface

fun getLocalIp(): String {
    try {
        val interfaces = NetworkInterface.getNetworkInterfaces() ?: return "10.15.70.174"
        for (intf in interfaces) {
            val addrs = intf.inetAddresses
            for (addr in addrs) {
                if (!addr.isLoopbackAddress && addr is Inet4Address) {
                    val host = addr.hostAddress ?: continue
                    if (host.startsWith("10.") || host.startsWith("192.168.")) {
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
    val totalBytes by remember { mutableStateOf(2L * 1024 * 1024 * 1024) }
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

                    // START LISTENING ON PORT 5000 IMMEDIATELY!
                    TransferForegroundService.start(context)
                    activeJob?.cancel()
                    activeJob = coroutineScope.launch {
                        try {
                            val fixedSalt = ByteArray(16) { 0x5A.toByte() }
                            val sessionKey = CryptoEngine.derivePinKey(newPin, fixedSalt)
                            val receiver = EncryptedChunkedReceiver(
                                sessionMasterKey = sessionKey,
                                expectedSessionId = 1L,
                                port = NetworkConfig.DEFAULT_PORT,
                                streamCount = 6
                            )
                            val meter = receiver.runReceiver(null, 0L, NetworkConfig.DEFAULT_CHUNK_SIZE) { report ->
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
                        } catch (e: Exception) {
                            if (screen == "transfer" || screen == "pairing_receive") {
                                screen = "home"
                            }
                        } finally {
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
                onConnect = { enteredPin ->
                    val actualPin = if (enteredPin.isNotBlank()) enteredPin else pin
                    pin = actualPin
                    screen = "transfer"
                    isComplete = false
                    currentMBps = 0.0
                    averageMBps = 0.0
                    bytesTransferred = 0L

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
                            val meter = sender.runSender(null, totalBytes) { report ->
                                currentMBps = report.currentMBps
                                averageMBps = report.averageMBps
                                bytesTransferred = report.bytesTransferred
                            }
                            averageMBps = meter.averageMegabytesPerSecond
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
                onConnect = {},
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
                fileName = "High-Speed Stream (2 GB)",
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
                onDone = {
                    screen = "home"
                    isComplete = false
                }
            )
        }
    }
}
