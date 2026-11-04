package com.hypersend.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.hypersend.engine.*
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BenchmarkScreen() {
    var isSending by remember { mutableStateOf(true) } // true: Phone -> Laptop, false: Laptop -> Phone
    var useChunkEngine by remember { mutableStateOf(true) } // true: M2 WorkQueue Chunk Engine, false: M1 Raw
    var chunkSizeMb by remember { mutableStateOf("2") } // 1, 2, 4 MB
    var targetIp by remember { mutableStateOf("10.15.70.247") } // Laptop IP on hotspot
    var port by remember { mutableStateOf("5000") }
    var streams by remember { mutableStateOf("4") }
    var testSizeGb by remember { mutableStateOf("2") }

    var isRunning by remember { mutableStateOf(false) }
    var statusText by remember { mutableStateOf("Ready") }
    var progressReport by remember { mutableStateOf<ProgressReport?>(null) }
    var finalAvgMBps by remember { mutableStateOf<Double?>(null) }

    val coroutineScope = rememberCoroutineScope()
    var activeJob by remember { mutableStateOf<Job?>(null) }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("HyperSend M2 Chunk Engine", fontWeight = FontWeight.Bold) },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = MaterialTheme.colorScheme.primaryContainer,
                    titleContentColor = MaterialTheme.colorScheme.onPrimaryContainer
                )
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .padding(16.dp)
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(14.dp)
        ) {
            // Speed Targets Header
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(12.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    Text("Wi-Fi 6 Targets (80-90% of iperf3 Ceiling)", fontWeight = FontWeight.Bold, fontSize = 15.sp)
                    Text("• Phone -> Laptop: >= 90 MB/s (M1 measured: 93.1 MB/s)", fontSize = 13.sp)
                    Text("• Laptop -> Phone: >= 65 MB/s (M2 Work Queue Optimization)", fontSize = 13.sp)
                }
            }

            // Direction Selector
            Text("Transfer Direction", fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FilterChip(
                    selected = isSending,
                    onClick = { if (!isRunning) isSending = true },
                    label = { Text("Phone -> Laptop") },
                    modifier = Modifier.weight(1f)
                )
                FilterChip(
                    selected = !isSending,
                    onClick = { if (!isRunning) isSending = false },
                    label = { Text("Laptop -> Phone") },
                    modifier = Modifier.weight(1f)
                )
            }

            // Engine Mode Selector
            Text("Engine Architecture", fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FilterChip(
                    selected = useChunkEngine,
                    onClick = { if (!isRunning) useChunkEngine = true },
                    label = { Text("M2: Dynamic Work Queue") },
                    modifier = Modifier.weight(1f)
                )
                FilterChip(
                    selected = !useChunkEngine,
                    onClick = { if (!isRunning) useChunkEngine = false },
                    label = { Text("M1: Static Raw TCP") },
                    modifier = Modifier.weight(1f)
                )
            }

            // Parameters Row
            if (isSending) {
                OutlinedTextField(
                    value = targetIp,
                    onValueChange = { targetIp = it },
                    label = { Text("Laptop IP Address") },
                    modifier = Modifier.fillMaxWidth(),
                    enabled = !isRunning
                )
            }

            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = streams,
                    onValueChange = { streams = it },
                    label = { Text("Streams (2-8)") },
                    modifier = Modifier.weight(1f),
                    enabled = !isRunning
                )
                OutlinedTextField(
                    value = chunkSizeMb,
                    onValueChange = { chunkSizeMb = it },
                    label = { Text("Chunk MB (1-4)") },
                    modifier = Modifier.weight(1f),
                    enabled = !isRunning && useChunkEngine
                )
                OutlinedTextField(
                    value = testSizeGb,
                    onValueChange = { testSizeGb = it },
                    label = { Text("Size (GB)") },
                    modifier = Modifier.weight(1f),
                    enabled = !isRunning
                )
            }

            // Performance Card
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
                shape = RoundedCornerShape(12.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Text("Live Throughput", fontWeight = FontWeight.Bold, fontSize = 16.sp)

                    val currentMBps = progressReport?.currentMBps ?: 0.0
                    val avgMBps = finalAvgMBps ?: progressReport?.averageMBps ?: 0.0
                    val transferredMB = (progressReport?.bytesTransferred ?: 0L) / (1024.0 * 1024.0)

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column {
                            Text("Current Rate", fontSize = 12.sp, color = Color.Gray)
                            Text(
                                text = String.format("%.1f MB/s", currentMBps),
                                fontSize = 28.sp,
                                fontWeight = FontWeight.Bold,
                                fontFamily = FontFamily.Monospace
                            )
                        }
                        Column(horizontalAlignment = Alignment.End) {
                            Text("Average Rate", fontSize = 12.sp, color = Color.Gray)
                            Text(
                                text = String.format("%.1f MB/s", avgMBps),
                                fontSize = 24.sp,
                                fontWeight = FontWeight.SemiBold,
                                fontFamily = FontFamily.Monospace,
                                color = if (avgMBps >= (if (isSending) 90.0 else 65.0)) Color(0xFF2E7D32) else Color.Unspecified
                            )
                        }
                    }

                    if (progressReport != null && progressReport!!.totalBytes > 0) {
                        LinearProgressIndicator(
                            progress = { (progressReport!!.percentage / 100.0).toFloat().coerceIn(0f, 1f) },
                            modifier = Modifier
                                .fillMaxWidth()
                                .height(8.dp)
                        )
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                            Text(String.format("%.1f MB transferred", transferredMB), fontSize = 12.sp)
                            Text(String.format("%.1f%%", progressReport!!.percentage), fontSize = 12.sp)
                        }
                    }

                    Text("Status: $statusText", fontWeight = FontWeight.Medium, fontSize = 14.sp)
                }
            }

            // Start / Stop Button
            Button(
                onClick = {
                    if (isRunning) {
                        activeJob?.cancel()
                        isRunning = false
                        statusText = "Cancelled"
                    } else {
                        isRunning = true
                        finalAvgMBps = null
                        statusText = if (isSending) "Sending to Laptop..." else "Waiting for Laptop connection..."

                        val streamNum = streams.toIntOrNull() ?: 4
                        val targetPort = port.toIntOrNull() ?: 5000
                        val chunkSize = (chunkSizeMb.toIntOrNull() ?: 2) * 1024 * 1024
                        val totalBytes = ((testSizeGb.toDoubleOrNull() ?: 2.0) * 1024 * 1024 * 1024).toLong()

                        activeJob = coroutineScope.launch {
                            try {
                                if (useChunkEngine) {
                                    if (isSending) {
                                        val sender = ChunkedTcpSender(targetIp, targetPort, streamNum, chunkSize)
                                        val meter = sender.runSender(null, totalBytes) { report ->
                                            progressReport = report
                                        }
                                        finalAvgMBps = meter.averageMegabytesPerSecond
                                        statusText = String.format("Done! Avg: %.1f MB/s", meter.averageMegabytesPerSecond)
                                    } else {
                                        val receiver = ChunkedTcpReceiver(targetPort, streamNum)
                                        val meter = receiver.runReceiver(null, totalBytes, chunkSize) { report ->
                                            progressReport = report
                                        }
                                        finalAvgMBps = meter.averageMegabytesPerSecond
                                        statusText = String.format("Done! Avg: %.1f MB/s", meter.averageMegabytesPerSecond)
                                    }
                                } else {
                                    if (isSending) {
                                        val sender = RawTcpSender(targetIp, targetPort, streamNum)
                                        val meter = sender.runSender(null, totalBytes) { report ->
                                            progressReport = report
                                        }
                                        finalAvgMBps = meter.averageMegabytesPerSecond
                                        statusText = String.format("Done! Avg: %.1f MB/s", meter.averageMegabytesPerSecond)
                                    } else {
                                        val receiver = RawTcpReceiver(targetPort, streamNum)
                                        val meter = receiver.runReceiver(null, totalBytes) { report ->
                                            progressReport = report
                                        }
                                        finalAvgMBps = meter.averageMegabytesPerSecond
                                        statusText = String.format("Done! Avg: %.1f MB/s", meter.averageMegabytesPerSecond)
                                    }
                                }
                            } catch (e: Exception) {
                                statusText = "Error: ${e.message}"
                            } finally {
                                isRunning = false
                            }
                        }
                    }
                },
                modifier = Modifier
                    .fillMaxWidth()
                    .height(56.dp),
                shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.buttonColors(
                    containerColor = if (isRunning) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary
                )
            ) {
                Text(
                    text = if (isRunning) "STOP TEST" else "START M2 CHUNK BENCHMARK",
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold
                )
            }
        }
    }
}
