# AIMonitor.Workflow

## Purpose

Own the safe edit workflow: Working candidates, staging, ledgers, hash classification, and recovery states.

## Inputs

- Watched file paths.
- Working candidate edits.
- Operator decisions.
- Expected staged hashes.

## Outputs

- `EditSessionStatus`.
- `StagedEditRecord` and `StagedEditSummary`.
- `ReviewDecisionResult`.
- `PreMergeValidationResult`.
- Workflow ledgers and runtime review artifacts.

## Data Flow

```text
refresh_file/new_file
  -> Working candidate
  -> stage_candidate_for_review
  -> StagedEditRecord
  -> launch_staged_diff
  -> WinMerge review
  -> record_diff_decision
  -> accepted / accepted-normalized / rejected / dirty-unexpected
```

## Owns

- Monitor-owned Working files.
- Staged runtime files.
- The shared editable-session guard used by text, span, and Roslyn typed-edit surfaces.
- Pre-merge validation copy creation and `dotnet build` execution.
- Vote-plus-hash accept/reject classification.
- Terminal staged-record guards.
- Dirty/unexpected recovery signals.
- Line-ending-preserving text operations.
- Per-file index-stale workflow state after accepted decisions.
- Clearing stale workflow flags when Indexing reports a successful full rebuild.

## Does Not Own

- WinMerge process launch.
- Post-accept index rebuild implementation.
- Post-accept index refresh response shaping.
- Agent-specific command parsing.

## Key Tests

- `AIMonitor.Workflow.Tests`
- CLI/MCP workflow integration tests
