# Backlog

Durable, prioritized task list. Active work goes in `tasks/<task-name>.md`, not here.

## High priority

<!-- TODO -->

## Medium priority

- Live exFAT enumeration/viewing — the custom `ExFatScanner` parser exists (deleted only);
  emitting + reading live exFAT files would follow (FAT-chain reads for fragmented files).
  (Deleted recovery is done for NTFS + FAT12/16/32 + exFAT.)
- Non-contiguous / overwrite-aware deleted recovery; overwrite-likelihood scoring.
- Carving: more formats (GIF/BMP/TIFF/MP4/HEIF) in `SignatureCarver`.
- Carving: streaming scan for multi-GB images (v1 caps in-memory at 512 MiB).
- Carved/disk-image grid thumbnails (ThumbnailViewModel) — currently only the main viewer
  loads carved/disk-image bytes.

## Low priority / someday

- Migrate the app from WPF to **Avalonia** — cross-platform UI; would also make the App and
  viewer testable on Linux/WSL, removing the current headless-WPF verification gap (GDI+/WIC).
