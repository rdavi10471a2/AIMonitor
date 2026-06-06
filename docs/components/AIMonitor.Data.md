# AIMonitor.Data

## Purpose

Persist and query the monitor-owned solution index.

## Inputs

- `MSBuildSolutionSnapshot`.
- Monitor runtime/index database path.

## Outputs

- SQLite index tables.
- `SolutionIndexSummary`.
- Query rows for projects, documents, symbols, references, packages, analyzers, and usings.
- Symbol accessibility/modifier metadata (`public`/`private`/`protected`/`internal`, static, abstract, sealed, virtual, override) and method kind metadata for constructor/member grouping when populated by the semantic provider.

## Data Flow

```text
MSBuildSolutionSnapshot
  -> SolutionIndexBuilder
  -> SolutionIndexStore
  -> SolutionIndexDatabase
  -> SolutionIndexQueryService
  -> CLI / MCP / WinForms
```

## Owns

- SQLite schema and row mapping.
- Additive SQLite schema migration for existing runtime indexes; new symbol columns default to empty/false so old databases can still be opened, and rebuilds populate richer metadata.
- Read-only query surface.
- Full snapshot replacement semantics, including aborting a destructive replacement when a degraded zero-project snapshot would overwrite an existing populated index.

## Does Not Own

- MSBuild loading.
- Agent response shaping.
- Workflow staging or decisions.
- Index rebuild orchestration.

## Key Tests

- `AIMonitor.Data.Tests`
- CLI/MCP integration query tests
