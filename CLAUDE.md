# AIMonitor Claude Rules

Claude should treat this repository as the V2 implementation of an AI safe edit monitor.

Use the project structure:

- `src/` for product code.
- `tests/` for tracked regression/unit/integration/smoke tests.
- `samples/` for watched-project examples.
- `docs/` for architecture, findings, decisions, workflows, and feature maps.

The core design rule:

> MCP is not the workflow. MCP is the Claude adapter over the shared workflow engine.

Claude Code MCP bindings should launch `AIMonitor.McpStdioBridge`, not `AIMonitor.McpServer` directly. The stdio bridge is intentionally thin: it connects Claude's MCP stdio stream to the WinForms-owned MCP proxy hub. WinForms receives live MCP traffic first, records request/response telemetry, and relays to the combined MCP server behind it.

Until the Claude Code launcher working directory is verified live, prefer the `dotnet <absolute-path-to-AIMonitor.McpStdioBridge.dll>` binding shape in local MCP config. The repo template uses relative paths for portability, but an installed user binding should use absolute paths for the bridge DLL, `--repo-root`, and `--config`.

Prefer MSBuild project truth over directory enumeration. When adding behavior, add tests beside it.

Do not hide data row/result classes inside repository classes. Schema-shaped POCOs get their own files so persisted/query data stays visible in reviews.

Treat MSBuild project/document loading as language-neutral. C# is the first semantic indexing provider because it is the current product focus; do not describe the whole architecture as C#-only.

Prefer workflow smoke/regression tests for monitor behavior. Tiny unit tests are acceptable when they pin a narrow contract that would be noisy in a smoke test.

Codex parity starts with the CLI workflow edit loop. For watched-project edits, Codex should use `edit refresh` to create a monitor-owned working candidate, edit that candidate, then use `edit stage`, `edit launch-diff`, and `edit record-decision`. `edit replace-text` is primarily a Codex-safe local command path for exact replacements; Claude may keep using its MCP/editor surface when that surface already preserves stable local edits. Claude/Codex may make multiple tool calls against the Working file before staging. Once `edit stage` records a candidate hash, further candidate changes must be made in the Working file and staged again before review or accept; staged runtime files are review artifacts, not the editing surface. Agents should preserve existing line endings when editing Working files directly; for new files, follow `.editorconfig` or the nearest existing project file. `edit launch-diff` runs the full pre-merge validation gate before WinMerge. If validation fails, the user must explicitly approve the validation override dialog before WinMerge opens. If no interactive dialog is available, the agent must ask the user in chat and rerun with `--force-validation` only after explicit approval. Do not describe direct watched-source patching or silent candidate copying as the clean path.

For new watched-project files, use `edit new --file <future-watched-path>`. This creates an empty monitor-owned Working candidate and later stages against a blank runtime review baseline. `edit launch-diff` opens WinMerge against monitor-owned runtime files for new-file review. The human/operator must save or create the future watched source file before `edit record-decision --decision accepted --expected-staged-hash <hash>` can classify the accept. `record-decision` verifies the reviewed watched content and staged hash; it must not create or copy the watched source file itself. Rejected new-file decisions leave the watched source absent.

After `edit record-decision` returns an accepted or accepted-normalized outcome, the next operation on that watched file must be `edit refresh`. Accepted decisions must include the staged hash expected by the operator. The refresh captures the watched-source bytes that WinMerge or the editor actually saved, including hashes and line endings. `accepted-normalized` is a successful accept with line-ending or equivalent normalization; do not treat it as dirty.

Accepted decisions rebuild the monitor-owned solution index and emit telemetry. Check the returned `indexRefresh` status before relying on fresh index rows.

Runtime workflow history, staged records, validation copies, logs, and index artifacts belong under `runtime/`. Prefer explicit cleanup/prune commands or UI buttons over automatic pruning on every run; clean partial test artifacts deliberately by exact path.

## Claude Skill Cards

Claude should read the focused AIMonitor skill cards from `docs/claude-skills/` when operating this repo or an AIMonitor watched project. These are not Markdown includes; they are required context files to open before editing. Start with:

- `docs/claude-skills/AIMonitorWorkflowQuickStart.md`
- `docs/claude-skills/SkillRouter.md`

For C# edits, treat the source-map tools as first-class precision tools, not optional fallback:

- use the solution index for broad discovery;
- use `get_source_map`, `get_symbol`, and `submit_symbol` for precise symbol replacement;
- use the typed Roslyn edit tools for additions/removals when they fit;
- use text/span tools for exact non-symbol edits;
- use `submit_file` for new files, generated files, or deliberate whole-file replacement.

Do not load every skill card by default. Route to the smallest card needed for the current task, then use live MCP tool descriptions for exact argument names.

## Razor Guidance

For Blazor/Razor projects, AIMonitor V2 indexes the parts it can defend:

- C# symbols and references from normal `.cs` files.
- Clean `.razor.cs` code-behind as normal C#.
- `.razor` and legacy mixed `.razor.cs` references when Razor/compiler source mappings point back to user-authored source.

Do not assume AIMonitor currently models every Razor markup binding, component parameter, or event handler string exactly like Visual Studio. If a task needs that level of precision, use build/compiler feedback plus grep and focused smoke tests. Add only representative hard assertions for known-good mapped cases.
