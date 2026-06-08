# Planned Session Edit Flow

How a planned multi-file MCP edit moves through AIMonitor on the `codex/planned-session-refresh` design. There are **two compile gates**, and they are different compiles.

## Current flow (as implemented on the branch)

```
PLAN     start_monitor_session(filesPlanned=[A,B,C])         (required)
            │
EDIT     refresh/new + edit each file → Working candidates
   edit A,B → overlay validation DEFERRED (not all candidates exist yet)
   edit C   → all candidates exist →  ┌─────────────────────────────────────┐
                                      │ GATE 1: overlay SEMANTIC compile     │
                                      │ (Roslyn, whole project, A'+B'+C'      │
                                      │  swapped in). Catches cross-file C#   │
                                      │  errors. SKIPS .razor; no MSBuild/    │
                                      │  analyzers/source-gen.                │
                                      └─────────────────────────────────────┘
            │
   agent reads result → noisy? real missing source? → REPLAN (add file, re-edit)
   unresolved errors? → ASK USER before merge
            │ clean / approved
STAGE    stage_candidate_for_review(A,B,C)
REVIEW   launch_staged_diff → ValidateStagedOverlay = HASH-ONLY (integrity, no build)
         user merges each file in WinMerge (diff-stability + light review)
         record_diff_decision(accepted)  ← file lands in watched source HERE
            │  (repeat per file)
TERMINAL after last file decided →  ┌─────────────────────────────────────┐
                                    │ GATE 2: FULL dotnet build over the    │
                                    │ staged overlay + index rebuild        │
                                    │ (razor runtime source mapping)        │
                                    └─────────────────────────────────────┘
```

## The one risk, pinned to the chart

`GATE 2` (the full `dotnet build`) sits **below** the `← file lands in watched source HERE` line.

- `GATE 1` is **semantic-only** (Roslyn overlay) — it cannot see `.razor` markup, source/Razor generators, analyzers, or MSBuild/cross-project errors.
- `GATE 2` is the compile that **can** see those — but it runs **after** the files are already merged into watched source.
- So a full-build-only error clears `GATE 1`, gets merged, and only fails at `GATE 2` → watched source is left **non-compiling with no rollback** (worse if the session is abandoned before the terminal step, so `GATE 2` never runs).

The common case (cross-file C# breaks) is caught by `GATE 1` before merge, so this is narrow — but real.

## The fix — one box moves up

Run the full build **before** any merge (the operator's "verifiable batch / overlay-first" model). Same number of builds; nothing reaches watched source until the whole plan compiles.

```
... GATE 1 (unchanged: fast inner-loop semantic check during editing) ...
            │ clean / approved
STAGE    stage_candidate_for_review(A,B,C)
         ┌─────────────────────────────────────┐
         │ GATE 2 (MOVED UP): FULL dotnet build  │   <-- the box that was at the bottom
         │ over the staged overlay, BEFORE merge │
         └─────────────────────────────────────┘
            │ green  (red → nothing merged; agent replans / asks user)
REVIEW   launch_staged_diff (diff only)
         user merges each file in WinMerge   ← merging an already-validated batch
         record_diff_decision(accepted)
            │
TERMINAL index rebuild only (already green; no second build needed)
```

## Two rules the flow depends on (document them)

1. **The operator must merge the staged bytes verbatim — no hand-editing in WinMerge.** The overlay's validity transfers to watched source only because the overlay *is* a copy of watched with the staged files swapped in; hand-edits during merge break that equivalence.
2. **Scoped (project-granular) post-accept refresh** in the `index rebuild` step must not cascade-delete other projects' inbound references — that's a separate data-loss bug tracked in `docs/findings/PlannedSessionRefreshReview-2026-06-08.md` (the one HIGH), orthogonal to gate placement.
