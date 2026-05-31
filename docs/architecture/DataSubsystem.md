# Data Subsystem

The data subsystem owns the SQLite index created from the watched solution.

## Single Path Rule

`Monitor:WatchedSolutionPath` is the only configured watched-code identity. Roslyn, MSBuild loading, indexing, MCP tools, CLI commands, and the WinForms host must all receive the watched solution through the same `MonitorSettings` value.

Do not add separate settings such as `RoslynSolutionPath`, `IndexSolutionPath`, or `McpSolutionPath`. If a subsystem needs the watched solution, pass `MonitorSettings.WatchedSolutionPath`.

## Database Location

The default index database is:

```text
runtime/watched-solutions/<solution-name>-<path-hash>/data/solution-index.sqlite
```

The path is derived from `MonitorSettings.RuntimeRoot` and `MonitorSettings.WatchedSolutionPath`, not from the watched project folder directly. Generated monitor state stays with AIMonitor, but each watched solution gets its own monitor-owned workspace folder.

The solution folder name includes a short hash of the full solution path so two different watched solutions named `App.sln` do not collide.

## Current Schema

The first schema stores the current MSBuild solution model, not a history of index runs:

- `solution_state` records the currently indexed solution path, refresh time, and counts.
- `projects` records MSBuild project identity, target framework data, output type, SDK, assembly/root namespace, nullable/implicit using settings, language version, and preprocessor symbols.
- `documents` records user-facing source documents by project path and SHA-256 content hash. This includes normal C# compile documents plus Razor documents that can be mapped back to user-authored `.razor` or legacy hybrid `.razor.cs` files.
- `symbols` records C# provider symbols with stable source keys.
- `symbol_references` records C# provider reference sites that resolve to indexed source symbols.
- `project_references` records evaluated MSBuild project references.
- `package_references` records evaluated PackageReference items.
- `framework_references` records evaluated FrameworkReference items.
- `global_usings` records evaluated MSBuild `Using` items.
- `diagnostics` records MSBuild workspace diagnostics.

Future caller, richer relationship, WinForms, and workflow tables should attach to `projects` and `documents` instead of inventing a second project identity source. Refresh history can be added later as telemetry, but it is not the primary navigation model.

## Semantic Provider Boundary

The database can store language-neutral project and document rows for any MSBuild-loaded project. Semantic rows should identify the provider that produced them when provider-specific tables are added.

Current implementation focus:

- MSBuild project/document/reference facts are the shared baseline.
- C# is the first semantic indexing provider.
- Razor is handled through generated C# plus Razor source mappings. Only mapped spans that point back to real user source are indexed or shown.
- Clean `.razor.cs` files remain normal C# code-behind. Legacy combined `.razor.cs` files that contain Razor markup or `@code` are treated as Razor input for indexing.
- Non-C# documents should not disappear just because C# symbol indexing cannot interpret them yet.

Razor rows should be treated as reliable facts only when they come from source-mapped generated C# or normal C# code-behind. Do not infer a complete Razor UI binding graph from markup text alone. If future MCP tools need component/event-binding precision, add a separate provider and schema instead of overloading `symbol_references` with guesses.

This keeps the schema aligned with the monitor's real goal: navigate a watched solution by project truth first, then layer language-specific intelligence on top.

## Shared Query Surface

`SolutionIndexQueryService` is the shared read-only adapter surface for the current index. CLI commands, MCP tools, and WinForms adapter verification should use this service instead of each adapter reaching into SQLite differently.

Document query rows include `contentHash`, the SHA-256 hash of the user-facing file bytes at the time the index was rebuilt. This is the shared file identity fact that adapters can use for stale-index checks, diff safety checks, and MCP-style file identity responses.

The first supported queries are:

- monitor status and configured database path
- index summary
- projects
- documents with optional project/file filters
- symbols with optional file/name filters
- references by target stable key
- references in a selected file
- package references

WinForms may use `SolutionIndexStore` or `SolutionIndexQueryService` directly for fast local navigation, but the Adapter Surface tab observes adapter events received by the WinForms-hosted log pipe. CLI and MCP callers remain the actors; WinForms owns log serialization and writes the shared runtime log.
