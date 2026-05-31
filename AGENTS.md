# AIMonitor Agent Instructions

This repository is the clean V2 monitor implementation.

- Keep `src`, `tests`, `samples`, and `docs` as top-level peers.
- Do not place product source at repository root.
- Do not use C# top-level statements.
- Keep MCP, CLI, and UI as adapters over shared Core/Workflow/MSBuild/Indexing services.
- Add tests in parallel with workflow behavior.
- Prefer MSBuild-loaded project truth over filesystem guessing.
- Keep generated runtime state under `runtime/` and out of watched projects.
- Do not hide data row/result classes inside repositories; schema-shaped POCOs get their own files.
- Treat MSBuild project/document loading as language-neutral; C# is the first semantic provider, not the whole architecture.
- Prefer smoke/regression tests for end-to-end monitor behavior. Use unit tests sparingly for small contracts that smoke tests would make slow or vague, such as path derivation and SQLite row mapping.
- For watched-project edits, Codex should edit monitor-owned working candidates returned by `edit refresh`, then use `edit stage`, `edit launch-diff`, and `edit record-decision`. Prefer `edit replace-text` for exact find/replace edits because it preserves the working file's dominant line ending and reports match counts. When editing working files directly, preserve the file's existing line endings; for new files, follow `.editorconfig` or the nearest existing project file. `edit launch-diff` must run the full pre-merge validation gate before WinMerge. If validation fails, the user must explicitly approve the validation override dialog before WinMerge opens. If no interactive dialog is available, the agent must ask the user in chat and rerun with `--force-validation` only after explicit approval. The normal workflow must not silently copy candidates into watched source.
- For new watched-project files, use `edit new --file <future-watched-path>` instead of creating the watched file directly. Write the candidate in the returned Working path, then stage, launch WinMerge, and record the decision. `edit launch-diff` may create an empty watched-source placeholder so WinMerge has a real save target; if the operator rejects and the placeholder is still empty, `record-decision rejected` removes it.
- After any accepted or accepted-normalized decision, run `edit refresh --file <watched-file>` before making another edit to that file. The refresh captures the saved watched-source bytes, hashes, and any line-ending normalization done by WinMerge or the editor.
- Accepted decisions rebuild the monitor-owned solution index and emit telemetry. Do not rely on stale index rows after accept.
- Runtime history, staged records, validation copies, logs, and index artifacts should remain under `runtime/`. Prefer explicit cleanup/prune commands or UI actions over automatic pruning on every workflow run; clean obvious partial test artifacts deliberately by exact path.

## Razor Boundary

Do not promise Visual Studio-level Razor binding analysis in V2. The current reliable model is:

- normal C# and clean `.razor.cs` code-behind are indexed as C#;
- user-authored `.razor` references are indexed only when compiler/Razor source mappings expose a source symbol cleanly;
- legacy mixed `.razor.cs` files are treated as Razor input only when Razor syntax is present and Razor source mappings exist;
- literal component/event binding strings and full Blazor UI binding semantics are not a hard correctness contract yet.

Use grep-verified smoke tests for representative Razor cases instead of trying to prove every markup binding on a production page.

## Edit Safety

The V2 workflow must preserve these V1 invariants:

- no direct protected watched-source mutation by the agent;
- bounded staged candidates;
- WinMerge review/save before accepted watched-source mutation;
- stable diff review;
- vote-plus-hash accept/reject classification;
- explicit dirty/unexpected recovery;
- regression tests for every fixed finding when feasible.
