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

