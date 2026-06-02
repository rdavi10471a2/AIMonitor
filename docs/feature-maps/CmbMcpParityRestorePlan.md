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

Shared implementation:

- Pass source-map `mode` into the actual mapping pipeline.
- Define mode behavior:
  - `navigation`: small orientation map;
  - `selector`: enough to choose symbols/selectors;
  - `detail`: selected detail without full generated noise;
  - `full`: complete map, including generated members if requested.
- Add budget fields:
  - estimated token/character count;
  - budget limit;
  - was truncated;
  - elided count.
- Add explicit narrowing hints when a map is too large.
- Add suggested next calls for common navigation paths.
- Make `auto` resolve to an actual mode instead of remaining an unshaped label.
- Return file-level metadata such as hash/length/diagnostic summary where available.
- Decide and document whether `get_symbol` reads committed watched source or the current Working candidate. If it reads Working, the response must say so clearly and tests must prove the behavior.
- Collapse generated/noisy regions with visible markers:
  - WinForms designer fields;
  - `InitializeComponent`;
  - `Dispose(bool)` designer override;
  - common interface/designer plumbing;
  - Razor generated render plumbing;
  - AI-history/change attributes where they only add noise.
- Do not silently drop content. Every filter/truncation needs a marker.

MCP exposure:

- `get_source_map` exposes mode, budget, truncation, filtering, and override behavior.
- `full` or explicit include-generated behavior must let an agent inspect generated code when needed.

CLI exposure:

- Only expose source-map knobs in CLI if the CLI already has or needs a source-map diagnostic command.

Tests:

- WinForms designer fixture: default/navigation map collapses designer noise and keeps user members.
- Razor fixture: generated render plumbing is collapsed and `.razor.cs`/user-authored code remains visible.
- AI-attribute fixture: noisy AI attributes are filtered or collapsed without hiding real user attributes.
- Mode test: `navigation` and `selector` are smaller than `full` and omit/collapse expected generated details.
- Budget test: oversized map truncates with explicit marker and suggested narrowing.
- MCP smoke: `get_source_map(mode=navigation)` and `get_source_map(mode=full)` produce observably different payloads.
- `auto` mode test proving it resolves to a concrete mode.
- File metadata test proving hash/length/diagnostic summary is present when available.
- `get_symbol` test proving the chosen source-of-truth behavior is explicit.

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

## Phase 7 - Monitor History, Runs, And Prune Policy

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

## Phase 8 - Final Parity Closure

Purpose: prove there are no hidden missing/high/medium parity losses left.

Tasks:

- Re-read `CmbMcpParityGap-2026-06-02.md`.
- Re-run the parity review manually or with Claude/Codex against current main.
- Update `CmbMcpCapabilityParity.md` statuses for every item.
- Any remaining missing/high/medium item must have:
  - restored behavior and tests;
  - explicit intentional replacement and tests;
  - explicit deferred decision with rationale.
- Remove or rewrite any tool descriptions that imply unimplemented capabilities.

Final test gate:

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

## Suggested Execution Order

1. Phase 0: parity harness and failing/skipped tests.
2. Phase 1: index richness. This removes the biggest fake/empty query surfaces.
3. Phase 2: session/staging lifecycle. This protects multi-file workflow clarity.
4. Phase 3: source-map quality. This restores agent navigation and keeps context bounded.
5. Phase 4: outline and guidance. Lower risk and improves Claude usability quickly.
6. Phase 5: self-check truthfulness. Small but important because lying tool descriptions are poisonous.
7. Phase 6: per-edit feedback. Useful, but lower safety urgency than the hard pre-merge gate.
8. Phase 7: monitor history/prune policy.
9. Phase 8: final parity closure and external review.

## Done Definition

This restore effort is done when:

- no hand-verified missing/high/medium item remains as an untracked stub;
- every appendix Missing, Degraded High, and Degraded Medium item has a status in the parity ledger;
- every restored item has behavior/richness tests;
- every intentional difference has operator rationale in docs;
- MCP descriptions match actual behavior;
- the full test gate passes;
- Claude and Codex can inspect the docs and reach the same architecture/layering conclusion without relying on conversation memory.
