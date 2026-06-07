# AIMonitor Workflow Quick Start

Use this card first when Claude is editing a watched project through AIMonitor.

## Binding

Claude Code should bind to `AIMonitor.McpStdioBridge`, not `AIMonitor.McpServer` directly. The bridge sends Claude's MCP traffic through the WinForms-owned proxy hub so the Monitor Status tab records live request/response telemetry.

Use absolute paths in the installed MCP config until relative working-directory behavior is verified:

```text
dotnet <absolute path>\src\AIMonitor.McpStdioBridge\bin\Debug\net10.0\AIMonitor.McpStdioBridge.dll --repo-root <absolute AIMonitor repo> --config <absolute AIMonitor config>
```

## First Calls

```text
get_monitor_status
get_workflow_status
get_current_task_context
get_self_check
get_staging_guide
get_tool_manifest
```

Use `get_current_task_context` for Plan Board task context. Do not inspect task-memory Markdown folders directly; Planning service responses are the AI-facing contract and exclude private Human Notes.

After `get_current_task_context`, briefly tell the operator the Current task title/status and the current iteration goal when present. Keep review evidence to a compact one- or two-line summary unless the operator asks for more.

Use the Solution Index before loading bodies:

```text
get_solution_index_status
get_solution_index_tree
query_solution_index
find_indexed_symbols
get_indexed_symbol
find_indexed_references
find_indexed_callers
find_indexed_relationships
```

Use source-map tools before symbol edits. They are important, not legacy:

```text
get_source_map(scope: "file", mode: "selector")
get_symbol(symbolSelectorJson)
submit_symbol(path, symbolSelectorJson, replacement)
```

The index is a broad discovery surface. Source maps and symbols are the precise edit surface for C# member/type surgery.

## Existing File Edit

```text
start_monitor_session
set_monitor_session_plan(sessionId, taskId, iterationId, filesPlanned)
refresh_file(sourceFilePath, sessionId)
edit only the returned Working candidate using MCP tools
stage_candidate_for_review(path, sessionId)
launch_staged_diff(stagedRecordId)
operator reviews/saves in WinMerge
record_diff_decision(stagedRecordId, "accepted", expectedStagedHash)
refresh_file before another edit to the same watched file
```

Before the first `refresh_file` / `new_file`, set the session's planned files on the session DTO. Include the Current `taskId`, Current `iterationId`, each planned watched file path, its MSBuild `owningProjectPath`, a short `reason`, and a `role` such as `edit`, `new-file`, `test`, `config`, or `context`. This is the front-door contract for multi-file work and future project-targeted index refresh. Keep it small and pass the same `sessionId` through the edit, stage, launch, and decision tools. `record_diff_decision` checks that DTO at the back door and reports whether the decided file is 1 of N, 2 of N, or the final planned file.

Safe editing tools include:

- `replace_text_in_file` with `expectedMatches`.
- `find_text_span` then `replace_span_in_file`.
- `get_source_map`, `get_symbol`, then `submit_symbol` for precise C# replacements.
- `add_symbol`, `remove_symbol`.
- `add_using`, `remove_using`, `set_type_partial`.
- `add_field`, `add_property`, `add_method`, `add_constructor`, `add_nested_type`.
- `submit_file` only for new files, generated files, or deliberate whole-file replacement.

CSS, JSON, config, markup, and other non-C# text assets use the same Working candidate, staging, WinMerge diff, and decision flow. They do not need semantic index rows to be diffable.

Do not edit watched source directly. Do not edit staged runtime files. After staging, further candidate changes must go back through the Working file and be staged again.

## New File Edit

```text
start_monitor_session
set_monitor_session_plan(sessionId, taskId, iterationId, filesPlanned)
new_file(sourceFilePath, sessionId)
submit_file(path, content, sessionId)
stage_candidate_for_review(path, sessionId)
launch_staged_diff(stagedRecordId)
operator creates/saves watched file in WinMerge
record_diff_decision(stagedRecordId, "accepted", expectedStagedHash)
```

Rejected new-file decisions leave watched source absent.

## Validation And Review

`launch_staged_diff` always runs pre-merge validation before WinMerge. If validation fails on an interactive Windows desktop, AIMonitor shows `Yes Launch` and `Cancel`.

- `Cancel`: WinMerge does not open; fix the Working candidate and stage again.
- `Yes Launch`: WinMerge opens despite failed validation; only record `accepted` if the operator deliberately saved the candidate into watched source.
- No dialog available: ask the operator in chat before using `forceValidation`.

Accepted or accepted-normalized decisions rebuild the solution index and return `indexRefresh`. Check that status before relying on fresh index rows.

`record_diff_decision` also attaches the staged-record decision to the Current task when one exists. If no Current task exists, the safe-edit decision is still recorded but no task memory evidence is attached.
