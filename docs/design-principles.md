# Design principles

Core identity and philosophy. Read at the start of any feature work.

## Project identity

ArtifactView is a Windows desktop media viewer and forensic browser. It must feel
like a fast, high-quality viewer first, while also supporting metadata inspection,
confidence-based forensic findings, ghost-file overlays from caches and sidecars,
embedded artifact extraction, provenance-safe reconstruction, integrity/corruption
checks, iPhone backups, Android caches, disk images, deleted items, and (future) video.

Do not turn ArtifactView into a generic image editor, a pure metadata dumper, or a
monolithic forensic lab UI.

## Primary product goals (priority order)

1. Fast first display and smooth browsing
2. Clear provenance for all extracted and inferred values
3. Readable, explainable findings rather than magic verdicts
4. Clean architecture with small, composable subsystems
5. Safe handling of originals and derived outputs

## Core design philosophy

- Viewer-first, forensic-aware
- Evidence-driven, not assumption-driven
- Confidence-based, not absolute
- Raw evidence and reconciled summaries must both exist
- Never silently drop conflicts
- Never invent metadata for reconstructed outputs
- Never overwrite originals by default
- Exact extracted artifacts may preserve original format
- Any composited or reconstructed image must export losslessly

## If unsure

Optimize for viewer responsiveness, provenance preservation, explainability, safe
defaults, and modularity. If a tradeoff exists between flashy automation and
trustworthy evidence handling, prefer trustworthy evidence handling.

## What to avoid

- Do not overwrite originals by default
- Do not hide uncertainty
- Do not silently merge conflicts away
- Do not treat cache artifacts as primary truth without context
- Do not create lossy outputs from reconstructed/composited results
- Do not block the UI with analysis work
- Do not make closed plugins mandatory for the app to feel useful
- Do not turn ArtifactView into a general image editor
