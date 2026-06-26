# GitHub Copilot Instructions for ArtifactView

## Project Identity
ArtifactView is a **Windows desktop media viewer and forensic browser**.

It is:
- viewer-first
- forensic-aware
- evidence-driven
- provenance-preserving
- confidence-based, not absolute
- modular and plugin-friendly

ArtifactView is **not** just an EXIF viewer and **not** just a forensic lab tool. It must remain pleasant and fast as a daily media browser.

## High-Level Product Goals
When generating code, preserve these goals:
- show media as fast as possible
- do expensive work in the background
- keep the UI responsive at all times
- preserve provenance for extracted, inferred, and reconstructed data
- separate raw evidence from reconciled summaries
- use confidence-based language for ambiguous findings
- keep reconstruction/export behavior honest and clearly labeled
- keep the core useful even when no plugins are loaded

## Architecture Principles
Use a layered architecture.

Preferred layers:
- **UI**: WPF windows, controls, view models, commands
- **Application**: orchestration, scheduling, workflows, use cases
- **Domain/Core**: media entities, findings, artifacts, reconciliation, confidence
- **Infrastructure**: filesystem, SQLite, blob store, parsers, plugin discovery/loading

Do not mix UI concerns with parsing, analysis, cache logic, or export logic.

### Keep these subsystems separate
- viewer/rendering pipeline
- source providers
- format handlers
- analyzers
- processors/reconstruction/exporters
- cache/index systems
- plugin discovery/loading
- report generation

## Core Domain Concepts
When adding models or logic, align with these concepts.

### SourceItem
A raw item exposed by a source provider.
Examples:
- live filesystem file
- thumbcache entry
- app cache preview
- iPhone backup item
- deleted file record

### MediaEntity
A logical browseable item shown in the grid.
Can be:
- live file
- enriched live file
- ghost/cache-only file
- merged entity with multiple contributors

### Contributor
Any source that adds evidence or metadata to a media entity.
Examples:
- live bytes
- EXIF metadata
- EXIF thumbnail
- Thumbs.db entry
- Windows thumbcache entry
- app DB row
- sidecar file

### EmbeddedArtifact
Any embedded or auxiliary media/data unit inside a media item.
Examples:
- EXIF thumbnail
- Motion Photo video
- depth map
- gain map
- unknown trailer payload

### Finding
A structured analysis result.
Findings should distinguish:
- observation
- interpretation
- observation confidence
- interpretation confidence
- supporting factors
- conflicting factors
- provenance

### ReconciledField
A preferred field value derived from one or more raw source values.
Must preserve provenance and conflict state.

## Naming and Semantics
Use precise naming.

Prefer names like:
- `MediaEntity`
- `EmbeddedArtifact`
- `ArtifactContributor`
- `ReconciledFieldValue`
- `IntegrityResult`
- `Finding`
- `ConfidenceScore`
- `SourceProvider`

Avoid vague names like:
- `DataThing`
- `Helper`
- `Utils`
- `Manager` unless the role is genuinely managerial/orchestration

Use suffixes intentionally:
- `Provider` for source/data providers
- `Analyzer` for read-only analysis logic
- `Processor` for derived visual or analytical processing
- `Exporter` for output creation
- `Service` only when something coordinates several components
- `Options` for immutable configuration objects
- `Result` for output DTOs/records

## UI and Performance Rules
ArtifactView is a viewer-first app.

### File-open behavior
When opening a file directly, especially from the command line:
1. display the image as fast as possible
2. improve render quality after first paint
3. load metadata and analysis progressively in the background

Never block first display on:
- deep metadata parsing
- cache scans
- integrity analysis
- ghost correlation
- signature matching
- reconstruction prep

### Viewer rendering
Use a staged rendering strategy:
- immediate preview first
- better render next
- exact/high-quality render last if needed

At 1:1 zoom:
- prefer exact pixel mapping
- avoid hidden smoothing

While actively zooming/panning:
- prefer responsiveness over best quality

### Grid population
The grid should support incremental enrichment.
A row may start with basic filesystem info and later receive:
- dimensions
- metadata
- findings
- integrity status
- ghost/correlation results

## Background Work and Scheduling
Prefer asynchronous and cancellable workflows.

Expensive operations should be background jobs, not UI-thread work.
Examples:
- metadata extraction
- thumbnail extraction
- integrity checking
- deep compare
- cache lookup
- global correlation indexing

Use clear priority ordering.
Typical order:
1. selected item render
2. selected item metadata
3. selected item quick findings
4. visible grid rows enrichment
5. nearby filmstrip items
6. local folder correlation
7. maintenance and global indexing

Every long-running operation should support cancellation where practical.

## Evidence and Provenance Rules
Preserve provenance everywhere.

For extracted or inferred values, keep:
- source type
- source identifier/path
- extraction method
- timestamp if relevant
- confidence if relevant

Do not overwrite raw evidence with merged results.
Always allow access to raw/source-specific values.

When two sources disagree, do not silently collapse them into one value without preserving the conflict.

## Confidence and Findings Rules
Use confidence-based reasoning for ambiguous conclusions.

### Do
Use wording like:
- consistent with
- likely
- possible
- inferred
- supported by
- conflicts with

### Do not
Use wording like:
- definitely from
- definitely tampered
- guaranteed original
- recovered original

unless the evidence is actually exact and deterministic.

Keep observation and interpretation distinct.
Example:
- Observation: embedded thumbnail differs from decoded image
- Interpretation: consistent with crop, edit, or stale preview

## Reconstruction and Export Rules
This project uses **reconstruction**, not synthetic recreation.

A reconstruction must be derived only from actual source artifacts.
Do not invent missing image content.

### Hard rules
- never modify originals by default
- reconstructed outputs must be clearly named as reconstructed
- never write inferred metadata into reconstructed files
- inferred/contextual/provenance data belongs in sidecars/reports, not inside reconstructed image metadata
- any composited/transformed/reconstructed image must be exported in a lossless format
- only exact binary artifact extraction may preserve original lossy format

### Terminology
Prefer:
- reconstruction
- partial reconstruction
- lo-fi reconstruction
- composite reconstruction
- exact artifact extraction

Avoid:
- restore original
- uncensor
- recover hidden content

## Ghost Files and Overlay Rules
Ghost files are first-class entities.

Use artifact sources in two ways:
1. enrich existing live files
2. create virtual ghost entries for missing files

Ghost items must be clearly labeled as such.
Do not present them as live originals.

A ghost entity may merge evidence from multiple sources, such as:
- Thumbs.db
- Windows thumbcache
- app cache preview
- sidecar metadata

## Metadata Merge / Reconciliation Rules
Each reconciled value should be explainable.

For merged fields, store:
- preferred value
- source used
- alternate/supporting values
- conflict state
- confidence
- rationale if useful

Not all fields should reconcile the same way.
Different fields may need different policies.
Examples:
- camera model: prefer direct embedded metadata
- timestamps: keep multiple variants visible
- keywords/tags: may merge distinct values
- GPS: preserve conflicts carefully

## File Format Strategy
Do not architect everything as one monolithic handler per extension.

Prefer:
- source providers
- format/container family handlers
- capability-based analyzers

Examples of handler families:
- JPEG family
- PNG
- TIFF/RAW family
- ISO-BMFF family (MP4/MOV/HEIC-like)

Analyzers should target capabilities, not only file extensions.

## Global Index vs Local Cache
Keep the **local working cache** separate from the **global correlation index**.

### Local cache
Optimized for:
- current folder/source browsing
- UI responsiveness
- previews
- immediate findings

### Global index
Optimized for:
- cross-folder correlation
- duplicate/variant detection
- session linking
- long-range matching

Heavy binary artifacts should stay in blob storage, not in SQLite rows.

## SQLite Usage
SQLite is acceptable as the source of truth for:
- local cache metadata
- global correlation catalog
- job state
- findings
- fingerprints
- artifact registry

Do not store large previews or heavy derived binaries inline in SQLite.
Use a filesystem blob store instead.

## Plugin Architecture Rules
ArtifactView supports open and closed plugins, but loading must be policy-controlled.

### Discovery
At startup, discover plugin manifests first.
Do not execute plugin code just to inspect it.

### User control
Users must be able to run:
- core only
- core + OSS plugins only
- core + signed plugins only
- full mode

Closed/proprietary plugins must remain optional.

### Plugin categories
Support different plugin types:
- source providers
- format handlers
- analyzers
- processors
- exporters
- signature/rule packs

## Open-Core Guidance
The open core should remain genuinely useful.
Do not assume premium plugins are always present.

Open core should be enough for:
- fast viewing
- metadata browsing
- basic integrity checks
- embedded thumbnail extraction
- basic findings
- ghost overlay basics
- exact artifact extraction
- basic reconstruction

## JPEG Decode Engine
Plan to add a selectable JPEG decode engine in the future. This will include options for alternative JPEG decoders such as knusperli and possibly libjpeg-turbo. The engine selection should allow different decoders for thumbnail decoding versus large image decoding. This is a future enhancement, not immediate work.

## Coding Style Guidance
Favor:
- small focused types
- immutable records where appropriate
- explicit DTOs/results
- async APIs for I/O and expensive work
- cancellation-aware operations
- testable services with interfaces only where they add value

Avoid:
- giant god classes
- static global mutable state
- UI-thread blocking I/O
- dumping unrelated logic into view models
- vague helper buckets

### Method design
Methods should usually do one clearly named thing.
Prefer returning structured results over tuples when the result has meaning.

### Error handling
Fail clearly and preserve context.
For parsing/analysis:
- surface partial results where safe
- preserve parser warnings/errors
- distinguish fatal failure from partial success

## Testing Guidance
Add tests for:
- metadata reconciliation
- ghost entity merging
- confidence calculations
- integrity result interpretation
- plugin manifest discovery/policy
- reconstruction naming/export rules
- fast-open scheduling logic where practical

Favor deterministic tests with small fixtures.

## Commenting Guidance
Comments should explain:
- why a rule exists
- provenance or forensic reasoning
- non-obvious tradeoffs

Avoid comments that only restate obvious code.

## Default UX Tone
ArtifactView should use cautious, professional language.

Good tone:
- neutral
- precise
- evidence-based
- non-accusatory

Avoid sensational or overconfident wording in UI text, reports, findings, and code comments.

## When in Doubt
When implementing new features, prefer these priorities:
1. preserve viewer responsiveness
2. preserve provenance
3. preserve explainability
4. keep raw evidence accessible
5. keep the architecture modular
6. avoid overclaiming certainty

## EXIF Thumbnail Comparison Rule
When comparing embedded EXIF thumbnail dimensions, note that they are always much smaller than the main image (e.g., 160x120 vs 4032x3024). Comparing pixel dimensions directly is not a valid forensic check. Instead, compare aspect ratios (with ~2% tolerance) to detect cropping or re-framing. Pixel content comparison should only run when aspect ratios match, since differing framing inflates the MAD artificially.
