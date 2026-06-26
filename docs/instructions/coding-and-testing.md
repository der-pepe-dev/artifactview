# Coding and testing

Read before writing or testing code.

## Coding guidance

- Prefer small focused classes over large god objects.
- Keep domain models explicit and strongly typed; keep provenance available in them.
- Avoid burying policy decisions in UI code.
- Avoid hardcoded extension-only logic when capability-based handling is cleaner.
- Keep analyzers composable and side-effect free where possible.
- Keep reconstruction/export logic separate from read-only analysis.
- Prefer async/background processing for any non-trivial IO or decode work.
- Design for incremental updates in the UI.

## Testing priorities

Prioritize tests for: metadata reconciliation, confidence scoring, ghost-item merging,
reconstruction naming and export rules, integrity-check behavior, direct-open fast-path
behavior, plugin policy and manifest loading, and timestamp/GPS interpretation rules.

## Test environment: WSL2

Tests run on WSL2 (`net10.0-windows` TFM on Linux). Known limitations:

**GDI+**: `System.Drawing.Bitmap` fails on WSL2 — `gdiplus.dll` not found. Use
`[SkippableFact]` with a runtime `Lazy<bool>` probe, not `OperatingSystem.IsWindows()`
(which returns false on WSL2):

```csharp
private static readonly Lazy<bool> s_gdiPlusAvailable = new(() =>
{
    try { _ = new Bitmap(1, 1); return true; }
    catch { return false; }
});
```

**Path separators**: `Path.GetFileName` / `GetDirectoryName` do not treat `\` as a
separator on Linux, but DiscUtils FAT/NTFS paths use `\`. Split on both separators
when extracting filenames from DiscUtils paths in tests.

## DiscUtils 0.16.13 gotchas

- `FatFileSystem.GetFiles(dir, "*", ...)` uses DOS wildcard semantics — `"*"` matches
  only extension-less files. Use `"*.*"` to match all files.
- `NtfsFileSystem.Detect()` and `FatFileSystem.Detect()` advance the stream without
  resetting. Seek to 0 before each Detect call.
- `FloppyDiskType` enum: `DoubleDensity=0` (720 KB), `HighDensity=1` (1.44 MB),
  `Extended=2` (2.88 MB).
- FAT12 (floppy) does not support 4-char extensions (`.heic`, `.heif`, `.jpeg`,
  `.tiff`, `.webp`, `.avif`).
- No public API for NTFS deleted-file enumeration — read `\$MFT` directly via
  `NtfsFileSystem.OpenFile` and parse 1024-byte records. MFT flags at offset 22
  (bit 0 = IN_USE); `$FILE_NAME` attribute type = 0x30; filename at contentStart+66
  (UTF-16LE); skip namespace byte 2 (DOS-only names).
- All MFT parser local variables must be declared `int` — `BitConverter.ToUInt16`
  returns `ushort` and causes CS0266 with `var` in arithmetic expressions.
