---
status: new
type: finding
created: 2026-06-01
scope: MCP/CLI response payload size vs debuggability — persist-verbose, return-compact
confidence: high (design recommendation grounded in existing telemetry/ledger surfaces)
related: SafeEditWorkflowEvaluation-2026-06-01.md (priority #3), WorkflowCostAndIncrementalRebuild-2026-06-01.md
---

## Summary

Today the workflow adapters echo the **entire `StagedEditRecord`** (all paths, both hashes, ledger path, compare ids,
launch status, messages) on every `stage_candidate_for_review`, `launch_staged_diff`, and `record_diff_decision` reply —
a one-file edit cycle re-sends that object 3+ times. That is the steady-state token leak called out in the workflow
evaluation. The fix does not trade away debuggability: the verbose history **already exists durably elsewhere**, so the
reply can shrink to a pointer.

**Principle: write more than we return.** Persist full fidelity; return a compact envelope plus an id that resolves back
to the full record on demand.

## Why debuggability is not lost

The full detail is already persisted and addressable, independent of the MCP reply:

- the WinForms **Monitor Status grid** records full request/response telemetry (live debugging);
- every stage/launch/accept writes the full record to `runtime/.../workflow/staged/`, `.../history/`, and a per-file
  **ledger** (`.../history/Ledgers/*.md`);
- each reply already carries `stagedRecordId`, which maps 1:1 to all of the above.

So trimming the inline echo removes redundancy, not information. Debugging becomes "look up the id," not "scroll the
transcript."

## Recommendation

1. **Compact default envelope** for stage/launch/record replies:
   ```
   { stagedRecordId, status, classification, stagedHash, launchStatus, recordPath, correlationId }
   ```
   Drop the repeated workingFilePath/stagedFilePath/reviewBaselineFilePath/compare ids/long messages from the default.

2. **`verbose: true` opt-in** per tool — when a hard debug session genuinely needs the full record inline, request it
   explicitly. Default compact, escalate on demand.

3. **Fetch-back tools** so trimmed detail is one call away when wanted:
   `get_staged_record(stagedRecordId)`, `get_ledger(...)`, `list_session_staged_records(...)` (the latter two already
   exist). This makes "compact by default" safe — the detail is always retrievable by id.

4. **Correlation id** threaded request -> telemetry -> ledger so a compact reply maps deterministically to its grid/log
   entry. `stagedRecordId` already mostly serves this; formalize it.

## Why this serves both project goals

- **Token minimization:** the per-cycle payload stops re-sending a large object 3+ times — directly the harness's stated
  goal, and the steady-state cost (every edit pays it), not a one-off.
- **Code safety / auditability:** unchanged. Full records still persist to disk + grid; the hash and decision trail are
  intact and addressable by id. Arguably better, because debugging reads the authoritative on-disk record rather than a
  transcript echo that could drift.

## Scope / notes

- This is a shared-adapter concern: implement the compact shape in the shared workflow result types so **both** CLI and
  MCP inherit it (consistent with "thin adapters over shared code"), not per-adapter.
- Pairs with the response-trimming item in SafeEditWorkflowEvaluation-2026-06-01.md (priority #3).
- Known risk already noted in the tool manifest: durable adapter logs persist response shape/redacted previews, not full
  payloads — keep large/snippet-bearing payloads in the addressable record, not in every reply.
