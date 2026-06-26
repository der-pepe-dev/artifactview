# ArtifactView Roadmap

## Phase 0 - Foundation
Goal: establish the base application and architecture.

Includes:
- WPF shell
- folder source
- grid + viewer + details layout
- local SQLite cache + blob store
- plugin discovery skeleton
- job queue and prioritization
- settings and logging
- core domain models

## Phase 1 - Viewer and Metadata Browser
Goal: make ArtifactView useful as a daily media viewer.

Includes:
- fast high-quality viewer
- zoom/pan/fit/1:1
- adaptive scaling
- dynamic columns
- metadata summary
- raw metadata view
- reconciled metadata view
- search and filters
- timeline grouping
- filmstrip/session strip
- compare workspace
- command-line fast-open behavior

## Phase 2 - Core Forensic Awareness
Goal: add foundational forensic analysis.

Includes:
- EXIF/XMP/IPTC extraction
- GPS extraction
- GPS UTC/local-time display
- timezone inference
- timestamp consistency checks
- software/editor detection
- embedded EXIF thumbnail extraction
- thumbnail-vs-main comparison
- JPEG integrity checks
- PNG integrity checks
- confidence-based findings
- explain-this-finding
- anomaly/review-priority score

## Phase 3 - Ghost Files and Reconstruction
Goal: elevate cache artifacts and missing items into first-class concepts.

Includes:
- Thumbs.db
- Windows thumbcache
- ghost-file overlay
- cache-based enrichment for live items
- exact artifact extraction
- ghost preview export
- partial/lo-fi reconstruction
- composite reconstruction
- provenance sidecars
- lossless export rules for reconstructed/composited outputs

## Phase 4 - Session Intelligence
Goal: move from file-by-file analysis to set/session understanding.

Includes:
- sequence gap detector
- burst/session clustering
- outlier detection
- duplicate detection
- near-duplicate/cropped/re-encoded variant detection
- session storyboard/contact sheet
- related-item linking

## Phase 5 - Embedded / Auxiliary Artifacts
Goal: support media files that carry sub-assets.

Includes:
- Motion Photo video extraction
- representative frame compare
- Google depth payloads
- Apple depth/auxiliary image support
- gain map detection
- unknown trailer/payload detection
- embedded artifact export
- artifact bundle view

## Phase 6 - Recognition Database
Goal: identify likely source apps, platforms, and workflows.

Includes:
- unified signature database
- app/workflow profiles
- platform/site profiles
- software/app icon column
- “consistent with X workflow” scoring
- conflict view for competing signatures
- versioned rule packs later

## Phase 7 - Advanced Sources ✓ (implemented 2026-04-30)
Goal: browse beyond normal folders.

Includes:
- iPhone backup source ✓ — ManifestDbReader + IPhoneBackupDiscovery + IPhoneBackupOpenWorkflow
- Android artifact/caches ✓ — AndroidDcimThumbnailScanner; ghost pass in FolderOpenWorkflow
- app DB correlation ✓ — AppDbCorrelator with WhatsApp/Telegram/Signal readers; enrichment pass in ShellViewModel
- disk image source ✓ — DiskImagePartitionReader (MBR/GPT, NTFS+FAT, deleted via raw MFT); DiskImageOpenWorkflow
- deleted record source ✓ — NTFS deleted files via $MFT parsing (MFT IN_USE=0); LogicalPath = \[DELETED]\<name>
- carved artifact source — v1 ✓ — SignatureCarver (JPEG+PNG) + CarvedArtifactSourceProvider/Session
  with carved-range OpenReadAsync; in-memory scan (streaming for multi-GB images + more formats deferred)
- unified source-type display in the grid ✓ — PrimarySourceType column shows filesystem + partition

## Phase 8 - Advanced Integrity and Salvage
Goal: go beyond simple corruption flags.

Includes:
- deeper TIFF/RAW integrity
- MP4/MOV integrity later
- partial decode maps
- salvage mode
- intact-artifact extraction from damaged files
- recovery suggestions

## Phase 9 - Video
Goal: grow into a broader media-forensics viewer.

Includes:
- MP4/MOV browsing
- video metadata extraction
- timeline thumbnails
- keyframe extraction
- integrity checks for supported containers
- workflow recognition for video later

## Phase 10 - Premium Plugin Ecosystem
Goal: formalize extensibility and monetization.

Includes:
- signed plugin support
- trust-aware load policies
- premium plugins
- registration/subscription for advanced intelligence packs
- enterprise reporting/workflows
- support/update channel integration

## Recommended MVP
Keep the first build focused.

Suggested MVP:
- filesystem folder source
- fast viewer
- command-line fast open
- dynamic grid columns
- metadata summary pane
- raw/reconciled metadata
- EXIF thumbnail extraction
- thumbnail compare
- basic confidence-based findings
- JPEG/PNG integrity checks
- basic ghost overlay from cache artifacts
- exact artifact extraction
- basic lo-fi reconstruction
- plugin discovery with trust policy
