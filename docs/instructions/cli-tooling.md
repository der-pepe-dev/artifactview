# CLI tooling

Fast CLI tools preferred over slower POSIX equivalents in shell pipelines. Check
`environment.md` for which are actually installed on the current host.

- `rg` (ripgrep) — code/text search, instead of `grep -r`.
- `fd` — file finding, instead of `find`.
- `jq` — JSON querying (CLI output, config, lockfiles).
- `yq` — YAML query/validate (e.g. CI workflow files).
- `delta` — readable git diffs (`git -c core.pager=delta diff`).
- `hyperfine` — command benchmarking (before/after timing).

Use dedicated editor/search tools when available; reach for these in shell pipelines.

- `sqlite3` — inspect the structured cache / correlation DBs and app-DB sources
  (WhatsApp/Telegram/Signal correlation, iPhone backup `Manifest.db`).
- `xxd` / `hexdump` — inspect raw bytes (disk-image partitions, `$MFT` records,
  embedded artifact trailers, format-family parsing).
