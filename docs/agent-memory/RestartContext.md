# Restart Context

Use this note after context loss, plugin/MCP restarts, or agent handoff. It is current operational memory, not historical analysis.

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

For watched source, never patch the watched file directly:

```text
refresh_file/new_file
edit Working candidate
stage_candidate_for_review
launch_staged_diff
operator reviews/saves in WinMerge
record_diff_decision
refresh_file before editing the same accepted file again
```

CSS, JSON, config, markup, and other non-C# text assets use the same diff workflow. They do not need semantic index rows.

## Known Deferred Items

- MCP elicitation for validation override is deferred. Current behavior uses the Host dialog or explicit chat approval plus `forceValidation`.
- Dependency-aware incremental validation/index refresh is deferred. Full validation and full index refresh are slower but safer.
- First-class all-files-at-once overlay validation is deferred. Current review/validation is per staged candidate, with session grouping for operator intent and telemetry.

## Test Notes

Integration tests are intentionally slower because workflow tests build validation copies. Use a longer timeout for the full integration project.

Recent expected integration shape:

```text
39 passed, 1 skipped
```

The skipped test is the direct stdio bridge test; live bridge/proxy behavior is covered by smoke tests that run through WinForms-visible telemetry.
