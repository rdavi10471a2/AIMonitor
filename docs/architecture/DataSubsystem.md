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
- `documents` records compile documents by project path.
- `project_references` records evaluated MSBuild project references.
- `package_references` records evaluated PackageReference items.
- `framework_references` records evaluated FrameworkReference items.
- `global_usings` records evaluated MSBuild `Using` items.
- `diagnostics` records MSBuild workspace diagnostics.

Future symbol, caller, reference, Razor, WinForms, and workflow tables should attach to `projects` and `documents` instead of inventing a second project identity source. Refresh history can be added later as telemetry, but it is not the primary navigation model.

## Semantic Provider Boundary

The database can store language-neutral project and document rows for any MSBuild-loaded project. Semantic rows should identify the provider that produced them when provider-specific tables are added.

Current implementation focus:

- MSBuild project/document/reference facts are the shared baseline.
- C# is the first semantic indexing provider.
- Non-C# documents should not disappear just because C# symbol indexing cannot interpret them yet.

This keeps the schema aligned with the monitor's real goal: navigate a watched solution by project truth first, then layer language-specific intelligence on top.
