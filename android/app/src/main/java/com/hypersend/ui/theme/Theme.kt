package com.hypersend.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val ElectricBlue = Color(0xFF4FC3F7)
val Teal = Color(0xFF26C6DA)
val GreenSuccess = Color(0xFF66BB6A)
val NavyDark = Color(0xFF0D1B2A)
val NavySurface = Color(0xFF1B2838)
val NavyCard = Color(0xFF243447)
val TextPrimary = Color(0xFFFFFFFF)
val TextSecondary = Color(0xFFB0BEC5)

private val HyperSendColorScheme = darkColorScheme(
    primary = ElectricBlue,
    onPrimary = Color(0xFF003549),
    primaryContainer = Color(0xFF004D6B),
    onPrimaryContainer = Color(0xFFB3E5FC),
    secondary = Teal,
    onSecondary = Color(0xFF003735),
    secondaryContainer = Color(0xFF004F4E),
    onSecondaryContainer = Color(0xFFB2DFDB),
    tertiary = GreenSuccess,
    onTertiary = Color(0xFF003920),
    tertiaryContainer = Color(0xFF00522E),
    onTertiaryContainer = Color(0xFFC8E6C9),
    background = NavyDark,
    onBackground = TextPrimary,
    surface = NavySurface,
    onSurface = TextPrimary,
    surfaceVariant = NavyCard,
    onSurfaceVariant = TextSecondary,
    error = Color(0xFFEF5350),
    onError = Color(0xFF000000)
)

@Composable
fun HyperSendTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = HyperSendColorScheme,
        content = content
    )
}

