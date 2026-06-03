# CMB MCP Parity Restore Plan

## Human Notes

This is the full recovery plan for the MonitorBaseClaude-to-AIMonitor MCP parity loss.

The goal is not to copy old code blindly. The goal is to restore every still-valid capability in the correct AIMonitor layer and prove it with tests that check behavior and useful payload content, not just response shape.

This plan covers:

- every hand-verified gap in `CmbMcpCapabilityParity.md`;
- every item in the `MISSING` appendix of `CmbMcpParityGap-2026-06-02.md`;
- every item in the `DEGRADED - HIGH` appendix of that finding.
- every item in the `DEGRADED - MEDIUM` appendix of that finding.

Low appendix items remain in the parity ledger as later reconciliation unless they are naturally fixed by the missing/high/medium work.

## Ground Rules

1. Shared service first.
   - Index facts belong in Indexing/Data.
   - Safe edit lifecycle belongs in Workflow.
   - Source-map and Roslyn edit facts belong in Workflow/Roslyn services.
   - MCP only exposes and shapes responses.
   - CLI receives the behavior only where the CLI command surface overlaps the same workflow/query contract.

2. No more silent descopes.
   - If a CMB behavior is not restored, the plan must say whether it is intentionally replaced, intentionally deferred, or not applicable.
   - Tool descriptions must not claim behavior the implementation does not perform.

3. Tests prove useful behavior.
   - A passing "tool returns JSON" test is not enough.
   - Every restored item needs an owning-layer test and, where practical, an MCP smoke proving the visible tool has meaningful data.

4. Safety floor stays intact.
   - Do not weaken watched-source immutability.
   - Do not bypass staged hashes.
   - Do not remove the pre-merge full-build gate.
   - Do not make WinMerge optional for accepted watched-source mutation.

## Phase 0 - Baseline And Parity Test Harness

Purpose: prevent another compatibility-shaped stub from looking complete.

Tasks:

- Add a parity test naming convention, such as `CmbParity_*`, in the relevant test projects.
- Add a small shared fixture solution that can express:
  - class/interface inheritance;
  - partial classes;
  - method calls and object creation;
  - a caller/callee pair;
  - a simple staged edit session with two staged records.
- Add tests that currently fail or are skipped with explicit issue names for each high/missing item that cannot be fixed in the same phase.
- Update `CmbMcpCapabilityParity.md` after each item moves from missing/thin to restored/replaced/deferred.

Tests:

- Documentation check: every `CMB-PARITY-*` item has a test name or an explicit deferred/not-applicable note.
- A smoke test must fail if a parity tool returns an empty compatibility payload while claiming success.

## Phase 1 - Index Richness And Staleness

Owns:

- CMB-PARITY-001: indexed symbol relationships.
- CMB-PARITY-002: indexed call sites and true callers.
- CMB-PARITY-003: rich indexed references.
- CMB-PARITY-005: index staleness and counts.
- CMB-PARITY-006: scoped index query semantics.
- Medium appendix: `MonitorStatusResult` count fields.
- Medium appendix: `RefreshSolutionIndex` timing and indexed counts.
- Medium appendix: `get_solution_index` clamps, envelope, and richer file/symbol metadata.
- Medium appendix: `query_solution_index` clamps, envelope, path scope, and unknown-scope behavior.
- Medium appendix: `find_indexed_symbols` clamps and richer row metadata.
- Medium appendix: `get_indexed_symbol` richer row metadata and selector bridge.
- Medium appendix: `refresh_solution_index_file` per-file diagnostic/hash/write-time/stale detail.
- High appendix: `get_solution_index_status`.
- High appendix: `find_indexed_references`.
- High appendix: `find_indexed_callers`.
- High appendix: `find_indexed_relationships`.

Shared implementation:

- Add durable relationship facts:
  - partial type grouping;
  - base type / inherits;
  - interface implementation;
  - override/implements relationships where Roslyn can defend them.
- Add durable call-site facts:
  - caller symbol identity;
  - callee symbol identity where known;
  - invocation/object-creation kind;
  - file path, span, line, and file hash.
- Extend reference rows or add joined query results so references can include caller/file/hash context.
- Persist enough file state to answer staleness:
  - content hash;
  - last write time;
  - parse/index status.
- Update query/status services to expose:
  - symbol count;
  - reference count;
  - call-site count;
  - relationship count;
  - stale-file count.
- Fix folder scoping to path-aware prefix matching and clamp limits.
- Return status/query envelopes that make missing index, scope, limits, and result counts clear.
- Add timing/count details for manual and file-level index refresh responses.
- Preserve or expose selector/source-anchor metadata where it is needed to bridge index results to Roslyn edit tools.

MCP exposure:

- `find_indexed_relationships` returns real rows or an honest unavailable result.
- `find_indexed_callers` uses call-site/caller identity, not `ReferenceKind.Contains`.
- `find_indexed_references` exposes the richer reference shape.
- `get_solution_index_status` exposes staleness and row counts.
- `query_solution_index` uses path-aware folder scope and clamped limits.
- `get_solution_index`, `query_solution_index`, `find_indexed_symbols`, `get_indexed_symbol`, and `refresh_solution_index_file` stop returning thin rows where richer index facts exist.

CLI exposure:

- Only update CLI commands that already expose overlapping index/status/query behavior.
- Do not create CLI-only copies of MCP navigation tools unless they are useful for diagnostics/tests.

Tests:

- Index fixture test for inheritance/implements/partial relationships.
- Index fixture test for method invocation and object creation call sites with caller identity.
- Query service test for folder prefix matching that does not over-match sibling paths.
- Status test that detects stale files after watched bytes differ from persisted file facts.
- MCP smoke for relationship/caller/reference tools proving non-empty meaningful rows.
- Regression test proving `find_indexed_callers` does not work by reference-kind substring alone.
- Query envelope tests for index-missing, scoped query, clamped limits, and row counts.
- File refresh test proving per-file diagnostics/hash/write-time/stale details are returned.
- Selector bridge test proving an indexed symbol result can drive a later symbol/edit call.

Progress:

- 2026-06-02: Initial storage/schema, shared query, and MCP exposure restored for call sites and symbol relationships. Focused data tests and MCP smoke prove `find_indexed_callers` and `find_indexed_relationships` return shared index rows instead of compatibility stubs.
- 2026-06-02: Status counts, stale-file detection, path-aware scoped query, query clamps/envelope fields, and richer indexed reference rows restored in shared Data services. Data tests prove stale detection and sibling-folder exclusion; direct MCP server smokes prove status/query/reference fields are exposed.
- 2026-06-02: `find_indexed_symbols` and `get_indexed_symbol` now return shared query items with selector hint JSON, relative path, counts, and clamps. `refresh_solution_index` returns timing and fresh status. `refresh_solution_index_file` returns per-file hash, length, write time, indexed/stale state, symbols, and references. Per-file diagnostic count remains null until diagnostics are persisted with file ownership.

## Phase 2 - Session-Scoped Staging And Record Lifecycle

Owns:

- CMB-PARITY-007: session-scoped staged records.
- CMB-PARITY-011: superseded staged-record lifecycle.
- Missing appendix: superseded-record handling.
- Missing appendix: review-chain blocking across a multi-file session.
- Medium appendix: `check_file_hash` durable per-file session state.
- Medium appendix: session file-fetch tracking and access kind.
- Medium appendix: `stage_candidate_for_review` stage response/state details.
- Medium appendix: `launch_staged_diff` blocked/superseded/status details.
- High appendix: `record_diff_decision` superseded/queue/session behavior.
- High appendix: dirty-unexpected blocking state machine.
- High appendix: `list_session_staged_records`.

Shared implementation:

- Persist `SessionId` on staged records.
- Thread session identity from refresh/new/edit/stage/launch/record paths where available.
- Make `list_session_staged_records` actually filter by session.
- Define same-file restage behavior:
  - archive prior staged record as superseded; or
  - block new staging until prior record is decided.
- Define decision behavior for superseded records:
  - reject or return an explicit `superseded` result;
  - never allow an old superseded record to be accepted as if it were current.
- Persist blocking queue/status for dirty-unexpected where required.
- For multi-file sessions, define review-chain behavior:
  - accepted decision may defer index refresh until the chain is complete; or
  - document that every file decision rebuilds independently.
  - The chosen behavior must be explicit and tested.
- Replace ad-hoc file-fetch/hash reconstruction with durable per-file session state where the MCP surface promises it.
- Persist enough stage/launch state for response shapes to explain blocked, superseded, dirty-unexpected, no-op, or launched outcomes.

MCP exposure:

- `list_session_staged_records` returns only the requested session.
- `stage_candidate_for_review`, `launch_staged_diff`, and `record_diff_decision` expose superseded/blocked/queue state.
- `record_diff_decision` honors `sessionId`/`note` where those arguments are part of the surface.

CLI exposure:

- CLI staging/launch/decision paths get the same terminal/superseded behavior because they overlap the same Workflow service.

Tests:

- Workflow test: two staged records for the same file in one session supersede/archive/block exactly as designed.
- Workflow test: accepting a superseded record cannot mutate or classify as accepted.
- Workflow test: dirty-unexpected persists a blocking state or explicitly prevents unsafe follow-up actions.
- MCP smoke: two sessions with staged records; `list_session_staged_records(A)` does not return B.
- Multi-file smoke: staged records in one session keep a common session identity and expose chain/queue status.
- CLI regression: overlapping staging/decision behavior matches MCP because both route through shared Workflow.
- Hash/fetch tracking test proving session file access survives multiple calls and reports access kind.
- Stage/launch tests for blocked, no-op, superseded, and launched response details.

Progress:

- 2026-06-02: Initial session/staged-record lifecycle restore implemented. `StagedEditRecord` and `StagedEditSummary` now carry `SessionId` plus supersede metadata. MCP `stage_candidate_for_review` threads `sessionId` into the shared Workflow record, and `list_session_staged_records` now filters by persisted session instead of returning all records. Same-file restaging supersedes older non-terminal staged records only after the replacement record is ready; superseded records are blocked from validation, launch, and decision recording. Focused Workflow tests prove supersede/block/session filtering, and the existing MCP multi-file smoke now also proves another session's staged record is not leaked into the requested session.
- 2026-06-02: Durable MCP session file-fetch tracking restored. Session JSON now carries typed per-file access rows with session id, relative path, access kind, fetch count, first/last access timestamps, and hash. `get_file` records both the historical event and the typed access row; `check_file_hash` reads the typed access row and returns it as `previousAccess`. The existing MCP hash smoke now proves unchanged/changed detection plus access kind, relative path, and typed previous-access payload.

## Phase 3 - Source-Map Density, Budget, And Noise Filtering

Owns:

- CMB-PARITY-008: source-map density and budget.
- CMB-PARITY-009: WinForms/Razor source-map noise filter.
- Missing appendix: source-map `SuggestedNarrowing`.
- Missing appendix: source-map `SuggestedNextCalls`.
- Missing appendix: source-map AI-attribute noise filter.
- Medium appendix: source-map `auto` mode resolution.
- Medium appendix: source-map file-level metadata.
- Medium appendix: `get_symbol` committed-vs-working source behavior.
- High appendix: source-map token budget/truncation.
- High appendix: source-map per-mode field shaping.

Layering rule:

- Owner is `AIMonitor.Workflow` / `RoslynEditService`. Source maps are live Roslyn/source-shape discovery, not persisted solution-index data.
- `AIMonitor.McpServer` must remain a thin adapter that exposes request knobs and serializes the shared result shape.
- CLI only participates if a source-map diagnostic command already exists or is deliberately added later. Do not bend CLI into an MCP-only discovery surface.
- Raw `.razor` markup remains outside this source-map contract unless a specific source-mapped Razor implementation is added. `.razor.cs` and normal C# files are in scope.

CMB behavior to restore:

- `auto` mode resolves to `selector` for file scope and `navigation` for folder/project/namespace scope.
- Mode purpose is explicit:
  - `navigation`: broad orientation;
  - `selector`: stable symbol selection;
  - `detail`: contract detail;
  - `full`: audit/debug.
- Per-mode shaping is real:
  - `navigation` strips stable keys, hashes, signatures/parameter detail, and source paths where they are not needed;
  - `selector` keeps stable symbol keys, hashes, parameters, usings/namespaces, and enough selector metadata for `get_symbol`;
  - `detail` keeps contract detail and attribute arguments without forcing full audit payload;
  - `full` keeps the complete source-map row, including source path/hash/length.
- Budget metadata is returned:
  - `estimatedTokenProxy`;
  - `budgetLimit`;
  - `wasTruncated`.
- Over-budget maps return explicit `suggestedNarrowing` and `suggestedNextCalls` instead of dumping an unbounded payload.
- Source maps preserve useful file-header attributes such as `AIFileContext` and `FileVersion`, while filtering legacy workflow-history attributes such as `AIChange`, `AIHistory`, `AIInstructions`, `UserHistory`, and other `AI*` noise from the map only. Source code is not modified.

Execution slices:

1. **Source-map contract shape**
   - Add nullable/defaulted fields to `RoslynSourceMapResult`, `RoslynSourceMapFile`, and `RoslynSourceMapSymbol` so existing consumers remain compatible.
   - Add source-map diagnostics, attributes, narrowing suggestions, and next-call records.
   - Keep top-level response names close to CMB where possible: `modePurpose`, `estimatedTokenProxy`, `budgetLimit`, `wasTruncated`, `suggestedNarrowing`, `suggestedNextCalls`.

2. **Auto mode and mode shaping**
   - Pass effective mode through the mapping pipeline.
   - Resolve `auto` to a concrete mode before mapping.
   - Shape file and symbol fields by mode.
   - Keep `full` as the audit escape hatch.

3. **Metadata and selector richness**
   - Return file hash, file length, diagnostic summary, and richer symbol metadata where Roslyn can provide it cheaply.
   - Include base types, documentation/attribute flags, static/async/override/virtual/partial flags, and attribute summaries where appropriate for the selected mode.
   - Ensure stable selector metadata continues to match `get_symbol`.

4. **Budget, truncation, and navigation guidance**
   - Estimate token proxy from serialized response size.
   - Apply mode-specific budget limits.
   - On overflow, return a bounded envelope with `wasTruncated = true`, no giant `files` payload, and ranked `suggestedNarrowing`.
   - Add `suggestedNextCalls` for navigation-to-selector and selector-to-symbol flows, plus namespace-neighborhood calls from usings.

5. **Noise filtering**
   - Filter AI-history attributes from source-map output only.
   - Collapse generated/designer noise with explicit markers where source-map traversal sees it:
     - WinForms `InitializeComponent`;
     - designer `Dispose(bool)` override;
     - designer fields and common generated plumbing;
     - generated Razor render plumbing only if the current source-map surface actually sees generated C# for the file.
   - Do not silently drop user-authored members.

6. **`get_symbol` source-of-truth proof**
   - State in the response whether the symbol body came from the monitor-owned Working candidate or watched source.
   - Add tests proving the behavior so stale assumptions do not reappear.

Tests:

- Workflow/Roslyn test: `auto` resolves to `selector` for file scope and `navigation` for project/folder/namespace scope.
- Workflow/Roslyn test: `navigation`, `selector`, `detail`, and `full` produce observably different field sets.
- Workflow/Roslyn test: selector output contains stable selector data that `get_symbol` can read.
- Workflow/Roslyn test: file metadata includes hash, length, and diagnostic summary when available.
- Workflow/Roslyn test: over-budget map reports `wasTruncated`, `estimatedTokenProxy`, `budgetLimit`, and `suggestedNarrowing`.
- Workflow/Roslyn test: `suggestedNextCalls` include file selector calls from navigation mode and symbol body calls from selector mode.
- Workflow/Roslyn test: AI-history attributes are filtered from source-map output while normal user attributes remain visible.
- Workflow/Roslyn test: WinForms designer fixture collapses generated/designer noise in default/navigation output and keeps user members.
- Razor boundary test: raw `.razor` source-map requests fail or return a clear unsupported-boundary message; `.razor.cs` works as C#.
- MCP smoke: `get_source_map(mode=navigation)` and `get_source_map(mode=full)` produce different payload sizes and visible mode metadata.
- MCP smoke: a large scope returns bounded truncation guidance instead of an unbounded response.
- MCP smoke: `get_symbol` response includes the source-of-truth marker.

Progress:

- 2026-06-02: Phase 3 restored for the C# Roslyn source-map surface. `get_source_map` now resolves `auto` to concrete modes, shapes navigation/selector/detail/full payloads differently, returns budget metadata, truncates oversized maps with `suggestedNarrowing`, and emits `suggestedNextCalls` for navigation-to-selector and selector-to-symbol flows. Source-map files now carry hash, length, and diagnostic summaries where available. Symbols now expose richer metadata for selector/detail/full modes, filter legacy AI-history attributes from the map only, and collapse WinForms designer/render-plumbing noise with explicit elision markers instead of silently dropping it. `get_symbol` now reports `sourceKind = working-candidate`, matching its monitor-owned Working-file behavior. Raw `.razor` markup remains an explicit unsupported boundary for Roslyn source maps; `.razor.cs` remains covered as C#.
- Proof added: Workflow/Roslyn unit tests cover auto mode, mode shaping, oversized truncation, AI-attribute filtering, WinForms designer elision, and `get_symbol` Working-candidate source behavior. MCP smoke proves mode metadata, selector next-call guidance, full-project truncation guidance, and `sourceKind` through the real MCP server adapter.

MCP exposure:

- `get_source_map` exposes mode, budget, truncation, filtering, and override behavior.
- `full` or explicit include-generated behavior must let an agent inspect generated code when needed.

CLI exposure:

- Only expose source-map knobs in CLI if the CLI already has or needs a source-map diagnostic command.

## Phase 4 - Roslyn Outline And Tool Guidance

Owns:

- CMB-PARITY-010: Roslyn-backed file outline.
- CMB-PARITY-012: real tool manifest and staging guide.
- Medium appendix: `get_tool_manifest`.
- High appendix: `get_file_outline`.
- High appendix: `get_staging_guide`.

Shared implementation:

- Replace line-text outline detection with syntax/Roslyn declaration walking.
- Include useful outline fields:
  - kind;
  - name;
  - line/start;
  - end line/end;
  - signature where available;
  - containing type/namespace where useful.
- Compose staging guide from current shipped docs:
  - watched-source safety;
  - refresh/new;
  - edit Working candidate;
  - stage;
  - launch diff and validation gate;
  - record decision;
  - post-accept index refresh;
  - failure paths.
- Compose tool manifest from live/current tool contracts rather than generic architecture prose.

MCP exposure:

- `get_file_outline` returns structured outline rows.
- `get_staging_guide` and `get_tool_manifest` return current, agent-useful guidance.

CLI exposure:

- CLI does not need tool-manifest parity unless it already exposes operator diagnostics for it.

Tests:

- Roslyn fixture with comments/strings that look like declarations; outline must ignore false positives.
- Roslyn fixture with nested classes, methods, properties; outline includes kind/name/span/signature.
- MCP smoke for `get_file_outline` proving structured rows.
- Guidance test that fails if staging guide omits validation, WinMerge, record-decision, or post-accept index refresh.
- Tool-manifest test that proves actual tool names and safety notes are present.

Progress:

- 2026-06-02: Phase 4 restored. `get_file_outline` was already Roslyn-backed and tested. `get_tool_manifest` now reflects the live MCP tool methods and emits per-tool names, descriptions, parameters, and safety notes instead of returning generic architecture prose. `get_staging_guide` now returns the current safe-edit sequence, including refresh/new, Working edits, source-map/symbol targeting, staging, pre-merge validation, WinMerge review, `record_diff_decision`, and post-accept `indexRefresh`. MCP smoke verifies the guidance contains the expected tool names and safety terms.

## Phase 5 - Self-Check Truthfulness And Guardrails

Owns:

- CMB-PARITY-004: real self-check guardrails.
- High appendix: `get_self_check` guardrail evaluation.

Shared implementation:

- Decide whether AIMonitor still needs CMB-style guardrails:
  - source implementation root;
  - legacy monitor root;
  - path collision detection;
  - expected runtime/config paths.
- If yes, implement guardrail checks in a shared service and expose through MCP.
- If no, change the tool description and result so it states exactly what is checked today and records the intentional difference.

MCP exposure:

- `get_self_check` must not claim guardrails unless it evaluates them.
- Response should distinguish passed, warning, failed, and unavailable checks.

CLI exposure:

- Optional. Only add CLI if useful for local diagnostics.

Tests:

- Guardrail service test for normal path layout.
- Guardrail service test for deliberate path collision.
- MCP smoke that proves `get_self_check` reports real checks or honestly reports unavailable checks.
- Description/contract test if practical: no description claims unsupported guardrails.

Progress:

- 2026-06-02: Phase 5 restored in the MCP support layer. `get_self_check` now returns typed guardrail rows and an `overallStatus` instead of a fixed safety summary. Checks cover configured roots, watched solution/project existence, runtime/Working/History/Staged placement, diff-tool availability, and runtime-under-watched-source collision detection. MCP smoke deliberately places runtime under watched source and verifies the failed guardrail row and failed overall status.

## Phase 6 - Per-Edit Feedback And Structured Edit Results

Owns:

- Missing appendix: overlay semantic compile on every typed edit.
- High appendix: per-edit syntax/overlay validation on text/span/submit writes.
- Medium appendix: `replace_text_in_file` total-match assertion/result richness.
- Medium appendix: `replace_span_in_file` validation/result richness.
- Medium appendix: `submit_file` validation/session/manifest threading.
- Medium appendix: `find_text_span` occurrence count.
- Medium appendix: typed-edit result record richness.
- Medium appendix: structured syntax-validation diagnostics.
- Medium appendix: `manifestJson` persistence and `OperationCount`.

Important triage:

The finding already corrected this: AIMonitor has the hard safety gate through full pre-merge build before WinMerge/accept. This phase is about earlier feedback and agent ergonomics, not watched-source safety.

Shared implementation:

- Add optional per-edit validation to Working-candidate edit operations:
  - syntax validation for C# candidates;
  - Roslyn compilation/overlay validation where cheap and defensible;
  - structured diagnostics with id, message, file, line, and column.
- Do not block text/CSS/JSON edits with C# validation.
- Keep pre-merge full build as the final gate.
- Add operation count/baseline/candidate-state detail where it helps the agent recover.
- Preserve or explicitly replace CMB's total-match assertion behavior for text replacement.
- Return occurrence counts from text-span discovery before an agent chooses a replacement.
- Thread manifest/session metadata through edit calls where the tool contract exposes it.

MCP exposure:

- Typed edit tools and text/span/submit tools return structured validation detail when available.
- Failures are explicit; warnings do not silently block unless the operation contract says so.

CLI exposure:

- CLI edit commands may expose compact validation summaries where they overlap.

Tests:

- Typed C# edit with syntax error returns structured diagnostics before stage.
- Text edit to non-C# asset does not invoke C# validation.
- Valid edit returns operation count and success detail.
- Pre-merge build gate still runs even if per-edit feedback passes.
- Text replace test proving total matches/selected occurrence behavior is explicit.
- Text-span test proving occurrence count is returned.
- Manifest/session test proving edit calls do not discard exposed metadata.

Progress:

- 2026-06-02: Phase 6 shared Workflow surface restored for candidate writes. `submit_file`, `replace_text_in_file`, `replace_span_in_file`, and Roslyn typed edits now route through shared candidate-write validation. C# syntax errors block the Working-candidate write with structured line/column diagnostics; non-C# text assets skip C# validation. A lightweight Roslyn overlay compilation runs after successful C# candidate writes and reports diagnostics for overlaid Working files without replacing the existing full pre-merge build gate.
- 2026-06-02: Edit result shapes now expose operation count, manifest JSON threading, syntax validation, overlay validation, total match counts, replacement counts, and text-span occurrence counts. MCP now passes `manifestJson` into shared Workflow/Roslyn edit services instead of discarding it.
- 2026-06-02: Added Workflow unit coverage for syntax rejection, overlay diagnostics, manifest/operation count persistence, text-span counts, and Roslyn typed-edit overlay feedback. Added MCP smoke assertions proving Claude-visible responses include the new fields.

## Phase 7 - Monitor History, Runs, And Prune Policy

Status: Deferred by operator decision on 2026-06-03.

Owns:

- High appendix: `get_monitor_run` last-500/case-sensitive behavior.
- High appendix: `prune_monitor_history` no-op vs CMB archival engine.
- Medium appendix: `list_monitor_runs` typed entries, ordering, clamping, and operation field.

Shared implementation:

- Fix `get_monitor_run` to search deterministically:
  - case-insensitive request/run id matching where appropriate;
  - no arbitrary last-500 blind spot unless documented and surfaced;
  - typed result shape where practical.
- Make `list_monitor_runs` ordered and clamped, and return typed entries with operation/tool metadata where available.
- Keep prune behavior aligned with current operator preference:
  - no automatic aggressive pruning;
  - explicit UI/command-driven cleanup is acceptable;
  - if CMB archival is not restored, document intentional difference.

MCP exposure:

- `get_monitor_run` retrieves old enough records or clearly reports search scope.
- `prune_monitor_history` either performs explicit configured pruning or honestly reports that pruning is intentionally disabled.

CLI exposure:

- Only if CLI exposes monitor-history diagnostics.

Tests:

- Monitor-run lookup finds records outside the old last-500 window.
- Monitor-run lookup handles casing according to the chosen contract.
- List-runs test proving ordering, clamp behavior, and typed operation fields.
- Prune command test proves either explicit archive/prune behavior or honest intentional no-op response.
- Documentation decision updated if archival remains intentionally deferred.

Progress:

- 2026-06-03: Deferred as a future feature. `prune_monitor_history` should continue to be honest about no-op behavior rather than pretending CMB archival was restored. Adapter-resident history reads are accepted as low-impact until Phase 7 is intentionally reopened. See `CmbMcpCapabilityParity.md` tracked deferrals and `docs/findings/Phase8HarnessClosureReview-2026-06-03.md`.

## Phase 8 - Final Parity Closure

Status: Completed for current scope on 2026-06-03, with tracked deferrals.

Purpose: prove there are no hidden missing/high/medium parity losses left.

This phase documents the one-time parity closure pass for the MonitorBaseClaude-to-AIMonitor recovery effort. Its gate is historical closure evidence, not the standing minimum required for every future day-to-day change.

Tasks:

- Re-read `CmbMcpParityGap-2026-06-02.md`.
- Re-run the parity review manually or with Claude/Codex against current main.
- Update `CmbMcpCapabilityParity.md` statuses for every item.
- Any remaining missing/high/medium item must have:
  - restored behavior and tests;
  - explicit intentional replacement and tests;
  - explicit deferred decision with rationale.
- Remove or rewrite any tool descriptions that imply unimplemented capabilities.

Closure pass test gate:

- `dotnet build AIMonitor.slnx -c Debug -p:UseSharedCompilation=false`
- All unit tests for affected projects.
- All MCP/tool smoke tests.
- Workflow smoke tests for:
  - existing-file edit;
  - new-file edit;
  - rejected decision;
  - accepted/accepted-normalized decision;
  - failed pre-merge validation with human override path.
- Live WinForms telemetry check for at least one MCP workflow run after the parity changes.

Progress:

- 2026-06-03: Closure review recorded parity restore as passed for CMB-PARITY-001 through CMB-PARITY-012 and the missing/high/medium appendix scope, with Phase 7, HIGH-2 monitor-session service extraction, MEDIUM-1 self-check service extraction, and degraded-low tail items explicitly tracked as deferrals. The live safety gap for native Claude watched-source writes was closed with the watched-source guard hook/setup step.

## Suggested Execution Order

1. Phase 0: parity harness and failing/skipped tests.
2. Phase 1: index richness. This removes the biggest fake/empty query surfaces.
3. Phase 2: session/staging lifecycle. This protects multi-file workflow clarity.
4. Phase 3: source-map quality. This restores agent navigation and keeps context bounded.
5. Phase 4: outline and guidance. Lower risk and improves Claude usability quickly.
6. Phase 5: self-check truthfulness. Small but important because lying tool descriptions are poisonous.
7. Phase 6: per-edit feedback. Useful, but lower safety urgency than the hard pre-merge gate.
8. Phase 7: monitor history/prune policy. Deferred by operator decision.
9. Phase 8: final parity closure and external review. Completed for current scope with tracked deferrals.

## Done Definition

This restore effort is done when:

- no hand-verified missing/high/medium item remains as an untracked stub;
- every appendix Missing, Degraded High, and Degraded Medium item has a status in the parity ledger;
- every restored item has behavior/richness tests;
- every intentional difference has operator rationale in docs;
- MCP descriptions match actual behavior;
- the full test gate passes;
- Claude and Codex can inspect the docs and reach the same architecture/layering conclusion without relying on conversation memory.
