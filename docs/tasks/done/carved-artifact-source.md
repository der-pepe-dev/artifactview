# Task: CarvedArtifactSource (signature carving)

Implements the deferred carving source (roadmap line 112). Recovers files from a raw
image by **file-signature carving** — no filesystem metadata.

## Scope (v1)
- `SignatureCarver` — scan a byte buffer for **JPEG** and **PNG** and return
  `CarvedArtifact(Offset, Length, Format, Extension)`.
  - JPEG: SOI `FF D8 FF`; segment-walk via 2-byte BE length fields to SOS `FF DA`,
    then scan entropy data for EOI `FF D9` (skip stuffed `FF 00` and restart `FF D0..D7`).
    Segment-walking skips embedded EXIF thumbnails (they sit inside APP1 length) — avoids
    the "first EOI is the thumbnail's" carving pitfall.
  - PNG: sig `89 50 4E 47 0D 0A 1A 0A`; chunk-walk (4-byte BE len + type + data + CRC)
    to `IEND`.
- `CarvedArtifactSourceProvider` (`ISourceProvider`, id `carved-artifact`) +
  `CarvedArtifactSourceSession` (`ISourceSession`). Enumerate carved artifacts; implement
  `OpenReadAsync` to return the carved byte range (the part `DiskImageSourceSession` stubs).
- Tests: buffers with JPEG/PNG embedded at known offsets among junk + back-to-back;
  assert offsets/lengths/bytes; provider+session round-trip via OpenReadAsync.

## Out of scope (follow-ups)
- Streaming scan for multi-GB images (v1 caps the scan to an in-memory buffer).
- More formats (GIF/BMP/TIFF/MP4/HEIF), unallocated-only carving, dedup vs live files.

## Checklist
- [x] SignatureCarver (JPEG+PNG) + CarvedArtifact
- [x] Provider + Session (+ OpenReadAsync carved range)
- [x] Tests (Infrastructure.Tests/Sources/Carving)
- [x] build + run-tests green
- [x] roadmap line 112 / current-status note; move to done/
