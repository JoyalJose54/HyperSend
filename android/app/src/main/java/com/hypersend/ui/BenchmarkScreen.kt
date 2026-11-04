package com.hypersend.ui

import androidx.compose.foundation.background
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
import com.hypersend.engine.ProgressReport
import com.hypersend.engine.RawTcpReceiver
import com.hypersend.engine.RawTcpSender
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BenchmarkScreen() {
    var isSending by remember { mutableStateOf(true) } // true: Phone -> Laptop, false: Laptop -> Phone
    var isSynthetic by remember { mutableStateOf(true) }
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
                title = { Text("HyperSend M1 Baseline", fontWeight = FontWeight.Bold) },
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
            verticalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            // Hotspot / Target Card
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant),
                shape = RoundedCornerShape(12.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Direct Link Target Ceiling", fontWeight = FontWeight.Bold, fontSize = 16.sp)
                    Text("• Phone -> Laptop: >= 90 MB/s (Ceiling ~110 MB/s)", fontSize = 13.sp)
                    Text("• Laptop -> Phone: >= 65 MB/s (Ceiling ~78 MB/s)", fontSize = 13.sp)
                }
            }

            // Direction Selector
            Text("Transfer Direction", fontWeight = FontWeight.SemiBold)
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FilterChip(
                    selected = isSending,
                    onClick = { if (!isRunning) isSending = true },
                    label = { Text("Phone -> Laptop (Send)") },
                    modifier = Modifier.weight(1f)
                )
                FilterChip(
                    selected = !isSending,
                    onClick = { if (!isRunning) isSending = false },
                    label = { Text("Laptop -> Phone (Receive)") },
                    modifier = Modifier.weight(1f)
                )
            }

            // Mode Selector
            Text("Test Mode", fontWeight = FontWeight.SemiBold)
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                FilterChip(
                    selected = isSynthetic,
                    onClick = { if (!isRunning) isSynthetic = true },
                    label = { Text("Synthetic (iperf3 equiv)") },
                    modifier = Modifier.weight(1f)
                )
                FilterChip(
                    selected = !isSynthetic,
                    onClick = { if (!isRunning) isSynthetic = false },
                    label = { Text("Storage File Test") },
                    modifier = Modifier.weight(1f)
                )
            }

            // Target IP and Streams
            if (isSending) {
                OutlinedTextField(
                    value = targetIp,
                    onValueChange = { targetIp = it },
                    label = { Text("Laptop IPv4 Address") },
                    modifier = Modifier.fillMaxWidth(),
                    enabled = !isRunning
                )
            }

            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = streams,
                    onValueChange = { streams = it },
                    label = { Text("Parallel Streams") },
                    modifier = Modifier.weight(1f),
                    enabled = !isRunning
                )
                OutlinedTextField(
                    value = testSizeGb,
                    onValueChange = { testSizeGb = it },
                    label = { Text("Test Size (GB)") },
                    modifier = Modifier.weight(1f),
                    enabled = !isRunning
                )
            }

            // Live Performance Metrics Card
            Card(
                colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer),
                shape = RoundedCornerShape(12.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text("Measured Performance", fontWeight = FontWeight.Bold, fontSize = 16.sp)

                    val currentMBps = progressReport?.currentMBps ?: 0.0
                    val avgMBps = finalAvgMBps ?: progressReport?.averageMBps ?: 0.0
                    val transferredMB = (progressReport?.bytesTransferred ?: 0L) / (1024.0 * 1024.0)

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column {
                            Text("Current Speed", fontSize = 12.sp, color = Color.Gray)
                            Text(
                                text = String.format("%.1f MB/s", currentMBps),
                                fontSize = 28.sp,
                                fontWeight = FontWeight.Bold,
                                fontFamily = FontFamily.Monospace
                            )
                        }
                        Column(horizontalAlignment = Alignment.End) {
                            Text("Average Speed", fontSize = 12.sp, color = Color.Gray)
                            Text(
                                text = String.format("%.1f MB/s", avgMBps),
                                fontSize = 22.sp,
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
                                .height(8.dp),
                        )
                        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                            Text(String.format("%.1f MB transferred", transferredMB), fontSize = 12.sp)
                            Text(String.format("%.1f%%", progressReport!!.percentage), fontSize = 12.sp)
                        }
                    }

                    Text("Status: $statusText", fontWeight = FontWeight.Medium, fontSize = 14.sp)
                }
            }

            // Start / Stop Action Button
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
                        val totalBytes = (testSizeGb.toDoubleOrNull() ?: 2.0) * 1024 * 1024 * 1024

                        activeJob = coroutineScope.launch {
                            try {
                                if (isSending) {
                                    val sender = RawTcpSender(targetIp, targetPort, streamNum)
                                    val meter = sender.runSender(
                                        sourceFile = null,
                                        syntheticBytes = totalBytes.toLong()
                                    ) { report ->
                                        progressReport = report
                                    }
                                    finalAvgMBps = meter.averageMegabytesPerSecond
                                    statusText = String.format("Done! Avg: %.1f MB/s", meter.averageMegabytesPerSecond)
                                } else {
                                    val receiver = RawTcpReceiver(targetPort, streamNum)
                                    val meter = receiver.runReceiver(
                                        destinationFile = null,
                                        expectedTotalBytes = totalBytes.toLong()
                                    ) { report ->
                                        progressReport = report
                                    }
                                    finalAvgMBps = meter.averageMegabytesPerSecond
                                    statusText = String.format("Done! Avg: %.1f MB/s", meter.averageMegabytesPerSecond)
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
                    text = if (isRunning) "STOP TEST" else "START M1 BASELINE TEST",
                    fontSize = 16.sp,
                    fontWeight = FontWeight.Bold
                )
            }
        }
    }
}

