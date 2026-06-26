# ArtifactView context map

Use this file to decide which memory files to read for a task. Do not read every
file by default.

## Always read at session start

- [[index]]
- [[current-status]]
- [[environment]]
- [[design-principles]]
- [[instructions/agent-rules]]
- [[tasks/lessons]]

## General architecture

Read when touching layering, project boundaries, source providers, format families,
the analyzer pipeline, or domain concepts.

- [[architecture]]
- [[design-principles]]

## Evidence / metadata / findings / reconstruction

Read when touching metadata extraction/reconciliation, confidence scoring, ghost
items, embedded artifacts, reconstruction/export, integrity, or time/GPS/OCR rules.

- [[evidence-model]]
- [[architecture]]

## Viewer / rendering / cache

Read when touching the viewer, rendering pipeline, adaptive scaling, fast-open path,
cache, blob store, or the global correlation index.

- [[viewer-and-cache]]

## Plugins / open core

Read when touching plugin discovery, trust policy, or open-core vs premium scope.

- [[plugins-and-open-core]]

## Coding / testing (always relevant when writing code)

Read before writing or testing code — contains the WSL2 GDI+ and DiscUtils gotchas.

- [[instructions/coding-and-testing]]
- [[instructions/cli-tooling]]

## Product / roadmap

Read for scope, phase, and product direction.

- [[product-spec]]
- [[roadmap]]
