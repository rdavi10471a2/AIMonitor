# AIMonitor.Indexing

## Purpose

Coordinate solution-index rebuilds and post-decision index refresh responses.

## Inputs

- `MonitorSettings`.
- Accepted or accepted-normalized decision results.
- MSBuild loader and index store dependencies.
- Shared workflow decision records.

## Outputs

- `PostAcceptIndexRefreshResult`.
- `ReviewDecisionWithIndexRefreshResult`.
- Rebuilt monitor-owned solution index.
- Telemetry describing post-accept refresh status.

## Data Flow

```text
record decision accepted
  -> StagedDecisionWorkflow
  -> PostAcceptIndexRefreshService
  -> SolutionIndexRebuildService
  -> SolutionIndexStore.SaveSnapshot
  -> indexRefresh result
```

## Owns

- Shared record-decision response orchestration for CLI/MCP adapters.
- Solution index rebuild composition.
- Post-accept rebuild orchestration.
- Index refresh result shape.

## Does Not Own

- Workflow classification.
- SQLite table implementation.
- Incremental/dependency-aware rebuilds.
- WinMerge launch orchestration.

## Key Tests

- `AIMonitor.Indexing.Tests`
- Integration accept workflow tests
