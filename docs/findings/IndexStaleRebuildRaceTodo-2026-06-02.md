---
status: todo
type: finding
created: 2026-06-02
source: Codex PR #10 follow-up review
scope: index-stale recovery race
priority: low
---

# Index-Stale Rebuild Race TODO

## Summary

`SolutionIndexRebuildService.RebuildAsync` currently clears all `IndexStale` workflow manifest flags after a successful full rebuild.

That is correct for the normal single-operator recovery path, but there is a narrow race:

1. A full index rebuild starts.
2. Another adapter records an accepted decision while MSBuild/indexing is still running.
3. That accepted decision sets `IndexStale = true`.
4. The rebuild completes from a snapshot that may not include the accepted change.
5. `MarkAllIndexesFresh` clears the newly stale manifest anyway.

The result is a possible false-fresh workflow status: the manifest says the index is fresh even though the successful rebuild may reflect pre-accept bytes.

## Current Risk

Low in the intended operating model. AIMonitor assumes one active agent/operator flow per watched project, and this requires overlapping a manual/full rebuild with a same-file or same-project accept operation.

Still, the failure is invisible in WinMerge and belongs to the index-state safety layer, so it should be fixed before treating stale-index recovery as fully hardened.

## Proposed Fix

Change the rebuild recovery from "clear every current stale manifest after rebuild" to a snapshot-based clear:

```text
before rebuild:
  capture stale manifests with watched path and last decision/version marker

after successful rebuild:
  clear only manifests still matching the captured stale marker
```

Reasonable markers:

- `WatchedFilePath`
- `LastDecisionAtUtc`
- `LastStagedRecordId`

If a manifest became stale during the rebuild, or its decision marker changed, leave it stale and require another rebuild.

## Test Shape

- Unit test: capture stale manifest A, start simulated rebuild, mark manifest B or update A's decision marker, then clear by snapshot and assert only unchanged captured stale entries are cleared.
- Integration-adjacent test: a successful manual rebuild clears a stale flag that existed before rebuild start.
- Regression assertion: a stale flag set after the captured marker is not cleared by the earlier rebuild completion.

## Reframing (operator-reviewed, 2026-06-02)

Operator + Claude review: this is a **parallelism-allowance** issue, not a `MarkAllIndexesFresh` logic bug, and the
snapshot-marker fix above is a **symptom-patch, not the root fix**.

- **A single agent cannot hit it.** The post-accept rebuild is synchronous and blocks the agent
  (`RebuildAfterAcceptedDecision` → `RebuildAsync().GetAwaiter().GetResult()`), so it cannot issue a concurrent accept.
  The only trigger is the operator manually firing the App "rebuild index" button / `refresh_solution_index` (a different
  process) **while an agent is mid-accept** — off the single-active-agent path, a one-in-a-million operator action.
  **Practical risk: negligible.**
- **The lock cannot close it, by construction.** `AcquireManifestLock` is a cross-process file lock but **per-file and
  short-held** — it makes manifest writes atomic but does not serialize a rebuild against an accept. The failure is an
  **ordering** problem (the committed snapshot predates the accept), which a per-file write-lock cannot catch. So the
  defect is that rebuild ∥ accept is *allowed*, not the clear logic.

**Preferred resolution (in order):**

1. **Confirm-and-close.** Accept the single-active-agent contract and document that a manual/full rebuild must not
   overlap a decision. Consistent with the LIFECYCLE-5 recalibration and `docs/decisions/0002-safety-enforcement-philosophy.md`
   (single-writer-per-project is the intended mode; do not build machinery to tolerate a concurrency the model forbids).
2. **If a code fix is wanted, serialize — don't marker.** Hold a solution-wide rebuild lock for the whole rebuild +
   `MarkAllIndexesFresh` that the accept's stale-set also acquires, making the overlap structurally impossible. Then
   `MarkAllIndexesFresh`-clears-all stays correct and simple. Prefer this over snapshot-markers, which tolerate the
   parallelism rather than removing it.

See `docs/findings/SystemReview3-Verification-2026-06-02.md` for the full analysis.

