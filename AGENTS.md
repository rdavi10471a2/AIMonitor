# AIMonitor Agent Instructions

This repository is the AIMonitor implementation.

- Keep `src`, `tests`, `samples`, and `docs` as top-level peers.
- Do not place product source at repository root.
- Do not use C# top-level statements.
- For any newly authored C# source, do not use top-level statements, do not use `using var`, `using` declarations, or `await using` declarations for resource lifetime, and always use braces for control-flow bodies.
- Keep MCP, CLI, and UI as adapters over shared Core/Workflow/MSBuild/Indexing services.
- Add tests in parallel with workflow behavior.
- Prefer MSBuild-loaded project truth over filesystem guessing.
- Keep generated runtime state under `runtime/` and out of watched projects.
- Do not hide data row/result classes inside repositories; schema-shaped POCOs get their own files.
- Treat MSBuild project/document loading as language-neutral; C# is the first semantic provider, not the whole architecture.
- Treat `docs/system-memory/README.md` as the authoritative system-memory contract for AIMonitor behavior.
- Use `docs/agent-memory/RestartContext.md` after plugin, MCP, or context restarts.
- Use `docs/components/` for component ownership and data-flow questions.
- Use `docs/feature-maps/` as the centralized per-feature memory surface. Before changing a workflow, adapter surface, semantic query, or test harness behavior, load the relevant feature map instead of assuming the source tree alone explains the contract.
- For MCP, Roslyn, Workflow, or Indexing parity work, read `docs/feature-maps/CmbMcpCapabilityParity.md`. Do not treat compatibility-shaped stubs or valid JSON response shapes as proof that prior MonitorBaseClaude behavior was faithfully restored.
- Prefer tight loops: small plan, bounded edit, focused test, inspect, then continue. Do not force exhaustive up-front plans when the edge cases need discovery.
- Reason in the cloud; compose locally. Do not write watched source directly.
- The standalone CLI surface is **deprecated**. Codex now drives the workflow through the **MCP surface** using the same tool names as Claude (`start_monitor_session`, `refresh_file`, `new_file`, `stage_candidate_for_review`, `launch_staged_diff`, `record_diff_decision`, `refresh_solution_index`, `find_indexed_references`, etc.). The planned-session + two-gate validation model applies to Codex via MCP exactly as it does to Claude. Do not author new `edit …` CLI command instructions; treat any lingering CLI references as legacy.
- After staging, staged runtime files are immutable review evidence. Further candidate changes go back through the Working file and must be staged again.
- Diff stability depends on complete local edit context: use source-map/symbol context for semantic edits, the whole Working file for text/whole-file edits, or bounded exact replacements constrained by the smallest safe edit rule before staging. The authoritative definition of the smallest safe edit rule lives in `docs/system-memory/README.md`.
- Prefer smoke/regression tests for end-to-end monitor behavior. Use unit tests sparingly for small contracts that smoke tests would make slow or vague, such as path derivation and SQLite row mapping.
- For watched-project edits, start a **planned session**: call `start_monitor_session` with `filesPlanned` listing every file you intend to change. A planned session is required before any `refresh_file`/`new_file`/edit — mutations to unplanned files are rejected. `filesPlanned` is your edit scope; the engine derives a separate inbound-reference closure for index refresh, which you do not hand-type. While planning, run `find_indexed_references` on each symbol you intend to change; if cross-project referencing sites exist, add any consumer you must edit to `filesPlanned` (the engine refreshes those dependent projects' index rows via the closure).
- Then edit monitor-owned working candidates returned by `refresh_file`, and use `stage_candidate_for_review`, `launch_staged_diff`, and `record_diff_decision`. Prefer `replace_text_in_file` for exact find/replace edits because it preserves the working file's dominant line ending and reports match counts. Codex may make multiple tool-driven edits to the Working file before staging; after `stage_candidate_for_review`, any further candidate change must go back through the Working file and be staged again so the recorded staged hash matches the reviewed content. When editing working files directly, preserve the file's existing line endings; for new files, follow `.editorconfig` or the nearest existing project file.
- Validation runs as **two gates** (see `docs/PlannedSessionEditFlow.md`). GATE 1 is a pre-merge Roslyn overlay semantic compile fired once every planned candidate exists; it catches cross-file C# breaks but is *allowed to be noisy* (skips `.razor` markup, no MSBuild/analyzers/source-gen), so known false positives (Razor/generated artifacts, duplicate-inclusion/ambiguous-reference) are operator judgment — the operator may merge anyway. GATE 2 is the full `dotnet build` on the **real watched tree, post-accept** — the authoritative gate; the "hard-stop, not a warning" stance attaches to GATE 2 / genuine breaks. When GATE 1 is a real break and no interactive dialog is available, ask the user in chat and only proceed with the force-validation path after explicit approval. The normal workflow must not silently copy candidates into watched source.
- **Verbatim-merge rule:** merge the staged bytes verbatim in WinMerge — no hand-editing during the merge. GATE 1's validity transfers to watched source only because watched ends up byte-equal to the staged candidate; a hand-edit breaks that equivalence.
- For new watched-project files, use `new_file` with the future watched path instead of creating the watched file directly. Write the candidate in the returned Working path, then stage and launch WinMerge. New-file WinMerge review uses a monitor-owned runtime review target; the operator must save or create the future watched source file before an accepted `record_diff_decision` can classify the accept. `record_diff_decision` verifies the reviewed watched content and staged hash; it must not create or copy the watched source file itself.
- After any accepted or accepted-normalized decision, call `refresh_file` for that watched file before making another edit to it. The refresh captures the saved watched-source bytes, hashes, and any line-ending normalization done by WinMerge or the editor.
- After accepted watched-source changes are recorded and refreshed, ask the user whether they want to run `dotnet build` for the watched solution. Do not silently build the watched solution unless the user has already asked for validation.
- If an accepted or accepted-normalized decision reports a failed `indexRefresh` or leaves the workflow session index-stale, do not trust solution-index rows for follow-up semantic work until `refresh_solution_index`, an equivalent rebuild, or a successful post-accept refresh clears the stale state.
- If a Working-candidate edit is rejected by C# syntax validation, do not force it to WinMerge. Rewrite the candidate into syntactically valid C# and retry the edit. Reserve validation override for syntax-valid candidates that fail GATE 1 as a genuine break, and only after explicit human approval.
- Accepted decisions rebuild the monitor-owned solution index and emit telemetry. Do not rely on stale index rows after accept.
- Runtime history, staged records, validation copies, logs, and index artifacts should remain under `runtime/`. Prefer explicit cleanup/prune commands or UI actions over automatic pruning on every workflow run; clean obvious partial test artifacts deliberately by exact path.

## Razor Boundary

Do not promise Visual Studio-level Razor binding analysis. The current reliable model is:

- normal C# and clean `.razor.cs` code-behind are indexed as C#;
- user-authored `.razor` references are indexed only when compiler/Razor source mappings expose a source symbol cleanly;
- legacy mixed `.razor.cs` files are treated as Razor input only when Razor syntax is present and Razor source mappings exist;
- component parameters, event handlers, implicit markup-to-code relationships, and other full Blazor markup binding semantics are grep/source-map assisted evidence, not a complete semantic contract.

Use grep-verified smoke tests for representative Razor cases instead of trying to prove every markup binding on a production page.

## Edit Safety

The workflow must preserve these safety invariants:

- no direct protected watched-source mutation by the agent;
- bounded staged candidates;
- WinMerge review/save before accepted watched-source mutation;
- stable diff review;
- vote-plus-hash accept/reject classification;
- explicit dirty/unexpected recovery;
- regression tests for every fixed finding when feasible.
