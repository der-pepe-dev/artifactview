# Task: View deleted FAT12/16/32 file bytes — done

## Done
- `FatDeletedFileScanner`: `ReadGeometry` (BPB → cluster size, FAT/root/data offsets, FAT type),
  recursive `Scan` (follows live-dir cluster chains via the FAT; collects 0xE5 deleted entries
  with media extensions; 8.3 name with '_' for the lost first char), `RecoverContiguous`
  (FAT clears the chain on delete → read `size` bytes from the start cluster; short read → null).
- `DiskImagePartitionReader`: `DiskImageFileEntry.FatStartCluster`; `ScanFatDeletedFiles` in
  `TryReadFat`; `ReadDeletedFatFileBytes(imagePath, partitionIndex, startCluster, size)`.
- `MediaEntityRow.DeletedFatStartCluster`; workflow sets it on FAT deleted entries.
- `ViewerViewModel`: deleted branch dispatches NTFS ($MFT record) vs FAT (start cluster);
  shared `DecodeRecoveredBytes` (best-effort).

## Verified
- `DiskImagePartitionReaderTests`: create FAT image → write → `DeleteFile` → enumerate the
  0xE5 entry → `ReadDeletedFatFileBytes` recovers exact bytes; + bad-cluster → null.
  Full Infrastructure suite 399 passed / 8 skipped / 0 failed; solution builds.

## Follow-up
- exFAT deleted (todo.md): no DiscUtils support → custom parser.
- Non-contiguous/overwritten recovery; overwrite-likelihood scoring.
