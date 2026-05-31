# AIMonitor Claude Rules

Claude should treat this repository as the V2 implementation of an AI safe edit monitor.

Use the project structure:

- `src/` for product code.
- `tests/` for tracked regression/unit/integration/smoke tests.
- `samples/` for watched-project examples.
- `docs/` for architecture, findings, decisions, workflows, and feature maps.

The core design rule:

> MCP is not the workflow. MCP is the Claude adapter over the shared workflow engine.

Prefer MSBuild project truth over directory enumeration. When adding behavior, add tests beside it.

Do not hide data row/result classes inside repository classes. Schema-shaped POCOs get their own files so persisted/query data stays visible in reviews.

Treat MSBuild project/document loading as language-neutral. C# is the first semantic indexing provider because it is the current product focus; do not describe the whole architecture as C#-only.

Prefer workflow smoke/regression tests for monitor behavior. Tiny unit tests are acceptable when they pin a narrow contract that would be noisy in a smoke test.

Codex parity starts with the CLI workflow edit loop. For watched-project edits, Codex should use `edit refresh` to create a monitor-owned working candidate, edit that candidate, then use `edit stage`, `edit launch-diff`, and `edit record-decision`. `edit replace-text` is primarily a Codex-safe local command path for exact replacements; Claude may keep using its MCP/editor surface when that surface already preserves stable local edits. Agents should preserve existing line endings when editing Working files directly; for new files, follow `.editorconfig` or the nearest existing project file. `edit launch-diff` runs the full pre-merge validation gate before WinMerge. If validation fails, the user must explicitly approve the validation override dialog before WinMerge opens. If no interactive dialog is available, the agent must ask the user in chat and rerun with `--force-validation` only after explicit approval. Do not describe direct watched-source patching or silent candidate copying as the clean path.

For new watched-project files, use `edit new --file <future-watched-path>`. This creates an empty monitor-owned Working candidate and later stages against a blank review baseline. `edit launch-diff` may create an empty watched-source placeholder so WinMerge has a real save target. The operator save path fills that placeholder, and `edit record-decision` verifies the result. If the operator rejects and the placeholder is still empty, `record-decision rejected` removes it.

After `edit record-decision` returns an accepted or accepted-normalized outcome, the next operation on that watched file must be `edit refresh`. This captures the watched-source bytes that WinMerge or the editor actually saved, including hashes and line endings. `accepted-normalized` is a successful accept with line-ending or equivalent normalization; do not treat it as dirty.

Accepted decisions rebuild the monitor-owned solution index and emit telemetry. Check the returned `indexRefresh` status before relying on fresh index rows.

Runtime workflow history, staged records, validation copies, logs, and index artifacts belong under `runtime/`. Prefer explicit cleanup/prune commands or UI buttons over automatic pruning on every run; clean partial test artifacts deliberately by exact path.

## Razor Guidance

For Blazor/Razor projects, AIMonitor V2 indexes the parts it can defend:

- C# symbols and references from normal `.cs` files.
- Clean `.razor.cs` code-behind as normal C#.
- `.razor` and legacy mixed `.razor.cs` references when Razor/compiler source mappings point back to user-authored source.

Do not assume AIMonitor currently models every Razor markup binding, component parameter, or event handler string exactly like Visual Studio. If a task needs that level of precision, use build/compiler feedback plus grep and focused smoke tests. Add only representative hard assertions for known-good mapped cases.
