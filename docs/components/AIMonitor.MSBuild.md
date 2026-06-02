# AIMonitor.MSBuild

## Purpose

Load watched .NET projects through MSBuild and expose project/document truth to indexing and validation workflows.

## Inputs

- `MonitorSettings.WatchedSolutionPath`.
- SDK-style projects and solutions.
- MSBuild-evaluated project items and references.

## Outputs

- `MSBuildSolutionSnapshot`.
- Project, document, symbol, reference, package, analyzer, and using snapshots.
- Conservative Razor/source-map facts when mappings are reliable.

## Data Flow

```text
WatchedSolutionPath
  -> MSBuildWorkspaceLoader
  -> MSBuildSolutionSnapshot
  -> SolutionIndexBuilder / SolutionIndexStore
```

## Owns

- MSBuild-first project loading.
- Project/document identity.
- C# provider and reliable Razor mapping extraction.

## Does Not Own

- SQLite persistence.
- Post-accept orchestration.
- WinMerge or runtime process launch.

## Key Tests

- `AIMonitor.MSBuild.Tests`
- Language corpus smoke tests
