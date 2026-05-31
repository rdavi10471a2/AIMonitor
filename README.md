# AIMonitor

AIMonitor is the V2 safe edit monitor for AI-assisted .NET development.

The goal is to keep the proven Monitor workflow while starting from a cleaner architecture:

- MSBuild-first project loading.
- One shared workflow engine for Claude and Codex.
- MCP as the Claude adapter, not the core API.
- CLI as the Codex-friendly adapter.
- Tests, samples, and docs as top-level peers of product source.
- First-class watched-project support for Blazor/Razor, WinForms, and console apps.
- A SQLite solution index under each watched solution workspace built from one configured watched solution path.
- Unified JSON-lines logging under `runtime/logs`.

## Initial Scope

Primary targets:

- Blazor/Razor component projects.
- WinForms projects.
- Console projects.

Expected to work through normal MSBuild loading when SDKs are installed:

- ASP.NET/Web API C# project loading and normal C# indexing.

Not a V2 focus:

- ASP.NET routing, middleware, auth-policy, hosting, deployment, or OpenAPI-specific semantic workflows.

## Project Layout

```text
src/       product code and adapters
tests/     unit, integration, smoke, and fixtures
samples/   human-readable watched-solution examples
docs/      architecture, decisions, findings, workflows, feature maps
config/    templates only; local config is ignored
runtime/   generated monitor state; ignored
```

## Configuration Rule

`Monitor:WatchedSolutionPath` is the single authoritative path for the watched solution. MSBuild loading, indexing, MCP, CLI, and the app host should all flow through that setting.

Generated monitor logs go under `runtime/logs/aimonitor.ndjson`. Adapters may print human status to their console/UI, but durable operational events should use the shared logger.

The operator app owns the shared logging service. Child controls and subsystems receive an `IMonitorLogger` and send log messages to it; they should not create their own UI log panes or long-lived file handles. The app log view listens to in-process log events while the service writes JSON-lines entries with shared file access.

Generated solution-specific state goes under `runtime/watched-solutions/<solution-name>-<path-hash>/`.

## Build

```powershell
dotnet build .\AIMonitor.slnx
```

## Test

```powershell
dotnet test .\AIMonitor.slnx
```

## Language Corpus Smoke

The old MonitorBaseClaude external corpus now lives under `tests/smoke/AIMonitor.LanguageCorpusSmokeTests`. It runs in report mode by default while V2 grows the C# semantic provider:

```powershell
dotnet run --project .\tests\smoke\AIMonitor.LanguageCorpusSmokeTests
```

Use `--assert` when the corpus is ready to become a hard gate.

## Rebuild Index

Create `config/appsettings.json` from `config/appsettings.template.json`, set `Monitor:WatchedSolutionPath`, then run:

```powershell
dotnet run --project .\src\AIMonitor.Cli -- index rebuild
```

## Architecture Rule

The safe edit workflow is not MCP. MCP is one adapter for Claude. Codex gets CLI/process-friendly access to the same engine.

```text
Claude -> MCP adapter -> shared workflow engine
Codex  -> CLI adapter -> shared workflow engine
App    -> host/UI     -> shared workflow engine
```
