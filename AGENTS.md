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
- For MCP, Roslyn, Workflow, or Indexing parity work, read `docs/feature-maps/CmbMcpCapabilityParity.md`. Do not treat compatibility-shaped stubs or valid JSON response shapes as proof that prior MonitorBaseClaude behavior was faithfully restored.
- Prefer tight loops: small plan, bounded edit, focused test, inspect, then continue. Do not force exhaustive up-front plans when the edge cases need discovery.
- Reason in the cloud; compose locally. Do not write watched source directly.
- After staging, staged runtime files are immutable review evidence. Further candidate changes go back through the Working file and must be staged again.
- Diff stability depends on complete local edit context: use source-map/symbol context for semantic edits, the whole Working file for text/whole-file edits, or bounded exact replacements constrained by the smallest safe edit rule before staging.
- Prefer smoke/regression tests for end-to-end monitor behavior. Use unit tests sparingly for small contracts that smoke tests would make slow or vague, such as path derivation and SQLite row mapping.
- For watched-project edits, Codex should edit monitor-owned working candidates returned by `edit refresh`, then use `edit stage`, `edit launch-diff`, and `edit record-decision`. Prefer `edit replace-text` for exact find/replace edits because it preserves the working file's dominant line ending and reports match counts. Claude/Codex may make multiple tool-driven edits to the Working file before staging; after `edit stage`, any further candidate change must go back through the Working file and run `edit stage` again so the recorded staged hash matches the reviewed content. When editing working files directly, preserve the file's existing line endings; for new files, follow `.editorconfig` or the nearest existing project file. `edit launch-diff` must run the full pre-merge validation gate before WinMerge. If validation fails, the user must explicitly approve the validation override dialog before WinMerge opens. If no interactive dialog is available, the agent must ask the user in chat and rerun with `--force-validation` only after explicit approval. The normal workflow must not silently copy candidates into watched source.
- For new watched-project files, use `edit new --file <future-watched-path>` instead of creating the watched file directly. Write the candidate in the returned Working path, then stage and launch WinMerge. New-file WinMerge review uses a monitor-owned runtime review target; the human/operator must save or create the future watched source file before `edit record-decision --decision accepted --expected-staged-hash <hash>` can classify the accept. `record-decision` verifies the reviewed watched content and staged hash; it must not create or copy the watched source file itself.
- After any accepted or accepted-normalized decision, run `edit refresh --file <watched-file>` before making another edit to that file. The refresh captures the saved watched-source bytes, hashes, and any line-ending normalization done by WinMerge or the editor.
- If a Working-candidate edit is rejected by C# syntax validation, do not force it to WinMerge. Rewrite the candidate into syntactically valid C# and retry the edit. Reserve validation override for syntax-valid candidates that fail the pre-merge build gate and only after explicit human approval.
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
