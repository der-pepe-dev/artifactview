# Task: Convert tests from xUnit to TUnit

Plan: `~/.claude/plans/convert-the-tests-to-compiled-lerdorf.md`

## Decisions
- Keep `dotnet test` working via `TestingPlatformDotnetTestSupport=true`.
- Custom `SkipAttribute` subclasses for GDI+/fixture-file conditional skips.
- TUnit pinned to **1.56.35**.

## Baseline (xUnit, captured 2026-06-26)
- Core.Tests: 13 passed
- Application.Tests: 26 passed
- Infrastructure.Tests: slow (>10 min, disk-image tests) — count TBD

## Toolchain (validated on Core)
- csproj: replace xunit refs with `<PackageReference Include="TUnit" Version="1.56.35" />`,
  add `<OutputType>Exe</OutputType>`. TUnit implicit usings supply `Test`/`Arguments`/`Assert`.
- **MTP opt-in is `global.json`** (`{"test":{"runner":"Microsoft.Testing.Platform"}}`),
  NOT the `TestingPlatformDotnetTestSupport` csproj prop (that's legacy VSTest-mode). Created at repo root.
- Code fixer: `EnableWindowsTargeting=true dotnet format analyzers <proj> --diagnostics TUXU0001 --severity info --no-restore`
- Run: `EnableWindowsTargeting=true dotnet test --project <proj>` (MTP needs --project/--solution flag).
- TODO: update CLAUDE.md `dotnet test ArtifactView.sln` → `dotnet test --solution ArtifactView.sln`.

## Checklist
- [x] Core.Tests: 13/13 pass under TUnit (was 13 xUnit)
- [x] Application.Tests: 26/26 pass (fixer missed 3 sync `void` TryActivate_* in PluginRegistryTests — hand-converted to async Task + IsNull())
- [ ] Application.Tests: csproj + convert + verify
- [x] Infrastructure.Tests: 381 passed, 8 skipped, 0 failed (after fixing 11 fixer mis-conversions; verified on ext4)
  - Custom `[RequiresGdiPlus]` (SkipAttribute) for RepresentativeFrameAnalyzerTests (7).
  - `RequiresFileAttribute` dropped: all fixture paths are runtime-computed (not const),
    so file-existence skips stay inline `Skip.When(!File.Exists(path), ...)`
    (PixelMotion, LoFi, ThumbsDb).
  - Fixer mis-conversions hand-repaired: predicate `Contains`/`DoesNotContain` arg-swap (~52);
    `Assert.All(coll, x=>...)` flattened → `foreach`; `Assert.Single(coll, pred)` →
    `.Where(pred).HasSingleItem()`; `Record.Exception(..)+IsNull` → `.ThrowsNothing()`;
    `Assert.InRange` → `IsBetween`; orphaned `ITestOutputHelper.output` → `Console.WriteLine`.
  - NOTE: `perl -i` corrupts files on drvfs (rename EPERM) — use `sed -i`/Edit only.
    3 files were deleted by a perl -i call and restored via `git checkout HEAD --`.
- [x] Update docs/instructions/coding-and-testing.md (skip pattern + TUnit section)
- [x] Update CLAUDE.md build/test commands (MTP `--solution`)
- [x] dated note in current-status.md
- [x] moved to docs/tasks/done/

## Bug review (2026-06-26): 11 real conversion bugs fixed; SIGABRT = drvfs only
Ran full Infrastructure suite on an **ext4 copy** (`~/av-run`) — drvfs (`/mnt/w`) wedges
under the test IO load (both on-drvfs runs died; ext4 ran clean). Result there: 11 failed,
370 passed, 8 skipped, **NO SIGABRT**. So the SIGABRT + the 12th drvfs failure were
**drvfs/parallelism-on-the-Windows-mount artifacts, not code bugs**. TUnit default
parallelism is fine on ext4 — no `[NotInParallel]` needed.

Three fixer mis-conversion categories (all fixed via sed on `/mnt/w`):
1. **Collection equality** (8): xUnit `Assert.Equal(arr,arr)` is BY VALUE; fixer →
   `.IsEqualTo()` = REFERENCE equality for arrays. Fix `.IsEqualTo(coll)` → `.IsEquivalentTo(coll)`.
   Sites: BlobStoreTests:50,99; JpegEmbeddedArtifactScannerTests:114; EmbeddedArtifactExtractorPluginTests:125;
   ThumbcacheReaderTests:99,152,192; LocalCacheDbTests:143.
2. **byte vs int** (6): `Assert.That(payload[i] /*byte*/).IsEqualTo(0xFF /*int*/)` throws
   `InvalidOperationException` ("No implicit conversion Byte→Int32"). Fix → `.IsEqualTo((byte)0xFF)`.
   Sites: ThumbsDbReaderTests:169-171,264-266. (ulong `.IsEqualTo(0x..ul)` are fine — same type.)
3. **Dropped string comparer** (1): orig `Assert.Equal("photo.jpg", x, StringComparer.OrdinalIgnoreCase)`
   → fixer dropped comparer (FAT uppercases 8.3). Fix → `.IsEqualTo("photo.jpg").IgnoringCase()`.
   Site: DiskImagePartitionReaderTests:90.
Static risks cleared: no float-tolerance `Assert.Equal` existed; TUnit `.All(predicate)` asserts;
IsBetween sites passed.
Verification: re-run on ext4 in progress (expect 0 failed). drvfs note: run tests from an
ext4 copy, not `/mnt/w`, to avoid wedging the mount.

## Notes
<!-- progress appended here -->
- Build needs `-p:EnableWindowsTargeting=true` on WSL (WPF App TFM).
