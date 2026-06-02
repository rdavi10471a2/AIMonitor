# Session Validation

Use for coupled multi-file C# edits.

## Rule

Compose and stage every coupled file in the same monitor session before the first review launch. The current implementation validates and reviews one staged candidate at a time, so the session is the operator's grouping and telemetry boundary. First-class all-files-at-once overlay validation is future work.

## Flow

```text
start_monitor_session
compose Working candidate A with sessionId
stage_candidate_for_review for file A with sessionId
compose Working candidate B with sessionId
stage_candidate_for_review for file B with sessionId
review pre-merge validation results
launch/review file A
record decision for file A
launch/review file B
record decision for file B
check each accepted decision's indexRefresh status before relying on solution-index queries
```

## Do Not

- Do not review file A before composing and staging coupled file B.
- Do not treat a clean single-file validation result as enough when another staged file is required for the feature to compile.
- Do not let empty reference results shrink the session by themselves; cross-check before deciding a change is single-file.
- Do not continue to later diffs if an earlier staged item is blocked by validation or review-gate state.
- Do not run manual index refresh tools after each accepted file in a coupled chain; `record_diff_decision` refreshes the monitor-owned index for accepted and accepted-normalized decisions.

## Unblock

- Stage a corrected candidate for the same blocked file.
- If diagnostics identify a missed consumer/call site, add that file to the same monitor session and stage it before retrying review.
- Or explicitly force-review the blocked item through the Host UI.
- Or abandon the chain and start a new monitor session for unrelated work.
