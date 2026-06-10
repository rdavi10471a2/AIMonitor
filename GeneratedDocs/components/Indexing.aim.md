# AIMonitor.Indexing — component architecture (generated)

> Generated from the solution index via the AIMonitor MCP server.
> **rank 3** in the solution layering. Solution-wide view: [`../Architecture.aim.md`](../Architecture.aim.md).

## Index evidence (project-scoped)

- **Files:** 7
- **Symbols:** 47 (public: 38)
- **Named types:** 7 across 1 namespace(s); public API surface: 7 type(s) + 31 public member(s)

## Place in the layering

```mermaid
flowchart TB
  Indexing -->|6 refs| Core
  Indexing -->|29 refs| Data
  Indexing -->|18 refs| Logging
  Indexing -.->|declared only| MSBuild
  Indexing -->|62 refs| Workflow
  App -->|1 refs| Indexing
  Cli -->|4 refs| Indexing
  McpServer -->|7 refs| Indexing
```

### Depends on (outbound)

| Dependency | Live refs (index) | Verdict |
|---|---|---|
| Core | 6 | live (caller/index-verified) |
| Logging | 18 | live (caller/index-verified) |
| MSBuild | 0 | declared-only (no public ref — structural/transitive or candidate-dead) |
| Workflow | 62 | live (caller/index-verified) |
| Data | 29 | live (caller/index-verified) |

### Depended on by (inbound)

| Consumer | Live refs into this project | Verdict |
|---|---|---|
| App | 1 | live (caller/index-verified) |
| Cli | 4 | live (caller/index-verified) |
| McpServer | 7 | live (caller/index-verified) |

## Namespaces & types

- **AIMonitor.Indexing**
  - `IndexingBoundary`
  - `PostAcceptIndexRefreshPlan`
  - `PostAcceptIndexRefreshResult`
  - `PostAcceptIndexRefreshService`
  - `ReviewDecisionWithIndexRefreshResult`
  - `SolutionIndexRebuildService`
  - `StagedDecisionWorkflow`

## Public API surface (cross-project anchors)

7 public type(s) other projects can bind to:

- `IndexingBoundary`
- `PostAcceptIndexRefreshPlan`
- `PostAcceptIndexRefreshResult`
- `PostAcceptIndexRefreshService`
- `ReviewDecisionWithIndexRefreshResult`
- `SolutionIndexRebuildService`
- `StagedDecisionWorkflow`

## Internal coupling (public-surface, intra-project reference sites)

```mermaid
flowchart LR
  f_PostAcceptIndexRefreshService_cs["PostAcceptIndexRefreshService.cs"]
  f_PostAcceptIndexRefreshResult_cs["PostAcceptIndexRefreshResult.cs"]
  f_PostAcceptIndexRefreshService_cs -->|46| f_PostAcceptIndexRefreshResult_cs
  f_StagedDecisionWorkflow_cs["StagedDecisionWorkflow.cs"]
  f_ReviewDecisionWithIndexRefreshResult_cs["ReviewDecisionWithIndexRefreshResult.cs"]
  f_StagedDecisionWorkflow_cs -->|14| f_ReviewDecisionWithIndexRefreshResult_cs
  f_PostAcceptIndexRefreshPlan_cs["PostAcceptIndexRefreshPlan.cs"]
  f_PostAcceptIndexRefreshService_cs -->|9| f_PostAcceptIndexRefreshPlan_cs
  f_StagedDecisionWorkflow_cs -->|4| f_PostAcceptIndexRefreshPlan_cs
  f_StagedDecisionWorkflow_cs -->|4| f_PostAcceptIndexRefreshResult_cs
  f_StagedDecisionWorkflow_cs -->|4| f_PostAcceptIndexRefreshService_cs
  f_SolutionIndexRebuildService_cs["SolutionIndexRebuildService.cs"]
  f_PostAcceptIndexRefreshService_cs -->|3| f_SolutionIndexRebuildService_cs
  f_ReviewDecisionWithIndexRefreshResult_cs -->|1| f_PostAcceptIndexRefreshResult_cs
```

_8 intra-project edge(s). Edge `A --> B` = file A references a public symbol defined in file B._

## Caveats

- **Public-surface only:** coupling and internal edges are built from references whose *target* symbol is `Public`. Intra-project use through `internal`/`private` symbols is not probed, so the internal graph is a **partial** view (real cohesion is higher).
- **Reference-site semantics:** a file consumed only by assembly load / DI / config shows no edge despite a runtime relationship.
- **Self-index, not live-watch:** produced by indexing AIMonitor as a *target* solution. Regenerate-and-diff is stable (same index → byte-identical doc).

