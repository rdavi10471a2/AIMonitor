# AIMonitor.Core

## Purpose

Shared settings, path identity, stable identifiers, and common monitor records.

## Inputs

- Repository root.
- `config/appsettings.json` or template-derived settings.
- Watched solution path.

## Outputs

- `MonitorSettings`.
- `WatchedSolutionInfo`.
- Runtime workspace path helpers.

## Data Flow

```text
config/appsettings.json
  -> MonitorSettingsLoader
  -> MonitorSettings
  -> Workflow / Data / MSBuild / Runtime / App / CLI / MCP
```

## Owns

- Configuration loading and saving.
- Common path identity primitives.
- Stable identifier helpers.

## Does Not Own

- SQLite storage.
- MSBuild workspace loading.
- Workflow decisions or staging.

## Key Tests

- `AIMonitor.Core.Tests`
