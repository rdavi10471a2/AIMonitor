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

The first schema is deliberately small:

- `index_runs` records each rebuild.
- `indexed_solutions` records the input solution for the run.
- `indexed_projects` records MSBuild project identity, language, path, and preprocessor symbols.
- `indexed_documents` records source files by project path.
- `index_diagnostics` records MSBuild workspace diagnostics.

Future symbol, Razor, WinForms, and workflow tables should attach to an index run instead of inventing a second project identity source.
