# Restart Context

Use this note after context loss, plugin/MCP restarts, or agent handoff. It is current operational memory, not historical analysis.

## Current Restart Note - 2026-06-06 Codex / VS Code MCP Wiring

Restart pointer update from the live Codex terminal session:

- 2026-06-07 Planning outer-loop redesign discussion: treat `codex/planning-outer-loop` as a spike branch for MCP elicitation and post-decision Planning. Before continuing implementation, read `docs/feature-maps/PlanningOuterLoopRedesign.md`. Current direction: no MCP sampling, no GitHub automation yet, keep `record_diff_decision` as the Workflow-to-Planning back door, add a compact front door around Current task/iteration, keep `get_current_task_context` small with explicit task-history drill-down tools, and rebuild shared Planning behavior with a thin MCP adapter rather than adding more orchestration to `Program.cs`.
- 2026-06-07 immediate restart prompt for next Codex session: read this restart note first, then verify live MCP with `get_monitor_status`, then search/call `test_elicitation`. The monitor app/proxy/server chain was started from the VS Code command palette and process check showed `AIMonitor.App.exe`, `AIMonitor.McpStdioBridge.dll`, and `AIMonitor.McpServer.dll` running. The AIMonitor log showed a fresh Codex initialize with client info `codex-mcp-client` / `Codex` / `0.137.0-alpha.4` and `capabilities: {"elicitation":{}}`; live `tools/list` included `test_elicitation`. However, this current Codex chat stayed pinned to an older closed transport and every MCP call still returned `Transport closed`. Start a fresh Codex chat/plugin session before judging live MCP behavior.
- Suggested redesign direction after live `test_elicitation` proof: treat MCP as the primary AIMonitor adapter for Codex and Claude; CLI is fallback/recovery only. Build post-accept planning as an MCP-first protocol gate, not a skill-memory ritual. Add first-class Planning iteration completion, then wire `record_diff_decision` accepted/accepted-normalized flow to elicit a flat form asking whether the current iteration is complete and what the next task/iteration action should be. If elicitation is unavailable or canceled, return a pending planning decision and require an explicit MCP resolver tool before mutating Planning.
- 2026-06-07 Codex elicitation proof update: a harmless MCP probe tool `test_elicitation` was added in `src/AIMonitor.McpServer/Program.cs`, with DTOs `AIMonitorElicitationProbeResult`, `AIMonitorElicitationProbeInput`, and `AIMonitorElicitationProbeNextAction`. It uses the real SDK APIs `ModelContextProtocol.Server.McpServer.ClientCapabilities` and `McpServer.ElicitAsync<T>()`.
- A focused integration smoke `Mcp_test_elicitation_round_trips_dynamic_form_request` was added in `tests/integration/AIMonitor.Integration.Tests/McpServerSmokeTests.cs`. It configures `McpClientOptions.Capabilities.Elicitation` plus `McpClientOptions.Handlers.ElicitationHandler`, auto-accepts the form, and proves the dynamic form request round-trips through a fresh MCP server. Command passed: `dotnet test tests\integration\AIMonitor.Integration.Tests\AIMonitor.Integration.Tests.csproj --no-restore --filter "FullyQualifiedName~Mcp_test_elicitation_round_trips_dynamic_form_request"`.
- The live Codex MCP session did not hot-load `test_elicitation`; `tool_search` still showed old tools. After the fresh-server smoke, a live `get_monitor_status` call returned `Transport closed`. `cmd /c codex mcp get ai-monitor` still reports the expected global registration, but this chat's active MCP transport appears closed. A fresh Codex/plugin session may be required to call the new live tool through Codex.
- Process check after the closed transport initially found no `AIMonitor.App`, `AIMonitor.McpStdioBridge`, or `AIMonitor.McpServer` command line except the diagnostic PowerShell process. The WinForms app was then restarted from `src\AIMonitor.App\bin\Debug\net10.0-windows\AIMonitor.App.exe --config C:\VSCodeProjects\AIMonitor\config\appsettings.json` and `AIMonitor.App.exe` was observed running, but this same Codex chat still returned `Transport closed` for `get_monitor_status`. Conclusion: restarting the monitor app does not revive an already-closed Codex MCP stdio transport; restart/reopen Codex or force a new MCP client session before retrying live MCP calls.
- Dynamic popup capability answer: MCP elicitation is not arbitrary UI. It is a client-rendered dynamic form generated from a server-provided flat primitive schema, plus a URL/out-of-band mode in the SDK/spec. For AIMonitor post-accept discussion, use form mode with flat fields and an MCP fallback resolver if the client lacks/loses elicitation.
- 2026-06-07 handoff: operator is restarting the Codex window/plugin because the current Codex session still cannot see AIMonitor MCP tools even though VS Code/CLI configuration appears present. `tool_search` for AIMonitor MCP tools returned 0 tools. Continue assuming true MCP-path validation has NOT happened until the new Codex session exposes callable `mcp__ai_monitor...` tools.
- Before handoff, no running `AIMonitor.App.exe`, `AIMonitor.McpStdioBridge.exe`, or `AIMonitor.McpServer` process was found by process-name/CIM checks. Do not assume the WinForms app/proxy hub is running after restart.
- User hit an "Access is denied" failure while trying to test Codex MCP server visibility. Strong suspicion remains that Codex CLI may not expand VS Code `${workspaceFolder}` variables or is using a different MCP registration/path than VS Code. Prefer absolute paths in Codex MCP registration and verify with `codex mcp get ai-monitor` / `codex mcp list` from a fresh shell.
- The current Codex session used CLI parity commands (`dotnet run --project src\AIMonitor.Cli ...`) for Planning/Workflow because AIMonitor MCP tools were not exposed. This exercised shared services but did not validate Codex-through-MCP.
- Latest watched-solution safe-edit loop completed through CLI parity:
  - Current task: `Integrate task into current workflow` (`task-1704511c69d4464891bdee98e86a9de0`).
  - Watched file: `C:\SchemaStudioWebViewer\Repositories\SchemaMCPRepository.cs`.
  - Iteration #3 row: `iteration-fa1ed3424d79465988a1d12442b9a3e8`.
  - Accepted staged record: `20260607T171342425-e443496fc36b475b9aaaace`.
  - Accepted hash: `d4302736ad6a2edae0edfbe47e27f337e05ffd8d00afc98fb5162a8f4bffbe0a`.
  - Change: added harmless `ElicitationPauseMarker` property after an explicit operator pause.
  - Planning evidence attached successfully; post-accept solution index rebuild completed with 0 diagnostics but took about 54 seconds.
  - Required post-accept `edit refresh` was run; Working matched watched source at the accepted hash.
- Elucidation/elicitation behavior still needs product follow-through. The skill card was updated to require a pause before executing an existing `currentIteration`, but Codex missed the post-accept next-step prompt once. Desired post-accept prompt shape: ask whether the iteration is complete, then ask what the next step for the overarching task is: add next iteration, replace current iteration, park/close/cancel task, or stop. Planning still lacks a first-class `complete iteration` operation, so open iteration rows remain open after accepted evidence.
- AIMonitor repo changes currently include Planning iteration update support and docs/skill updates:
  - `PlanningService.UpdateIterationGoal`
  - `PlanningIterationUpdateResult`
  - CLI `plan update-iteration`
  - MCP `UpdateTaskIteration`
  - Planning unit test for correcting an iteration goal
  - docs updated for explicit elicitation/correction and compact current-task handshakes
  - `dotnet test tests\unit\AIMonitor.Planning.Tests\AIMonitor.Planning.Tests.csproj --no-restore` passed with 17 tests.
  - `dotnet build .\AIMonitor.slnx --no-restore` passed with the known WindowsBase warning.
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
