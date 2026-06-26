# Backlog

Durable, prioritized task list. Active work goes in `tasks/<task-name>.md`, not here.

## High priority

- View disk-image LIVE file bytes in the viewer — read content via DiscUtils
  (`fat/ntfs.OpenFile`) by partition + internal path; add a DiskImage byte-source to
  `MediaEntityRow` + a `ViewerViewModel` branch (mirrors the carved path). Deleted files
  (MFT data-run reconstruction) are a separate, harder follow-up.

## Medium priority

- Carving: more formats (GIF/BMP/TIFF/MP4/HEIF) in `SignatureCarver`.
- Carving: streaming scan for multi-GB images (v1 caps in-memory at 512 MiB).
- Carved/disk-image grid thumbnails (ThumbnailViewModel) — currently only the main viewer
  loads carved bytes.

## Low priority / someday
