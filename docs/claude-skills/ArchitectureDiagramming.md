# Architecture Diagramming

Use this card when Claude needs to turn ambiguous architecture, workflow, validation, indexing, MCP/tool, or agent behavior into a clear diagram.

Prefer ASCII timelines first. Use Mermaid or another format only when the target doc already uses it or the operator asks.

## First Move

If prose is slippery, force it onto these axes:

- **Time:** what happens first, next, last.
- **Actor:** operator, Claude, MCP adapter, shared service, compiler, database, UI.
- **Artifact:** watched source, Working candidate, staged record, overlay, index row, source map, build output.
- **Gate:** hash check, validation, launch, human review, build, index refresh, policy decision.
- **Authority:** advisory, predictive, authoritative, durable, review evidence.
- **Failure path:** noisy, rejected, stale, abandoned, forced, replan.

If two ideas sound similar, put them in different boxes and label the difference directly.

## Use The MCP Surface

Before drawing AIMonitor behavior, use MCP evidence where available:

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

Use tool descriptions as the live contract for argument names. Do not infer MCP behavior from CLI command names.

When a host exposes capability-gated tool groups, prefer read-only exploration tools for documentation and diagramming. Do not require mutation tools to be visible. If mutation tools are hidden, label that as expected capability gating instead of a missing documentation signal.

## Freshness Check

Before diagramming a repo with active branches or review docs, separate evidence by freshness:

- **Implemented:** current code path or tests prove it.
- **Published contract:** docs on the requested branch say it, but code may lag.
- **Branch/proposal:** review notes, diagrams, PR docs, or findings describe intended behavior.
- **Inference:** reasoned from source shape but not directly proven.

Label the diagram or key with the evidence class. If docs and code disagree, diagram the disagreement instead of smoothing it over.

## Default Timeline Shape

```text
PLAN     declare intent / scope
           |
EDIT     create candidate artifacts
           |
GATE 1   predictive check
           |
REVIEW   human/tool review boundary
           |
GATE 2   authoritative confirmation
           |
STATE    durable result / follow-up
```

After the diagram, add a short key explaining what the diagram proves.

## Good Diagram Labels

- `semantic overlay compile`
- `HASH-ONLY integrity check`
- `full build on real watched tree`
- `Working candidate`
- `staged immutable record`
- `operator saves watched source here`
- `index refresh closure`
- `source-map evidence`

Avoid mushy labels such as `validation`, `refresh`, `sync`, `process`, or `finalize` unless the next sentence defines them.

## Documentation Engine Notes

When this card is used as part of AIMonitor.Documentation planning, prefer folder-local docs:

```text
SelectedFolder/
  Docs/
    Folder.aim.md
    SomeClass.aim.md
    manifest.aim.json
```

For each `.cs` file doc, make a best effort to explain "who uses me" from indexed callers/references, source search, or explicitly weak inference. Do not imply a consumer relationship is semantic when the evidence is only textual.

Keep generated docs compact:

- folder docs describe folder purpose, owned concepts, dataflow, and evidence gaps;
- file docs describe one source file's purpose, ownership, dataflow, key methods, known consumers, invariants, and evidence;
- user-editable sections may come later, but generated sections must remain hash/freshness aware.

## Portable Host Vocabulary

Keep behavior host-neutral first:

```text
stage immutable review evidence
launch human diff review
record accepted/rejected decision with staged hash
```

Then add host names only when needed:

```text
MCP: stage_candidate_for_review -> launch_staged_diff -> record_diff_decision
CLI: edit stage -> edit launch-diff -> edit record-decision
```

Do not mix MCP and CLI names inside ordinary operator steps.

## AIMonitor Example

```text
PLAN     start_monitor_session(filesPlanned=[A,B,C])
           |
EDIT     refresh/new + create Working candidates A', B', C'
           |
GATE 1   semantic overlay compile
         watched tree + A' + B' + C'
         predictive; may be noisy for Razor/generated artifacts
           |
STAGE    stage_candidate_for_review(A,B,C)
           |
REVIEW   launch_staged_diff
         operator merges staged bytes in WinMerge
           |
DECIDE   record_diff_decision
         watched source changes here on accepted save
           |
GATE 2   full build on real watched tree
         authoritative confirmation
           |
INDEX    rebuild affected index rows
```

Key read:

- GATE 1 predicts whether the planned overlay should work.
- GATE 2 confirms what actually landed.
- If GATE 1 and GATE 2 disagree, the question is fidelity, placement, or merge drift.
- The point where watched source mutates must be visible in the diagram.

## Architecture Proof Method (verified on AIMonitor itself)

Proving a *layered* architecture is a repeatable procedure — "Layered Evidence." Verified end-to-end against AIMonitor's own source.

1. **Skeleton from compile-enforced structure (`source-verified`).** Read `.csproj` `ProjectReference`/`PackageReference` → the dependency DAG. The build's success *proves acyclicity* ⇒ a valid topological layering exists. Complete and certain at the project altitude — no index needed.
2. **Rank + polarity.** `rank = longest dependency chain`; render `flowchart TB` with edges consumer→dependency so the most-depended-upon (`Core`, external truth libs) sink to the **bottom** = foundation. Tier *names* (foundation/orchestration/entry) are **maintained intent**, never graph-derived.
3. **Coupling from the index (`caller/index-verified`) — the augmentation.** For each public symbol, `find_indexed_references` → map each reference *site*'s file to its project → aggregate cross-project edge weights. Upgrades each declared edge from "declared" to "live + weighted," and **flags declared-but-zero-reference edges as candidate-dead** — the thing source-reading cannot catch.

Caveats that must be stated on any generated diagram:
- coupling counts **public targets only**; `internal`+`InternalsVisibleTo` use is uncounted, so `0` is *candidate*-dead, not proven-dead;
- assembly-load / DI-registration / config-only dependencies show `0` despite being needed;
- low `relationshipCount` means composition-over-inheritance — the architecture lives in the *reference* graph, not a type hierarchy.

## Self-Documentation: AIMonitor Indexing Itself

AIMonitor cannot *live-watch* itself (no recursive guard loop), but it **can index itself as a target** to MCP-back its own docs:

```text
config: Monitor.WatchedSolutionPath -> AIMonitor.slnx, Monitor.RuntimeRoot -> throwaway dir
CLI:    index rebuild --config <that>            (isolated DB, live watched index untouched)
drive:  launch AIMonitor.McpServer.dll over stdio (McpClient) --config <that>
        -> get_solution_index (all symbols, each tagged projectPath)
        -> find_indexed_references(stableSymbolKey, responseShape=rich)
```

This is the augmentation contract made real: source-only reading caps at `name-inferred`/`grep-backed`; routing the same focus through the MCP DLL upgrades it to `caller/index-verified`.
