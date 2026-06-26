# Viewer and cache

Performance and storage rules. Read when touching the viewer, rendering pipeline,
cache, blob store, or global correlation index.

## Viewer performance rules

The viewer is critical. When opening a file directly, prioritize in this order:
1. show pixels as fast as possible
2. make navigation responsive
3. load metadata in the background
4. run analysis later

Never block first render on deep analysis. Use progressive loading: fast preview
first, better-quality render next, metadata/artifact/findings enrichment in
background. Use adaptive scaling: fast during interaction, high-quality when settled,
exact/no interpolation at 1:1, optional nearest-neighbor inspection mode at high zoom.

## Cache rules

Use a structured cache plus a blob store. Do not store large preview/reconstruction
blobs inside the main metadata DB. Separate concepts: local fast-view cache, optional
global correlation index, blob/artifact store.

Cache validity depends primarily on source fingerprint, file fingerprint,
analyzer/parser version, and schema version. Time-based expiration is secondary.

The DB is the source of truth. Blob files are disposable and must be garbage-collected.

## Global correlation index

If implementing cross-folder matching for large libraries, keep it separate from the
local folder cache. Store compact searchable signals: exact hashes, perceptual hashes,
key metadata, dimensions, timestamps, app/workflow summaries, cluster hints. Do not
turn the global index into a giant blob store.
