# Skill Eval Prompt

Use the architecture diagramming skill to review AIMonitor's own worktree.

This is read-only. Do not edit files.

## Target

Workspace: AIMonitor repository root.

Generate diagrams that clarify the architecture/workflows most likely to be ambiguous to agents:

- MCP / CLI / UI adapter boundaries over shared services.
- Safe edit, Working candidate, stage, launch, WinMerge review, decision, and refresh flow.
- Solution index, MSBuild, Razor/source-map, project refresh, and rebuild flow.
- Plan Board / Current task context and how agents should retrieve it.

Prefer evidence from docs and code. Use the MCP surface as evidence when visible:

```text
get_tool_manifest
get_monitor_status
get_workflow_status
get_current_task_context
get_solution_index_status
get_solution_index_tree
query_solution_index
find_indexed_symbols
find_indexed_references
find_indexed_callers
find_indexed_relationships
get_source_map
list_session_staged_records
get_staged_record
compare_file
get_staging_guide
```

If MCP tools are not visible, say so and use local docs/source search.

## Output

Produce:

- 2-4 ASCII diagrams.
- A short key after each diagram explaining what it proves.
- A short critique of whether the skill helped.
- A short list of missing or weak skill instructions discovered during the run.

Keep the output portable: behavior first, host-specific tool names only where they matter.
