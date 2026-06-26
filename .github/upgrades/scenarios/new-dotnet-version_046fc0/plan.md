# .NET 10.0 Upgrade Plan — ArtifactView

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Migration Strategy](#2-migration-strategy)
3. [Detailed Dependency Analysis](#3-detailed-dependency-analysis)
4. [Project-by-Project Migration Plans](#4-project-by-project-migration-plans)
   - [ArtifactView.Contracts](#41-artifactviewcontracts)
   - [ArtifactView.Plugins.Abstractions](#42-artifactviewpluginsabstractions)
   - [ArtifactView.Core](#43-artifactviewcore)
   - [ArtifactView.Infrastructure](#44-artifactviewinfrastructure)
   - [ArtifactView.App](#45-artifactviewapp)
   - [ArtifactView.Core.Tests](#46-artifactviewcoretests)
5. [Package Update Reference](#5-package-update-reference)
6. [Breaking Changes Catalog](#6-breaking-changes-catalog)
7. [Testing & Validation Strategy](#7-testing--validation-strategy)
8. [Risk Management](#8-risk-management)
9. [Complexity & Effort Assessment](#9-complexity--effort-assessment)
10. [Source Control Strategy](#10-source-control-strategy)
11. [Success Criteria](#11-success-criteria)

---

## 1. Executive Summary

### Scenario
Upgrade all ArtifactView projects from `.NET 8.0` (`net8.0-windows`) to `.NET 10.0 LTS` (`net10.0-windows`).

### Scope

| Item | Value |
|---|---|
| Projects affected | 6 (all projects in solution) |
| Current framework | `net8.0-windows` |
| Target framework | `net10.0-windows` |
| Total LOC | 4,130 |
| NuGet packages requiring update | 3 |
| Security vulnerabilities | None |

### Selected Strategy
**All-At-Once Strategy** — All 6 projects upgraded simultaneously in a single coordinated operation.

**Rationale:**
- Small solution (6 projects)
- Uniform baseline — all projects currently on `net8.0-windows`
- Clear, acyclic dependency structure (depth 3)
- No security vulnerabilities requiring prioritised remediation
- All NuGet packages have known compatible target versions
- The dominant "API issue" count (773 binary-incompatible WPF APIs in `ArtifactView.App`) is a **framework reference artefact** — these are resolved automatically when the TFM is changed to `net10.0-windows`; no source-code changes are required for WPF API compatibility

### Complexity Classification
**Simple** — meets all All-At-Once ideal conditions. A single atomic upgrade pass followed by test validation is the appropriate execution model.

### Critical Issues
None. No security vulnerabilities, no circular dependencies, no legacy project format (all SDK-style), no unsupported packages.

---

## 2. Migration Strategy

### Approach: All-At-Once

All 6 projects are upgraded simultaneously in a single atomic operation. There are no intermediate states, no multi-targeting, and no per-project deployment gates.

**Justification:**

| Factor | Assessment | Implication |
|---|---|---|
| Solution size | 6 projects | Well within All-At-Once threshold (< 30) |
| Framework baseline | All `net8.0-windows` | Uniform — no mixed-version complications |
| Dependency depth | 3 levels, no cycles | Clean resolution order available |
| Total LOC | 4,130 | Small — full-solution build/test turnaround is fast |
| API issues | 773 binary-incompatible (all WPF TFM artefacts) | Resolved by TFM change alone; no source edits |
| Package updates | 3 packages | Routine version bumps; all have `10.0.x` releases |
| Security vulnerabilities | None | No emergency remediation path needed |

### WPF API "Binary Incompatible" Clarification

The assessment reports 773 binary-incompatible API usages in `ArtifactView.App`. **All of these are standard WPF framework types** (e.g., `BitmapSource`, `Application`, `Key`, `Visibility`, `DispatcherTimer`). These are flagged because WPF ships as a Windows Desktop workload referenced via the `-windows` TFM suffix. Changing the TFM from `net8.0-windows` to `net10.0-windows` makes these APIs fully available again — **no source-code modifications are required for WPF API compatibility**.

The 1 source-incompatible API and 7 behavioral changes (all related to `System.Uri`) are assessed separately in §6 Breaking Changes Catalog.

### Execution Phases

| Phase | Description |
|---|---|
| Phase 0: Prerequisites | Verify .NET 10 SDK is installed |
| Phase 1: Atomic Upgrade | Update all TFMs + packages + restore + build + fix compilation errors |
| Phase 2: Test Validation | Execute test suite; address any test failures |

### Ordering Principles

Within Phase 1, project file edits follow the natural dependency order (leaves first) to produce the cleanest MSBuild resolution, but all edits are applied before any build is attempted — making the overall operation atomic from MSBuild's perspective.

---

## 3. Detailed Dependency Analysis

### Dependency Graph

```
Level 0 — Leaf nodes (no project dependencies):
  ArtifactView.Contracts
  ArtifactView.Plugins.Abstractions

Level 1 — Depends on Level 0:
  ArtifactView.Core  →  ArtifactView.Contracts

Level 2 — Depends on Level 0–1:
  ArtifactView.Infrastructure  →  ArtifactView.Plugins.Abstractions
                                →  ArtifactView.Core
                                →  ArtifactView.Contracts

Level 3 — Root applications / test runners (depend on all below):
  ArtifactView.App         →  ArtifactView.Infrastructure
                           →  ArtifactView.Plugins.Abstractions
                           →  ArtifactView.Core
                           →  ArtifactView.Contracts
  ArtifactView.Core.Tests  →  ArtifactView.Core
```

### Migration Phase Grouping

Because the **All-At-Once strategy** is used, all projects are upgraded in a single coordinated operation. The dependency levels above are documented for informational context only — they do not imply sequential upgrade passes.

| Project | Dependency Level | Type |
|---|---|---|
| `ArtifactView.Contracts` | 0 | Class Library |
| `ArtifactView.Plugins.Abstractions` | 0 | Class Library |
| `ArtifactView.Core` | 1 | Class Library |
| `ArtifactView.Infrastructure` | 2 | Class Library |
| `ArtifactView.App` | 3 | WPF Application |
| `ArtifactView.Core.Tests` | 3 | xUnit Test Project |

### Critical Path
`ArtifactView.Contracts` → `ArtifactView.Core` → `ArtifactView.Infrastructure` → `ArtifactView.App`

### Circular Dependencies
None.

---

## 4. Project-by-Project Migration Plans

### 4.1 ArtifactView.Contracts

**Current State**
- Target framework: `net8.0-windows`
- Project type: Class Library (SDK-style)
- Dependencies: none
- Dependants: `ArtifactView.App`, `ArtifactView.Core`, `ArtifactView.Infrastructure`
- Files: 13 | LOC: 106
- Risk: **Low**
- API issues: none
- Package issues: none

**Target State**
- Target framework: `net10.0-windows`
- No package changes required

**Migration Steps**
1. Update `TargetFramework` in `src\ArtifactView.Contracts\ArtifactView.Contracts.csproj` from `net8.0-windows` to `net10.0-windows`.

**Expected Breaking Changes:** None.

**Validation Checklist**
- [ ] Project builds without errors
- [ ] Project builds without warnings

### 4.2 ArtifactView.Plugins.Abstractions

**Current State**
- Target framework: `net8.0-windows`
- Project type: Class Library (SDK-style)
- Dependencies: none
- Dependants: `ArtifactView.App`, `ArtifactView.Infrastructure`
- Files: 2 | LOC: 34
- Risk: **Low**
- API issues: none
- Package issues: none

**Target State**
- Target framework: `net10.0-windows`
- No package changes required

**Migration Steps**
1. Update `TargetFramework` in `src\ArtifactView.Plugins.Abstractions\ArtifactView.Plugins.Abstractions.csproj` from `net8.0-windows` to `net10.0-windows`.

**Expected Breaking Changes:** None.

**Validation Checklist**
- [ ] Project builds without errors
- [ ] Project builds without warnings

### 4.3 ArtifactView.Core

**Current State**
- Target framework: `net8.0-windows`
- Project type: Class Library (SDK-style)
- Dependencies: `ArtifactView.Contracts`
- Dependants: `ArtifactView.App`, `ArtifactView.Infrastructure`, `ArtifactView.Core.Tests`
- Files: 11 | LOC: 282
- Risk: **Low**
- API issues: none
- Package issues: none

**Target State**
- Target framework: `net10.0-windows`
- No package changes required

**Migration Steps**
1. Update `TargetFramework` in `src\ArtifactView.Core\ArtifactView.Core.csproj` from `net8.0-windows` to `net10.0-windows`.

**Expected Breaking Changes:** None.

**Validation Checklist**
- [ ] Project builds without errors
- [ ] Project builds without warnings

### 4.4 ArtifactView.Infrastructure

**Current State**
- Target framework: `net8.0-windows`
- Project type: Class Library (SDK-style)
- Dependencies: `ArtifactView.Plugins.Abstractions`, `ArtifactView.Core`, `ArtifactView.Contracts`
- Dependants: `ArtifactView.App`
- Files: 16 | LOC: 1,379
- Risk: **Low**
- API issues: none
- Package issues: 1 package upgrade

**Target State**
- Target framework: `net10.0-windows`
- `Microsoft.Data.Sqlite`: `8.0.0` → `10.0.5`

**Migration Steps**
1. Update `TargetFramework` in `src\ArtifactView.Infrastructure\ArtifactView.Infrastructure.csproj` from `net8.0-windows` to `net10.0-windows`.
2. Update `Microsoft.Data.Sqlite` package reference from `8.0.0` to `10.0.5`.

**Expected Breaking Changes:** None anticipated. `Microsoft.Data.Sqlite` 10.x is a routine version bump aligned with .NET 10; no API-level breaking changes affect this codebase.

**Validation Checklist**
- [ ] Project builds without errors
- [ ] Project builds without warnings
- [ ] SQLite-dependent functionality operates correctly at runtime

### 4.5 ArtifactView.App

**Current State**
- Target framework: `net8.0-windows`
- Project type: WPF Application (SDK-style)
- Dependencies: `ArtifactView.Infrastructure`, `ArtifactView.Plugins.Abstractions`, `ArtifactView.Core`, `ArtifactView.Contracts`
- Dependants: none (root application)
- Files: 13 | LOC: 2,226
- Risk: **Medium** (largest project; contains the 1 source-incompatible API; WPF UI layer)
- API issues: 773 binary-incompatible (all WPF TFM artefacts — auto-resolved by TFM change), 1 source-incompatible, 7 behavioral changes (all in XAML-generated `obj/` files)
- Package issues: 2 package upgrades

**Target State**
- Target framework: `net10.0-windows`
- `Microsoft.Extensions.Logging`: `8.0.1` → `10.0.5`
- `Microsoft.Extensions.Logging.Debug`: `8.0.1` → `10.0.5`

**Migration Steps**
1. Update `TargetFramework` in `src\ArtifactView.App\ArtifactView.App.csproj` from `net8.0-windows` to `net10.0-windows`.
2. Update `Microsoft.Extensions.Logging` package reference from `8.0.1` to `10.0.5`.
3. Update `Microsoft.Extensions.Logging.Debug` package reference from `8.0.1` to `10.0.5`.
4. Fix the source-incompatible `TimeSpan.FromSeconds` overload ambiguity in `src\ArtifactView.App\App.xaml.cs` at line 90:
   - **Current:** `TimeSpan.FromSeconds(3)`
   - **Fix:** `TimeSpan.FromSeconds(3.0)` (explicitly selects the `double` overload, eliminating ambiguity with the new `long` overload added in .NET 8)

**WPF Binary Incompatibilities — No Action Required**

The 773 binary-incompatible API hits are all standard WPF types (`BitmapSource`, `Application`, `Key`, `Visibility`, `DispatcherTimer`, `ScrollViewer`, etc.) that exist unchanged in .NET 10 on Windows. The assessment flags them because they are outside the portable `.NETCore` surface; they resolve automatically when `net10.0-windows` is targeted and the Windows Desktop workload is active. No source-code modifications are required.

**Behavioral Changes — No Action Required**

All 7 behavioral-change hits are in XAML compiler-generated files (`obj\Debug\net8.0-windows\*.g.i.cs`). These files are regenerated fresh when the project is rebuilt targeting `net10.0-windows`. No hand-written code is affected.

**Validation Checklist**
- [ ] Project builds without errors
- [ ] Project builds without warnings
- [ ] Application starts successfully
- [ ] Media viewer opens and displays images
- [ ] Fullscreen viewer keyboard navigation works
- [ ] WPF DataGrid populates with folder contents

### 4.6 ArtifactView.Core.Tests

**Current State**
- Target framework: `net8.0-windows`
- Project type: xUnit Test Project (SDK-style)
- Dependencies: `ArtifactView.Core`
- Dependants: none
- Files: 4 | LOC: 103
- Risk: **Low**
- API issues: none
- Package issues: none (`Microsoft.NET.Test.Sdk 17.11.1`, `xunit 2.9.2`, `xunit.runner.visualstudio 2.8.2` — all assessed as compatible)

**Target State**
- Target framework: `net10.0-windows`
- No package changes required (all test packages compatible as-is)

**Migration Steps**
1. Update `TargetFramework` in `tests\ArtifactView.Core.Tests\ArtifactView.Core.Tests.csproj` from `net8.0-windows` to `net10.0-windows`.

**Expected Breaking Changes:** None.

**Validation Checklist**
- [ ] Project builds without errors
- [ ] All existing tests pass

---

## 5. Package Update Reference

### Packages Requiring Update

| Package | Current Version | Target Version | Project | Reason |
|---|---|---|---|---|
| `Microsoft.Data.Sqlite` | `8.0.0` | `10.0.5` | `ArtifactView.Infrastructure` | Align with target .NET version |
| `Microsoft.Extensions.Logging` | `8.0.1` | `10.0.5` | `ArtifactView.App` | Align with target .NET version |
| `Microsoft.Extensions.Logging.Debug` | `8.0.1` | `10.0.5` | `ArtifactView.App` | Align with target .NET version |

### Compatible Packages (No Update Required)

| Package | Current Version | Project | Status |
|---|---|---|---|
| `MetadataExtractor` | `2.8.1` | `ArtifactView.Infrastructure` | ✅ Compatible |
| `Microsoft.NET.Test.Sdk` | `17.11.1` | `ArtifactView.Core.Tests` | ✅ Compatible |
| `xunit` | `2.9.2` | `ArtifactView.Core.Tests` | ✅ Compatible |
| `xunit.runner.visualstudio` | `2.8.2` | `ArtifactView.Core.Tests` | ✅ Compatible |

---

## 6. Breaking Changes Catalog

### Source-Incompatible Changes (require code edits)

| # | API | File | Line | Issue | Fix |
|---|---|---|---|---|---|
| 1 | `TimeSpan.FromSeconds(double)` | `src\ArtifactView.App\App.xaml.cs` | 90 | Ambiguous overload: .NET 8 added `TimeSpan.FromSeconds(long)` — passing an integer literal (`3`) is now ambiguous between `double` and `long` overloads | Change `TimeSpan.FromSeconds(3)` → `TimeSpan.FromSeconds(3.0)` |

### Behavioral Changes (verify at runtime, no source edits required)

All 7 behavioral-change flags are in XAML-generated files in the `obj\` directory. These files are not hand-authored and are regenerated automatically on every build. When the project is rebuilt targeting `net10.0-windows`, the generator emits updated code against the .NET 10 surface and the flags no longer apply.

| File | Count | Action |
|---|---|---|
| `obj\Debug\net8.0-windows\Views\FullscreenViewerWindow.g.i.cs` | 2 | Auto-regenerated on build — no action |
| `obj\Debug\net8.0-windows\App.g.i.cs` | 3 | Auto-regenerated on build — no action |
| `obj\Debug\net8.0-windows\MainWindow.g.i.cs` | 2 | Auto-regenerated on build — no action |

### Binary-Incompatible WPF APIs (no source edits required)

The 773 binary-incompatible API usages are all standard WPF framework types available on .NET 10 Windows. They are flagged by the static analyser because the analysis ran against the `net8.0-windows` build artefacts, not the target platform surface. Retargeting to `net10.0-windows` provides the Windows Desktop workload and makes all these types fully available.

Representative types affected (no action needed):
`BitmapSource`, `BitmapDecoder`, `BitmapFrame`, `BitmapCacheOption`, `BitmapCreateOptions`, `Application`, `Key`, `Visibility`, `DispatcherTimer`, `Dispatcher`, `ScrollViewer`, `DataGrid`, `Image`, `TextBlock`, `ScaleTransform`, `CollectionViewSource`, `SortDescription`

---

## 7. Testing & Validation Strategy

### Phase 1 Validation — Build Success

After the atomic upgrade (all TFM + package changes applied):

1. Run `dotnet restore` on the solution — expect 0 errors.
2. Run `dotnet build` on the solution — expect 0 errors, 0 warnings related to the upgrade.
3. Confirm the `TimeSpan.FromSeconds` source-incompatible error is either pre-fixed or appears and is addressed.

### Phase 2 Validation — Test Suite

Test project: `tests\ArtifactView.Core.Tests\ArtifactView.Core.Tests.csproj`

- Run `dotnet test` against the solution.
- Expected: all existing tests pass.
- xUnit, `Microsoft.NET.Test.Sdk`, and `xunit.runner.visualstudio` are all assessed as compatible with .NET 10 — no test infrastructure changes expected.

### Smoke Validation Checklist (manual, post-build)

- [ ] Application launches without exceptions
- [ ] Main window grid populates when a folder is opened
- [ ] Image renders in the main viewer panel
- [ ] Fullscreen viewer opens from the grid
- [ ] Keyboard navigation in fullscreen (arrows, TAB version cycling, Z zoom, L/R rotate) works
- [ ] Mouse wheel scrolling in fullscreen navigates files
- [ ] Metadata and findings panels populate for a selected image
- [ ] SQLite cache reads/writes without error (folder re-open should restore thumbnails)
- [ ] Ghost items display the ghost overlay icon correctly
- [ ] No unexpected `System.Uri`-related regressions visible in URI-handling paths

---

## 8. Risk Management

### Risk Register

| Project | Risk Level | Risk Description | Mitigation |
|---|---|---|---|
| `ArtifactView.App` | **Medium** | WPF rendering or input handling regressions under .NET 10 WPF runtime | Run smoke validation checklist; WPF on .NET 10 is a mature, well-tested path with no known breaking changes for standard APIs |
| `ArtifactView.App` | **Low** | `TimeSpan.FromSeconds(double)` ambiguity causes build error if not pre-fixed | Fix is mechanical (append `.0` to the literal) and is described precisely in §6 |
| `ArtifactView.Infrastructure` | **Low** | `Microsoft.Data.Sqlite` 10.x introduces unexpected SQLite behaviour | Package is a routine version bump; no API-level breaking changes are documented for this upgrade range |
| All projects | **Low** | .NET 10 SDK not installed on build/dev machine | Verify SDK installation before execution (Phase 0) |

### Security Vulnerabilities
None identified in the assessment. No remediation required.

### Contingency Plans

**If the WPF application fails to launch after upgrade:**
- Confirm `net10.0-windows` TFM is set (not `net10.0` without the `-windows` suffix).
- Confirm the .NET 10 Windows Desktop workload is installed: `dotnet workload install windowsdesktop`.
- Review any WPF breaking changes documented in the [.NET 10 breaking changes list](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10.0).

**If a NuGet package restore fails:**
- Clear the local NuGet cache: `dotnet nuget locals all --clear`.
- Verify network access to nuget.org.
- Check that the exact versions (`10.0.5`) are available on the configured feed.

**If tests fail after upgrade:**
- Isolate failing tests to determine whether failures are upgrade-related or pre-existing.
- xUnit and `Microsoft.NET.Test.Sdk` are assessed as compatible; test infrastructure failures are unlikely.

---

## 9. Complexity & Effort Assessment

### Per-Project Complexity

| Project | Complexity | LOC | Dependencies | Risk | Notes |
|---|---|---|---|---|---|
| `ArtifactView.Contracts` | **Low** | 106 | 0 | Low | TFM change only |
| `ArtifactView.Plugins.Abstractions` | **Low** | 34 | 0 | Low | TFM change only |
| `ArtifactView.Core` | **Low** | 282 | 1 | Low | TFM change only |
| `ArtifactView.Infrastructure` | **Low** | 1,379 | 3 | Low | TFM change + 1 package update |
| `ArtifactView.App` | **Medium** | 2,226 | 4 | Medium | TFM + 2 package updates + 1 line code fix (WPF artefact flags are noise) |
| `ArtifactView.Core.Tests` | **Low** | 103 | 1 | Low | TFM change only; test packages already compatible |

### Overall Assessment
**Low-to-Medium overall complexity.** The upgrade is dominated by mechanical TFM changes across 5 simple projects. The single non-trivial item is a one-line code fix in `App.xaml.cs`. The 773 binary-incompatible API hits are framework-reference noise that vanish on retargeting. Total hand-authored lines requiring modification: **≤5 lines** (1 line in `App.xaml.cs` + 3 package version strings + 6 TFM strings).

---

## 10. Source Control Strategy

### Branching
No Git repository was detected in this workspace. Source control operations (branching, committing, pull requests) are not applicable.

If the project is placed under version control before or during the upgrade, the recommended approach is:
- Perform all upgrade changes on a dedicated feature branch (e.g., `upgrade/net10`).
- Merge to the main branch only after Phase 2 test validation passes.

### Commit Strategy (if Git is available)
- **Single commit** covering all changes: TFM updates, package updates, and the `TimeSpan.FromSeconds` code fix.
- Commit message: `chore: upgrade solution from net8.0-windows to net10.0-windows`
- This is consistent with the All-At-Once strategy — the upgrade is one atomic operation and benefits from a single, reviewable diff.

---

## 11. Success Criteria

The upgrade is complete when **all** of the following are satisfied:

### Technical Criteria

- [ ] All 6 projects have `TargetFramework` set to `net10.0-windows`
- [ ] `Microsoft.Data.Sqlite` updated to `10.0.5` in `ArtifactView.Infrastructure`
- [ ] `Microsoft.Extensions.Logging` updated to `10.0.5` in `ArtifactView.App`
- [ ] `Microsoft.Extensions.Logging.Debug` updated to `10.0.5` in `ArtifactView.App`
- [ ] `TimeSpan.FromSeconds(3)` ambiguity resolved in `App.xaml.cs`
- [ ] `dotnet restore` completes with 0 errors
- [ ] `dotnet build` completes with 0 errors
- [ ] `dotnet test` completes with all tests passing
- [ ] No package dependency conflicts
- [ ] No security vulnerabilities in restored packages

### Quality Criteria

- [ ] No regression in WPF UI behaviour (smoke checklist passed)
- [ ] SQLite cache functionality verified
- [ ] No unexpected `System.Uri` behavioural changes observed at runtime

### Process Criteria (All-At-Once strategy)

- [ ] All changes applied in a single coordinated operation (no intermediate partial states committed)
- [ ] No projects left on `net8.0-windows`
- [ ] No multi-targeting introduced
