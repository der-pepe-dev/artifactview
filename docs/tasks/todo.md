# Backlog

Durable, prioritized task list. Active work goes in `tasks/<task-name>.md`, not here.

## High priority

<!-- TODO -->

## Medium priority

- Deleted **exFAT** file viewing — DiscUtils has NO exFAT support, so it needs a custom
  parser (boot sector, FAT, directory entry-sets: File + Stream-Extension + File-Name;
  InUse bit for deleted; NoFatChain for contiguous recovery). (NTFS + FAT12/16/32 deleted
  recovery are done.)
- Carving: more formats (GIF/BMP/TIFF/MP4/HEIF) in `SignatureCarver`.
- Carving: streaming scan for multi-GB images (v1 caps in-memory at 512 MiB).
- Carved/disk-image grid thumbnails (ThumbnailViewModel) — currently only the main viewer
  loads carved/disk-image bytes.

## Low priority / someday
