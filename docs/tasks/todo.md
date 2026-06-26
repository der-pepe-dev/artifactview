# Backlog

Durable, prioritized task list. Active work goes in `tasks/<task-name>.md`, not here.

## High priority

<!-- TODO -->

## Medium priority

- Deleted FAT/exFAT file viewing — DiscUtils doesn't enumerate deleted FAT entries and has
  NO exFAT support, so both need raw directory parsing (FAT: 0xE5 entries, recover assuming
  contiguous since FAT chain is cleared; exFAT: custom boot/FAT/dir parser). (NTFS deleted
  recovery is done.)
- Carving: more formats (GIF/BMP/TIFF/MP4/HEIF) in `SignatureCarver`.
- Carving: streaming scan for multi-GB images (v1 caps in-memory at 512 MiB).
- Carved/disk-image grid thumbnails (ThumbnailViewModel) — currently only the main viewer
  loads carved/disk-image bytes.

## Low priority / someday
