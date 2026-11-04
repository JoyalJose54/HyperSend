# HyperSend: Ultra High-Speed Wireless File Transfer

Peer-to-peer file transfer between Android (Samsung Galaxy S23 FE, Android 14+) and Windows 11 (Intel Wi-Fi 6E AX211) saturating the physical 5 GHz Wi-Fi 6 Mobile Hotspot bandwidth ceiling.

## Monorepo Structure
- `/windows`: C# .NET 8 solution
  - `HyperSend.Core`: High-performance parallel socket engine, buffer pooling, network discovery.
  - `HyperSend.Benchmark`: CLI benchmark utility for synthetic network testing and raw file transfers.
  - `HyperSend.Tests`: xUnit automated test suite.
- `/android`: Modern Android application
  - Kotlin coroutines, Java NIO non-blocking direct ByteBuffers, Jetpack Compose Material 3 UI.
- `/protocol`: Language-agnostic wire protocol specification (`PROTOCOL.md`).
- `/docs`: Complete architecture (`ARCHITECTURE.md`) and milestone reports (`M1_BASELINE.md`).

## Quick Start (Milestone 1 Baseline Benchmark)

### Windows
```powershell
# Run xUnit tests
dotnet test windows/HyperSend.sln

# Run loopback pipeline verification
dotnet run --project windows/HyperSend.Benchmark -- loopback

# Query Wi-Fi 6 link health & auto-detect phone gateway IP
dotnet run --project windows/HyperSend.Benchmark -- discover
```

### Android
```powershell
# Build debug APK
cd android
.\gradlew.bat assembleDebug

# Run unit tests
.\gradlew.bat testDebugUnitTest
```
Built APK is located at: `android/app/build/outputs/apk/debug/app-debug.apk`.
