# Evidence model

The forensic rules that govern how ArtifactView represents evidence, findings,
ghost items, reconstruction, and integrity. Read when touching analysis, metadata
reconciliation, findings/scoring, or reconstruction/export.

## Source model

Do not think only in terms of plain files. A media entity may be backed by multiple
contributors: live file bytes, EXIF metadata, EXIF thumbnail, Windows thumbnail
caches, app cache previews, sidecar files, app DB rows, backup records, inferred
context. A media entity may be present/live, present-and-enriched, ghost/cache-only,
or merged from multiple evidence contributors.

## Ghost files

Ghost files are first-class entities, clearly labeled, never treated as normal files.
Use ghost/cached artifacts two ways: (1) enrich existing live files, (2) create
overlay entries for missing files. Ghost items must retain: source type, contributor
list, confidence of association, preview availability, and whether original bytes are
present or missing.

## Metadata rules

Always preserve both raw extracted values and reconciled/preferred values. Reconciled
values must include: chosen value, source used, merge status, confidence, and
conflicting alternatives where applicable. Never collapse multiple values without
preserving provenance.

Prefer field-specific merge policies over one global rule:
- camera model: prefer direct embedded metadata
- GPS: prefer direct coordinates over inferred context
- filesystem timestamps: keep separate and weaker than capture metadata

## Findings and scoring

Model every ambiguous check as: observation, interpretation, observation confidence,
interpretation confidence. Do not present uncertain outcomes as definitive. Use
language like "consistent with / likely / possible / inferred / supported by /
conflicts with"; avoid "definitely from / definitely tampered / guaranteed original".

A top-level summary score is framed as review priority or anomaly score — never as
guilt or authenticity certainty.

## Embedded artifacts

Treat embedded artifacts as first-class objects (EXIF thumbnail, Motion Photo video,
representative frame, depth map, gain map, auxiliary image, unknown payload/trailer).
Each carries: type, source namespace/detection method, hash, dimensions if applicable,
decode status, parse confidence, and export path if extracted.

## Reconstruction rules

Use the term **reconstruction**, not recreation. Reconstruction is a derived output
built only from available evidence artifacts; it may scale, align, mask, and composite
but must not invent scene content.

Categories: exact artifact extraction, partial reconstruction, lo-fi reconstruction,
composite reconstruction, historical framing reconstruction.

Hard rules:
- never claim reconstructed output is the original unless it is an exact extracted artifact
- never add inferred metadata into reconstructed files
- keep inferred/contextual information in sidecars or reports only
- all reconstructed/composited outputs must use a lossless export format
- only exact binary extractions may preserve original lossy format

Filename rules make the reconstructed nature obvious, e.g.
`<name>__partial_reconstruction__exifthumb.png`,
`<name>__lofi_reconstruction__thumbcache.png`,
`<name>__ghost_reconstruction__thumbsdb.jpg` (only if exact JPEG bytes were extracted).

## Integrity and corruption checking

Basic corruption/integrity checking belongs in the core. Model integrity separately
from interpretation. Track: structural validity, decode validity, decode coverage,
embedded artifact integrity. States include fully decodable, partially decodable,
corrupt main but intact thumbnail, corrupt embedded artifact, trailing data present.
Do not mix repair/salvage logic into normal read-only analysis.

## Time and GPS handling

GPS time is UTC. Never replace raw GPS UTC with a localized/inferred value. Always
preserve the raw UTC timestamp and show localized/inferred forms as separate derived
values. When comparing capture time against GPS-localized time, allow benign
explanations (camera timezone not updated, travel, DST mismatch, metadata
rewrite/export). Treat tampering as one possible interpretation, not the default.

## OCR / visual timestamps

OCR-derived timestamps are visual evidence, not authoritative capture metadata. Store
raw OCR text, parsed candidates, confidence, bounding boxes, and comparison results
against EXIF/GPS/filesystem times.

## Workflow / app recognition

Use a unified signature database for apps, workflows, platform/site outputs, cache
artifacts, and output patterns. Return results as "consistent with",
weak/moderate/strong match, or conflicting matches. Do not claim certain app origin
unless multiple strong evidence sources support it.
