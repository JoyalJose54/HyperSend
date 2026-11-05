package com.hypersend.ui

import android.net.Uri
import android.provider.OpenableColumns
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.core.*
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.hypersend.ui.theme.*

data class SelectedFileInfo(
    val uri: Uri,
    val name: String,
    val size: Long
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PairingScreen(
    isSender: Boolean,
    pin: String,
    localIp: String = "",
    targetIp: String,
    onTargetIpChanged: (String) -> Unit,
    onConnect: (String, List<SelectedFileInfo>) -> Unit,
    onBack: () -> Unit
) {
    var pinInput by remember { mutableStateOf("") }
    var selectedFiles by remember { mutableStateOf<List<SelectedFileInfo>>(emptyList()) }
    val context = LocalContext.current

    val filePickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.OpenMultipleDocuments()
    ) { uris ->
        val newFiles = uris.mapNotNull { uri ->
            var name = "File"
            var size = 0L
            try {
                context.contentResolver.query(uri, null, null, null, null)?.use { cursor ->
                    val nameIdx = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    val sizeIdx = cursor.getColumnIndex(OpenableColumns.SIZE)
                    if (cursor.moveToFirst()) {
                        if (nameIdx != -1) name = cursor.getString(nameIdx)
                        if (sizeIdx != -1) size = cursor.getLong(sizeIdx)
                    }
                }
            } catch (_: Exception) {}
            SelectedFileInfo(uri, name, size)
        }
        selectedFiles = (selectedFiles + newFiles).distinctBy { it.uri }
    }

    val infiniteTransition = rememberInfiniteTransition(label = "pinPulse")
    val pulseScale by infiniteTransition.animateFloat(
        initialValue = 1f,
        targetValue = 1.08f,
        animationSpec = infiniteRepeatable(
            animation = tween(900, easing = EaseInOutSine),
            repeatMode = RepeatMode.Reverse
        ),
        label = "pinPulse"
    )
    val pulseAlpha by infiniteTransition.animateFloat(
        initialValue = 0.6f,
        targetValue = 0.0f,
        animationSpec = infiniteRepeatable(
            animation = tween(900, easing = EaseInOutSine),
            repeatMode = RepeatMode.Reverse
        ),
        label = "pinAlpha"
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
                .padding(horizontal = 20.dp, vertical = 20.dp),
            verticalArrangement = Arrangement.SpaceBetween
        ) {
            // Top Header
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically
            ) {
                IconButton(onClick = onBack) {
                    Text("←", fontSize = 24.sp, color = ElectricBlue)
                }
                Text(
                    text = if (isSender) "Connect to PC" else "Waiting for Connection",
                    fontSize = 22.sp,
                    fontWeight = FontWeight.Bold,
                    color = TextPrimary,
                    modifier = Modifier.padding(start = 4.dp)
                )
            }

            // Main content
            if (!isSender) {
                // RECEIVER MODE
                Column(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(24.dp)
                ) {
                    // Pulsing ring around PIN card
                    Box(contentAlignment = Alignment.Center) {
                        Box(
                            modifier = Modifier
                                .size(260.dp)
                                .scale(pulseScale)
                                .background(
                                    ElectricBlue.copy(alpha = pulseAlpha),
                                    CircleShape
                                )
                        )

                        Card(
                            shape = RoundedCornerShape(24.dp),
                            colors = CardDefaults.cardColors(containerColor = NavyCard),
                            elevation = CardDefaults.cardElevation(8.dp),
                            modifier = Modifier.size(220.dp)
                        ) {
                            Column(
                                modifier = Modifier
                                    .fillMaxSize()
                                    .padding(20.dp),
                                horizontalAlignment = Alignment.CenterHorizontally,
                                verticalArrangement = Arrangement.Center
                            ) {
                                Text(
                                    text = "Your PIN",
                                    fontSize = 14.sp,
                                    color = TextSecondary,
                                    fontWeight = FontWeight.Medium
                                )
                                Spacer(modifier = Modifier.height(8.dp))
                                Text(
                                    text = pin,
                                    fontSize = 48.sp,
                                    fontWeight = FontWeight.ExtraBold,
                                    fontFamily = FontFamily.Monospace,
                                    letterSpacing = 8.sp,
                                    color = ElectricBlue,
                                    textAlign = TextAlign.Center
                                )
                                Spacer(modifier = Modifier.height(8.dp))
                                Text(
                                    text = android.os.Build.MODEL,
                                    fontSize = 11.sp,
                                    color = TextSecondary
                                )
                                if (localIp.isNotBlank()) {
                                    Spacer(modifier = Modifier.height(4.dp))
                                    Text(
                                        text = "IP: $localIp",
                                        fontSize = 12.sp,
                                        fontWeight = FontWeight.SemiBold,
                                        color = ElectricBlue
                                    )
                                }
                            }
                        }
                    }

                    Text(
                        text = "Enter this PIN on the PC to connect",
                        fontSize = 15.sp,
                        color = TextSecondary,
                        textAlign = TextAlign.Center,
                        modifier = Modifier.padding(horizontal = 16.dp)
                    )

                    Surface(
                        shape = RoundedCornerShape(50),
                        color = ElectricBlue.copy(alpha = 0.1f)
                    ) {
                        Row(
                            modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            val dotInfinite = rememberInfiniteTransition(label = "dots")
                            for (i in 0..2) {
                                val dotAlpha by dotInfinite.animateFloat(
                                    initialValue = 0.2f,
                                    targetValue = 1f,
                                    animationSpec = infiniteRepeatable(
                                        animation = tween(600, delayMillis = i * 200),
                                        repeatMode = RepeatMode.Reverse
                                    ),
                                    label = "dot$i"
                                )
                                Box(
                                    modifier = Modifier
                                        .size(8.dp)
                                        .background(ElectricBlue.copy(alpha = dotAlpha), CircleShape)
                                )
                                if (i < 2) Spacer(modifier = Modifier.width(6.dp))
                            }
                            Spacer(modifier = Modifier.width(10.dp))
                            Text(
                                text = "Listening for PC on port 5000...",
                                fontSize = 13.sp,
                                color = TextSecondary
                            )
                        }
                    }
                }
            } else {
                // SENDER MODE
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .weight(1f, fill = false)
                        .verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(14.dp)
                ) {
                    // 1. Files to send card
                    Card(
                        shape = RoundedCornerShape(20.dp),
                        colors = CardDefaults.cardColors(containerColor = NavyCard),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Column(
                            modifier = Modifier.padding(16.dp),
                            verticalArrangement = Arrangement.spacedBy(10.dp)
                        ) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically
                            ) {
                                Text(
                                    text = "Files to Send",
                                    fontSize = 15.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = TextPrimary
                                )
                                Button(
                                    onClick = { filePickerLauncher.launch(arrayOf("*/*")) },
                                    colors = ButtonDefaults.buttonColors(containerColor = ElectricBlue),
                                    shape = RoundedCornerShape(10.dp),
                                    contentPadding = PaddingValues(horizontal = 12.dp, vertical = 6.dp)
                                ) {
                                    Text("+ Add Files", fontSize = 12.sp, fontWeight = FontWeight.Bold, color = Color.Black)
                                }
                            }

                            if (selectedFiles.isEmpty()) {
                                Text(
                                    text = "No files added. (Instant 2 GB high-speed benchmark will run if empty)",
                                    fontSize = 12.sp,
                                    color = TextSecondary
                                )
                            } else {
                                selectedFiles.forEach { file ->
                                    Row(
                                        modifier = Modifier
                                            .fillMaxWidth()
                                            .padding(vertical = 4.dp),
                                        horizontalArrangement = Arrangement.SpaceBetween,
                                        verticalAlignment = Alignment.CenterVertically
                                    ) {
                                        Column(modifier = Modifier.weight(1f)) {
                                            Text(
                                                text = file.name,
                                                fontSize = 13.sp,
                                                fontWeight = FontWeight.Medium,
                                                color = TextPrimary,
                                                maxLines = 1,
                                                overflow = TextOverflow.Ellipsis
                                            )
                                            val displaySize = when {
                                                file.size < 1024 -> "${file.size} B"
                                                file.size < 1024 * 1024 -> String.format("%.1f KB", file.size / 1024.0)
                                                file.size < 1024L * 1024L * 1024L -> String.format("%.1f MB", file.size / (1024.0 * 1024.0))
                                                else -> String.format("%.2f GB", file.size / (1024.0 * 1024.0 * 1024.0))
                                            }
                                            Text(
                                                text = displaySize,
                                                fontSize = 11.sp,
                                                color = TextSecondary
                                            )
                                        }
                                        IconButton(
                                            onClick = { selectedFiles = selectedFiles.filter { it.uri != file.uri } },
                                            modifier = Modifier.size(28.dp)
                                        ) {
                                            Text("✕", fontSize = 14.sp, color = Color.Red)
                                        }
                                    }
                                }

                                val totalSize = selectedFiles.sumOf { it.size }
                                val totalDisplay = when {
                                    totalSize < 1024 -> "$totalSize B"
                                    totalSize < 1024 * 1024 -> String.format("%.1f KB", totalSize / 1024.0)
                                    totalSize < 1024L * 1024L * 1024L -> String.format("%.1f MB", totalSize / (1024.0 * 1024.0))
                                    else -> String.format("%.2f GB", totalSize / (1024.0 * 1024.0 * 1024.0))
                                }
                                Text(
                                    text = "Total: ${selectedFiles.size} file(s) • $totalDisplay",
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.Bold,
                                    color = ElectricBlue
                                )
                            }
                        }
                    }

                    // 2. Connection Details card
                    Card(
                        shape = RoundedCornerShape(20.dp),
                        colors = CardDefaults.cardColors(containerColor = NavyCard),
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Column(
                            modifier = Modifier.padding(16.dp),
                            verticalArrangement = Arrangement.spacedBy(12.dp)
                        ) {
                            Text(
                                text = "Connection Details",
                                fontSize = 15.sp,
                                fontWeight = FontWeight.Bold,
                                color = TextPrimary
                            )

                            OutlinedTextField(
                                value = targetIp,
                                onValueChange = onTargetIpChanged,
                                label = { Text("PC IP Address") },
                                modifier = Modifier.fillMaxWidth(),
                                singleLine = true,
                                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                                colors = OutlinedTextFieldDefaults.colors(
                                    focusedBorderColor = ElectricBlue,
                                    focusedLabelColor = ElectricBlue,
                                    cursorColor = ElectricBlue
                                )
                            )

                            OutlinedTextField(
                                value = pinInput,
                                onValueChange = { if (it.length <= 6) pinInput = it },
                                label = { Text("6-Digit PIN from PC") },
                                modifier = Modifier.fillMaxWidth(),
                                singleLine = true,
                                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                                textStyle = LocalTextStyle.current.copy(
                                    fontFamily = FontFamily.Monospace,
                                    fontSize = 24.sp,
                                    letterSpacing = 6.sp
                                ),
                                colors = OutlinedTextFieldDefaults.colors(
                                    focusedBorderColor = ElectricBlue,
                                    focusedLabelColor = ElectricBlue,
                                    cursorColor = ElectricBlue
                                )
                            )
                        }
                    }

                    Button(
                        onClick = { onConnect(pinInput, selectedFiles) },
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(54.dp),
                        shape = RoundedCornerShape(16.dp),
                        colors = ButtonDefaults.buttonColors(containerColor = ElectricBlue),
                        enabled = targetIp.isNotBlank()
                    ) {
                        Text(
                            text = "Connect & Send →",
                            fontSize = 16.sp,
                            fontWeight = FontWeight.Bold,
                            color = Color.Black
                        )
                    }
                }
            }

            // Bottom cancel/back button
            OutlinedButton(
                onClick = onBack,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(48.dp),
                shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.outlinedButtonColors(contentColor = TextSecondary)
            ) {
                Text(text = "Cancel", fontSize = 15.sp)
            }
        }
    }
}
