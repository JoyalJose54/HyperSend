package com.hypersend.ui

import androidx.compose.animation.core.*
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.StrokeJoin
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.hypersend.ui.theme.*

@Composable
fun CompletionScreen(
    averageMBps: Double,
    totalBytes: Long,
    isSender: Boolean,
    onDone: () -> Unit
) {
    // Animate checkmark draw progress
    var trigger by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) { trigger = true }

    val checkProgress by animateFloatAsState(
        targetValue = if (trigger) 1f else 0f,
        animationSpec = tween(1200, easing = EaseOutQuart),
        label = "checkmark"
    )
    val circleProgress by animateFloatAsState(
        targetValue = if (trigger) 1f else 0f,
        animationSpec = tween(700, easing = EaseOutQuart),
        label = "circle"
    )

    val targetExceeded = averageMBps >= 90.0

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
                .padding(horizontal = 24.dp, vertical = 32.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.SpaceBetween
        ) {
            Spacer(modifier = Modifier.height(16.dp))

            // Animated checkmark
            Column(
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(20.dp)
            ) {
                Canvas(modifier = Modifier.size(140.dp)) {
                    val strokeWidth = 10.dp.toPx()
                    val radius = size.width / 2 - strokeWidth / 2
                    val center = Offset(size.width / 2, size.height / 2)

                    // Animated circle
                    drawArc(
                        color = GreenSuccess,
                        startAngle = -90f,
                        sweepAngle = 360f * circleProgress,
                        useCenter = false,
                        style = Stroke(width = strokeWidth, cap = StrokeCap.Round),
                        topLeft = Offset(strokeWidth / 2, strokeWidth / 2),
                        size = androidx.compose.ui.geometry.Size(size.width - strokeWidth, size.height - strokeWidth)
                    )

                    // Animated checkmark path (draw after circle)
                    if (checkProgress > 0f) {
                        val startX = center.x - radius * 0.35f
                        val midX = center.x - radius * 0.05f
                        val endX = center.x + radius * 0.4f
                        val startY = center.y + radius * 0.05f
                        val midY = center.y + radius * 0.35f
                        val endY = center.y - radius * 0.3f

                        // Parametric checkmark: 0..0.5 draws first segment, 0.5..1 draws second
                        val path = Path()
                        path.moveTo(startX, startY)

                        val seg1Progress = (checkProgress * 2f).coerceIn(0f, 1f)
                        val seg2Progress = ((checkProgress - 0.5f) * 2f).coerceIn(0f, 1f)

                        // First segment: start → mid
                        path.lineTo(
                            startX + (midX - startX) * seg1Progress,
                            startY + (midY - startY) * seg1Progress
                        )

                        // Second segment: mid → end
                        if (seg2Progress > 0f) {
                            path.lineTo(
                                midX + (endX - midX) * seg2Progress,
                                midY + (endY - midY) * seg2Progress
                            )
                        }

                        drawPath(
                            path = path,
                            color = GreenSuccess,
                            style = Stroke(
                                width = strokeWidth,
                                cap = StrokeCap.Round,
                                join = StrokeJoin.Round
                            )
                        )
                    }
                }

                Text(
                    text = "Transfer Complete! ✓",
                    fontSize = 26.sp,
                    fontWeight = FontWeight.ExtraBold,
                    color = GreenSuccess,
                    textAlign = TextAlign.Center
                )

                Text(
                    text = if (isSender) "Files sent successfully" else "Files received successfully",
                    fontSize = 15.sp,
                    color = TextSecondary,
                    textAlign = TextAlign.Center
                )
            }

            // Summary card
            Card(
                shape = RoundedCornerShape(24.dp),
                colors = CardDefaults.cardColors(containerColor = NavyCard),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(
                    modifier = Modifier.padding(24.dp),
                    verticalArrangement = Arrangement.spacedBy(16.dp)
                ) {
                    Text(
                        text = "Transfer Summary",
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold,
                        color = TextPrimary
                    )

                    HorizontalDivider(color = NavySurface)

                    SummaryRow(
                        label = "Total Transferred",
                        value = formatBytes(totalBytes)
                    )

                    SummaryRow(
                        label = "Average Speed",
                        value = String.format("%.1f MB/s", averageMBps)
                    )

                    SummaryRow(
                        label = "Time Taken",
                        value = if (averageMBps > 0.01)
                            formatEta((totalBytes / (averageMBps * 1024 * 1024)).toLong())
                        else "--"
                    )

                    if (targetExceeded) {
                        HorizontalDivider(color = NavySurface)
                        Surface(
                            shape = RoundedCornerShape(12.dp),
                            color = GreenSuccess.copy(alpha = 0.1f),
                            modifier = Modifier.fillMaxWidth()
                        ) {
                            Row(
                                modifier = Modifier.padding(12.dp),
                                horizontalArrangement = Arrangement.Center,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(
                                    text = "🏆 Target Exceeded! (≥ 90 MB/s)",
                                    fontSize = 14.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = GreenSuccess
                                )
                            }
                        }
                    }
                }
            }

            // Done button
            Button(
                onClick = onDone,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(56.dp),
                shape = RoundedCornerShape(16.dp),
                colors = ButtonDefaults.buttonColors(containerColor = GreenSuccess)
            ) {
                Text(
                    text = "Done",
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold
                )
            }
        }
    }
}

@Composable
private fun SummaryRow(label: String, value: String) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(text = label, fontSize = 14.sp, color = TextSecondary)
        Text(
            text = value,
            fontSize = 14.sp,
            fontWeight = FontWeight.Bold,
            color = TextPrimary
        )
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

