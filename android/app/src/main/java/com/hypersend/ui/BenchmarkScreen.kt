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

@Composable
fun HyperSendApp() {
    var screen by remember { mutableStateOf("home") }
    var isSender by remember { mutableStateOf(false) }
    var pin by remember { mutableStateOf(CryptoEngine.generateRandomPin()) }
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
                    pin = CryptoEngine.generateRandomPin()
                    screen = "pairing_receive"
                }
            )

            "pairing_send" -> PairingScreen(
                isSender = true,
                pin = pin,
                targetIp = targetIp,
                onTargetIpChanged = { targetIp = it },
                onConnect = {
                    screen = "transfer"
                    TransferForegroundService.start(context)
                    activeJob = coroutineScope.launch {
                        try {
                            val fixedSalt = ByteArray(16) { 0x5A.toByte() }
                            val sessionKey = CryptoEngine.derivePinKey(pin, fixedSalt)
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
                targetIp = "",
                onTargetIpChanged = {},
                onConnect = {
                    screen = "transfer"
                    TransferForegroundService.start(context)
                    activeJob = coroutineScope.launch {
                        try {
                            val fixedSalt = ByteArray(16) { 0x5A.toByte() }
                            val sessionKey = CryptoEngine.derivePinKey(pin, fixedSalt)
                            val receiver = EncryptedChunkedReceiver(
                                sessionMasterKey = sessionKey,
                                expectedSessionId = 1L,
                                port = NetworkConfig.DEFAULT_PORT,
                                streamCount = 6
                            )
                            val meter = receiver.runReceiver(null, totalBytes, NetworkConfig.DEFAULT_CHUNK_SIZE) { report ->
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

            "transfer" -> TransferScreen(
                isSender = isSender,
                deviceName = android.os.Build.MODEL,
                peerDeviceName = "Windows PC",
                fileName = "Synthetic Transfer (2 GB)",
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
