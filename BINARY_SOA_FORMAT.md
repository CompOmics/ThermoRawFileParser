# RCIA Binary SoA Stream Format — v1

A lossless, low-overhead binary format for streaming Thermo RAW spectrum
data into downstream consumers. Designed to be:

1. **Lossless** — every scan-level value RawFileReader exposes is captured
2. **Streamable** — sequence of self-describing records over a pipe
3. **Zero-copy on the read side** — SoA arrays at known offsets, naturally aligned
4. **Gracefully nullable** — missing values use NaN (floats) or `-1` / `0` sentinels (integers)
5. **Forward-compatible** — variable-length sections at offsets, fixed scalar fields at known positions

All multi-byte integers and floats are **little-endian** (matches all modern x86/ARM CPUs).
Floats follow IEEE 754.

## Streaming vs file output

The writer produces the **identical byte format** in both modes; the difference
is only the destination and a few I/O tuning details:

| Mode | Invocation | Destination | Buffering |
|------|-----------|-------------|-----------|
| **Streaming** (primary use) | `--format=BinarySoa --stdout` | parent process via OS pipe | 1 MB BufferedStream wrap |
| **File** (sidecar/cache) | `--format=BinarySoa --output=path.rcia.bin` | regular file | 1 MB BufferedStream wrap |

Streaming is the primary use case; downstream pipelines consume records as they
arrive without materializing the whole run on disk. File output exists so the
same downstream consumer can replay a file later (e.g., re-search the same RAW
with different parameters without re-reading via RawFileReader).

A consumer cannot tell the two modes apart from the byte stream — the writer
emits the same file header, records, and EOF marker regardless of destination.

---

## Byte order

All values are little-endian. The format does not currently include a BOM;
the file-header magic `RCIASTR1` doubles as an endianness check (its byte
sequence is direction-sensitive).

---

## File header — written once at stream start (32 bytes)

| Off | Size | Type     | Field              | Notes |
|-----|------|----------|--------------------|-------|
| 0   | 8    | `[u8;8]` | `magic`            | ASCII bytes `R C I A S T R 1` (0x52 0x43 0x49 0x41 0x53 0x54 0x52 0x31) |
| 8   | 2    | `u16`    | `format_version`   | `1` for this spec |
| 10  | 2    | `u16`    | `file_header_size` | `32`. Readers must skip past this many bytes from start to reach first record. |
| 12  | 4    | `u32`    | `flags`            | Reserved, `0` |
| 16  | 16   | `[u8;16]`| `reserved`         | Zero-filled |

---

## Per-spectrum record

A record consists of:

```
[Fixed Header — 128 bytes, all scalar fields present at known offsets]
[Filter string — filter_string_len bytes, UTF-8, no null terminator]
[Pad to 8-byte alignment]
[Peak arrays — mz f64×N, intensity f32×N, then optional arrays flagged in the header]
[Pad to 8-byte alignment]
[Optional metadata dump — key/value strings of all trailer fields]
[Pad to 8-byte alignment, brings record to multiple of 8]
```

### Fixed header (128 bytes)

| Off | Size | Type | Field | Nullability |
|-----|------|------|-------|-------------|
| 0   | 4 | `u32` | `record_size` | Total bytes of this record (including padding); used to skip to next record. |
| 4   | 4 | `u32` | `scan_id` | RAW file scan number. |
| 8   | 1 | `i8`  | `ms_order` | Raw Thermo MS order (`1`, `2`, `3`, ...; negative values preserve parent, neutral loss, and neutral gain scans). |
| 9   | 1 | `u8`  | `polarity` | `0` = negative, `1` = positive, `255` = unknown |
| 10  | 1 | `u8`  | `scan_data_type` | `0` = profile, `1` = centroid |
| 11  | 1 | `u8`  | `activation_type` | See enum below; `0` = none, `255` = unknown |
| 12  | 4 | `u32` | `n_peaks` | May be `0`. |
| 16  | 8 | `f64` | `retention_time_seconds` | Always present. |
| 24  | 8 | `f64` | `precursor_mz` | NaN if MS1 or unavailable. |
| 32  | 8 | `f64` | `precursor_mz_monoisotopic` | NaN if no monoisotopic correction was reported. |
| 40  | 8 | `f64` | `base_peak_mz` | NaN if no peaks. |
| 48  | 4 | `f32` | `isolation_lower` | NaN if MS1 or unavailable. Lower isolation window bound in Da, aligned with the parquet column. |
| 52  | 4 | `f32` | `isolation_upper` | NaN if MS1 or unavailable. Upper isolation window bound in Da, aligned with the parquet column. |
| 56  | 4 | `f32` | `isolation_width` | NaN if MS1. |
| 60  | 4 | `f32` | `precursor_intensity` | `0.0` if MS1 or not computed. |
| 64  | 4 | `f32` | `base_peak_intensity` | NaN if no peaks. |
| 68  | 4 | `f32` | `total_ion_current` | NaN if not available. |
| 72  | 4 | `f32` | `ion_injection_time_ms` | NaN if not in trailer. |
| 76  | 4 | `f32` | `collision_energy` | NaN if MS1 or unavailable. |
| 80  | 4 | `f32` | `faims_compensation_voltage` | NaN if no FAIMS. |
| 84  | 4 | `f32` | `elapsed_scan_time_ms` | NaN if not available. |
| 88  | 4 | `f32` | `low_mass` | Scan range start (Da). NaN if not available. |
| 92  | 4 | `f32` | `high_mass` | Scan range end (Da). NaN if not available. |
| 96  | 4 | `i32` | `precursor_charge` | `-1` if unknown. |
| 100 | 4 | `i32` | `master_scan_number` | `-1` if no parent (MS1) or unknown. |
| 104 | 4 | `u32` | `peak_flags` | Bit 0: `peaks_sorted_by_mz`; bit 1: charge array present; bit 2: noise arrays present. |
| 108 | 4 | `u32` | `auxiliary_array_count` | Number of entries in each optional noise array. `0` if no noise arrays are present. |
| 112 | 2 | `u16` | `filter_string_len` | Bytes (UTF-8) of filter string immediately after header. |
| 114 | 2 | `u16` | `reserved2` | `0` |
| 116 | 4 | `u32` | `arrays_offset` | Byte offset (from record start) to the mz array. Always `>= 128 + filter_string_len`. |
| 120 | 4 | `u32` | `metadata_offset` | Byte offset to optional metadata key/value block. `0` if absent. |
| 124 | 4 | `u32` | `metadata_length` | Bytes of metadata block. `0` if absent. |

Total: **128 bytes** (two 64-byte cache lines).

### Activation type enum (`u8`)

| Value | Meaning |
|-------|---------|
| 0     | None (MS1 or no activation reported) |
| 1     | CID (Collision-Induced Dissociation) |
| 2     | HCD (Higher-Energy Collisional Dissociation) |
| 3     | ETD (Electron Transfer Dissociation) |
| 4     | ECD (Electron Capture Dissociation) |
| 5     | EThcD / ETciD (ETD/ECD + HCD/CID supplemental) |
| 6     | UVPD (Ultraviolet Photodissociation) |
| 7     | NETD (Negative Electron Transfer) |
| 8     | MPD (Multi-Photon Dissociation) |
| 9     | PQD (Pulsed Q Dissociation) |
| 10    | PTR (Proton Transfer Reaction) |
| 11    | nPTR (Negative Proton Transfer Reaction) |
| 255   | Other / unknown |

Note: `5` is reserved for caller-detected supplemental ETD/ECD activation (ETD/ECD + supplemental HCD/CID); the
encoder maps the primary reaction's `ActivationType` and leaves EThcD/ETciD detection to a
higher layer that inspects sequential reactions.

### Filter string

Immediately after the fixed header, `filter_string_len` UTF-8 bytes of the
Thermo filter string (e.g., `"FTMS + p NSI Full ms2 408.5142@hcd28.00 [115.0000-1700.0000]"`).
No null terminator. May be empty (`filter_string_len = 0`).

### Peak arrays (at `arrays_offset`)

```
arrays_offset + 0       : f64 mz_array[N]          (8-byte aligned)
arrays_offset + 8N      : f32 intensity_array[N]   (4-byte aligned)
optional pad to 8-byte alignment when optional f64 arrays follow
if peak_flags & 0x2     : f64 charge_array[N]
if peak_flags & 0x4     : f64 noise_mz_array[auxiliary_array_count]
                           f64 noise_intensity_array[auxiliary_array_count]
                           f64 noise_baseline_array[auxiliary_array_count]
```

`mz_array` is sorted ascending if `peak_flags & 0x1` is set (which is the
default for all output produced by the writer).

The optional charge and noise arrays are emitted only when the matching TRFP
options are requested and RawFileReader provides the data. Default SoA output
contains only the hot-path `mz` and `intensity` arrays.

### Metadata block (optional, at `metadata_offset`)

If `metadata_length > 0`, a verbatim dump of all key/value pairs from the
RAW file's per-scan trailer. This captures every vendor-reported value
(AGC target, conversion parameters, all "MS{N} ..." fields, etc.)
without selective filtering.

```
metadata_offset + 0    : u32 n_pairs
metadata_offset + 4    : repeating block:
                           u16 key_len
                           key_len bytes (UTF-8)
                           u16 value_len
                           value_len bytes (UTF-8)
```

### Padding

After the last peak/metadata byte, the record is zero-padded so that
`record_size` is a multiple of 8. This guarantees the next record starts
at an 8-byte boundary, so its `f64` fields can be cast/loaded aligned.

---

## End-of-stream marker

After the last spectrum, the writer emits a single `u32` with value `0`.
Readers should interpret a record_size of `0` as end-of-stream.

```
... last record bytes ...
[ u32 record_size = 0 ]
```

---

## Reading example (Rust pseudocode)

```rust
let mut hdr = [0u8; 128];
loop {
    if reader.read_exact(&mut hdr[..4]).is_err() { break; }
    let record_size = u32::from_le_bytes(hdr[..4].try_into().unwrap());
    if record_size == 0 { break; }                     // EOF marker
    
    reader.read_exact(&mut hdr[4..128])?;
    let filter_string_len = u16::from_le_bytes(hdr[112..114].try_into().unwrap()) as usize;
    let arrays_offset     = u32::from_le_bytes(hdr[116..120].try_into().unwrap()) as usize;
    let metadata_offset   = u32::from_le_bytes(hdr[120..124].try_into().unwrap()) as usize;
    let metadata_length   = u32::from_le_bytes(hdr[124..128].try_into().unwrap()) as usize;
    let n_peaks           = u32::from_le_bytes(hdr[12..16].try_into().unwrap()) as usize;
    
    // Read remaining bytes
    let mut rest = vec![0u8; (record_size as usize) - 128];
    reader.read_exact(&mut rest)?;
    
    let filter_string = std::str::from_utf8(&rest[0..filter_string_len])?;
    
    let mz_start  = arrays_offset - 128;
    let int_start = mz_start + 8 * n_peaks;
    let mz_bytes        = &rest[mz_start  .. mz_start  + 8 * n_peaks];
    let intensity_bytes = &rest[int_start .. int_start + 4 * n_peaks];
    let mz: &[f64]        = bytemuck::cast_slice(mz_bytes);
    let intensity: &[f32] = bytemuck::cast_slice(intensity_bytes);
    
    // ... process spectrum ...
}
```

---

## Versioning policy

* Increments to `format_version` are **breaking** — readers should reject
  unknown versions explicitly.
* New fields may be appended **without** a version bump if and only if they
  live in a new variable-length section pointed to by an offset that was
  reserved as `0` in v1. Readers tolerant to v1 still skip new sections
  cleanly because `record_size` accounts for them.
* Remaining bits in `peak_flags` and `reserved*` fields may be allocated for
  non-breaking signals.

---

## Design rationale

* **128-byte header** = exactly two cache lines on x86/ARM.
* **f64 mz, f32 intensity** = matches RawFileReader's native output;
  matches PyTorch tensor channel layouts; eliminates downstream conversion.
* **SoA layout** = each downstream consumer that reads only `mz` (e.g.,
  fragment matching) loads only the `mz_array` cache lines, not interleaved
  intensity bytes.
* **NaN sentinels** for floats and `-1` for signed ints = no separate
  presence bitmask required for most fields; downstream code handles
  missing values with the same arithmetic (NaN propagates, `-1` is a
  trivial branch).
* **Optional metadata dump** = preserves every per-scan trailer value
  without enumerating them in the header. Engines can mine for
  instrument-specific signals later (lock-mass calibration, AGC stats,
  conversion parameters) without format changes.
* **Variable-section offsets** = forward-compat: readers seek to known
  offsets and ignore unfamiliar tails.
