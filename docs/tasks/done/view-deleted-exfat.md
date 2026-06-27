# Task: View deleted exFAT file bytes (custom parser) — done

DiscUtils has no exFAT support, so this is a from-scratch parser.

## Done
- `ExFatScanner`: `ReadGeometry` (boot: BytesPerSectorShift/SectorsPerClusterShift,
  Fat/ClusterHeap offsets, root cluster; "EXFAT   " sig), 32-bit `ReadFat`, recursive `Scan`
  of directory entry-sets (File 0x85/0x05, Stream 0xC0/0x40, Name 0xC1/0x41; InUse bit =
  deleted) collecting deleted media files, `RecoverContiguous`.
- `DiskImagePartitionReader`: `TryReadExFat` (in `ReadPartition`, before FAT);
  `ReadDeletedExFatFileBytes`. Entries use `Filesystem="exFAT"`, `FatStartCluster=firstCluster`.
- `ViewerViewModel`: deleted FAT branch dispatches FAT vs exFAT by `DiskImageFilesystem`
  (reuses `DeletedFatStartCluster` + `DecodeRecoveredBytes`; no new row fields).

## Verified
- `ExFatScannerTests`: hand-built exFAT image → `ReadAllMediaFiles` finds the deleted entry
  (cluster/size/name) → `ReadDeletedExFatFileBytes` recovers exact bytes; + bad-cluster null.
  Full Infrastructure suite green; solution builds.

## Follow-ups (todo.md)
Live exFAT enumeration/viewing; non-contiguous/overwrite-aware recovery.
