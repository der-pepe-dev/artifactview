# Task: View disk-image LIVE file bytes (done)

Make live files inside a raw disk image viewable (the todo.md High follow-up to carved viewing).

## Done
- `DiskImagePartitionReader.ReadFileBytes(imagePath, partitionIndex, internalPath, filesystem)`
  — reuses the open/partition/Detect logic; DiscUtils `OpenFile` → bytes (512 MiB cap, null on failure).
- `MediaEntityRow`: `DiskImagePath`/`DiskImagePartitionIndex`/`DiskImageInternalPath`/`DiskImageFilesystem`.
- `DiskImageOpenWorkflow`: sets those on LIVE (non-deleted) entries.
- `ViewerViewModel.LoadDiskImageFileAsync`: reads bytes → `ImageDecoder.Decode(Stream)` (EXIF-oriented).

## Verified
- DiskImagePartitionReader tests 11/11 (added FAT round-trip + missing-path null); full solution builds on ext4.
- WPF viewer not runtime-verified (no headless GUI on WSL); branch mirrors the proven carved path,
  `ReadFileBytes` unit-tested.

## Follow-up
- Deleted disk-image files (MFT data-run reconstruction) → todo.md Medium.
