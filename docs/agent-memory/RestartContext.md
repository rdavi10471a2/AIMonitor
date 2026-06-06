# Restart Context

Use this note after context loss, plugin/MCP restarts, or agent handoff. It is current operational memory, not historical analysis.

## Current Restart Note - 2026-06-06 Codex / VS Code MCP Wiring

Restart pointer update from the live Codex terminal session:

- AIMonitor app/proxy hub was launched directly from `src\AIMonitor.App\bin\Debug\net10.0-windows\AIMonitor.App.exe --config C:\VSCodeProjects\AIMonitor\config\appsettings.json`.
- The live MCP chain was then started manually and showed these processes: `AIMonitor.App.exe`, `AIMonitor.McpStdioBridge.exe`, and `dotnet ... AIMonitor.McpServer.dll`.
- Codex MCP registration was toggled by running `codex mcp remove ai-monitor` followed by `codex mcp add ai-monitor -- dotnet C:\VSCodeProjects\AIMonitor\src\AIMonitor.McpStdioBridge\bin\Debug\net10.0\AIMonitor.McpStdioBridge.dll --repo-root C:\VSCodeProjects\AIMonitor --config C:\VSCodeProjects\AIMonitor\config\appsettings.json`.
- `codex mcp get ai-monitor` reports `enabled: true`, but the current already-running Codex chat still does not expose AIMonitor MCP tools. Restart Codex from the terminal before expecting the `ai-monitor` tool namespace.
- After restart, first try a live MCP call such as `get_monitor_status`; if unavailable, verify `cmd /c codex mcp get ai-monitor` and then check whether `AIMonitor.App.exe` is still running.
- Boundary correction: AIMonitor MCP/edit commands are for editing the watched solution configured by `Monitor:WatchedSolutionPath`; do not use them to edit AIMonitor repository docs or memory files. AIMonitor repo files such as this restart note can be edited directly unless they are part of the watched solution.
- Operator is restarting the Codex extension while leaving the AIMonitor app/proxy/server processes running.

The AIMonitor MCP server is registered globally for Codex:

```powershell
codex mcp get ai-monitor
```

Expected config:

```text
command: dotnet
args: C:\VSCodeProjects\AIMonitor\src\AIMonitor.McpStdioBridge\bin\Debug\net10.0\AIMonitor.McpStdioBridge.dll --repo-root C:\VSCodeProjects\AIMonitor --config C:\VSCodeProjects\AIMonitor\config\appsettings.json
```

VS Code workspace MCP config was also added at `.vscode/mcp.json` using the current VS Code `servers` shape. VS Code showed an active `mcpServer.mcp.config.ws0.aiMonitor` session after this was added.

The WinForms app/proxy hub was started with:

```powershell
dotnet run --project src\AIMonitor.App\AIMonitor.App.csproj -- --config config\appsettings.json
```

Live process check before Codex/plugin restart showed all three pieces running:

```text
AIMonitor.App
AIMonitor.McpStdioBridge
AIMonitor.McpServer
```

A manual JSON-RPC smoke through `AIMonitor.McpStdioBridge` returned `serverInfo.name = AIMonitor.McpServer` and a real `tools/list` response, proving the bridge connected through the WinForms proxy hub.

Codex CLI path issue was fixed before restart. Plain `codex` was resolving to stale `C:\Users\rdavi\.codex\.sandbox-bin\codex.exe` (`codex-cli 0.118.0-alpha.2`), which rejected `model = "gpt-5.5"` before MCP commands could run. The current WinGet install has `codex-cli 0.137.0` at:

```text
C:\Users\rdavi\AppData\Local\Microsoft\WinGet\Packages\OpenAI.Codex_Microsoft.Winget.Source_8wekyb3d8bbwe\codex-x86_64-pc-windows-msvc.exe
```

Added a user-path wrapper next to it:

```text
C:\Users\rdavi\AppData\Local\Microsoft\WinGet\Packages\OpenAI.Codex_Microsoft.Winget.Source_8wekyb3d8bbwe\codex.cmd
```

The wrapper forwards to `codex-x86_64-pc-windows-msvc.exe`, and `cmd /c codex --version` now returns `codex-cli 0.137.0`. `cmd /c codex mcp get ai-monitor` returns the expected MCP registration.

After restarting Codex, verify whether the session exposes AIMonitor MCP tools. Try a small live call first:

```text
get_monitor_status
find_indexed_symbols(text: "SolutionIndexQueryService.FindSymbols", kind: "Method")
```

Important distinction: Codex MCP registration lives in `C:\Users\rdavi\.codex\config.toml`; VS Code MCP registration lives in `.vscode/mcp.json`. Codex does not hot-load new MCP tools into an already-running chat. Restart the Codex session/plugin before expecting an `ai-monitor` tool namespace.

Current repo state before restart: `main` is even with `origin/main`, with local uncommitted changes in:

- `.vscode/mcp.json` - VS Code workspace MCP config for `aiMonitor`.
- `docs/agent-memory/RestartContext.md` - this restart note.
- `tests/unit/AIMonitor.Data.Tests/McpVsGrepTokenBenchmarkTests.cs` - pre-existing wording-only cleanup changing generated benchmark summary wording from pre-fix language to current "qualified lookup still returns 0" language.
- `C:\Users\rdavi\AppData\Local\Microsoft\WinGet\Packages\OpenAI.Codex_Microsoft.Winget.Source_8wekyb3d8bbwe\codex.cmd` - local PATH wrapper outside the repo so `codex` resolves to WinGet `codex-cli 0.137.0` instead of stale `.codex\.sandbox-bin`.

Commit or discard deliberately.

## First Checks

```powershell
git status --short --branch
dotnet sln .\AIMonitor.slnx list
dotnet build .\AIMonitor.slnx --no-restore
```

If working on live Claude/MCP behavior, start or confirm the WinForms app before expecting Monitor Status telemetry.

## Host Entry Points

- Codex reads `AGENTS.md`.
- Claude reads `CLAUDE.md`.
- Claude skill routing starts with `docs/claude-skills/AIMonitorWorkflowQuickStart.md` and `docs/claude-skills/SkillRouter.md`.
- Component ownership starts at `docs/components/README.md`.

## Live MCP Pattern

Claude Code should launch `AIMonitor.McpStdioBridge`, not `AIMonitor.McpServer` directly.

Expected live path:

```text
Claude Code stdio
  -> AIMonitor.McpStdioBridge
  -> WinForms MCP proxy hub
  -> AIMonitor.McpServer
  -> shared services
```

The WinForms Monitor Status tab should show request/response telemetry when live MCP calls pass through the proxy hub. Deterministic tests may launch `AIMonitor.McpServer` directly when they are testing server behavior without the UI.

## Safe Edit Reminder

For watched source, never patch the watched file directly. Use the command surface for the active host:

Claude/MCP:

```text
refresh_file/new_file
edit Working candidate
stage_candidate_for_review
launch_staged_diff
operator reviews/saves in WinMerge
record_diff_decision
refresh_file before editing the same accepted file again
```

Codex/CLI:

```text
edit refresh / edit new
edit Working candidate
edit stage
edit launch-diff
operator reviews/saves in WinMerge
edit record-decision
edit refresh before editing the same accepted file again
```

CSS, JSON, config, markup, and other non-C# text assets use the same diff workflow. They do not need semantic index rows.

## Known Deferred Items

- MCP elicitation for validation override is deferred. Current behavior uses the Host dialog or explicit chat approval plus `forceValidation`.
- Dependency-aware incremental validation/index refresh is deferred. Full validation and full index refresh are slower but safer.
- First-class all-files-at-once overlay validation is deferred. Current review/validation is per staged candidate, with session grouping for operator intent and telemetry.

## Test Notes

Integration tests are intentionally slower because workflow tests build validation copies. Use a longer timeout for the full integration project.

Expected integration counts change as coverage grows. Treat the current test output as authoritative, and expect the direct stdio bridge test to be skipped when the live bridge/proxy path is covered elsewhere.

Live bridge/proxy behavior is covered by smoke tests that run through WinForms-visible telemetry.
