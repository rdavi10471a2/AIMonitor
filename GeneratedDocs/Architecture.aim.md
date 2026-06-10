# AIMonitor Architecture (generated)

> Generated from the solution index via the AIMonitor MCP server.
> **Skeleton** = `source-verified` (csproj ProjectReference, compiler-enforced). **Coupling weights** = `caller/index-verified` (live cross-project reference sites from the index).
>
> Per-project internal views: [`components/`](components/README.md).

## Index evidence

```json
{"repositoryRoot":"C:\\VSCodeProjects\\aimonitor-selfdoc","runtimeRoot":"C:\\VSCodeProjects\\aimonitor-selfdoc\\runtime-selfdoc","watchedSolutionPath":"C:\\VSCodeProjects\\AIMonitor\\AIMonitor.slnx","watchedProjectFolder":"C:\\VSCodeProjects\\AIMonitor","databasePath":"C:\\VSCodeProjects\\aimonitor-selfdoc\\runtime-selfdoc\\watched-solutions\\AIMonitor-7b45c38d3961\\data\\solution-index.sqlite","databaseExists":true,"projectCount":22,"documentCount":134,"symbolCount":2056,"referenceCount":8476,"callSiteCount":3808,"relationshipCount":7,"staleFileCount":0,"diagnosticCount":0}
```

## Layered architecture (foundation at bottom)

```mermaid
flowchart TB
  Logging -->|3 refs| Core
  MSBuild -->|20 refs| Core
  Workflow -->|28 refs| Core
  Data -->|12 refs| Core
  Data -->|48 refs| MSBuild
  McpStdioBridge -->|3 refs| Core
  McpStdioBridge -->|2 refs| Logging
  Runtime -->|1 refs| Core
  Runtime -->|6 refs| Logging
  Runtime -->|54 refs| Workflow
  Indexing -->|6 refs| Core
  Indexing -->|29 refs| Data
  Indexing -->|18 refs| Logging
  Indexing -.->|declared only| MSBuild
  Indexing -->|62 refs| Workflow
  App -->|11 refs| Core
  App -->|87 refs| Data
  App -->|1 refs| Indexing
  App -->|75 refs| Logging
  App -.->|declared only| MSBuild
  App -.->|declared only| Runtime
  App -.->|declared only| Workflow
  Cli -->|13 refs| Core
  Cli -->|19 refs| Data
  Cli -->|4 refs| Indexing
  Cli -->|35 refs| Logging
  Cli -.->|declared only| MSBuild
  Cli -->|1 refs| Runtime
  Cli -->|32 refs| Workflow
  McpServer -->|19 refs| Core
  McpServer -->|50 refs| Data
  McpServer -->|7 refs| Indexing
  McpServer -->|14 refs| Logging
  McpServer -.->|declared only| MSBuild
  McpServer -->|8 refs| Runtime
  McpServer -->|158 refs| Workflow
```

## Layers (topological rank from the csproj DAG)

- **rank 0** — Core
- **rank 1** — Logging, MSBuild, Workflow
- **rank 2** — Data, McpStdioBridge, Runtime
- **rank 3** — Indexing
- **rank 4** — App, Cli, McpServer

## Edge evidence (declared vs. live coupling)

| Consumer | Dependency | Declared (csproj) | Live refs (index) | Verdict |
|---|---|---|---|---|
| Logging | Core | yes | 3 | live (caller/index-verified) |
| MSBuild | Core | yes | 20 | live (caller/index-verified) |
| Workflow | Core | yes | 28 | live (caller/index-verified) |
| Data | Core | yes | 12 | live (caller/index-verified) |
| Data | MSBuild | yes | 48 | live (caller/index-verified) |
| McpStdioBridge | Core | yes | 3 | live (caller/index-verified) |
| McpStdioBridge | Logging | yes | 2 | live (caller/index-verified) |
| Runtime | Core | yes | 1 | live (caller/index-verified) |
| Runtime | Logging | yes | 6 | live (caller/index-verified) |
| Runtime | Workflow | yes | 54 | live (caller/index-verified) |
| Indexing | Core | yes | 6 | live (caller/index-verified) |
| Indexing | Data | yes | 29 | live (caller/index-verified) |
| Indexing | Logging | yes | 18 | live (caller/index-verified) |
| Indexing | MSBuild | yes | 0 | declared-only (no public ref — structural/transitive or candidate-dead) |
| Indexing | Workflow | yes | 62 | live (caller/index-verified) |
| App | Core | yes | 11 | live (caller/index-verified) |
| App | Data | yes | 87 | live (caller/index-verified) |
| App | Indexing | yes | 1 | live (caller/index-verified) |
| App | Logging | yes | 75 | live (caller/index-verified) |
| App | MSBuild | yes | 0 | declared-only (no public ref — structural/transitive or candidate-dead) |
| App | Runtime | yes | 0 | declared-only (no public ref — structural/transitive or candidate-dead) |
| App | Workflow | yes | 0 | declared-only (no public ref — structural/transitive or candidate-dead) |
| Cli | Core | yes | 13 | live (caller/index-verified) |
| Cli | Data | yes | 19 | live (caller/index-verified) |
| Cli | Indexing | yes | 4 | live (caller/index-verified) |
| Cli | Logging | yes | 35 | live (caller/index-verified) |
| Cli | MSBuild | yes | 0 | declared-only (no public ref — structural/transitive or candidate-dead) |
| Cli | Runtime | yes | 1 | live (caller/index-verified) |
| Cli | Workflow | yes | 32 | live (caller/index-verified) |
| McpServer | Core | yes | 19 | live (caller/index-verified) |
| McpServer | Data | yes | 50 | live (caller/index-verified) |
| McpServer | Indexing | yes | 7 | live (caller/index-verified) |
| McpServer | Logging | yes | 14 | live (caller/index-verified) |
| McpServer | MSBuild | yes | 0 | declared-only (no public ref — structural/transitive or candidate-dead) |
| McpServer | Runtime | yes | 8 | live (caller/index-verified) |
| McpServer | Workflow | yes | 158 | live (caller/index-verified) |

## Findings

- **6 declared ProjectReferences carry zero public cross-project references** — candidate-dead, or used only structurally/transitively (assembly-load-only, DI registration, `MSBuildLocator` init). Verify before removal: Indexing → MSBuild; App → MSBuild; App → Runtime; App → Workflow; Cli → MSBuild; McpServer → MSBuild.
- **relationshipCount = 7.** AIMonitor barely uses inheritance/interface relationships across the indexed surface — it is composition-over-inheritance; the architecture lives in the *reference* graph, not a type hierarchy.
- **Heaviest coupling** is McpServer→Workflow, App→Data, App→Logging — the orchestration/entry tier leans hardest on Workflow + Data.

## Layered architecture — experimental (centrality-derived)

> **Experimental, shown beside the topological view above — not a replacement.** This layering ranks by **weighted PageRank** over the consumer→dependency graph (edge weight = live ref count) — *how much depends on you* (afferent centrality), per the layout-derivation research. Foundation = highest PageRank, rendered at the bottom. Bands are cut at the largest score gaps (data-driven, no fixed thresholds). **Class** is by edge direction (supplier vs. driver). Tier *names* are deliberately omitted: the research confirms naming/role is maintained intent, not graph-derived — only derived position and class are shown.

```mermaid
flowchart TB
  subgraph tier0["PageRank 0.12–0.12"]
    direction LR
    App
    Cli
    Indexing
    McpServer
    McpStdioBridge
    Runtime
  end
  subgraph tier1["PageRank 0.35–0.23"]
    direction LR
    Data
    Logging
    MSBuild
    Workflow
  end
  subgraph tier2["PageRank 1.00–1.00"]
    direction LR
    Core
  end
  tier0 ~~~ tier1
  tier1 ~~~ tier2
```

## Layering comparison — topological depth vs. centrality

| Project | Depth rank (current) | Weighted PageRank (norm) | Inbound refs | Fan-in / Fan-out | Edge-direction class |
|---|---|---|---|---|---|
| Core | 0 | 1.000 | 116 | 10 / 0 | pure foundation (sink — depended-upon only) |
| Workflow | 1 | 0.353 | 306 | 5 / 1 | supplier-leaning (more depended-upon) |
| MSBuild | 1 | 0.273 | 48 | 5 / 1 | supplier-leaning (more depended-upon) |
| Logging | 1 | 0.261 | 150 | 6 / 1 | supplier-leaning (more depended-upon) |
| Data | 2 | 0.227 | 185 | 4 / 2 | supplier-leaning (more depended-upon) |
| Indexing | 3 | 0.123 | 12 | 3 / 5 | consumer-leaning (depends outward) |
| Runtime | 2 | 0.120 | 9 | 3 / 3 | supplier-leaning (more depended-upon) |
| App | 4 | 0.116 | 0 | 0 / 7 | driver / entry (leaf consumer) |
| Cli | 4 | 0.116 | 0 | 0 / 7 | driver / entry (leaf consumer) |
| McpServer | 4 | 0.116 | 0 | 0 / 7 | driver / entry (leaf consumer) |
| McpStdioBridge | 2 | 0.116 | 0 | 0 / 2 | driver / entry (leaf consumer) |

## Evidence & caveats

- **Skeleton** (the DAG + ranks): `source-verified` — csproj `ProjectReference`, compiler-enforced; the build's acyclicity *proves* a valid layering exists.
- **Coupling weights**: `caller/index-verified` — live cross-project reference sites pulled from the self-index via the AIMonitor MCP server DLL.
- **Public-surface only:** coupling counts references whose *target* is a `Public` symbol. Cross-project use via `internal` + `InternalsVisibleTo` is **not** counted, so a `0` is *candidate*-dead, not proven-dead.
- **Reference-site semantics:** a dependency used only by assembly load / DI registration / config (no symbol reference) also shows `0` despite being needed at runtime.
- **Polarity:** rendered foundation-at-bottom (`flowchart TB`; `Core` is a sink). Tier *names* are not graph-derived — they are maintained intent.
- **Self-index, not live-watch:** AIMonitor cannot live-watch itself; it was indexed as a *target* solution to produce this. Regenerate-and-diff is stable (same index → byte-identical doc).

