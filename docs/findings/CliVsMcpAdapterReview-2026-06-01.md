---
status: partially-addressed
type: finding
created: 2026-06-01
scope: CLI vs MCP adapters over the shared workflow + data engine
method: read-only multi-agent review (5 parallel reviewers + verifying synthesis), claims confirmed against source
confidence: high
---

## Summary

## Resolution update - 2026-06-01

Addressed in the follow-up fix after this review:

- Engine now persists pre-merge validation status on each staged record and `RecordDecision` refuses accepted decisions if validation never ran or failed without explicit force approval.
- Accepted `dirty-unexpected` decisions now throw instead of recording an accepted decision against stale watched bytes.
- MCP `submit_file` now delegates to `WorkflowEditService.SubmitFile`, which creates/uses the monitor-owned session, enforces refresh-required, and normalizes submitted content to the Working file's existing line endings.
- MCP write helpers that call `EnsureSession` now reject refresh-required sessions before touching the Working candidate.
- `get_ledger` now validates supplied ledger paths with `Path.GetRelativePath` boundary checks instead of a raw string prefix check.
- The stale VS Code `build bridge` task now targets `AIMonitor.McpStdioBridge`.
- `src/AIMonitor.Storage` was removed; durable store behavior remains in `AIMonitor.Data` until a real tested storage boundary exists.
- `launch-diff` orchestration is shared by CLI and MCP through `AIMonitor.Runtime.StagedDiffLaunchWorkflow`, so validation,
  prompt fallback, telemetry, review-file preparation, and WinMerge launch use the same path.
- Span find/replace positioning moved into `WorkflowEditService`, including CRLF-aware line/column semantics.
- `replace_text_in_file` / `edit replace-text` now honor `occurrenceIndex` through the workflow service.
- Stage/launch/decision replies are compact by default, with `--verbose` / `verbose: true` for full inline records and
  `edit staged-record` / `get_staged_record` for explicit fetch-back.
- Workflow manifests now use an advisory per-file lock around read-modify-write paths.
- MCP index reference tools now return a visible guidance payload for source-map selector keys instead of a silent `[]`;
  `find_indexed_callers` now filters invocation/object-creation rows instead of broad identifier references.
- Roslyn source-map/symbol calls now return actionable MCP-visible guidance when pointed at Razor markup.

Still open/backlog from this review:

- Push selected index filters into SQL and reduce repeated schema setup on reads.
- Revisit raw build-output fallback for validation diagnostics and the lower-priority cleanup items.
- MCP elicitation and dependency-aware/incremental validation/indexing remain deliberately deferred.

Read-only deep dive evaluating whether the AIMonitor monitor code meets its core design rule —
**"MCP is not the workflow. MCP is the Claude adapter over the shared workflow engine"** — and whether
the CLI (Codex's path) and MCP (Claude's path) both resolve down to **one** shared workflow engine and
**one** shared data surface.

**Verdict: the intended architecture largely holds, with two real cracks.**

Confirmed by reading both adapters directly:

- There is **one data surface** — `AIMonitor.Data.SolutionIndexQueryService`. All reads route through it;
  neither adapter touches `SolutionIndexStore`/`SolutionIndexDatabase` SQL directly
  (CLI `Program.cs:478-485`; MCP singleton at `Program.cs:32`).
- There is **one edit engine** — `AIMonitor.Workflow.WorkflowEditService` / `RoslynEditService`. The core loop
  (`Refresh/NewFile/GetStatus/ReplaceText/Stage/GetStagedRecord/PrepareReviewFileForLaunch/RecordDiffLaunch/RecordDecision`)
  is engine-owned and called by both adapters (CLI `Program.cs:184,188-204`; MCP `Program.cs:618,848`).
- **Decision classification** (`ReviewDecisionClassifier`) and **hashing** (`FileHash`) live only in the engine.
- **Dependency direction is clean and acyclic**: `AIMonitor.Workflow` references only `Core`; both adapters
  reference the same shared set; neither adapter references the other; no WinMerge/Process logic leaks into the engine.

The two cracks (both expected for a V0.1 built in a day, not yet independently reviewed):

1. **The safety-critical validation gate is duplicated AND not engine-owned.**
2. **A few MCP-only edit helpers leak workflow byte-handling into the adapter** (notably line-ending normalization).

> Until #1 is addressed, the shared engine cannot itself guarantee the safety invariant — which is the whole point of
> the harness. Treat the engine-owned validation gate as the prerequisite before broad testing.

---

## Adapter parity: CLI vs MCP

| Capability | CLI command | MCP tool | Shared service called | Parity |
|---|---|---|---|---|
| Monitor status | `status` | `get_monitor_status` | `SolutionIndexQueryService.GetMonitorStatus` | Shared, parity |
| Index summary/projects/docs/symbols/refs/packages | `index summary/projects/documents/symbols/references/...` | `get_solution_index*`, `find_indexed_*`, `query_solution_index` | `SolutionIndexQueryService.*` | Shared, parity |
| Index rebuild | `index rebuild` (`Program.cs:415-417`) | `refresh_solution_index` | indexing primitives, **composed inline in BOTH** | Shared primitives, duplicated composition |
| Refresh working candidate | `edit refresh` | `refresh_file` | `WorkflowEditService.Refresh` | Shared, parity |
| New file | `edit new` | `new_file` | `WorkflowEditService.NewFile` | Shared, parity |
| Replace exact text | `edit replace-text` | `replace_text_in_file` | `WorkflowEditService.ReplaceText` | Shared; MCP ignores `occurrenceIndex` (`Program.cs:610`) |
| Whole-file submit | (none) | `submit_file` | **none** — raw `File.WriteAllText` (`Program.cs:587`) | MCP-only, diverges from engine normalization |
| Span find/replace | (none) | `find_text_span`, `replace_span_in_file` | **none** — adapter-local (`Program.cs:634-698`) | MCP-only, no engine method |
| Roslyn typed edits | (none) | `submit_symbol`, `add_method`, etc. | `RoslynEditService.*` | MCP-only but properly engine-backed |
| Stage candidate | `edit stage` | `stage_candidate_for_review` | `WorkflowEditService.Stage` | Shared, parity |
| Launch validated diff | `edit launch-diff` | `launch_staged_diff` | `PreMergeValidationService` + `WinMergeDiffToolLauncher` + `RecordDiffLaunch` | Shared services, **orchestration duplicated** |
| Record decision | `edit record-decision` | `record_diff_decision` | `WorkflowEditService.RecordDecision` | Shared, parity |
| Accept (shortcut) | `edit accept` (undocumented, `--expected-hash`) | (none) | `WorkflowEditService.Accept` | CLI-only, undocumented, flag mismatch |
| Reject (shortcut) | `edit reject` (undocumented) | (none) | `WorkflowEditService.Reject` | CLI-only, undocumented |
| Compare snapshot | (none) | `compare_file` | `WorkflowEditService.Compare` | MCP-only |
| Sessions/runs/ledgers | (none) | `start_monitor_session`, `list_monitor_runs`, etc. | inline in adapter | MCP-only |

---

## Where the adapters share code (good)

- **Single data surface.** Both adapters route every read through `SolutionIndexQueryService`; neither touches
  `SolutionIndexStore`/`SolutionIndexDatabase` SQL directly (CLI `Program.cs:478-485`; MCP `queryService.ListSymbols/ListDocuments/GetMonitorStatus`). No duplicated query logic.
- **Single edit engine for the core loop.** `Refresh/NewFile/GetStatus/ReplaceText/Stage/GetStagedRecord/PrepareReviewFileForLaunch/RecordDiffLaunch/RecordDecision`
  are all engine methods both adapters call (CLI `Program.cs:188-204`; MCP `Program.cs:618,848,884,930,940`).
- **Decision classification and hashing are engine-only.** `ReviewDecisionClassifier` is encapsulated inside
  `RecordDecision` (`WorkflowEditService.cs:519`); neither adapter re-derives classification. `FileHash` is the single hash source.
- **Roslyn edits delegate cleanly.** `RoslynEditService` composes `WorkflowEditService` + `WorkflowEditPaths` and routes
  through the same session lifecycle — no duplicated session/hash logic.
- **Runtime boundary shared.** `WinMergeDiffToolLauncher`, `DiffLaunchRequest/Result`, and `PreMergeValidationOverridePrompt`
  (AIMonitor.Runtime) are called identically; no process-launch logic leaks into the engine.
- **Clean dependency direction.** `AIMonitor.Workflow` references only Core; adapters reference the same shared set with no cross-adapter reference.

---

## Where they duplicate or diverge (issues)

- **launch-diff / record-decision orchestration duplicated verbatim.** CLI `LaunchDiff` (`Program.cs:207-277`) and
  MCP `LaunchStagedDiff` (`Program.cs:876-942`) are line-for-line equivalent (validate → `PreMergeValidationOverridePrompt.Prompt`
  → premerge telemetry → `RecordDiffLaunch(blocked)` → `PrepareReviewFileForLaunch` → `WinMergeDiffToolLauncher.Launch`
  → `RecordDiffLaunch(updated)`). The post-accept index-refresh block is likewise duplicated
  (CLI `CreateDecisionResponse` `:309-336` vs MCP `RecordDiffDecision` `:842-873`). Already drifting in wording.
  *Fix: factor a single `WorkflowEditService.LaunchDiff(...)` / `RecordReviewedDecision(...)` the thin adapters call.*
- **submit_file bypasses engine normalization (high).** `File.WriteAllText(status.WorkingFilePath, content)` (MCP `Program.cs:587`)
  vs engine's `DetectDominantLineEnding` / `NormalizeLineEndingsForFile` in `ReplaceText` (`WorkflowEditService.cs:216-238`).
  Can flip every line ending and force `accepted-normalized` churn; violates CLAUDE.md "preserve existing line endings."
- **Span edits are MCP-only workflow logic.** `ReplaceSpanInFile` / `FindTextSpan` / `GetIndex` / `GetPosition`
  (`Program.cs:634-698`, `1212-1288`) do byte positioning and `File.WriteAllText` with no engine method and no CLI parity;
  `GetPosition` counts only `\n`, so CRLF column math can desync. *Fix: push span logic into the engine and normalize.*
- **Undocumented CLI `accept`/`reject` with mismatched flag.** Dispatched at CLI `Program.cs:200-201`; absent from help
  (`:35-41`); `Accept` requires `--expected-hash` (`:304`) while documented `record-decision` uses `--expected-staged-hash` (`:291`).
  Real footgun. *Fix: remove or document, and unify the flag.*
- **Index rebuild composition inline in both adapters.** CLI `Program.cs:415-417` and MCP `RefreshSolutionIndex` each wire
  `SolutionIndexStore` + `SolutionIndexBuilder` + `MSBuildWorkspaceLoader`. *Fix: a shared rebuild façade in AIMonitor.Indexing.*
- **replace_text_in_file advertises but ignores occurrenceIndex.** `_ = occurrenceIndex;` (`Program.cs:610`); engine does
  global `Replace` gated by `expectedMatches` (`WorkflowEditService.cs:238`), so a single chosen occurrence cannot be targeted.
  *Fix: honor it (route to span) or drop it.*

---

## Shared workflow engine issues

- **Engine does not own the validation gate (high).** `RecordDiffLaunch` (`WorkflowEditService.cs:432-440`) sets `LaunchStatus`
  from an adapter bool; `RecordDecision` accept guard only checks `LaunchStatus=="launched"` (`:502-505`). A buggy/future adapter
  calling `RecordDiffLaunch(id, launched:true, ...)` with no validation, then `RecordDecision(id,"accepted",hash)`, succeeds.
  (Note: `RecordDecision` does re-hash the staged file at `:496-500`, so staged-content tampering is caught — but validation
  status is not.) *Fix: record `validationStatus`/`forceApproved` on `StagedEditRecord` and re-check in `RecordDecision`.*
- **Accept does not block `dirty-unexpected` (high).** On accept with mismatched watched bytes the classifier returns
  `dirty-unexpected`, yet `RecordDecision` still writes `Decision="accepted"` and sets `RequiresRefresh=false` (`:542`),
  leaving the session editable against a stale `OriginalHash`. *Fix: `Accept` should throw on `dirty-unexpected`.*
- **Stage byte-exact guard vs normalization-aware accept (medium).** `Stage` uses `FilesAreIdentical` byte comparison
  (`WorkflowEditService.cs:294`) while the classifier treats EOL-only diffs as `accepted-normalized`; a pure EOL change passes
  Stage, runs full validation + WinMerge, then forces a refresh. Two conflicting notions of "same."
  *Fix: pick one authoritative comparison and document it.*
- **GetReviewedFilePath ignores ReviewBaselineFilePath (medium).** `GetReviewedFilePath` returns `record.WatchedFilePath`
  unconditionally (`:549-552`) and never reads the `ReviewBaselineFilePath` it was given; works for new files only by coincidence.
  *Fix: use the baseline or remove the field/abstraction.*
- **Non-atomic multi-step file reads (medium).** No lock/ownership across Stage → validate → RecordDecision; manifest is
  one-per-relative-path last-writer-wins, so concurrent CLI+MCP sessions on the same path can interleave.
  *Fix: advisory lock around manifest read-modify-write.*
- **PreMergeValidationService parses `: error ` literal (medium).** `ExtractBuildErrors` (`PreMergeValidationService.cs:417-424`)
  keys on the English token; block decision is sound (driven by exit code) but localized output yields empty diagnostics,
  weakening the human override dialog. *Fix: surface raw build output when no diagnostics parsed.*
- **GetStatus re-runs classifier with synthetic decisions (low).** `GetStatus` (`:152-185`) feeds fake "rejected"/"accepted"
  inputs to probe state; brittle coupling to classifier branch ordering. *Fix: a pure classify-current-state method.*
- **Id slice magic constants (low).** `[..44]` (`:279`) vs `[..42]` (Stage / PreMergeValidationService) — latent
  `ArgumentOutOfRangeException` if the prefix ever shortens.

---

## Shared data surface issues

- **ListDocuments/ListSymbols load full tables, filter in memory (medium).** `store.ListDocuments()`/`ListSymbols()` no-arg
  full SELECTs then LINQ `Where` (`SolutionIndexQueryService.cs:60-92`, `ListReferencesInFile` `:106-108`);
  `idx_documents_file`/`idx_symbols_file`/`idx_symbols_name` go unused. `ListReferences(stableKey)` already shows the correct
  pushed-down pattern. *Fix: push filters into parameterized SQL.*
- **EnsureCreated() on every read (medium).** Each store read re-runs the full DDL/migration sequence and opens a fresh
  connection; a composite MCP overview re-runs it 3+ times per request. *Fix: run schema creation/migration once at startup.*
- **DateTimeOffset.Parse without InvariantCulture (low).** `GetSummary` (`SolutionIndexStore.cs:69`) parses round-trip "O"
  timestamps with ambient culture. *Fix: `InvariantCulture` + `RoundtripKind`.*
- **NOT NULL columns vs nullable writes coerced to DBNull (low).** `Execute` does `value ?? DBNull.Value` against `text not null`
  columns; `InsertProject` lacks the guard entirely. *Fix: coalesce to `string.Empty` at write or relax NOT NULL.*
- **Positional ordinal mapping fragility (info).** 14-field all-string `IndexedProjectRow` mapped by `GetString(0..13)` across
  three files; a mid-list column insert silently transposes with no compile-time catch. *Fix: `GetOrdinal` or a single mapping helper.*
- **WAL on every short-lived connection, no checkpoint (info).** Leaves `-wal`/`-shm` sidecars under `runtime/`; little benefit
  for this read-mostly workload.
- **Empty ContentHash is ambiguous (info).** Indistinguishable between "not computed" and "empty file"; a concern only if
  `content_hash` becomes load-bearing for staleness.

---

## Wiring & hygiene

- **AIMonitor.Bridge is fully dead and still referenced by a broken task (medium, addressed 2026-06-01).** No `*.csproj` under `src/AIMonitor.Bridge`
  and it is not in the slnx, yet `.vscode/tasks.json:29-39` ("build bridge") points at
  `${workspaceFolder}/src/AIMonitor.Bridge/AIMonitor.Bridge.csproj`, which will fail.
  *Fix: delete the stale bin/obj and remove/retarget the task to `AIMonitor.McpStdioBridge`.*
- **AIMonitor.Storage is a behavior-free placeholder (medium).** Single file `StorageBoundary.cs` holding one const string;
  no schema, state, or tests. CLAUDE.md forbids re-adding Storage without behavior+tests; the SQLite responsibility currently
  lives in AIMonitor.Data. Correctly excluded from the slnx, but the folder is purposeless. *Fix: delete until durable state actually migrates.*
- **RuntimeBoundary marker constant is unused (low).** Same empty-marker pattern as StorageBoundary; if these are boundary
  docs they belong in `docs/`, not compiled symbols. (Runtime itself is a real, used library.)
- **Dependency direction is correct (good).** Workflow → Core only; both adapters reference the same shared set; no cross-adapter
  reference; no WinMerge/Process logic leaked into the engine.
- **Native TaskDialog cleanup hardcodes count (low).** `PreMergeValidationOverridePrompt.cs:99` loops `index < 2` instead of
  `buttons.Length`; no live bug (2 buttons today) but leaks one marshalled struct if a third is added.

---

## Prioritized issue list

### High
- Engine cannot enforce its own validation gate — `WorkflowEditService.cs:432-440` (RecordDiffLaunch) + `:502-505` (RecordDecision).
  *Fix: persist validationStatus/forceApproved on StagedEditRecord and re-check on accept.*
- `Accept` permits `dirty-unexpected` — `WorkflowEditService.cs:519-544`, `554-572`.
  *Fix: throw on dirty-unexpected instead of recording an accepted decision with an editable session.*
- `submit_file` bypasses line-ending normalization — MCP `Program.cs:587`.
  *Fix: add a `WorkflowEditService.SubmitFile` that normalizes like ReplaceText; adapter delegates.*

### Medium
- launch-diff/record-decision orchestration duplicated — CLI `Program.cs:207-277`/`309-336` vs MCP `Program.cs:876-942`/`842-873`.
  *Fix: single engine LaunchDiff + decision-response method.*
- Span edit logic is MCP-only, no engine method, CRLF column desync — MCP `Program.cs:634-698`, `1212-1288`.
  *Fix: move span positioning/edit into the engine with EOL-aware columns.*
- `replace_text_in_file` ignores advertised `occurrenceIndex` — MCP `Program.cs:610`.
  *Fix: honor (route to span replace) or remove the parameter.*
- Undocumented CLI `accept`/`reject` with flag mismatch — CLI `Program.cs:200-201`, `304` (`--expected-hash` vs `--expected-staged-hash`).
  *Fix: remove or document and unify the flag.*
- ListDocuments/ListSymbols full-table scans — `SolutionIndexQueryService.cs:60-92`. *Fix: push filters into parameterized SQL.*
- EnsureCreated() on every read — `SolutionIndexStore.cs` read methods. *Fix: create/migrate schema once at startup.*
- Stage byte-exact vs normalization-aware accept asymmetry — `WorkflowEditService.cs:294`. *Fix: choose one authoritative comparison.*
- Non-atomic file reads / no manifest lock — `WorkflowEditService.cs:372-407`, `496-500`. *Fix: advisory lock around manifest read-modify-write.*
- PreMergeValidationService English-token error parse — `PreMergeValidationService.cs:417-424`. *Fix: surface raw build output when no diagnostics parsed.*
- AIMonitor.Bridge dead + broken build task — `.vscode/tasks.json:29-39`. **Addressed 2026-06-01.**
- AIMonitor.Storage empty placeholder — `src/AIMonitor.Storage/StorageBoundary.cs`. *Fix: delete until real durable state migrates.*

### Low
- `GetReviewedFilePath` ignores ReviewBaselineFilePath — `WorkflowEditService.cs:549-552`. *Fix: use the baseline or drop the field.*
- Index rebuild composed inline in both adapters — CLI `Program.cs:415-417`, MCP RefreshSolutionIndex. *Fix: shared rebuild façade in AIMonitor.Indexing.*
- GetStatus probes via synthetic decisions — `WorkflowEditService.cs:152-185`. *Fix: pure classify-current-state method.*
- Id slice magic constants 44/42 — `WorkflowEditService.cs:279`/`383`, `PreMergeValidationService.cs:47`. *Fix: guarded length helper.*
- CLI failures collapse to exit 1, no JSON error envelope — CLI `Program.cs:388-392`. *Fix: structured JSON error + differentiated exit codes.*
- CLI option parser has no "flag expecting value" notion — CLI `Program.cs:502-513`, `526-540`. *Fix: treat a following `--token` as missing value.*
- DateTimeOffset.Parse ambient culture — `SolutionIndexStore.cs:69`. *Fix: InvariantCulture + RoundtripKind.*
- NOT NULL vs DBNull coercion — `SolutionIndexStore.cs:453-468`, InsertProject `:292-305`. *Fix: coalesce to empty at write.*
- find_indexed_callers includes Identifier refs — MCP `Program.cs:312-322`. *Fix: invocation-only filter, ideally shared query.*
- find_indexed_relationships always returns [] — MCP `Program.cs:326-338`. *Fix: return explicit not-implemented status.*
- TaskDialog cleanup hardcodes `< 2` — `PreMergeValidationOverridePrompt.cs:99`. *Fix: use buttons.Length.*
- RuntimeBoundary unused marker constant — `RuntimeBoundary.cs`. *Fix: move boundary docs to docs/.*

### Info
- Dead `SemanticEditNotImplemented` helper — MCP `Program.cs:1173-1184`. *Fix: delete.*
- Roslyn edit tools lack per-parameter `[Description]` — MCP `Program.cs:721-838`. *Fix: add descriptions for schema quality.*
- CLI re-parses response JSON 3x for telemetry — CLI `Program.cs:382-384`, `603-640`. *Fix: parse once and reuse.*
- Positional ordinal row mapping fragility — `SolutionIndexStore.cs:200-214`. *Fix: GetOrdinal/mapping helper.*
- WAL sidecar files under `runtime/`, no checkpoint — `SolutionIndexDatabase.cs:16-27`.
- Ambiguous empty ContentHash — `IndexedDocumentRow.cs:9`, store write `:328`.

---

## Notes

- Method: five parallel read-only reviewers (CLI adapter, MCP adapter, shared workflow engine, data surface, wiring/hygiene)
  followed by a synthesis pass that independently re-opened both `Program.cs` files to confirm the shared-vs-duplicated claims.
- Out of scope by request: VS Code / MCP server connection wiring and the stdio-bridge pipe handshake.
- Line numbers reflect the working tree as of 2026-06-01 and will drift as the files change.
