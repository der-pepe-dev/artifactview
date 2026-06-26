# ArtifactView

Windows desktop media viewer and forensic browser

Repository: `https://github.com/me/ArtifactView`

## Main goals

- A fast, high-quality Windows desktop media viewer that is also forensic-aware.
- Evidence-driven analysis: confidence-based findings with preserved provenance.
- Ghost-file overlays, embedded-artifact extraction, and provenance-safe reconstruction.
- Source breadth: folders, caches, iPhone backups, Android caches, disk images, deleted items.

## How agents should use this memory

- Start with this file, [[current-status]], [[instructions/agent-rules]], and [[tasks/lessons]].
- Use [[context-map]] to pick only the relevant docs for the task.
- Check [[environment]] before suggesting shell commands.
- Create one file per active task under `tasks/` (parallel tasks supported).
- Use [[tasks/todo]] as the durable backlog only.

## Instructions

- [[instructions/agent-rules]]
- [[instructions/cli-tooling]]
- [[context-map]]

## Task tracking

- [[tasks/todo]] — durable backlog by priority
- `tasks/<task-name>.md` — one file per active task
- `tasks/done/` — completed task files
- [[tasks/lessons]] — correction patterns and recurring mistakes
- [[tasks/task-template]] — reusable task note template

## Main documents

- [[current-status]]
- [[environment]]

- [[design-principles]] — identity, philosophy, what to avoid
- [[architecture]] — layers, sources, formats, analyzer pipeline, cache
- [[evidence-model]] — metadata, findings, ghost, reconstruction, integrity, time/GPS
- [[viewer-and-cache]] — viewer performance, cache, global index
- [[plugins-and-open-core]] — plugin trust model, open-core vs premium
- [[instructions/coding-and-testing]] — coding rules + WSL2/DiscUtils gotchas
- [[product-spec]] — full product specification
- [[roadmap]] — phased roadmap
