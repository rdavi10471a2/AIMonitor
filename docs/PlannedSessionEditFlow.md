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

## The two gates answer different questions (both correctly placed)

- **GATE 1 — overlay compile (pre-merge):** *"if we apply all of this, will it still compile?"* A prediction against the overlay (watched + the staged files swapped in), before anything touches the real tree. Its job is to gate the **decision to merge**.
- **GATE 2 — full build on the real watched tree (post-merge):** *"did it actually compile?"* This is intentionally **after** the merge, because the point is to verify the **real source tree the operator just committed** — not a copy — and to catch any merge-time divergence. You can only build the real tree once it exists.

GATE 2 belongs after the merge by design. The flow chart above is correct.

## GATE 1 is *allowed* to be noisy — that's why "merge anyway" exists

GATE 1 cannot model `.razor` markup, Razor-generated code, or source generators, so some of its errors are **false positives** — e.g. a `.razor.cs` partial referencing generated members, `@inject` / `ComponentBase` plumbing, or other Razor-runtime artifacts the overlay can't see. Because GATE 1 is a **predictor, not the authority**, the workflow surfaces its result for human judgment and lets the operator **merge anyway** when the error is one of these known-noisy cases:

- GATE 1 red **and clearly a real break** → replan / fix before merge.
- GATE 1 red **but a recognizable noisy Razor/generated artifact** → operator may merge anyway.
- GATE 1 green → proceed.

In every case **GATE 2 — the full build on the real watched tree after merge — is the authoritative answer.** This is the design: GATE 1 trades fidelity for speed and is *allowed* to be noisy precisely because GATE 2 is the real check. (The same blind spot is why GATE 1 can also be falsely **green** — see below — so GATE 2 backstops the prediction in both directions.)

## The watch-item: fidelity, not placement

The only real risk is **how faithfully GATE 1 predicts GATE 2.** As implemented, GATE 1 is a Roslyn **semantic** overlay — it skips `.razor` markup and runs no MSBuild, analyzers, or source/Razor generators. So GATE 1 can go **green** while the real build (GATE 2) would fail on exactly those error classes. When that happens, the operator merges on a green overlay and GATE 2 reports the failure **after** the files are on the real tree, leaving it transiently non-compiling until the operator acts.

This is recoverable — the operator is in the loop, GATE 2 surfaces the break, and watched source is under version control — but it is a behavior change from `main`, where the pre-merge gate was a full build that blocked a build-breaking merge up front.

Levers (in order; **do not** relocate GATE 2 — building the real tree post-merge is the intended authoritative check):
1. **Raise GATE 1 fidelity** toward a full overlay build (or at least cover Razor / MSBuild / analyzers / source-gen) so a green overlay reliably predicts a green GATE 2 — fewer post-merge surprises.
2. **Make a GATE 2 failure loud, and don't let an abandoned session skip it silently** — that's the one way the real tree stays broken without anyone being told.
3. Recovery is via version control; there is no automatic rollback of a merged file.

## Two rules the flow depends on (document them)

1. **The operator must merge the staged bytes verbatim — no hand-editing in WinMerge.** The overlay's validity transfers to watched source only because the overlay *is* a copy of watched with the staged files swapped in; hand-edits during merge break that equivalence.
2. **Scoped (project-granular) post-accept refresh** in the `index rebuild` step must not cascade-delete other projects' inbound references — that's a separate data-loss bug tracked in `docs/findings/PlannedSessionRefreshReview-2026-06-08.md` (the one HIGH), orthogonal to gate placement.
