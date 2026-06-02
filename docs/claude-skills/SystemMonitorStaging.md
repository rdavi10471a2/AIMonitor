# AIMonitor Staging

Use when changing watched source.

The active safety mechanism is monitor-owned Working files, pre-merge validation, and gated WinMerge review. If files are coupled, stage all required candidates into one monitor session before launching the first review.

## Rules

- Do not edit watched source directly.
- All watched-source changes go through AIMonitor staging.
- Reason in the cloud; compose locally. Use source-map selectors and AIMonitor edit tools to describe the intended edit, then let local tooling splice/stage the candidate.
- Use the Solution Index MCP surface for cheap project context before body reads: `get_solution_index_tree`, `query_solution_index`, `find_indexed_symbols`, `get_indexed_symbol`, `find_indexed_references`, `find_indexed_callers`, and `find_indexed_relationships`.
- If the target file is already known, call `query_solution_index(scope: "file", value: "<relative path>")` first and use the returned symbol row's `StableSymbolKey`. Do not manually compose stable keys.
- Use the smallest safe edit unit: symbol edit before whole-file replacement.
- For any file at or above 32KB in a cold session, call `refresh_file` and chunk-read the returned Working file path. Do not use `get_file` for that cold-session entry.
- For warm-session text edits, do not re-read the file. Use the text already in context with `replace_text_in_file` and `expectedMatches: 1`, or `replace_span_in_file` when exact bounds are already known.
- Stage candidates, let AIMonitor run pre-merge validation, then use Operator review.
- For coupled multi-file C# edits, keep every required file in one monitor session and review the files deliberately as a
  chain. Current pre-merge validation is per staged candidate; first-class all-files-at-once overlay validation is still
  future work.
- Record the Operator decision with `record_diff_decision`.
- After each accepted `record_diff_decision`, check the returned `IndexRefresh` status. Accepted decisions refresh the
  monitor-owned solution index immediately; do not assume a separate batch rebuild for a multi-file session.
- Stop on `dirty-unexpected`; recovery is explicit refresh/rebase/restage or Operator reconcile.
- A Working candidate persists across sessions when the watched-source baseline hash is unchanged. If the first edit in a new pass inherits prior in-progress candidate content, either continue deliberately or discard the Working mirror/state before starting a clean test.
- Cached source-map or solution-index selectors are advisory only. Before `submit_symbol`, `remove_symbol`, or related submit/remove operations, refresh the file selector map with live `get_source_map(scope: "file", mode: "selector")` or verify the file with `check_file_hash`, then call `get_symbol`.

## Choose The Staging Mode

| Intent | Preferred tool |
|---|---|
| Replace one method/property/field/type body or signature | `submit_symbol` |
| Add a method | `add_method` |
| Add a field | `add_field` |
| Add a property | `add_property` |
| Add a constructor | `add_constructor` |
| Add a nested type | `add_nested_type` |
| Remove a member | `remove_symbol` |
| Change using directives | `add_using` / `remove_using` |
| Make an existing type partial | `set_type_partial` |
| Replace exact text in Razor, markup, CSS, JSON, config, or other text | `replace_text_in_file` |
| Replace a known line/column span | `replace_span_in_file` |
| Create a brand-new file | `submit_file` |
| Create a brand-new Razor component | Two `submit_file` calls in one session: `.razor` markup + `.razor.cs` partial-class companion. Do not start a new Razor file with inline `@code`. |
| Migrate legacy inline-`@code` Razor to two-file form | Use `get_file`/`submit_file` carefully in one session for `.razor` and `.razor.cs`; no dedicated split tool exists yet. |
| Regenerate or deliberately replace a whole file | `submit_file` |

Do not use `submit_file` for ordinary member-level edits just because you have the full file in context.

## Usual Flow

```text
get_monitor_status
get_workflow_status
get_tool_manifest when discovering the current tool contract
get_staging_guide when the client needs the staging and session rules
find_file, unless the full path was returned by a Roslyn or Monitor tool in this session
get_solution_index_tree for project orientation, or query_solution_index for folder/namespace/file slices
query_solution_index(scope: "file", value: path) when the file is already known; use returned StableSymbolKey
find_indexed_symbols / get_indexed_symbol for target declarations
find_indexed_references / find_indexed_callers before changing public or shared APIs
find_indexed_relationships when partials, inheritance, overrides, or interface implementations may affect the edit
get_source_map(scope: "file", mode: "selector")
get_symbol for the smallest needed body
submit_symbol / set_type_partial / add_field / add_property / add_method / add_constructor / add_nested_type
add_symbol / remove_symbol / add_using / remove_using, when the narrow typed tools do not fit
stage_candidate_for_review
launch_staged_diff or Host/sidecar WinMerge review/save
Operator review
record_diff_decision
check IndexRefresh status before doing more index-dependent work
```

For multi-file work:

```text
start_monitor_session
compose Working candidate A with sessionId
stage_candidate_for_review for file A with sessionId
compose Working candidate B with sessionId
stage_candidate_for_review for file B with sessionId
pre-merge validation checks the staged candidate before WinMerge
launch/review file A
record decision for file A
launch/review file B
record decision for file B
check each accepted decision's IndexRefresh status before doing index-dependent follow-up
```

For coupled edits, briefly name why the files must validate together before review.

## Discovery Discipline

Empty Roslyn reference/caller results are not proof that no consumers exist. Before treating an API/signature/rename as single-file, cross-check with at least one other signal: diagnostics, symbol search, public API surface, targeted source map, known UI fields/properties, or explicit Operator knowledge.

Prefer Monitor Solution Index queries as the first broad signal. Use `query_solution_index` for namespace/folder/file surfaces, `find_indexed_symbols` for declarations, `find_indexed_references` / `find_indexed_callers` for indexed impact, and `find_indexed_relationships` for partial declarations, inheritance, overrides, and interface implementations. These are local SQLite rows built from Roslyn semantics and are cheaper than loading dependency bodies.

Before emitting a call site to a type reached through a `using`, local namespace context, or a known dependency, load that target type's real callable surface first. Use `find_indexed_symbols`, `query_solution_index`, `get_source_map`, and `get_symbol` for watched-project types. Write calls against actual method names, return types, parameter types, and overloads.

Use `get_source_map(scope: "namespace", namespaceName: "...")` when a file's `using` directives or namespace neighborhood point at related watched-project types. Namespace scope is the preferred structural form for "find similar nearby types" when the namespace is known.

If pre-merge/build diagnostics reveal a missed or broken call site that the index did not surface, text search is allowed as a diagnostic fallback. Use it to locate the missed file or literal call site, then confirm structure where possible and stage the corrected file into the same monitor session before retrying review. Do not use grep as the first-pass way to understand C# code.

For whole-file staging, use Roslyn shape plus `get_file`; skip `get_source_map` unless you need stable selectors or structure. For symbol staging, use Roslyn shape plus `get_source_map`/`get_symbol`; skip `get_file` unless symbol context is insufficient.

For files at or above 32KB of any type, the cold-session read path is `refresh_file(sourceFilePath)` followed by bounded chunk reads from the returned Working file path. This prevents oversized MCP results. If the file is already loaded in the current session, skip the read and use the in-context text as the guard for `replace_text_in_file`.

If a candidate target came from `get_solution_index`, `get_solution_index_tree`, `query_solution_index`, `find_indexed_symbols`, `get_indexed_symbol`, `find_indexed_references`, `find_indexed_callers`, `find_indexed_relationships`, or cached compact index JSON, treat that target as discovery only. Refresh the file selector/hash in the current session before body read and mutation; if the live selector is missing, ambiguous, or hash-drifted, refresh/rebuild the selector data and restart the narrow edit path.

Debug-only reference: `get_smoke_test_catalog` is for maintainers investigating or extending smoke coverage, not for normal Claude review or edit planning.
