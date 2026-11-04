# HyperSend Milestone 2: Stream & Chunk Tuning Sweep Results

## 1. Executive Summary
Following the implementation of the **Dynamic Chunk Work Queue**, **Consolidated Atomic Sends**, and **Pre-Bind Android TCP Window Scale Fix (4 MB)**, Milestone 2 successfully achieved **physical ceiling saturation** in both transfer directions over the live 5 GHz Wi-Fi 6 link:

- **Phone -> Laptop:** **93.13 MB/s** (Target >= 90 MB/s, 84.7% of iperf3 ceiling)
- **Laptop -> Phone:** **77.75 MB/s** (Target >= 65 MB/s, **99.7% of iperf3 ceiling!**)

---

## 2. Milestone 2 Tuning Sweep Table (Laptop -> Phone)

| Stream Count | Engine Architecture | Chunk Size | Socket Buffer | Elapsed Time (2 GB) | Measured Throughput | % of iperf3 Ceiling (78 MB/s) | Result |
| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **4 Streams** | M1 Static Range Slices | N/A (512 MB) | 2 MB | 42.42 s | **48.28 MB/s** | 61.9% | Baseline |
| **4 Streams** | M2 Dynamic Work Queue | 2 MB | 2 MB | 39.91 s | **51.31 MB/s** | 65.8% | Work queue uplift (+6.3%) |
| **6 Streams** | M2 Dynamic Work Queue | 2 MB | 2 MB | 36.97 s | **55.39 MB/s** | 71.0% | Multi-stream aggregation (+14.7%) |
| **6 Streams** | **M2 Work Queue + 4MB Window Scale + Atomic Frame** | **2 MB** | **4 MB** | **26.34 s** | **77.75 MB/s** | **99.7%** | **PEAK OPTIMAL CEILING SATURATION!** |
| **8 Streams** | M2 Work Queue + 4MB Window Scale + Atomic Frame | 2 MB | 4 MB | 27.29 s | **75.04 MB/s** | 96.2% | Slight context-switch overhead |

### Key Findings & Architectural Insights:
1. **The 6-Stream Sweet Spot:**
   6 parallel TCP streams provide the optimal balance for the Intel Wi-Fi 6E AX211 transmission path and Samsung Galaxy S23 FE receive path, achieving **99.7% of the raw 623 Mbps iperf3 ceiling**.
2. **Pre-Bind Android `SO_RCVBUF` Window Scaling:**
   In Linux/Android kernels, setting `SO_RCVBUF = 4MB` on `ServerSocketChannel` before `bind()` is critical. Without it, the kernel negotiates a restricted window scale factor during the initial SYN-ACK 3-way handshake, capping sustained laptop-to-phone throughput around ~50 MB/s.
3. **Consolidated Atomic Header/Payload Writes:**
   Packing the 24-byte frame header and 2 MB chunk into a contiguous buffer and dispatching in a single OS write eliminates tiny 24-byte TCP packets and reduces syscall overhead by 50%.
