package com.hypersend.ui

import android.net.wifi.WifiManager
import androidx.compose.animation.core.*
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.hypersend.ui.theme.*

@Composable
fun HomeScreen(
    onSendClicked: () -> Unit,
    onReceiveClicked: () -> Unit
) {
    val context = LocalContext.current

    // Wi-Fi info
    val wifiInfo = remember {
        try {
            val wm = context.applicationContext.getSystemService(android.content.Context.WIFI_SERVICE) as WifiManager
            val info = wm.connectionInfo
            val ssid = info.ssid?.removePrefix("\"")?.removeSuffix("\"") ?: "Unknown"
            val linkSpeed = info.linkSpeed
            Pair(ssid, linkSpeed)
        } catch (_: Exception) {
            Pair("Unknown", 0)
        }
    }

    // Pulsing animation
    val infiniteTransition = rememberInfiniteTransition(label = "pulse")
    val ring1Scale by infiniteTransition.animateFloat(
        initialValue = 1f,
        targetValue = 1.3f,
        animationSpec = infiniteRepeatable(
            animation = tween(1200, easing = EaseInOutSine),
            repeatMode = RepeatMode.Reverse
        ),
        label = "ring1"
    )
    val ring1Alpha by infiniteTransition.animateFloat(
        initialValue = 0.5f,
        targetValue = 0.0f,
        animationSpec = infiniteRepeatable(
            animation = tween(1200, easing = EaseInOutSine),
            repeatMode = RepeatMode.Reverse
        ),
        label = "ring1Alpha"
    )
    val ring2Scale by infiniteTransition.animateFloat(
        initialValue = 1f,
        targetValue = 1.6f,
        animationSpec = infiniteRepeatable(
            animation = tween(1600, easing = EaseInOutSine),
            repeatMode = RepeatMode.Reverse
        ),
        label = "ring2"
    )
    val ring2Alpha by infiniteTransition.animateFloat(
        initialValue = 0.3f,
        targetValue = 0.0f,
        animationSpec = infiniteRepeatable(
            animation = tween(1600, easing = EaseInOutSine),
            repeatMode = RepeatMode.Reverse
        ),
        label = "ring2Alpha"
    )

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
                .padding(horizontal = 20.dp, vertical = 16.dp),
            verticalArrangement = Arrangement.SpaceBetween
        ) {
            // Top Row: Title + Device Chip
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = "HyperSend ⚡",
                    fontSize = 26.sp,
                    fontWeight = FontWeight.ExtraBold,
                    color = ElectricBlue
                )
                Surface(
                    shape = RoundedCornerShape(50),
                    color = NavyCard,
                    tonalElevation = 2.dp
                ) {
                    Text(
                        text = android.os.Build.MODEL,
                        modifier = Modifier.padding(horizontal = 14.dp, vertical = 6.dp),
                        fontSize = 12.sp,
                        fontWeight = FontWeight.Medium,
                        color = TextSecondary
                    )
                }
            }

            Spacer(modifier = Modifier.height(8.dp))

            // Center: Pulsing circle + subtitle
            Column(
                modifier = Modifier.fillMaxWidth(),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Box(
                    contentAlignment = Alignment.Center,
                    modifier = Modifier.size(200.dp)
                ) {
                    // Outer ring
                    Box(
                        modifier = Modifier
                            .size(190.dp)
                            .scale(ring2Scale)
                            .background(
                                ElectricBlue.copy(alpha = ring2Alpha),
                                CircleShape
                            )
                    )
                    // Mid ring
                    Box(
                        modifier = Modifier
                            .size(150.dp)
                            .scale(ring1Scale)
                            .background(
                                ElectricBlue.copy(alpha = ring1Alpha),
                                CircleShape
                            )
                    )
                    // Inner solid circle
                    Box(
                        contentAlignment = Alignment.Center,
                        modifier = Modifier
                            .size(110.dp)
                            .background(
                                Brush.radialGradient(
                                    colors = listOf(ElectricBlue, Color(0xFF0288D1))
                                ),
                                CircleShape
                            )
                    ) {
                        Text(text = "⚡", fontSize = 44.sp)
                    }
                }

                Spacer(modifier = Modifier.height(20.dp))

                Text(
                    text = "Ultra-Fast P2P Transfer",
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold,
                    color = TextPrimary
                )
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = "AES-256-GCM · Multi-Stream · Wi-Fi 6",
                    fontSize = 13.sp,
                    color = TextSecondary,
                    textAlign = TextAlign.Center
                )
            }

            Spacer(modifier = Modifier.height(8.dp))

            // Action Cards
            Column(
                modifier = Modifier.fillMaxWidth(),
                verticalArrangement = Arrangement.spacedBy(14.dp)
            ) {
                ActionCard(
                    emoji = "📤",
                    title = "Send Files",
                    subtitle = "Transfer files to another device",
                    accentColor = ElectricBlue,
                    onClick = onSendClicked
                )
                ActionCard(
                    emoji = "📥",
                    title = "Receive Files",
                    subtitle = "Accept files from another device",
                    accentColor = Teal,
                    onClick = onReceiveClicked
                )
            }

            Spacer(modifier = Modifier.height(8.dp))

            // Bottom Wi-Fi bar
            val (ssid, linkSpeed) = wifiInfo
            val isGoodConnection = linkSpeed > 100
            Surface(
                shape = RoundedCornerShape(50),
                color = if (isGoodConnection) GreenSuccess.copy(alpha = 0.15f)
                        else Color(0xFFEF5350).copy(alpha = 0.15f),
                modifier = Modifier.fillMaxWidth()
            ) {
                Row(
                    modifier = Modifier.padding(horizontal = 16.dp, vertical = 10.dp),
                    horizontalArrangement = Arrangement.Center,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = if (isGoodConnection) "🟢" else "🟡",
                        fontSize = 16.sp
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    Text(
                        text = if (linkSpeed > 0) "$ssid · ${linkSpeed} Mbps" else "5 GHz Wi-Fi 6",
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Medium,
                        color = if (isGoodConnection) GreenSuccess else Color(0xFFFFB74D)
                    )
                }
            }
        }
    }
}

@Composable
private fun ActionCard(
    emoji: String,
    title: String,
    subtitle: String,
    accentColor: Color,
    onClick: () -> Unit
) {
    Card(
        onClick = onClick,
        shape = RoundedCornerShape(24.dp),
        colors = CardDefaults.cardColors(
            containerColor = NavySurface.copy(alpha = 0.95f)
        ),
        elevation = CardDefaults.cardElevation(defaultElevation = 4.dp),
        modifier = Modifier.fillMaxWidth()
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(20.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            Box(
                contentAlignment = Alignment.Center,
                modifier = Modifier
                    .size(56.dp)
                    .background(accentColor.copy(alpha = 0.15f), RoundedCornerShape(16.dp))
            ) {
                Text(text = emoji, fontSize = 28.sp)
            }
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = title,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold,
                    color = TextPrimary
                )
                Text(
                    text = subtitle,
                    fontSize = 13.sp,
                    color = TextSecondary
                )
            }
            Text(
                text = "›",
                fontSize = 28.sp,
                fontWeight = FontWeight.Light,
                color = accentColor
            )
        }
    }
}

