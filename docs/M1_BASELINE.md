# HyperSend Milestone 1: Measured Throughput & Baseline Report

## 1. Measured Physical Baseline Summary

The M1 bare TCP baseline test was conducted across the live ad-hoc Wi-Fi 6 link between the **Samsung Galaxy S23 FE** (5 GHz Hotspot AP, Channel 149, 80 MHz, Wi-Fi 6 enabled) and the **Windows 11 Laptop** (Intel Wi-Fi 6E AX211):

| Direction | Measured Throughput | Elapsed Time (2 GB) | Target | iperf3 Ceiling | % of Ceiling | Status |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Phone -> Laptop** | **93.13 MB/s** (745.02 Mbps) | **21.99 s** | >= 90.0 MB/s | ~110.0 MB/s | **84.7%** | **TARGET EXCEEDED** |
| **Laptop -> Phone** | **48.28 MB/s** (386.21 Mbps) | **42.42 s** | >= 65.0 MB/s | ~78.0 MB/s | **61.9%** | **NEEDS WORK QUEUE (M2)** |

---

## 2. In-Depth Bottleneck Analysis: Laptop -> Phone (48.28 MB/s)

### Why Phone -> Laptop Succeeded (93.13 MB/s):
Phone -> Laptop traffic has 0 retransmissions and naturally balanced per-stream TCP window scaling over the S23 FE's Broadcom Wi-Fi chip transmission path. With our 2 MB socket buffers and zero-copy direct buffers, it achieved **84.7% of the pure iperf3 ceiling on the very first run**.

### Root Cause of Laptop -> Phone Drag (48.28 MB/s):
1. **Static Range Partitioning vs. Uneven Streams:**
   In M1, the 2048 MB file was statically partitioned into 4 equal 512 MB slices (Stream 0: 0-512MB, Stream 1: 512-1024MB, etc.).
   As observed in the environment spec: *“Laptop -> phone: ~623 Mbit/s (~78 MB/s), uneven per-stream rates”*.
   When one stream drops to 10 MB/s due to 802.11ax beamforming/aggregation jitter, the other 3 streams finish their 512 MB early and drop to 0 MB/s, leaving the entire transfer bottlenecked by the slowest stream at the tail end.
2. **Solution in Milestone 2:**
   Replace static range partitioning with a **Dynamic Work Queue of 2 MB Chunks**.
   Streams pull chunks dynamically as they finish. If Stream 1 is fast, it processes 60% of the chunks; if Stream 3 is slow, it only processes 10%. Out-of-order writes ensure every chunk is written directly to its pre-allocated offset without blocking.
3. **Stream Count & Chunk Size Tuning:**
   Tuning stream count (2, 4, 6, 8) and chunk sizes (1MB, 2MB, 4MB) in Milestone 2 to maximize the Intel AX211 transmission path.

---

## 3. Plan for Milestone 2: Chunk Engine
1. Implement thread-safe `ChunkWorkQueue` on sender and `OutOfOrderChunkWriter` on receiver.
2. Implement 2 MB default chunking with frame header (`Magic`, `Type=DATA_CHUNK`, `FileId`, `ChunkIndex`, `Offset`, `Length`).
3. Overlapped bounded pipeline: Disk Read -> Queue -> Socket Send (and Socket Recv -> Queue -> Disk Write) with backpressure.
4. Run automated sweep across (Streams: 2, 4, 6, 8) and (Chunk sizes: 1MB, 2MB, 4MB) to measure throughput uplift.
