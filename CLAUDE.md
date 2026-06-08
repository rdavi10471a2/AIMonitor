# AIMonitor Claude Instructions

Claude should treat this repository as the implementation of AIMonitor, an AI safe edit monitor.

This file is the Claude/Claude Code entry point. `AGENTS.md` is the Codex host entry point. The two files share the same architecture and safety invariants, but each should stay tuned for its own agent host.

## Core Rules

- Keep product code under `src/`, tests under `tests/`, samples under `samples/`, and documentation under `docs/`.
- MCP is not the workflow. MCP is Claude's adapter over shared Core, Workflow, MSBuild, Indexing, and Runtime services.
- Prefer MSBuild-loaded project truth over directory guessing.
- For any newly authored C# source, do not use top-level statements, do not use `using var`, `using` declarations, or `await using` declarations for resource lifetime, and always use braces for control-flow bodies.
- Add tests beside workflow behavior when changing behavior.
- Keep generated runtime state under `runtime/`, not in watched projects.
- Do not describe AIMonitor as C#-only. C# is the first semantic provider; MSBuild project/document loading is language-neutral.
- Treat `docs/system-memory/README.md` as the authoritative system-memory contract for AIMonitor behavior.
- Use `docs/agent-memory/RestartContext.md` after plugin, MCP, or context restarts.
- Use `docs/components/` for component ownership and data-flow questions.
- Use `docs/feature-maps/` as the centralized per-feature memory surface. Before changing a workflow, adapter surface, semantic query, or test harness behavior, load the relevant feature map instead of assuming the source tree alone explains the contract.
- For MCP, Roslyn, Workflow, or Indexing parity work, read `docs/feature-maps/CmbMcpCapabilityParity.md`. Do not treat compatibility-shaped stubs or valid JSON response shapes as proof that prior MonitorBaseClaude behavior was faithfully restored.
- Prefer tight loops: small plan, bounded edit, focused test, inspect, then continue. Do not force exhaustive up-front plans when the edge cases need discovery.
- Reason in the cloud; compose locally. Do not write watched source directly.
- Claude MCP tool names and Codex/CLI command names are different adapter surfaces over the same shared workflow. Do not mix host-specific names when writing operator instructions, notes, or handoff docs.
- After staging, staged runtime files are immutable review evidence. Further candidate changes go back through the Working file and must be staged again.
- Diff stability depends on complete local edit context: use source-map/symbol context for semantic edits, the whole Working file for text/whole-file edits, or bounded exact replacements constrained by the smallest safe edit rule before staging. The authoritative definition of the smallest safe edit rule lives in `docs/system-memory/README.md`.

## MCP Binding

Claude Code should launch `AIMonitor.McpStdioBridge`, not `AIMonitor.McpServer` directly. The bridge sends Claude's MCP stdio stream through the WinForms-owned MCP proxy hub so the Monitor Status tab sees live request/response telemetry before requests reach the combined MCP server.

Use an installed MCP config with absolute paths so Claude Code does not depend on an implicit launcher working directory:

```text
dotnet <absolute path>\src\AIMonitor.McpStdioBridge\bin\Debug\net10.0\AIMonitor.McpStdioBridge.dll --repo-root <absolute AIMonitor repo> --config <absolute AIMonitor config>
```

## Watched-Source Safety

Never edit watched source directly. For watched-project edits:

1. Start a *planned* session: call `start_monitor_session` with `filesPlanned` listing every file you intend to change. A planned session is required before any `refresh_file`/`new_file`/edit — mutations to unplanned files are rejected. (`filesPlanned` is your edit scope; the engine derives a separate inbound-reference closure for index refresh — you do not hand-type it.)
   - While planning, run `find_indexed_references` on each symbol you intend to change. If referencing sites exist in other projects, add any consumer you must edit to `filesPlanned`. The engine refreshes those dependent projects' index rows via the inbound-reference closure, so cross-project references are not silently orphaned.
2. Use `refresh_file` for existing files or `new_file` for future watched files.
3. Edit only the monitor-owned Working candidate with AIMonitor MCP tools.
4. Stage with `stage_candidate_for_review`.
5. Launch review with `launch_staged_diff`.
6. Let the operator review/save in WinMerge.
7. Record the operator decision with `record_diff_decision`.
8. For accepted or accepted-normalized decisions, check `indexRefresh.status` before relying on solution-index rows.

After an accepted or accepted-normalized decision, call `refresh_file` before editing that same watched file again.

If an accepted or accepted-normalized decision reports a failed `indexRefresh` or leaves the workflow session index-stale, do not trust solution-index rows for follow-up semantic work until `refresh_solution_index`, an equivalent rebuild, or a successful post-accept refresh clears the stale state.

New-file review does not create watched source automatically. The operator must create/save the future watched file through WinMerge before an accepted decision can be classified.

If an MCP edit tool rejects a Working candidate because C# syntax validation failed, do not force it to WinMerge. Revise the candidate into syntactically valid C# and retry the edit. This is agent feedback, not a human override gate.

A planned session has **two compile gates**, and they answer different questions (see `docs/PlannedSessionEditFlow.md`):

- **GATE 1 — overlay semantic compile (pre-merge).** Once every planned candidate exists, a Roslyn compile runs over the whole project with the staged candidates swapped in. It catches cross-file C# breaks before any merge but is a *predictor that is allowed to be noisy*: it skips `.razor` markup and runs no MSBuild/analyzers/source-or-Razor generators, so it produces known false positives (Razor/generated-artifact references; duplicate-inclusion / dual-`SqlClient` ambiguous-reference errors). A failing GATE 1 is operator judgment: if it is a recognizable noisy class the operator **may merge anyway**; if it is a clear real break, replan/fix or ask the operator before merge.
- **GATE 2 — full `dotnet build` on the real watched tree (post-accept).** This is the **authoritative** gate and is intentionally placed after the merge so it builds the real source the operator just committed. The "must not be treated as a warning, hard-stop on failure" stance attaches to **GATE 2 / genuine breaks**, not to noisy GATE 1 overlay errors.

When GATE 1 reports a *real* break, do not treat `launch_staged_diff` as a warning. Use the Host dialog result. If no dialog is available, stop and ask the operator in chat before using `forceValidation`. Proceed only after an explicit approval such as "yes, launch anyway" or "force validation approved" for that staged record. Silence, ambiguity, or approval for a different file/session is not enough.

**Verbatim-merge rule:** merge the staged bytes verbatim in WinMerge — do not hand-edit during the merge. GATE 1's validity transfers to watched source only because the overlay is a copy of watched with the staged files swapped in; a hand-edit during merge breaks that equivalence (watched must end up byte-equal to the staged candidate).

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
- New Razor component authoring: `BlazorPageTriadAuthoring.md`
- Live telemetry checks: `TroubleshootingDashboard.md`

Use live MCP tool descriptions for exact argument names.

## Razor Boundary

AIMonitor indexes the Razor facts it can defend: normal C# files, clean `.razor.cs` code-behind, and Razor/compiler source-mapped references when those mappings point back to user-authored source. Do not promise full Visual Studio-level Razor binding semantics for markup strings, component parameters, or event handlers.

## Text Assets

CSS, JSON, config, markup, and other non-C# text assets do not need semantic indexing to be safely edited. They still use the same protected workflow: `refresh_file` or `new_file`, edit the Working candidate with text/file tools such as `replace_text_in_file`, `replace_span_in_file`, or `submit_file`, then `stage_candidate_for_review`, `launch_staged_diff`, WinMerge review, and `record_diff_decision`.
