# ArtifactView Architecture

## Overview

ArtifactView should be built as a modular Windows desktop media evidence platform with four core pillars:

1. UI shell
2. Source/case system
3. Analysis pipeline
4. Cache and artifact store

The architecture should support growth from:
- local image viewer/browser

into:
- forensic media triage tool
- artifact correlation tool
- backup and disk-image explorer
- later video-capable media forensics app

## Layered architecture

### UI layer
Responsible for:
- WPF shell
- grid/viewer/details UI
- commands and state
- progress and status

### Application layer
Responsible for:
- orchestrating scans
- opening sources
- job scheduling
- cache refresh
- reconstruction/export workflows
- report generation

### Domain layer
Responsible for:
- media entities
- contributors
- findings
- embedded artifacts
- reconciled fields
- confidence model

### Infrastructure layer
Responsible for:
- filesystem access
- SQLite storage
- blob store
- decoders/parsers
- plugin discovery/loading
- optional backup/disk image integrations

## Main domain concepts

### SourceItem
A raw item exposed by a source.
Examples:
- live file
- cache entry
- backup file
- deleted record
- carved artifact

### MediaEntity
A logical browseable item shown in the grid.
May be:
- live file
- ghost item
- cache-only item
- merged item with multiple contributors

### Contributor
Any source of evidence for a MediaEntity:
- live bytes
- EXIF metadata
- EXIF thumbnail
- Thumbs.db preview
- Windows thumbcache preview
- sidecar metadata
- app DB rows
- inferred/contextual support

### EmbeddedArtifact
Any embedded or auxiliary artifact inside a media item:
- EXIF thumbnail
- Motion Photo video
- depth map
- gain map
- unknown trailer

### Finding
A structured result with:
- observation
- interpretation
- observation confidence
- interpretation confidence
- supporting factors
- penalty factors
- provenance

### ReconciledField
A preferred summary value produced from multiple candidate values while preserving provenance and conflicts.

## Source architecture

ArtifactView should not be filesystem-only.
Use source providers.

### Source types
- FileSystemSource ✓
- FolderArtifactSource ✓ (Thumbs.db, thumbcache, ZbThumbnail, Android .thumbnails)
- WindowsThumbCacheSource ✓
- AppCacheSource ✓ (WhatsApp/Telegram/Signal DB correlation via AppDbCorrelator)
- IPhoneBackupSource ✓ (ManifestDbReader + IPhoneBackupDiscovery + IPhoneBackupOpenWorkflow)
- AndroidArtifactSource ✓ (AndroidDcimThumbnailScanner; ghost pass in FolderOpenWorkflow)
- DiskImageSource ✓ (DiskImagePartitionReader: MBR/GPT, NTFS, FAT; DiskImageOpenWorkflow)
- DeletedRecordSource ✓ (NTFS deleted files via raw $MFT parsing inside DiskImagePartitionReader)
- CarvedArtifactSource — deferred

### DiscUtils notes (disk image parsing)
- DiscUtils 0.16.13 used for raw disk image parsing
- `FatFileSystem.GetFiles(dir, "*")` uses DOS wildcard semantics — use `"*.*"` to match all files
- `NtfsFileSystem.Detect()` and `FatFileSystem.Detect()` advance stream without resetting — seek to 0 before each call
- No public API for deleted file enumeration — read `\$MFT` directly and parse 1024-byte records

### Design rule
The UI should browse logical media entities, not raw files only.

## Format architecture

Architecture-wise, format support should be structured by format/container families, not by extension-only plugins.

### Format families
- JPEG family
- PNG
- TIFF/RAW-like family
- ISO-BMFF family (`mp4`, `mov`, `heic` style)
- unknown/binary

### Capability-based model
Format handlers expose capabilities such as:
- metadata carrier
- image pixels
- embedded preview
- motion video
- depth data
- video tracks

Analyzers should target capabilities, not only file extensions.

## Analyzer architecture

Use staged analysis.

### Stage 0 - Inventory
- enumerate items
- path/name/size/type/timestamps

### Stage 1 - Quick metadata
- dimensions
- basic EXIF/XMP
- camera/date/GPS/software

### Stage 2 - Focused forensic checks
- thumbnail extraction
- thumbnail compare
- integrity checks
- timestamp/GPS consistency
- workflow signature matching

### Stage 3 - Deep analysis
- detailed compare
- ELA later
- resampling/ghost maps later
- context/session analysis
- artifact correlation

### Stage 4 - Advanced source correlation
- backup DB correlation
- deleted item linking
- cross-source correlation

## Cache architecture

Use two main stores:

### Structured cache (SQLite)
For:
- item inventory
- metadata summaries
- findings
- reconciliation state
- artifact registry
- job state

### Blob store (filesystem)
For:
- previews
- extracted thumbnails
- overlays
- reconstructions
- derived comparison assets

### Cache design rules
- local fast-view cache should be separate from global correlation index
- DB is the source of truth
- blob store is disposable
- analyzer/parser versions should participate in invalidation

## Global index

Optional subsystem for large collections.
Use for:
- cross-folder correlation
- duplicate/variant detection
- session linking
- workflow/source linking
- ghost-to-live matching across libraries

Recommended design:
- separate SQLite catalog
- store hashes, perceptual fingerprints, key metadata, cluster/match tables
- keep heavy blobs out of the global DB

## Ghost overlay system

Cache and sidecar artifacts should be usable in two ways:
1. enrich existing files
2. create virtual ghost entries for missing items

Ghost items should:
- appear inline in the grid
- be clearly labeled
- support preview display if available
- support comparison and reconstruction
- carry source and confidence information

## Reconstruction architecture

Treat reconstruction as a dedicated subsystem.

### Categories
- Exact extraction
- Partial reconstruction
- Lo-fi reconstruction
- Composite reconstruction
- Historical framing reconstruction

### Hard rules
- never overwrite originals by default
- never invent metadata
- exact extracted lossy artifacts may keep native format
- any transformed/composited/reconstructed output must export losslessly
- reconstruction naming must indicate reconstructed nature

## Plugin system

Plugin discovery should be manifest-first.
Do not execute plugin code just to inspect it.

### Plugin categories
- source plugins
- format handlers
- analyzer plugins
- processing/reconstruction plugins
- export/report plugins
- signature/rule-pack plugins

### Trust model
User should be able to choose:
- core only
- OSS plugins only
- signed plugins only
- full mode

### Open-core strategy
Open core should remain useful without proprietary plugins.
Advanced plugins can provide:
- mobile/backup sources
- advanced cache parsers
- platform/workflow signature packs
- enterprise workflows/support

## Startup / fast-open behavior

When launched with a file path from command line:
1. show the image as fast as possible
2. improve rendering quality immediately after first paint
3. defer metadata/artifact analysis to background jobs

First visible render must always take priority over analysis.
