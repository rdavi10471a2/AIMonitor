# Shared Adapter Surface

## Human Notes

The CLI and MCP server are adapters over the same monitor-owned data model. WinForms may call `.Data` directly for normal navigation, but it must also expose an adapter transcript view so CLI/MCP behavior can be inspected without attaching Claude.

## AI-Maintained Map

- `AIMonitor.Data` owns the read-only query surface through `SolutionIndexQueryService`.
- `AIMonitor.Data` owns the active SQLite query/store implementation today. `AIMonitor.Storage` is an existing boundary scaffold for durable monitor state and schema ownership, not a replacement adapter surface.
- `AIMonitor.Cli` exposes that query surface as JSON commands.
- `AIMonitor.App` exposes an Adapter Surface tab backed by the in-process `MonitorLogService`.
- `AIMonitor.App` hosts the named-pipe log ingress and owns runtime log serialization.
- The Monitor Status grid is a live telemetry view. It shows request/response phase, tool command, request id, workflow outcome, WinMerge review state, file hint, staged record id, duration, item count, and response preview.
- `accepted-normalized`, `dirty-unexpected`, `refresh-required`, `accepted`, and `rejected` outcomes are surfaced as first-class grid values instead of being buried only in JSON.
- Pre-merge validation is visible as `premerge.validation.completed` telemetry before `edit launch-diff` opens WinMerge.
- Post-accept index refresh is visible as `index.refresh-after-accept.started` and completed/failed telemetry after accepted decisions.
- CLI adapters send log events to the pipe and fall back to direct file writes only when the hub is not running. MCP logging should use the same pipe/fallback path when the scaffold becomes a real adapter.
- The CLI is a one-shot adapter. The WinForms hub is the long-running owner of pipe ingress, runtime log serialization, and the observer UI.
- `AIMonitor.McpServer` should later expose the same query service and DTOs, not a separate database model.

## Dataflow

```text
config/appsettings.json
  -> MonitorSettingsLoader
  -> MonitorSettings
  -> MonitorDataPaths
  -> SolutionIndexDatabase
  -> SolutionIndexStore
  -> SolutionIndexQueryService
  -> CLI JSON commands
  -> named-pipe log ingress
  -> WinForms MonitorLogService
  -> shared runtime log
  -> WinForms Adapter Surface
  -> future MCP tools
```

For adapter verification today, callers invoke the CLI executable. WinForms receives adapter log events over a named pipe, writes the shared runtime log, and displays the events as they arrive. The log file is durable output, not the primary communication channel. Future MCP verification should use the same telemetry path once MCP emits workflow events.

If the hub is not running, CLI adapters write a warning event directly to the runtime log with `hubRunning=false` and `logDelivery=direct-file-fallback`. The CLI also exposes `hub start` so the hub can be launched deliberately.

Starting WinForms starts the named-pipe log ingress automatically and emits `adapter.hub.started`.

## Invariants

- `Monitor:WatchedSolutionPath` remains the single watched-code identity.
- SQLite rows and DTOs remain visible files, not nested repository implementation details.
- CLI and future MCP commands must share `.Data` services before adding adapter-specific payload shaping.
- WinForms navigation can use in-process `.Data`, but adapter verification is observed through runtime log events sent by external adapter callers.

## Tests / Smokes

- `tests/integration/AIMonitor.Integration.Tests/CliIndexQueryTests.cs` seeds a SQLite index and verifies CLI JSON for status, symbol filtering, and references-in-file.
- TODO: add or keep a focused regression that sets a non-empty document `ContentHash` and asserts it is persisted and returned from `SolutionIndexStore`.
- Existing smoke tests continue to prove real watched-solution indexing.

## Known Risks

- Adapter events now include full response JSON in bounded command responses. Keep long-running or bulk payloads bounded before expanding this further.
- `accepted-normalized` is intentionally visible because it can mean the operator save changed line endings while preserving normalized text. Treat it as successful but worth reviewing when formatting stability matters.
- After an accepted or accepted-normalized workflow decision, the CLI reports `refresh-required` until the watched file is refreshed. This keeps later telemetry tied to the hashes and line endings actually saved by WinMerge/editor.
- Failed pre-merge validation should be treated as a human gate, not a warning. On an interactive Windows desktop the CLI displays a `Yes Launch`/`Cancel` override dialog before launching WinMerge, with `Yes`/`No` as a Win32 fallback if custom button text is unavailable. If no dialog is available, Codex/Claude must ask the user in chat and use `--force-validation` only after explicit approval.
