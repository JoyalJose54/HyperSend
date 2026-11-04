# HyperSend Wire Protocol Specification (v1.0)

## 1. Network Architecture & Connection Management
HyperSend operates over a single TCP listening port (default: `5000`) on the server peer (typically the phone when hosting the hotspot, but bidirectional).

A single transfer session consists of:
1. **Control Connection:** A persistent, bi-directional TCP stream used for session negotiation, mutual authentication, file metadata manifests (`OFFER`), chunk synchronization/acknowledgments (`ACCEPT`/`CHUNK_ACK`), and session tear-down.
2. **Data Connections (1 to N parallel TCP streams):** Multiple worker TCP streams dedicated solely to carrying chunk frames (`DATA_CHUNK`).

### 1.1 Connection Handshake & Multiplexing
All inbound connections connect to port `5000`. The server inspects the first 16 bytes:

```
+-------------------------------------------------------+
|  Bytes 0..6: Magic ASCII "HS_CTRL" or "HS_DATA"       | (7 bytes)
+-------------------------------------------------------+
|  Byte 7: Reserved / Pad (0x00)                        | (1 byte)
+-------------------------------------------------------+
|  Bytes 8..15: Session ID (uint64 Big-Endian)          | (8 bytes)
+-------------------------------------------------------+
|  (If HS_DATA: Byte 16 is Stream ID [0x01..0xFF])      | (Optional 1 byte)
+-------------------------------------------------------+
```

- If `HS_CTRL` is received: A new session is initialized or an existing session is reattached.
- If `HS_DATA` is received: The socket is attached to the existing session matching the `Session ID` as worker stream `Stream ID`.

---

## 2. Frame Structure
All communications on both Control and Data connections use uniform fixed-header framing:

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|       Magic (0x48, 0x53)      |  Version (1)  |  Type (1)     |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                           File ID                             |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                         Chunk Index                           |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                       Payload Length (L)                      |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
+                   Nonce Counter (64-bit uint)                 +
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|                   Payload (L - 16 bytes)                      |
|                           ...                                 |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                    AEAD Tag (16 bytes)                        |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

### 2.1 Header Field Definitions
- **Magic (2 bytes):** `0x48, 0x53` ("HS" in ASCII). Frames missing this magic byte are immediately dropped and the socket is closed.
- **Version (1 byte):** `0x01`.
- **Type (1 byte):**
  - `0x01`: `HELLO`
  - `0x02`: `AUTH_CHALLENGE`
  - `0x03`: `AUTH_RESPONSE`
  - `0x04`: `OFFER` (Manifest of files to be sent)
  - `0x05`: `ACCEPT` (Acceptance & bitmap of already received chunks)
  - `0x06`: `DATA_CHUNK` (Chunk data frame)
  - `0x07`: `CHUNK_ACK` (Individual or bitmap chunk confirmation)
  - `0x08`: `FILE_DONE` (File transfer complete & final BLAKE3/SHA-256 hash)
  - `0x09`: `PING` / `PONG` (Keep-alive and latency measurement)
  - `0x0A`: `CANCEL` (Transfer cancellation request)
  - `0xFF`: `ERROR` (Error code & description)
- **File ID (4 bytes, Big-Endian uint32):** Unique identifier of the file in the transfer batch. `0x00000000` for control/session messages.
- **Chunk Index (4 bytes, Big-Endian uint32):** 0-based chunk sequence index within the file.
- **Payload Length (4 bytes, Big-Endian uint32):** Total byte length of the following payload INCLUDING the 16-byte AEAD authentication tag. In M1 (unencrypted baseline), this equals the raw data length and AEAD Tag is absent.
- **Nonce Counter (8 bytes, Big-Endian uint64):** Monotonically increasing counter per stream. Prevents replay attacks.

---

## 3. Cryptography & Pairing Handshake

### 3.1 6-Digit Pairing & Key Derivation (PAKE / HMAC-Based)
1. **Phone Hotspot Setup:** The phone generates a random 6-digit numeric pairing code `C` (e.g. `482910`) and a 16-byte random salt `S_phone`.
2. **Laptop Discovery:** Laptop discovers phone gateway IP (`10.15.70.174`) and connects to TCP port 5000.
3. **Handshake Flow:**
   - Laptop -> Phone: `HELLO` with laptop ephemeral X25519 public key `PK_laptop` and client nonce `N_laptop`.
   - Phone -> Laptop: `AUTH_CHALLENGE` with phone ephemeral X25519 public key `PK_phone`, salt `S_phone`, and server nonce `N_phone`.
   - User inputs 6-digit code `C` into Laptop UI.
   - Both sides compute:
     - Shared Secret `SS = X25519(SK, PK_peer)`
     - Code Key `K_code = PBKDF2-HMAC-SHA256(password=C, salt=S_phone, iterations=10000, keylen=32)`
     - Master Key `MK = HKDF-Extract(salt=N_laptop || N_phone, IKM = SS || K_code)`
     - Derived Keys via `HKDF-Expand`:
       - `K_ctrl_enc`, `K_ctrl_dec` (256-bit AEAD keys for control stream)
       - `K_data_master` (256-bit root key for parallel data streams)
   - Laptop -> Phone: `AUTH_RESPONSE` containing `HMAC-SHA256(MK, "LAPTOP_CONFIRM" || N_phone)`.
   - If valid, Phone responds with `AUTH_SUCCESS` containing `HMAC-SHA256(MK, "PHONE_CONFIRM" || N_laptop)`.
   - If invalid: Phone increments failed attempt counter. After 5 failed attempts, the session locks for 5 minutes.

### 3.2 Authenticated Encryption (AEAD)
- **Algorithm:** ChaCha20-Poly1305 or AES-256-GCM.
- **Data Stream Keying:** Stream `i` derives its key as:
  `K_stream_i = HKDF-Expand(K_data_master, info="HyperSend-Stream-" || i, keylen=32)`.
- **Nonce Construction:**
  `Nonce (12 bytes) = [ StreamID (4 bytes) ] || [ NonceCounter (8 bytes) ]`.
  Because each stream has an independent key and a monotonically increasing 64-bit counter, nonce reuse is cryptographically impossible.

---

## 4. Message Payloads (JSON / Binary)

### 4.1 OFFER Message (Payload)
Sent by sender to declare the batch of files:
```json
{
  "transfer_id": "a9c08d1f-8012-4c2f-bcfb-4a5c95a2e921",
  "direction": "SENDER_TO_RECEIVER",
  "total_bytes": 10737418240,
  "chunk_size": 2097152,
  "files": [
    {
      "file_id": 1,
      "relative_path": "Videos/Sample.mp4",
      "size": 10737418240,
      "modified_time_utc": 1730700000,
      "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      "total_chunks": 5120
    }
  ]
}
```

### 4.2 ACCEPT Message (Payload)
Sent by receiver in response to `OFFER`:
```json
{
  "transfer_id": "a9c08d1f-8012-4c2f-bcfb-4a5c95a2e921",
  "accepted": true,
  "file_resumes": [
    {
      "file_id": 1,
      "received_chunks_bitmap_base64": "//////8="
    }
  ]
}
```

### 4.3 FILE_DONE Message (Payload)
```json
{
  "file_id": 1,
  "final_hash": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
  "elapsed_ms": 11520,
  "average_mbps": 932.06
}
```

---

## 5. Milestone 1 (M1 Raw Baseline) Unencrypted Protocol Mode
In Milestone 1, the encryption layer is omitted to establish the raw TCP baseline:
- Header: Magic (2) + Version (1) + Type (1) + FileId (4) + ChunkIndex (4) + Length (4) + Nonce (8) = 24 bytes.
- Type `0x05` (`DATA_CHUNK`): Raw binary payload of size `Length` follows immediately without AEAD tag.
- 4 Parallel streams connect directly with `HS_DATA` header and pull chunks or contiguous stream blocks from sender.
- Built-in Synthetic Mode (`FileId = 0xFFFFFFFF`): In-memory synthetic pattern generation (no disk I/O) to verify that the socket layer reaches 100% of iperf3 bandwidth.

