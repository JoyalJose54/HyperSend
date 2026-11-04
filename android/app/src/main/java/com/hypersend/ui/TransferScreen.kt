package com.hypersend.ui

import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.hypersend.ui.theme.*
import kotlin.math.max

@Composable
fun TransferScreen(
    isSender: Boolean,
    deviceName: String,
    peerDeviceName: String,
    fileName: String,
    totalBytes: Long,
    onCancel: () -> Unit,
    currentMBps: Double,
    averageMBps: Double,
    bytesTransferred: Long,
    isComplete: Boolean
) {
    val progress = if (totalBytes > 0) (bytesTransferred.toFloat() / totalBytes.toFloat()).coerceIn(0f, 1f) else 0f
    val animatedProgress by animateFloatAsState(
        targetValue = progress,
        animationSpec = tween(600, easing = EaseOutQuart),
        label = "progress"
    )
    val animatedSpeed by animateFloatAsState(
        targetValue = currentMBps.toFloat(),
        animationSpec = tween(400),
        label = "speed"
    )

    // Data flow dots animation
    val infiniteTransition = rememberInfiniteTransition(label = "flow")
    val flowOffset by infiniteTransition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(
            animation = tween(1200, easing = LinearEasing),
            repeatMode = RepeatMode.Restart
        ),
        label = "flowOffset"
    )

    val etaSeconds = remember(averageMBps, bytesTransferred, totalBytes) {
        val remaining = totalBytes - bytesTransferred
        if (averageMBps > 0.1) (remaining / (averageMBps * 1024 * 1024)).toLong() else 0L
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(
                Brush.verticalGradient(
                    colors = listOf(NavyDark, NavySurface, Color(0xFF0F2235))
                )
            )
    ) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(horizontal = 20.dp, vertical = 20.dp),
            verticalArrangement = Arrangement.SpaceBetween
        ) {

            // --- TOP: Device flow row ---
            Card(
                shape = RoundedCornerShape(20.dp),
                colors = CardDefaults.cardColors(containerColor = NavyCard),
                modifier = Modifier.fillMaxWidth()
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(16.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    // Left device
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(text = "📱", fontSize = 28.sp)
                        Text(
                            text = deviceName.take(12),
                            fontSize = 11.sp,
                            color = TextSecondary,
                            textAlign = TextAlign.Center
                        )
                        Surface(
                            shape = RoundedCornerShape(50),
                            color = if (isSender) ElectricBlue.copy(alpha = 0.2f) else Teal.copy(alpha = 0.2f)
                        ) {
                            Text(
                                text = if (isSender) "SENDING" else "RECEIVING",
                                modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp),
                                fontSize = 9.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (isSender) ElectricBlue else Teal
                            )
                        }
                    }

                    // Animated flow dots
                    Row(
                        modifier = Modifier.weight(1f),
                        horizontalArrangement = Arrangement.Center,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        val dotCount = 5
                        for (i in 0 until dotCount) {
                            val dotPhase = if (isSender) {
                                // Dots flow left-to-right (sender): dot at position i lights up at flowOffset = i/dotCount
                                val p = (flowOffset - i.toFloat() / dotCount + 1f) % 1f
                                if (p > 0.8f) (1f - p) / 0.2f else if (p < 0.2f) p / 0.2f else 0.3f
                            } else {
                                // Dots flow right-to-left (receiver): reversed
                                val p = (flowOffset - (dotCount - 1 - i).toFloat() / dotCount + 1f) % 1f
                                if (p > 0.8f) (1f - p) / 0.2f else if (p < 0.2f) p / 0.2f else 0.3f
                            }
                            Box(
                                modifier = Modifier
                                    .size(8.dp)
                                    .background(
                                        ElectricBlue.copy(alpha = dotPhase.coerceIn(0.1f, 1f)),
                                        CircleShape
                                    )
                            )
                            if (i < dotCount - 1) Spacer(modifier = Modifier.width(8.dp))
                        }
                    }

                    // Right device
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(text = "💻", fontSize = 28.sp)
                        Text(
                            text = peerDeviceName.take(12),
                            fontSize = 11.sp,
                            color = TextSecondary,
                            textAlign = TextAlign.Center
                        )
                        Surface(
                            shape = RoundedCornerShape(50),
                            color = if (!isSender) ElectricBlue.copy(alpha = 0.2f) else Teal.copy(alpha = 0.2f)
                        ) {
                            Text(
                                text = if (!isSender) "SENDING" else "RECEIVING",
                                modifier = Modifier.padding(horizontal = 8.dp, vertical = 2.dp),
                                fontSize = 9.sp,
                                fontWeight = FontWeight.Bold,
                                color = if (!isSender) ElectricBlue else Teal
                            )
                        }
                    }
                }
            }

            // --- CENTER: Circular progress arc ---
            Box(
                contentAlignment = Alignment.Center,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(220.dp)
            ) {
                Canvas(modifier = Modifier.size(200.dp)) {
                    val strokeWidth = 18.dp.toPx()
                    val arcSize = size.width - strokeWidth
                    val topLeft = Offset(strokeWidth / 2, strokeWidth / 2)

                    // Background arc
                    drawArc(
                        color = NavyCard,
                        startAngle = -90f,
                        sweepAngle = 360f,
                        useCenter = false,
                        topLeft = topLeft,
                        size = Size(arcSize, arcSize),
                        style = Stroke(width = strokeWidth, cap = StrokeCap.Round)
                    )

                    // Foreground arc (progress)
                    drawArc(
                        brush = Brush.sweepGradient(
                            colors = listOf(ElectricBlue, Teal, ElectricBlue),
                            center = Offset(size.width / 2, size.height / 2)
                        ),
                        startAngle = -90f,
                        sweepAngle = animatedProgress * 360f,
                        useCenter = false,
                        topLeft = topLeft,
                        size = Size(arcSize, arcSize),
                        style = Stroke(width = strokeWidth, cap = StrokeCap.Round)
                    )
                }

                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(
                        text = "${(animatedProgress * 100).toInt()}%",
                        fontSize = 13.sp,
                        color = TextSecondary
                    )
                    Text(
                        text = String.format("%.1f", animatedSpeed),
                        fontSize = 48.sp,
                        fontWeight = FontWeight.ExtraBold,
                        fontFamily = FontFamily.Monospace,
                        color = ElectricBlue,
                        lineHeight = 50.sp
                    )
                    Text(
                        text = "MB/s",
                        fontSize = 16.sp,
                        color = TextSecondary
                    )
                }
            }

            // --- File info card ---
            Card(
                shape = RoundedCornerShape(16.dp),
                colors = CardDefaults.cardColors(containerColor = NavyCard),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    Text(
                        text = fileName.take(40),
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Medium,
                        color = TextPrimary
                    )
                    Text(
                        text = formatBytes(totalBytes),
                        fontSize = 12.sp,
                        color = TextSecondary
                    )
                    LinearProgressIndicator(
                        progress = { animatedProgress },
                        modifier = Modifier.fillMaxWidth().height(6.dp),
                        color = ElectricBlue,
                        trackColor = NavySurface
                    )
                }
            }

            // --- Stats Row ---
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(10.dp)
            ) {
                StatCard(label = "Avg Speed", value = String.format("%.1f", averageMBps), unit = "MB/s", modifier = Modifier.weight(1f))
                StatCard(label = "Transferred", value = formatBytes(bytesTransferred), unit = "", modifier = Modifier.weight(1f))
                StatCard(label = "ETA", value = formatEta(etaSeconds), unit = "", modifier = Modifier.weight(1f))
            }

            // --- Target indicator ---
            val targetExceeded = averageMBps >= 90.0
            Surface(
                shape = RoundedCornerShape(50),
                color = if (targetExceeded) GreenSuccess.copy(alpha = 0.15f) else NavyCard,
                modifier = Modifier.fillMaxWidth()
            ) {
                Row(
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.Center
                ) {
                    Text(
                        text = if (targetExceeded) "✅ Target 90 MB/s Exceeded!" else "🎯 Target: 90 MB/s",
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Medium,
                        color = if (targetExceeded) GreenSuccess else TextSecondary
                    )
                }
            }

            // --- Cancel ---
            OutlinedButton(
                onClick = onCancel,
                modifier = Modifier.fillMaxWidth().height(48.dp),
                shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.outlinedButtonColors(contentColor = Color(0xFFEF5350))
            ) {
                Text("Cancel Transfer", fontSize = 15.sp, fontWeight = FontWeight.Medium)
            }
        }
    }
}

@Composable
private fun StatCard(label: String, value: String, unit: String, modifier: Modifier = Modifier) {
    Card(
        shape = RoundedCornerShape(14.dp),
        colors = CardDefaults.cardColors(containerColor = NavyCard),
        modifier = modifier
    ) {
        Column(
            modifier = Modifier.padding(12.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(text = label, fontSize = 11.sp, color = TextSecondary)
            Spacer(modifier = Modifier.height(4.dp))
            Text(
                text = if (unit.isNotEmpty()) "$value $unit" else value,
                fontSize = 13.sp,
                fontWeight = FontWeight.Bold,
                color = TextPrimary,
                textAlign = TextAlign.Center
            )
        }
    }
}

private fun formatBytes(bytes: Long): String {
    return when {
        bytes >= 1024L * 1024 * 1024 -> String.format("%.2f GB", bytes / (1024.0 * 1024.0 * 1024.0))
        bytes >= 1024L * 1024 -> String.format("%.1f MB", bytes / (1024.0 * 1024.0))
        bytes >= 1024L -> String.format("%.0f KB", bytes / 1024.0)
        else -> "$bytes B"
    }
}

private fun formatEta(seconds: Long): String {
    return when {
        seconds <= 0 -> "--"
        seconds < 60 -> "${seconds}s"
        seconds < 3600 -> "${seconds / 60}m ${seconds % 60}s"
        else -> "${seconds / 3600}h ${(seconds % 3600) / 60}m"
    }
}

