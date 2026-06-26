# Task: View deleted NTFS file bytes (best-effort undelete) — done

## Done
- `NtfsDeletedFileRecovery`: `ApplyFixup` (USA), `ParseDataAttribute` (unnamed `$DATA`:
  resident inline, or non-resident data runs; null on compressed/encrypted), `Recover`
  (resident → inline; non-resident → read clusters at `base + LCN*clusterSize`, truncate
  to real size; sparse → zeros; short read → null for forensic integrity).
- `DiskImagePartitionReader`: `DiskImageFileEntry.MftRecordNumber`; `ScanNtfsDeletedFiles`
  tracks the record number + sets real size via the parsed `$DATA`;
  `ReadDeletedFileBytes(imagePath, partitionIndex, mftRecordNumber)` (cluster size from BPB,
  MFT record via `NtfsFileSystem.OpenFile(\$MFT)` for fragmentation safety, raw cluster reads).
- `MediaEntityRow.DeletedMftRecordNumber`; `DiskImageOpenWorkflow` sets it on deleted entries.
- `ViewerViewModel.LoadDeletedFileAsync` → `ReadDeletedFileBytes` → `ImageDecoder.Decode(Stream)`,
  shown as best-effort (IsGhostPreview).

## Verified
- 5 `NtfsDeletedFileRecoveryTests` (synthetic MFT records): resident parse, resident recover,
  non-resident cluster reconstruction, compressed→null, fixup restore. Full solution builds; no regressions.
- Integration (DiscUtils path in `ReadDeletedFileBytes`) NOT unit-tested: DiscUtils can't
  format NTFS on Linux (Windows SIDs). WPF viewer branch compile-checked only.

## Follow-up
- Deleted FAT/exFAT (todo.md Medium): no DiscUtils support; needs raw directory parsing.
- Compressed/encrypted `$DATA`; overwrite-likelihood scoring.
