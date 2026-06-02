---
status: verified-merged
type: finding
created: 2026-06-02
scope: third independent review of the post-remediation + cloud-fix state, triaged by decision 0002
reviewed-commit: 70f6233 (codex/cloud-review-trigger-20260602), now merged to main via PR #10 (08a6aca); main @ 4025cb0
prior: docs/findings/SystemReview2-2026-06-02.md, docs/findings/SystemReview2-RemediationVerification-2026-06-02.md
triage-basis: docs/decisions/0002-safety-enforcement-philosophy.md (visibility rule)
method: multi-agent workflow (6 dimensions, adversarial verify + visibility triage); 29 raised, 28 confirmed; build 0/0
---

## Verdict: merge-ready (and merged)

Third deep review of `70f6233` (system-review-2 remediation + the two cloud follow-up fixes). Build **0 warnings /
0 errors**. **0 High, 0 Medium.** Every open item from reviews #1 and #2 is now closed with test-backed proof, and both
new cloud fixes are correct. The branch has since merged to `main` (PR #10, `08a6aca`). This pass was triaged by the
decision-0002 visibility rule, and the by-design items (`forceValidation`, REG-BRIDGE-1, LIFECYCLE-5) were correctly
**not** re-filed.

## Closed since review #2 (test-backed)

| Item | Proof |
| --- | --- |
| **PARITY-1** | Roslyn edits → `EnsureSession` → `EnsureEditableSession` → `EnsureSessionCanEdit` throws on `RequiresRefresh`; defense-in-depth re-check in `WriteWorkingCandidate`. `Roslyn_typed_edit_blocks_after_accept_until_refresh` passed. |
| **INDEXREFRESH-1** | `StagedDecisionWorkflow.CreateNextStep` returns the stale-index warning on `indexRefresh.IsError`. `Record_reports_stale_index_when_post_accept_rebuild_fails` passed. |
| **INDEXREFRESH-2** | `SolutionIndexStore.SaveSnapshot` throws on a 0-project snapshot over a populated index, before any transaction. `SaveSnapshot_zero_project_snapshot_does_not_clear_existing_index` passed. |
| **INDEXREFRESH-3** | Accept sets `IndexStale`; `GetStatus` classifies `index-stale`; `Refresh` preserves it; `MarkIndexFresh`/`MarkAllIndexesFresh` clear it. `Refresh_preserves_index_stale_after_accept_until_rebuild_marks_fresh` passed. |
| **LIFECYCLE-4** | `EnsureRecordNotDecided` + `IsTerminalDecision`, called in `RecordDecision`, `StagedDecisionWorkflow.Record`, and `StagedDiffLaunchWorkflow.Launch`. Terminal-reuse tests passed. |
| **PARITY-DECISION-1** | Single `StagedDecisionWorkflow.Record` + single `CreateSummary`; CLI record-decision/accept/reject and MCP `RecordDiffDecision` all route through it. |

## The two cloud follow-up fixes — verified correct

- **CLI reject routing.** `reject` now routes through `StagedDecisionWorkflow.Record(decision="rejected",
  expectedStagedHash=null)`, inheriting `EnsureRecordNotDecided`, the shared response shape, and NextStep, and correctly
  skipping the index rebuild. `Edit_reject_shortcut_returns_shared_decision_shape_and_keeps_watched_source_original`
  passed. New-file reject deletes only the zero-length blank baseline; non-blank operator content survives as
  `dirty-unexpected` — the hard floor cannot destroy non-blank watched content.
- **`MarkAllIndexesFresh`.** Clears solution-wide `IndexStale` only after a non-throwing full rebuild; solution-scoped,
  lock-file-excluding, and TOCTOU-safe on the per-manifest write (re-reads under `AcquireManifestLock`). Success and
  failure-leaves-stale tests both passed.

## Open: one residual race (Codex-found, correctly invisible-in-diff)

Codex Cloud's own PR-#10 follow-up review caught a **deeper** race than this review did and documented it:
[IndexStaleRebuildRaceTodo-2026-06-02.md](IndexStaleRebuildRaceTodo-2026-06-02.md) (`4025cb0`).

- **The race:** a manifest can become stale *during* a long rebuild — an accept lands while MSBuild/indexing is still
  running (steps: rebuild starts → concurrent accept sets `IndexStale=true` → rebuild completes from a snapshot taken
  before that accept → `MarkAllIndexesFresh` clears the newly-stale manifest anyway). Result: **false-fresh** — the
  manifest reports the index fresh though the rebuild reflects pre-accept bytes.
- **Honest correction to this review:** the third review verified `MarkAllIndexesFresh` is TOCTOU-safe on the *narrow*
  lock-window (check-then-write on a single manifest) — and it is. It **missed the wider rebuild-duration race.** Codex's
  review caught it. (Second time the cloud review out-caught this review on the index-stale path; the first was the
  per-file-vs-solution-wide flag clear.)
- **Triage + reachability (operator-reviewed, corrected):** invisible-in-diff (index-state; nothing shows at WinMerge),
  so by decision 0002 it is not a soft/by-design item. **But it is reachable only by overlapping a rebuild with a
  concurrent accept**, and a single agent's post-accept rebuild is synchronous/blocking — so the *only* trigger is the
  operator manually hitting the App rebuild button / `refresh_solution_index` (a different process) while an agent is
  mid-accept. That is off the single-active-agent path — a one-in-a-million operator action. **Practical risk:
  negligible.**
- **Root-cause reframing — fix the parallelism, not the clear (this corrects an earlier-too-generous endorsement of the
  marker fix).** `AcquireManifestLock` is a cross-process file lock but **per-file and short-held**, so it makes manifest
  writes atomic but does **not** serialize a rebuild against an accept — the failure is an *ordering* problem (the
  committed snapshot predates the accept), which a per-file write-lock cannot catch. So the defect is that rebuild ∥
  accept is *allowed*, not `MarkAllIndexesFresh`'s logic. Preferred resolutions: **(1) confirm-and-close** — accept the
  single-active-agent contract and document that a manual rebuild must not overlap a decision (consistent with the
  LIFECYCLE-5 recalibration and decision 0002); or **(2) serialize** — a solution-wide rebuild lock spanning the whole
  rebuild + clear that the accept's stale-set also acquires, making overlap structurally impossible. Codex's
  snapshot-marker patch *tolerates* the parallelism — machinery for a state the single-active-agent model forbids — and
  is the symptom-fix, not the root-fix. Reframing published to `IndexStaleRebuildRaceTodo-2026-06-02.md`.

## Optional cleanup nits (low/clarity, diff-visible — not must-harden)

- **Dead `WorkflowEditService.Accept`/`Reject(string)` instance methods** (`WorkflowEditService.cs:903-947`): zero
  callers since CLI routes through `StagedDecisionWorkflow.Record`. They call `RecordDecision` directly and skip the
  post-accept rebuild wrapping — harmless now, but delete or `[Obsolete]` them so no future caller re-introduces a
  rebuild-skipping accept path. (A verifier sub-claim that `Reject(string)` skips `EnsureRecordNotDecided` is **wrong** —
  `RecordDecision` guards all callers; disregard that part.)
- **Redundant per-file `MarkIndexFresh`** at `PostAcceptIndexRefreshService.cs:48`: now a no-op after
  `MarkAllIndexesFresh`. Idempotent; safe to drop.

## Net

Merged state is sound: hard floor intact, all prior items closed and tested, decision-0002 triage held (no by-design
re-files). One genuine, low-probability, invisible-in-diff hardening item remains — the rebuild-duration index-stale
race — already tracked with a correct fix shape. No watched-source-safety exposure in any confirmed finding.
