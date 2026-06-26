# ArtifactView Product Specification

## Product summary

ArtifactView is a Windows desktop media viewer and forensic browser.

It should feel like a fast, high-quality viewer first, while also supporting:
- metadata inspection
- forensic hints and confidence-based findings
- embedded artifact extraction
- ghost-file overlays from caches and sidecars
- reconstruction from embedded/cached/auxiliary artifacts
- integrity/corruption checking
- source/workflow recognition
- future support for backups, disk images, deleted items, and video

Primary philosophy:
- viewer-first
- forensic-aware
- evidence-driven
- provenance-preserving
- confidence-based, not absolute
- modular and plugin-friendly

## Primary use cases

### Daily Viewer / Browser
- open a folder and browse images quickly
- sort and filter by media and metadata columns
- inspect details in the right-side pane
- launch from command line with a single file and display it immediately

### Metadata and Provenance Inspection
- inspect EXIF/XMP/IPTC
- inspect reconciled metadata from multiple sources
- inspect raw metadata per source
- compare embedded and cached previews against the main image

### Ghost / Historical Artifact Discovery
- show cache-only or sidecar-only ghost files inline
- enrich current files with extra cache/app/OS metadata
- reconstruct cache-only previews into standalone derived outputs

### Forensic Triage
- detect anomalies and integrity issues
- detect crop/framing mismatch
- detect stale preview/thumbnails
- detect likely exported/shared copies
- detect likely workflow/platform signatures
- cluster files into sessions and detect outliers

### Reconstruction
- extract exact artifacts
- reconstruct lo-fi views from embedded/cached previews
- reconstruct wider framing from historical previews
- export provenance-safe derivatives

### Advanced Sources Later
- browse iPhone backups
- parse Android caches
- browse disk images
- inspect deleted records and carved artifacts
- support video and media families beyond still images

## UI model

### Main window layout
- top toolbar / menu / source controls / search
- main file grid
- main viewer area
- right-side details pane
- optional bottom status / jobs / filmstrip region

### Main grid
Columns may include:
- Name
- Path
- Extension
- Size
- Created
- Modified
- Resolution
- Orientation
- Camera Make
- Camera Model
- Date Taken
- GPS
- Software / Edited By
- Embedded Thumbnail
- Integrity
- Findings Count
- Review Priority / Anomaly Score
- Ghost / Presence State
- Source Type
- Confidence
- Related Session
- App/Workflow Match

Grid should support:
- show/hide columns
- reorder columns
- sorting
- filtering
- presets
- virtualization
- incremental enrichment
- row badges/icons

### Viewer
Capabilities:
- fit
- fill
- 1:1 exact pixel mode
- zoom
- pan
- fullscreen later
- side-by-side compare
- blink compare
- overlay compare
- filmstrip / nearby item navigation

Rendering strategy:
- show something immediately
- then improve quality
- then run background work

Adaptive scaling:
- fast scaler while interacting
- high-quality scaler when settled
- exact / no interpolation at 1:1
- nearest-neighbor inspection mode for high zoom

### Details pane tabs
- Summary
- Metadata
- Raw Metadata
- Reconciled Metadata
- Integrity
- Embedded Artifacts
- Thumbnail
- Compare
- GPS / Time
- Structure
- Related Artifacts
- Workflow / Source Match
- Findings
- Reconstruction
- Repair / Normalize
- Notes later

## Metadata strategy

### Raw metadata
Store and expose raw values per source:
- EXIF
- XMP
- IPTC
- filesystem
- cache DB
- app DB
- sidecar
- inferred/contextual

### Reconciled metadata
Produce a preferred summary view using merge/reconciliation rules.
Each reconciled value should include:
- preferred value
- source used
- merge status
- confidence
- conflict notes

### Merge outcomes
- Resolved
- Merged
- Ambiguous
- Conflicted

### Never lose provenance
Raw extracted values must never be destroyed by reconciliation.

## Confidence model

ArtifactView should use confidence scores for ambiguous or multi-interpretation checks.

Distinguish:
- Observation
- Interpretation
- Observation Confidence
- Interpretation Confidence

Use wording like:
- consistent with
- likely
- possible
- inferred
- supported by
- conflicting with

Avoid wording like:
- definitely from
- definitely tampered
- guaranteed original

## Time and GPS analysis

### Raw time types
- filesystem created/modified
- EXIF DateTimeOriginal
- EXIF create/modify variants
- GPS UTC date/time
- OCR-detected visible timestamp
- app DB timestamps
- inferred local times

### GPS handling
GPS time should be treated as UTC.
Show:
- raw GPS UTC
- translated local time when timezone can be resolved
- inferred local time when timezone is not explicit

Never replace raw GPS UTC with local time.

### Inconsistency interpretation
Possible explanations for time conflicts include:
- camera timezone/clock wrong
- travel / timezone not updated
- DST mismatch
- metadata rewritten/exported
- actual tampering

Do not assume tampering first.

## Integrity and corruption

Basic corruption/integrity checking should be in the core.

Goals:
- structural validity
- decode validity
- decode coverage
- artifact-level integrity

Outputs may include:
- Fully decodable
- Partially decodable
- Corrupt main but intact thumbnail
- Corrupt embedded artifact
- Trailing data present
- Structural anomaly

## Ghost files and overlay system

Ghost files are virtual media entities representing missing or non-live items supported by artifacts.

Sources may include:
- embedded preview references
- Thumbs.db
- Windows thumbcache
- app cache previews
- sidecar metadata
- deleted file records later

Two uses of cache/artifact files:
1. enrich existing live files
2. create overlay/ghost entries for missing files

Ghost items should:
- appear inline in the grid
- be clearly labeled
- have source/contributor lists
- support preview display if available
- support reconstruction/export
- support comparison against live files
- carry confidence for identity/path association

## Embedded artifacts

Supported artifact types may include:
- EXIF thumbnail
- Motion Photo video
- representative frame
- Google depth payloads
- Apple auxiliary/depth images
- gain maps
- secondary image payloads
- unknown trailer data

Artifact fields:
- type
- source metadata namespace
- offset/length
- MIME/type
- hash
- dimensions if applicable
- parse confidence
- decode status
- exported path
- relation to main item

## Reconstruction system

Reconstruction should be evidence-derived reconstruction, not synthetic recreation.

Categories:
- Exact artifact extraction
- Partial reconstruction
- Lo-fi reconstruction
- Composite reconstruction
- Historical framing reconstruction
- Contextual reconstruction

Hard rules:
- never claim reconstructed output is the original unless exact
- never invent metadata
- preserve provenance separately
- reconstructed outputs must be clearly named
- exact extracted lossy artifacts may keep native format
- any composited/transformed/reconstructed output must export losslessly

### Naming
Examples:
- `<name>__partial_reconstruction__exifthumb.png`
- `<name>__lofi_reconstruction__thumbcache.png`
- `<name>__composite_reconstruction__main+thumbcache.png`

## OCR / visual timestamp detection

Potential uses:
- screenshots
- CCTV captures
- dashcam frames
- photos of screens
- shared images with burned-in timestamps

Outputs:
- raw text
- parsed date/time candidates
- confidence
- bounding boxes
- comparison against EXIF/GPS/filesystem times

Treat OCR timestamps as visual evidence, not authoritative capture metadata.

## Session intelligence

Features may include:
- burst/session clustering
- sequence gap detection
- outlier detection
- duplicate detection
- near-duplicate/cropped/re-encoded variant detection
- cross-folder session linking later
- cache-only and mixed-source session reconstruction

## App / platform / workflow recognition

Use a unified recognition/signature database.

Recognition dimensions:
- app family
- platform
- workflow type
- artifact patterns
- output patterns
- metadata retention/stripping
- naming conventions
- dimensions/aspect ratios
- software tags
- cache/app DB evidence

Example profiles:
- WhatsApp / Android / image share
- WhatsApp / Android / document share
- Telegram / iPhone / cached media
- Instagram / downloaded/shared copy
- Apple Photos / shared without location
- generic camera-original candidate
- generic edited/exported copy candidate

Output should say:
- likely source/workflow matches
- consistency scores
- conflicting signatures
- supporting factors
- raw software metadata

## OS / sidecar / app artifacts

Support may include:
- Thumbs.db
- ehthumbs.db
- Windows thumbcache_*.db
- desktop.ini
- .DS_Store
- __MACOSX
- AppleDouble `._*`
- .picasa.ini
- XMP sidecars
- Adobe Bridge cache
- app thumbnail/metadata caches later

These should be treated as:
- enrichment sources
- overlay/ghost sources
- supporting evidence
not primary truth by default

## Global correlation database

Optional subsystem for large collections.
Use for:
- cross-folder correlation
- duplicate/variant detection
- session linking
- workflow/source linking
- ghost-to-live matching across libraries

Use a separate global index/correlation database, not the same DB as the local fast cache.

SQLite is acceptable as the authoritative catalog for a long time.
Heavy blobs should stay out of it.

## Plugin system

Plugins should be a first-class architecture concept.

Categories:
- Source plugins
- Format handlers
- Analyzer plugins
- Processing / reconstruction plugins
- Export/report plugins
- Signature/rule-pack plugins
- Advanced/premium plugins

At boot:
- scan plugin folders
- read manifests first
- do not execute plugin code just to inspect it
- apply load policy
- then load approved plugins

User policies:
- core only
- OSS plugins only
- signed plugins only
- full mode

Closed-source plugins should be discoverable but not executed unless allowed.

## Open core vs premium

### Open core
- fast viewer/browser
- metadata extraction
- dynamic columns
- ghost-file overlay basics
- exact artifact extraction
- basic reconstruction
- EXIF thumbnail compare
- basic integrity checks
- confidence-based findings / anomaly score
- plugin SDK

### Premium / closed plugins
- advanced cache parsers
- mobile artifact sources
- iPhone backup parsing
- app DB correlation
- platform/workflow intelligence packs
- advanced reconstruction processors
- deleted-file/disk-image sources
- enterprise reporting/support
- frequently updated recognition/signature packs

## Suggested initial MVP
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
