# ArtifactView

Fast Windows media viewer and forensic browser for inspecting metadata, embedded artifacts, ghost files, and cache-derived reconstructions.

## What it is

ArtifactView is a Windows desktop media viewer and forensic browser.

It is designed to feel like a fast, high-quality viewer first, while also supporting:
- metadata inspection
- confidence-based forensic findings
- embedded artifact extraction
- ghost-file overlays from caches and sidecars
- integrity/corruption checks
- provenance-safe reconstruction
- future support for backups, disk images, deleted items, and video

## Core principles

- Viewer first
- Forensic aware
- Evidence driven
- Provenance preserving
- Confidence based, not absolute
- Modular and plugin friendly

## Key capabilities

### Viewer and browsing
- Fast image/media viewing
- Explorer-style grid
- Dynamic metadata columns
- Right-side details pane
- High-quality zoom, pan, fit, and 1:1 mode
- Fast command-line open mode for single files

### Metadata and findings
- EXIF/XMP/IPTC extraction
- Raw and reconciled metadata views
- GPS and time analysis
- Confidence-based findings
- Explain-this-finding support
- Basic anomaly/review-priority score

### Ghost files and artifacts
- EXIF thumbnail extraction
- Thumbnail-vs-main comparison
- Ghost files from cache artifacts
- Cache-derived enrichment of live files
- Reconstruction from embedded/cached previews
- Embedded artifact extraction and comparison

### Integrity
- Basic JPEG and PNG integrity/corruption checks
- Structural validity
- Decode validity
- Coverage reporting

### Extensibility
- Plugin discovery at startup
- Trust-aware loading policy
- Open-core design with optional advanced plugins

## Planned advanced features
- Windows thumbnail cache support
- Motion Photo and depth extraction
- Session clustering and gap detection
- Platform/workflow recognition (WhatsApp, Telegram, Instagram, etc.)
- iPhone backup and Android artifact sources
- Disk image and deleted-file support
- Video support

## Project structure

See:
- [`docs/architecture.md`](docs/architecture.md)
- [`docs/roadmap.md`](docs/roadmap.md)
- [`docs/product-spec.md`](docs/product-spec.md)

## Suggested initial MVP
- Filesystem folder source
- Fast viewer
- Command-line fast open
- Dynamic grid columns
- Metadata summary pane
- Raw/reconciled metadata
- EXIF thumbnail extraction
- Thumbnail compare
- Basic confidence-based findings
- JPEG/PNG integrity checks
- Ghost overlay basics from cache sources
- Exact artifact extraction
- Basic lo-fi reconstruction
- Provenance-safe export rules
- Plugin discovery with trust policy
