# HyperSend: High-Speed Wireless File Transfer (Android <-> Windows)
## Comprehensive Architecture Document & Master Plan

### 1. Executive Summary & Design Constraints
HyperSend is a localized, zero-cloud, peer-to-peer file transfer system engineered specifically to saturate the physical bandwidth ceiling of an ad-hoc 5 GHz Wi-Fi 6 (802.11ax) Mobile Hotspot link between a modern Android device (Samsung Galaxy S23 FE, Android 14+) and a Windows 11 laptop equipped with an Intel Wi-Fi 6E AX211 adapter.

#### Measured Hardware & Physical Link Facts:
- **Topology:** Phone acts as the Mobile Hotspot AP (5 GHz, 80 MHz channel 149, Wi-Fi 6 enabled). The laptop associates as a client station.
- **Addressing:** The phone's IP is the laptop's default IPv4 gateway (e.g. `10.15.70.174`), while the laptop is assigned via DHCP (e.g. `10.15.70.247`). Zero external internet access is required or permitted.
- **Physical Link Rate:** 721 Mbps RX / 576 Mbps TX (80 MHz 802.11ax).
- **iperf3 4-stream Physical Network Ceilings:**
  - Phone -> Laptop: ~878 Mbit/s (~110 MB/s), 0 retransmissions, balanced streams.
  - Laptop -> Phone: ~623 Mbit/s (~78 MB/s), uneven per-stream throughput.
- **HyperSend Throughput Target (80–90% of physical ceiling):**
  - Phone -> Laptop: **>= 90 MB/s**
  - Laptop -> Phone: **>= 65 MB/s**

---

### 2. Technology Stack & Framework Selection

#### 2.1 Monorepo Layout
```
/HyperSend/
├── /protocol/                # Language-agnostic protocol specification and test vectors
│   ├── PROTOCOL.md           # Binary framing, message state machine, security specs
│   └── test_vectors.json     # Shared AEAD, hashing, and framing test vectors
├── /docs/                    # Architectural specs, benchmark logs, and runbooks
│   ├── ARCHITECTURE.md       # Full system architecture (this document)
│   └── M1_BASELINE.md        # M1 baseline test results & analysis
├── /windows/                 # C# .NET 8 solution
│   ├── HyperSend.sln
│   ├── HyperSend.Core/       # Framing, socket engine, buffer pool, pipeline, crypto
│   ├── HyperSend.Benchmark/  # CLI benchmarking tool (synthetic & raw file transfer)
│   ├── HyperSend.UI/         # WPF .NET 8 desktop application
│   └── HyperSend.Tests/      # Unit and protocol compliance tests
└── /android/                 # Kotlin Android application
    ├── build.gradle.kts
    ├── app/
    │   ├── build.gradle.kts
    │   └── src/
    │       ├── main/
    │       │   ├── AndroidManifest.xml
    │       │   └── java/com/hypersend/
    │       │       ├── protocol/       # Binary framing & packet encoders/decoders
    │       │       ├── engine/         # Sockets, worker pools, chunk pipelines
    │       │       ├── service/        # TransferForegroundService, WakeLock/WifiLock
    │       │       ├── storage/        # MediaStore & direct FileChannel I/O
    │       │       └── ui/             # Jetpack Compose UI
    │       └── test/                   # Unit & protocol tests
```

#### 2.2 Windows Platform Decision: WPF (.NET 8) vs. WinUI 3
**Decision: WPF on .NET 8 is chosen.**
*Rationale:*
1. **Packaging & Deployment Reliability:** WinUI 3 requires the Windows App SDK runtime bootstrapper, MSIX containerization, or unpackaged bootstrapping that frequently encounters DLL-activation, elevation, and sandbox failures across Windows builds. WPF runs natively as a single self-contained or framework-dependent .NET 8 executable without third-party runtime dependencies.
2. **I/O & Drag-and-Drop Robustness:** WPF has zero sandbox constraints regarding Shell drag-and-drop (`DragDrop.Drop`), direct Win32 file handles, or elevated execution.
3. **High-Performance Direct Integration:** WPF seamlessly hosts real-time rendering components (e.g. 60 FPS live throughput graph via SkiaSharp or lightweight custom Canvas) and interoperates directly with `System.IO.Pipelines` and native memory pointers without COM marshalling overhead.

#### 2.3 Android Platform
- **Language & Runtimes:** Kotlin 1.9+, Java 17/21 bytecode target, Coroutines + Channels for asynchronous orchestration.
- **UI:** Jetpack Compose with Material 3.
- **I/O Engine:** Java NIO (`SocketChannel`, `FileChannel`) with direct off-heap `ByteBuffer` allocation to prevent JVM GC pauses during 100+ MB/s transfers.
- **Service Architecture:** Sticky Foreground Service with `WIFI_MODE_FULL_HIGH_PERF` `WifiLock` and partial `WakeLock`.

---

### 3. Core Speed & Pipeline Architecture

```
   ================ SENDER PIPELINE ================
   [ Disk File ]
         │ (FileChannel.read / FileOptions.Asynchronous)
         ▼
   [ Bounded Disk Read Queue (ArrayPool / Direct Buffers) ]
         │ (Hardware AES-256-GCM / ChaCha20-Poly1305)
         ▼
   [ Encrypted Chunk Work Queue ]
         │
         ├── Data Stream 1 (TCP_NODELAY, SO_SNDBUF 2-4MB) ──┐
         ├── Data Stream 2 (TCP_NODELAY, SO_SNDBUF 2-4MB) ──┼──> [ 5 GHz 802.11ax Link ]
         ├── Data Stream 3 (TCP_NODELAY, SO_SNDBUF 2-4MB) ──┤
         └── Data Stream 4 (TCP_NODELAY, SO_SNDBUF 2-4MB) ──┘

   =============== RECEIVER PIPELINE ===============
         ┌── Data Stream 1 (SO_RCVBUF 2-4MB) <──────────────┐
         ├── Data Stream 2 (SO_RCVBUF 2-4MB) <──────────────┼──< [ 5 GHz 802.11ax Link ]
         ├── Data Stream 3 (SO_RCVBUF 2-4MB) <──────────────┤
         └── Data Stream 4 (SO_RCVBUF 2-4MB) <──────────────┘
         │
         ▼
   [ Frame Reassembly & Authenticated Decryption Queue ]
         │ (AEAD Auth Tag Verification)
         ▼
   [ Random-Access Out-of-Order Disk Writer Queue ]
         │ (FileChannel.position(offset).write / Pre-allocated sparse file)
         ▼
   [ Disk File (.part -> Final Rename) ]
```

#### Speed Techniques Applied:
1. **Parallel Stream Multiplexing:** 4 independent TCP sockets multiplex chunks from a thread-safe shared work queue. A faster socket automatically pulls the next chunk, preventing head-of-line blocking on uneven streams (observed in laptop->phone).
2. **Chunking & Out-of-Order Writing:** Fixed 2 MB chunk size. The receiver pre-allocates the complete target file size (`SetLength` on Windows, `fallocate` / `seek` on Android) and writes chunks directly to `chunk_index * chunk_size` offset as they arrive.
3. **Zero-Copy & Memory Pool Allocation:**
   - Windows: `ArrayPool<byte>.Shared` and `MemoryPool<byte>.Shared` across pipelines.
   - Android: Reusable pooled direct `ByteBuffer` instances allocated once per stream. Zero JVM allocations inside the streaming loop.
4. **Decoupled Pipeline Stages:** Disk reading, AEAD encryption, and network streaming run in separate concurrency stages connected by bounded non-blocking queues (backpressure capacity: 8 chunks / 16 MB).
5. **Small File Bundling:** Files < 1 MB are packed sequentially into composite virtual archive containers (4–8 MB bundles) before transmission, completely eliminating per-file metadata roundtrips.

---

### 4. Wire Protocol Architecture (Summary)

The full wire protocol specification is detailed in `/protocol/PROTOCOL.md`.
- **Port Strategy:** Control connection opens on TCP port 5000. Data connections (Stream IDs 1..N) connect to the same port, initiating an immediate 16-byte handshake:
  `[HS_DATA (7 bytes)][SessionID (8 bytes)][StreamID (1 byte)]`.
- **Framing Structure:**
  - `Header (24 bytes)`:
    - `Magic (2 bytes)`: `0x48, 0x53` ("HS")
    - `Version (1 byte)`: `0x01`
    - `Type (1 byte)`: `0x01` (HELLO), `0x02` (AUTH), `0x03` (OFFER), `0x04` (ACCEPT), `0x05` (DATA_CHUNK), `0x06` (CHUNK_ACK), `0x07` (FILE_DONE), `0x08` (CANCEL), `0x09` (PING), `0x0A` (ERROR)
    - `FileId (4 bytes)`: Big-endian integer
    - `ChunkIndex (4 bytes)`: Big-endian integer
    - `PayloadLength (4 bytes)`: Length of payload including 16-byte AEAD tag
    - `NonceCounter (8 bytes)`: 64-bit monotonically increasing counter
  - `Payload`: Encrypted chunk data + 16-byte Poly1305 / GCM authentication tag.

---

### 5. Milestone Execution Roadmap

| Milestone | Deliverables | Success Criteria & Verification |
| :--- | :--- | :--- |
| **M1: Raw Baseline** | Bare TCP multi-stream engine (4 sockets, no crypto), synthetic memory generator, raw file streamer on both Windows & Android. | Verified throughput test against iperf3 ceiling (>70 MB/s laptop->phone, >90 MB/s phone->laptop). |
| **M2: Chunk Engine** | 2 MB chunking, work queue, out-of-order write dispatcher, buffer pool, pipeline backpressure. | Benchmark matrix sweeping chunk sizes (1MB, 2MB, 4MB) and stream counts (2, 4, 6, 8). |
| **M3: Discovery & Pairing** | Gateway IP resolution, 6-digit code PAKE/HMAC handshake, HKDF ephemeral key derivation, AEAD encryption with shared test vectors. | Verified encrypted transfer with zero plaintext wire leakage in Wireshark/pcap; test vector test passing on both sides. |
| **M4: Android Service** | Android Foreground Service, `WifiLock`, `WakeLock`, battery optimization prompts, MediaStore direct streaming. | Zero throttling or socket drops during 10-minute screen-off transfer. |
| **M5: Reliability & Resume** | Chunk bitmap state persistence, reconnect daemon, SHA-256 / BLAKE3 file verification, cancel/pause/resume. | Transfer interrupted at 50% cleanly resumes without re-transmitting completed chunks. Corrupt chunk test repairs chunk. |
| **M6: Bundling & Folders** | Recursive folder parser, path sanitization (traversal defense), composite bundle packer for < 1 MB files. | 10,000 small files transfer at >= 50% of single large-file throughput. |
| **M7: User Interface** | Jetpack Compose Android UI + WPF Windows UI, pairing QR code, live speed graph, link-health panel. | Visual pairing flow, live throughput display, link rate warnings (2.4 GHz vs 5 GHz). |
| **M8: Fallback Channels** | USB detection (`adb reverse tcp:5000 tcp:5000` / USB tethering) + LAN mDNS mode. | Automatic seamless failover: USB -> Hotspot -> LAN. |
| **M9: Hardening & Packaging** | Parser fuzzing, 1-hour soak test, firewall rule configuration, signed debug APK, Windows standalone executable. | All unit tests pass; standalone executables/APKs verified on clean environments. |

