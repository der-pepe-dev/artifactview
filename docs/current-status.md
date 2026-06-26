# ArtifactView — current status

_Update only when durable project status changes: major feature completed, known
limitation discovered, milestone changed, or durable architectural direction changed.
Prefer appending dated notes over rewriting._

## Status

Active development. Foundation, viewer, and forensic-awareness phases are the base;
Phase 7 (Advanced Sources) is implemented as of 2026-04-30: iPhone backup source,
Android artifact/caches, app DB correlation (WhatsApp/Telegram/Signal), disk image
source (MBR/GPT, NTFS+FAT, deleted via raw $MFT), and deleted-record source. Carved
artifact source is deferred. See [[roadmap]] for phase detail.

## Known limitations

- Tests run on WSL2 with `net10.0-windows`; GDI+ and DiscUtils have known
  limitations — see [[instructions/coding-and-testing]].
- Carving (CarvedArtifactSource): v1 done (JPEG+PNG, in-memory). Streaming for multi-GB
  images and more formats (GIF/BMP/TIFF/MP4/HEIF) not yet implemented.
- Video (Phase 9) and premium plugin ecosystem (Phase 10) are future work.

## Recent notes

<!-- Append dated notes here, newest first: -->
<!-- - YYYY-MM-DD: ... -->
- 2026-06-27: Viewer can now open disk-image LIVE files and carved artifacts.
  `DiskImagePartitionReader.ReadFileBytes` (DiscUtils OpenFile) + `MediaEntityRow`
  DiskImage*/Carved* byte-source fields + `ViewerViewModel` branches +
  `ImageDecoder.Decode(Stream)`. Deleted disk-image files (MFT data runs) still not viewable.
- 2026-06-26: Carved artifact source v1 implemented — `SignatureCarver` (JPEG+PNG signature
  carving, segment/chunk-walked end detection) + `CarvedArtifactSourceProvider`/`Session`
  (carved-range `OpenReadAsync`). In-memory scan; streaming for multi-GB images and more
  formats are follow-ups. See [[tasks/done/carved-artifact-source]].
- 2026-06-26: Test suite migrated from xUnit to **TUnit** (Microsoft.Testing.Platform).
  All 3 projects: Core 13, Application 26, Infrastructure 381 + 8 skipped — 0 failed.
  `dotnet test` runs in MTP mode via root `global.json`; needs `--solution`/`--project`.
  GDI+ skips now use `[RequiresGdiPlus]`; runtime-path fixture skips use inline `Skip.When`.
  WSL gotcha: running the Infrastructure suite over `/mnt/w` (drvfs) wedges the mount —
  run tests from an ext4 copy. See [[tasks/done/convert-tests-to-tunit]].
