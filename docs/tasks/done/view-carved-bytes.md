# Task: View carved artifact bytes

Carved rows are listed by `DiskImageOpenWorkflow` but `LogicalPath=""`, so the viewer
can't open them. Make carved rows viewable by reading their byte range from the source
image — mirrors the existing ghost-file cache-extraction path in `ViewerViewModel`.

## Scope (this PR)
- Carved rows viewable (offset+length range from the image → decode).
- Disk-image LIVE/deleted file byte loading is a follow-up (needs DiscUtils content reads).

## Steps
- `SignatureCarver.CarveFile(path)` — DRY read(+512 MiB cap)+Carve; reuse in session.
- `MediaEntityRow`: add `CarvedImagePath`, `CarvedOffset`, `CarvedLength` (init props,
  mirroring the Thumbcache path+offset+size pattern).
- `DiskImageOpenWorkflow`: carving pass uses `CarveFile`; set the three carved fields.
- `ViewerViewModel.LoadAsync`: before the empty-LogicalPath gate, branch on
  `CarvedImagePath` → `LoadCarvedAsync` (read range → BitmapDecoder, like LoadGhostAsync).
- Test: `SignatureCarver.CarveFile` round-trip on a temp file.

## Verify
- run-tests (carving tests green). Note: WPF viewer can't run headless on WSL — viewer
  branch is compile-checked + mirrors the proven ghost path; byte extraction is unit-tested.

## Checklist
- [x] CarveFile + session reuse
- [x] MediaEntityRow fields
- [x] workflow sets fields
- [x] ViewerViewModel LoadCarvedAsync
- [x] test + build green
- [x] note follow-up (disk-image live/deleted); move to done/
