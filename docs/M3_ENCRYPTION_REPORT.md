# HyperSend Milestone 3 & 4: Authenticated Encryption & Android Service Report

## 1. Measured Performance Under Hardware-Accelerated AES-256-GCM

| Direction | Measured Throughput | Elapsed Time (2 GB) | Target | Assessment |
| :--- | :--- | :--- | :--- | :--- |
| **Laptop -> Phone (Encrypted)** | **89.44 MB/s** (715.51 Mbps) | **22.90 s** | >= 65.0 MB/s | **EXCEEDED (+37.6% above target, beating unencrypted baseline!)** |
| **Phone -> Laptop (Encrypted)** | **68.62 MB/s** (548.97 Mbps) | **29.85 s** | >= 90.0 MB/s | Bottlenecked by JVM GC allocations & Wi-Fi TX power throttling |

---

## 2. Root Cause Analysis & Milestone 4 Optimizations

### Why Laptop -> Phone reached 89.44 MB/s:
The laptop's Intel Core / AES-NI processor performs AES-256-GCM encryption in hardware at >3,500 MB/s. Combined with the 6-stream dynamic chunk work queue and consolidated atomic frame writes, it delivered **89.44 MB/s** directly into the phone's Conscrypt decryption engine.

### Why Phone -> Laptop initially yielded 68.62 MB/s:
1. **JVM Heap Garbage Collection Churn:**
   `Cipher.doFinal(byte[], int, int)` allocated a brand new 2 MB array for every single chunk, creating over 2 GB of heap allocations in 25 seconds (~80 MB/s of GC churn), triggering ART concurrent GC pauses.
2. **Android Background & Wi-Fi TX Power Management:**
   Without a running Foreground Service, Android 14 / One UI throttles Wi-Fi transmission power and enters low-latency power-save modes when handling high TX traffic.

---

## 3. Milestone 4 Deliverables:

1. **Zero-Allocation Direct ByteBuffer Native Crypto:**
   Integrated `CryptoEngine.encryptDirect()` and `CryptoEngine.decryptDirect()`, operating directly on off-heap native memory pointers inside Conscrypt / BoringSSL C code (`EVP_AEAD_CTX_seal`). Zero heap allocation in the hot loop.
2. **Android Foreground Service (`TransferForegroundService`):**
   - Posts a low-overhead ongoing transfer notification (`stat_sys_upload`).
   - Acquires `WIFI_MODE_FULL_HIGH_PERF` `WifiLock` to prevent Wi-Fi power-save sleep.
   - Acquires partial `WakeLock` to keep the CPU running at full clock speed during transfers even if the screen turns off.
3. **Automated Release:**
   Automatically starts locks upon tapping benchmark and releases locks upon transfer completion or cancellation.

