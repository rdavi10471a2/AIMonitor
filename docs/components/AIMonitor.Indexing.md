# AIMonitor.Indexing

## Purpose

Coordinate solution-index rebuilds, project-scoped refreshes, and post-decision index refresh responses.

## Inputs

- `MonitorSettings`.
- Accepted or accepted-normalized decision results.
- Optional post-accept refresh plan with affected owning MSBuild project paths.
- MSBuild loader and index store dependencies.
- Shared workflow decision records.

## Outputs

- `PostAcceptIndexRefreshResult`.
- `ReviewDecisionWithIndexRefreshResult`.
- Rebuilt monitor-owned solution index.
- Project-scoped replacement of selected index rows when a normal C# edit has known project ownership.
- Telemetry describing post-accept refresh status.
- Cleared workflow index-stale flags after successful accepted-decision refreshes.

## Data Flow

```text
record decision accepted
  -> StagedDecisionWorkflow
  -> PostAcceptIndexRefreshService
  -> full solution rebuild or project-scoped refresh
  -> SolutionIndexStore.SaveSnapshot or SolutionIndexStore.ReplaceProjects
  -> clear stale workflow flags after successful refresh
  -> indexRefresh result
```

## Owns

- Shared record-decision response orchestration for CLI/MCP adapters.
- Solution index rebuild composition.
- Project-scoped index refresh for safe normal C# edits with known owning projects.
- Post-accept refresh orchestration.
- Index refresh result shape.
- Successful rebuild recovery for stale workflow flags.

## Does Not Own

- Workflow classification.
- SQLite table implementation.
- Dependency-aware reverse-project refresh beyond the explicitly affected owning project set.
- WinMerge launch orchestration.

## Key Tests

- `AIMonitor.Indexing.Tests`
- Integration accept workflow tests
