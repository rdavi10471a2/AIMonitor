# AIMonitor.App — component architecture (generated)

> Generated from the solution index via the AIMonitor MCP server.
> **rank 4** in the solution layering. Solution-wide view: [`../Architecture.aim.md`](../Architecture.aim.md).

## Index evidence (project-scoped)

- **Files:** 11
- **Symbols:** 416 (public: 117)
- **Named types:** 35 across 2 namespace(s); public API surface: 10 type(s) + 107 public member(s)

## Place in the layering

```mermaid
flowchart TB
  App -->|11 refs| Core
  App -->|87 refs| Data
  App -->|1 refs| Indexing
  App -->|75 refs| Logging
  App -.->|declared only| MSBuild
  App -.->|declared only| Runtime
  App -.->|declared only| Workflow
```

### Depends on (outbound)

| Dependency | Live refs (index) | Verdict |
|---|---|---|
| Core | 11 | live (caller/index-verified) |
| Logging | 75 | live (caller/index-verified) |
| MSBuild | 0 | declared-only (no public ref — structural/transitive or candidate-dead) |
| Workflow | 0 | declared-only (no public ref — structural/transitive or candidate-dead) |
| Data | 87 | live (caller/index-verified) |
| Runtime | 0 | declared-only (no public ref — structural/transitive or candidate-dead) |
| Indexing | 1 | live (caller/index-verified) |

### Depended on by (inbound)

_None — top of the stack (no src project references this one)._

## Namespaces & types

- **AIMonitor.App**
  - `AppStartupOptions`
  - `Form1`
  - `HubHandshake` _private_
  - `McpProxyHubService`
  - `MessageMetadata` _private_
  - `PendingRequest` _private_
  - `Program` _internal_
- **AIMonitor.App.Controls**
  - `AdapterEventDetail` _private_
  - `AdapterEventView` _private_
  - `AdapterSurfaceControl`
  - `AppPathResolver` _internal_
  - `DependencyFolderNodeTag` _private_
  - `DetailProperty` _private_
  - `DetailPropertyBag` _private_
  - `DetailPropertyDescriptor` _private_
  - `DocumentNodeTag` _private_
  - `DocumentView` _private_
  - `FileOverviewControl`
  - `FilePropertyBag` _private_
  - `FolderNodeTag` _private_
  - `MonitorDashboardControl`
  - `PackageNodeTag` _private_
  - `ProjectNodeTag` _private_
  - `ProjectView` _private_
  - `ReferenceView`
  - `ReferenceView` _private_
  - `SelectionDetailsControl`
  - `SharedLogControl`
  - `SolutionIndexControl`
  - `SolutionNodeTag` _private_
  - `SourceReferenceGrouping` _private_
  - `SourceReferenceNodeTag` _private_
  - `SymbolNodeTag` _private_
  - `SymbolTreeTag` _private_
  - `SymbolView` _private_

## Public API surface (cross-project anchors)

10 public type(s) other projects can bind to:

- `AdapterSurfaceControl`
- `AppStartupOptions`
- `FileOverviewControl`
- `Form1`
- `McpProxyHubService`
- `MonitorDashboardControl`
- `ReferenceView`
- `SelectionDetailsControl`
- `SharedLogControl`
- `SolutionIndexControl`

## Internal coupling (public-surface, intra-project reference sites)

```mermaid
flowchart LR
  f_MonitorDashboardControl_cs["MonitorDashboardControl.cs"]
  f_McpProxyHubService_cs["McpProxyHubService.cs"]
  f_MonitorDashboardControl_cs -->|7| f_McpProxyHubService_cs
  f_SolutionIndexControl_cs["SolutionIndexControl.cs"]
  f_MonitorDashboardControl_cs -->|5| f_SolutionIndexControl_cs
  f_FileOverviewControl_cs["FileOverviewControl.cs"]
  f_SolutionIndexControl_cs -->|5| f_FileOverviewControl_cs
  f_AdapterSurfaceControl_cs["AdapterSurfaceControl.cs"]
  f_MonitorDashboardControl_cs -->|4| f_AdapterSurfaceControl_cs
  f_Program_cs["Program.cs"]
  f_AppStartupOptions_cs["AppStartupOptions.cs"]
  f_Program_cs -->|3| f_AppStartupOptions_cs
  f_AppPathResolver_cs["AppPathResolver.cs"]
  f_SolutionIndexControl_cs -->|3| f_AppPathResolver_cs
  f_Form1_cs["Form1.cs"]
  f_Form1_cs -->|1| f_AppStartupOptions_cs
  f_Form1_cs -->|1| f_MonitorDashboardControl_cs
  f_MonitorDashboardControl_cs -->|1| f_AppStartupOptions_cs
  f_MonitorDashboardControl_cs -->|1| f_AppPathResolver_cs
  f_Program_cs -->|1| f_Form1_cs
```

_11 intra-project edge(s). Edge `A --> B` = file A references a public symbol defined in file B._

## Caveats

- **Public-surface only:** coupling and internal edges are built from references whose *target* symbol is `Public`. Intra-project use through `internal`/`private` symbols is not probed, so the internal graph is a **partial** view (real cohesion is higher).
- **Reference-site semantics:** a file consumed only by assembly load / DI / config shows no edge despite a runtime relationship.
- **Self-index, not live-watch:** produced by indexing AIMonitor as a *target* solution. Regenerate-and-diff is stable (same index → byte-identical doc).

