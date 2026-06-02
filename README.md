# AIMonitor

AIMonitor is a safe edit monitor for AI-assisted .NET development. It gives agents a protected way to inspect, stage, validate, diff, and record edits to a watched solution without letting them silently mutate watched source.

It is also a harness around agent work: it gives the agent repeatable tools, memory, validation gates, telemetry, and a human review loop. The important distinction is that the harness is bounded by local project truth and shared workflow services instead of becoming a loose prompt/process convention.

The core idea is simple:

```text
discover -> edit monitor-owned Working files -> stage -> validate -> WinMerge review -> record accept/reject -> refresh index
```

Claude, Codex, and the WinForms app all use the same workflow services. MCP and CLI are adapters, not separate workflow implementations.

## Layered Workflow

```text
Claude Code -> MCP stdio bridge -> WinForms MCP proxy hub -> MCP server -> shared services
Codex      -> CLI adapter                              -> shared services
Operator   -> WinForms app                             -> shared services
```

Shared services own the behavior:

- `AIMonitor.Core`: settings, path identity, and common records.
- `AIMonitor.MSBuild`: MSBuild-loaded solution/project truth.
- `AIMonitor.Data`: SQLite solution index storage and query surface.
- `AIMonitor.Indexing`: post-accept index refresh orchestration.
- `AIMonitor.Workflow`: Working files, staging, ledgers, decisions, hashes, and recovery rules.
- `AIMonitor.Runtime`: validation, WinMerge launch, process/runtime boundaries.
- `AIMonitor.Logging`: shared JSON-lines logging and live log pipe.

Adapters stay thin:

- `AIMonitor.Cli`: Codex-friendly command surface.
- `AIMonitor.McpServer`: Claude-facing MCP tools over shared services.
- `AIMonitor.McpStdioBridge`: stdio-to-WinForms proxy bridge for live Claude sessions.
- `AIMonitor.App`: operator UI, live MCP proxy hub, solution index view, and monitor status view.

## Safe Edit Rules

- Agents do not edit watched source directly.
- Existing watched files are refreshed into monitor-owned Working candidates.
- Future watched files use `new_file` / `edit new` and are reviewed against a blank runtime baseline.
- Candidates are staged before review.
- `launch_staged_diff` / `edit launch-diff` runs pre-merge validation before WinMerge.
- WinMerge is the human review/save surface.
- `record_diff_decision` / `edit record-decision` classifies the watched result by decision plus staged hash.
- Accepted and accepted-normalized decisions refresh the monitor-owned solution index.
- After accept, refresh the same watched file before editing it again.

CSS, JSON, config, markup, Razor markup, and other non-C# text assets are diffable through the same protected Working/stage/review/decision flow. They do not need semantic index rows.

## Semantic Scope

AIMonitor is MSBuild-first and language-provider aware. The current semantic provider is C#:

- normal `.cs` files are indexed as C#;
- clean `.razor.cs` code-behind is indexed as C#;
- user-authored `.razor` references are indexed only when compiler/Razor source mappings expose user-source spans cleanly.

AIMonitor does not currently promise full Visual Studio-level Razor component/event binding analysis. Use builds, representative smoke tests, source-map facts, and targeted text search as the practical safety net for those cases.

## Lineage And Credit

AIMonitor builds on prior work in safe-edit monitors, Roslyn/MCP tool surfaces, and compiler-library-style corpus testing. Those references shaped the source-map workflow, semantic edit vocabulary, and known-answer smoke corpus. The implementation here keeps those ideas behind AIMonitor's own layered workflow: shared services first, MCP/CLI/UI as adapters, and human-reviewed staged diffs as the watched-source gate.

## Project Layout

```text
src/       product code and adapters
tests/     unit, integration, smoke, fixtures, and corpus coverage
samples/   committed sample notes; local watched samples are ignored
docs/      architecture, component memory, workflows, findings, setup, skills
config/    templates only; local config is ignored
runtime/   generated monitor state; ignored
```

Root instruction files:

- `AGENTS.md`: Codex host instructions.
- `CLAUDE.md`: Claude / Claude Code host instructions.
- `docs/system-memory/README.md`: authoritative contract memory for how AIMonitor works.
- `docs/agent-memory/RestartContext.md`: restart and handoff note for agent/plugin/MCP recovery.
- `docs/components/`: per-component purpose and data-flow memory.
- `docs/claude-skills/`: focused Claude skill cards.

## Configuration

Create `config/appsettings.json` from `config/appsettings.template.json` and set:

```json
{
  "Monitor": {
    "WatchedSolutionPath": "C:\\path\\to\\watched\\solution.sln"
  }
}
```

`Monitor:WatchedSolutionPath` is the single authoritative watched-code identity. MSBuild loading, indexing, MCP tools, CLI commands, and the WinForms app all flow through that setting.

Generated state is isolated under:

```text
runtime/watched-solutions/<solution-name>-<path-hash>/
```

Durable monitor events are written to:

```text
runtime/logs/aimonitor.ndjson
```

## Build

```powershell
dotnet build .\AIMonitor.slnx
```

## Test

```powershell
dotnet test .\AIMonitor.slnx
```

Useful focused checks:

```powershell
dotnet test .\tests\integration\AIMonitor.Integration.Tests\AIMonitor.Integration.Tests.csproj
dotnet run --project .\tests\smoke\AIMonitor.SmokeTests
dotnet run --project .\tests\smoke\AIMonitor.ToolSmokeTests
dotnet run --project .\tests\smoke\AIMonitor.LanguageCorpusSmokeTests
```

## Rebuild Index

```powershell
dotnet run --project .\src\AIMonitor.Cli -- index rebuild
```

## Documentation Memory

AIMonitor cannot safely monitor its own edits through the watched-project workflow while it is being changed. The repository therefore uses docs as operational memory:

- `docs/system-memory/README.md` marks the authoritative contract memory.
- `CLAUDE.md` and `AGENTS.md` are small host entry points.
- Skill cards are loaded only when needed.
- Component docs explain ownership and data flow.
- Findings preserve historical investigations and deferred decisions.
- Restart context records the current recovery pattern for plugin/MCP/session weirdness.

Do not load every document by default. Start from the relevant host file, route through skills or component docs, then read deeper findings only when the task needs history.
