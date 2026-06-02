# AIMonitor.Indexing

## Purpose

Coordinate index rebuilds after accepted workflow decisions.

## Inputs

- `MonitorSettings`.
- Accepted or accepted-normalized decision results.
- MSBuild loader and index store dependencies.

## Outputs

- `PostAcceptIndexRefreshResult`.
- Rebuilt monitor-owned solution index.
- Telemetry describing post-accept refresh status.

## Data Flow

```text
record decision accepted
  -> PostAcceptIndexRefreshService
  -> MSBuildWorkspaceLoader
  -> SolutionIndexStore.SaveSnapshot
  -> indexRefresh result
```

## Owns

- Post-accept rebuild orchestration.
- Index refresh result shape.

## Does Not Own

- Workflow classification.
- SQLite table implementation.
- Incremental/dependency-aware rebuilds.

## Key Tests

- `AIMonitor.Indexing.Tests`
- Integration accept workflow tests
