---
status: confirmed-backlog
type: finding
created: 2026-06-01
scope: edit-workflow latency (full-solution rebuild/validation) and token cost, observed live
confidence: high (measured timings; token costs estimated from response sizes)
method: live workflow test — added UI/DatabaseDomainTestForm.cs (new-file) + SchemaViewer.cs routing (symbol edit) against watched Schema Studio.sln
---

## Summary

Investigation update - 2026-06-01:

- This is still a real cost issue after the adapter safety fixes.
- No partial "edited project only" build was applied in this pass because it can miss downstream solution compile breaks
  when a shared/public API changes.
- No single-file SQLite row replacement was applied in this pass because correct reference freshness needs dependency
  and target-symbol invalidation, not just deleting rows for the changed file.
- The next correct implementation should add an indexing/validation orchestration layer that can compute affected
  projects/files, reuse validation workspaces, and upsert only safe slices while falling back to full rebuild when the
  affected set is uncertain.

A two-cycle safe-edit workflow test (one new file, one existing-file symbol edit) surfaced two cost problems that
are latency/token issues, not correctness issues. Both edits validated clean (full-solution build, 0 diagnostics) and
were accepted (`accepted-normalized` — LF candidate normalized to the file's CRLF on save; benign).

## Finding 1 — No real incremental rebuild (latency)

Every `launch_staged_diff` runs a **full-solution build** before WinMerge, and every accept runs a **full solution
index rebuild**. Measured on a one-file change against a 6-project / 85-document solution:

- Post-accept index rebuild: **15.3s** (cycle 1), **10.8s** (cycle 2).
- Pre-merge full-solution build: additional seconds per launch, on top.

The per-file entry points exist but are not actually incremental. `refresh_solution_index_file` states verbatim:
*"AIMonitor currently rebuilds the semantic index and returns the requested file slice."* So `refresh_solution_index`,
`refresh_solution_index_file`, and `refresh_file_and_index` all do a **full** rebuild today; the per-file variants only
filter the returned slice.

**Is an affected-only rebuild possible?** Yes, and it is the right fix:
- **Index:** replace the full rebuild with a single-file **upsert** — delete + reinsert only the changed file's
  symbol/reference/document rows. The post-accept path (`indexRefresh`) and the per-file tools should use this instead
  of `SolutionIndexBuilder` over the whole solution.
- **Validation:** scope the pre-merge build to the **affected project(s)** rather than the full solution; reuse the
  validation workspace so MSBuild incrementality applies instead of recreating a fresh workspace each launch.

This compounds with the data-surface findings in `CliVsMcpAdapterReview-2026-06-01.md` (full-table scans,
`EnsureCreated()` per read) — the whole stack currently prefers "rebuild/scan everything" over "touch what changed."

## Finding 2 — Workflow token cost (estimated, no exact inline meter)

Observed biggest consumers in the test, worst first:

| Source | Cost | Cause |
|---|---|---|
| `get_solution_index_tree` | highest (~8–10k) | dumps all 85 files + namespaces when only one folder was needed |
| `get_file` ×2 (whole files: ~14k-char form + repo) | high | whole-file reads to learn conventions instead of scoped queries/symbol summaries |
| stage / launch / record responses ×4 | medium each | every step echoes the **entire** staged record (all paths, both hashes, ledger path, compare ids) |
| `get_source_map` (selector) + `get_symbol` ×2 | low–medium | appropriately scoped — the good pattern |

Two levers:
- **Caller discipline (agent side):** prefer `query_solution_index(scope: folder/file)` and `get_source_map`/`get_symbol`
  summaries over `get_solution_index_tree` and whole-file `get_file`. (This was an agent miss in the test.)
- **Adapter payload trimming (server side):** stage/launch/record return the full staged-record object on every call.
  A one-file edit cycle echoes paths+hashes 4+ times. A compact response (id, status, classification, stagedHash, and a
  `verbose` opt-in for the rest) would cut steady-state token cost materially — directly serving the harness's
  token-minimization goal.

## Notes

- `accepted-normalized` recurred both cycles because `submit_file` writes raw bytes (LF) with no normalization while the
  watched files are CRLF — see the `submit_file` finding in `CliVsMcpAdapterReview-2026-06-01.md`. Authoring new files
  with the project's existing EOL (CRLF here) would avoid the normalization round-trip.
- Both edits compiled in the pre-merge full-solution build, confirming the new form + routing are wired correctly.
