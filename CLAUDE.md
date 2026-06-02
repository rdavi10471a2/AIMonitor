# AIMonitor Claude Instructions

Claude should treat this repository as the implementation of AIMonitor, an AI safe edit monitor.

This file is the Claude/Claude Code entry point. `AGENTS.md` is the Codex host entry point. The two files share the same architecture and safety invariants, but each should stay tuned for its own agent host.

## Core Rules

- Keep product code under `src/`, tests under `tests/`, samples under `samples/`, and documentation under `docs/`.
- MCP is not the workflow. MCP is Claude's adapter over shared Core, Workflow, MSBuild, Indexing, and Runtime services.
- Prefer MSBuild-loaded project truth over directory guessing.
- Add tests beside workflow behavior when changing behavior.
- Keep generated runtime state under `runtime/`, not in watched projects.
- Do not describe AIMonitor as C#-only. C# is the first semantic provider; MSBuild project/document loading is language-neutral.
- Treat `docs/system-memory/README.md` as the authoritative system-memory contract for AIMonitor behavior.
- Use `docs/agent-memory/RestartContext.md` after plugin, MCP, or context restarts.
- Use `docs/components/` for component ownership and data-flow questions.
- For MCP, Roslyn, Workflow, or Indexing parity work, read `docs/feature-maps/CmbMcpCapabilityParity.md`. Do not treat compatibility-shaped stubs or valid JSON response shapes as proof that prior MonitorBaseClaude behavior was faithfully restored.
- Prefer tight loops: small plan, bounded edit, focused test, inspect, then continue. Do not force exhaustive up-front plans when the edge cases need discovery.
- Reason in the cloud; compose locally. Do not write watched source directly.
- After staging, staged runtime files are immutable review evidence. Further candidate changes go back through the Working file and must be staged again.
- Diff stability depends on complete local edit context: use source-map/symbol context for semantic edits, the whole Working file for text/whole-file edits, or bounded exact replacements constrained by the smallest safe edit rule before staging.

## MCP Binding

Claude Code should launch `AIMonitor.McpStdioBridge`, not `AIMonitor.McpServer` directly. The bridge sends Claude's MCP stdio stream through the WinForms-owned MCP proxy hub so the Monitor Status tab sees live request/response telemetry before requests reach the combined MCP server.

Use an installed MCP config with absolute paths so Claude Code does not depend on an implicit launcher working directory:

```text
dotnet <absolute path>\src\AIMonitor.McpStdioBridge\bin\Debug\net10.0\AIMonitor.McpStdioBridge.dll --repo-root <absolute AIMonitor repo> --config <absolute AIMonitor config>
```

## Watched-Source Safety

Never edit watched source directly. For watched-project edits:

1. Start or reuse the intended monitor session.
2. Use `refresh_file` for existing files or `new_file` for future watched files.
3. Edit only the monitor-owned Working candidate with AIMonitor MCP tools.
4. Stage with `stage_candidate_for_review`.
5. Launch review with `launch_staged_diff`.
6. Let the operator review/save in WinMerge.
7. Record the operator decision with `record_diff_decision`.
8. For accepted or accepted-normalized decisions, check `indexRefresh.status` before relying on solution-index rows.

After an accepted or accepted-normalized decision, call `refresh_file` before editing that same watched file again.

New-file review does not create watched source automatically. The operator must create/save the future watched file through WinMerge before an accepted decision can be classified.

If pre-merge validation fails, `launch_staged_diff` must not be treated as a warning. Use the Host dialog result. If no dialog is available, stop and ask the operator in chat before using `forceValidation`. Proceed only after an explicit approval such as "yes, launch anyway" or "force validation approved" for that staged record. Silence, ambiguity, or approval for a different file/session is not enough.

## Skills

Use the focused cards in `docs/claude-skills/` instead of loading all documentation.

Start with:

- `docs/claude-skills/AIMonitorWorkflowQuickStart.md`
- `docs/claude-skills/SkillRouter.md`

Then load the smallest relevant card:

- Semantic discovery: `RoslynFirstNavigation.md`
- Watched-source staging: `SystemMonitorStaging.md`
- Coupled multi-file edits: `SessionOverlayValidation.md`
- WinMerge and validation gates: `ReviewQueueAndGates.md`
- Formatting/newline-safe edits: `FormattingOracle.md`
- Async or signature propagation: `AsyncPropagation.md`
- Companion partial refactors: `PartialClassRefactor.md`
- Live telemetry checks: `TroubleshootingDashboard.md`

Use live MCP tool descriptions for exact argument names.

## Razor Boundary

AIMonitor indexes the Razor facts it can defend: normal C# files, clean `.razor.cs` code-behind, and Razor/compiler source-mapped references when those mappings point back to user-authored source. Do not promise full Visual Studio-level Razor binding semantics for markup strings, component parameters, or event handlers.

## Text Assets

CSS, JSON, config, markup, and other non-C# text assets do not need semantic indexing to be safely edited. They still use the same protected workflow: `refresh_file` or `new_file`, edit the Working candidate with text/file tools such as `replace_text_in_file`, `replace_span_in_file`, or `submit_file`, then `stage_candidate_for_review`, `launch_staged_diff`, WinMerge review, and `record_diff_decision`.
