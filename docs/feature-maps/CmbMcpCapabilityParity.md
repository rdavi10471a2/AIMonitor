# CMB MCP Capability Parity

## Human Notes

This map exists because AIMonitor was expected to carry forward the useful MonitorBaseClaude MCP behavior unless the operator explicitly approved a replacement or removal.

Do not treat a compatibility-shaped stub, a successful JSON response, or a passing smoke test as proof of parity. A parity item is complete only when the behavior, richness, and safety contract are implemented or the operator has recorded an intentional difference.

## AI-Maintained Map

The verified source finding is `docs/findings/CmbMcpParityGap-2026-06-02.md`.

The implementation plan is `docs/feature-maps/CmbMcpParityRestorePlan.md`.

The architectural rule is:

```text
CMB behavior
  -> evaluate whether the behavior still applies
  -> implement in the shared service that owns the behavior
  -> expose through MCP, CLI, or UI only where that adapter overlaps
  -> prove with behavior/richness tests, not shape-only stubs
```

MCP is the Claude-facing adapter. It must not contain independent workflow, index, or Roslyn truth when a shared service owns that behavior. CLI should receive the same behavior only where its command surface overlaps the shared workflow or query contract.

## Current Verified Gaps

The items below are the hand-verified restore set from the finding. They are safe to plan against immediately.

### CMB-PARITY-001 — Indexed Symbol Relationships

- **Owner:** Indexing/Data
- **Adapter surface:** MCP query tools
- **Current status:** Initial restore implemented
- **Required proof:** Index rows for partial/inherits/implements/overrides plus MCP query returning real relationship rows.
- **Proof added:** Data fixture persists `symbol_relationships` rows from relationship-shaped Roslyn/MSBuild references, and MCP smoke proves `find_indexed_relationships` returns those rows instead of an empty compatibility payload.
- **Remaining Phase 1 work:** Broaden corpus coverage for inherits/implements/overrides and decide whether inverse relationship rows are persisted or query-projected.

### CMB-PARITY-002 — Indexed Call Sites And True Callers

- **Owner:** Indexing/Data
- **Adapter surface:** MCP query tools
- **Current status:** Initial restore implemented
- **Required proof:** Call-site table or equivalent persisted caller identity. `find_indexed_callers` cannot infer callers by string-matching reference kind.
- **Proof added:** Data fixture persists `call_sites` rows with caller symbol identity and target stable key, and MCP smoke proves `find_indexed_callers` returns caller identity from shared index storage.
- **Remaining Phase 1 work:** Broaden corpus coverage for object creation and additional invocation forms.

### CMB-PARITY-003 — Rich Indexed References

- **Owner:** Indexing/Data
- **Adapter surface:** MCP query tools
- **Current status:** Initial restore implemented
- **Required proof:** References include enough caller/file/hash/partial context for agent navigation, or an explicit accepted replacement.
- **Proof added:** `IndexedReferenceRow` now includes target symbol metadata, containing caller symbol metadata when defensible from indexed spans, and indexed file content hash. Data tests and direct MCP server smokes verify the richer shape.

### CMB-PARITY-004 — Real Self-Check Guardrails

- **Owner:** Core/Runtime or MCP shared support
- **Adapter surface:** MCP `get_self_check`
- **Current status:** Restored in MCP support
- **Required proof:** Either run guardrail/collision checks or change tool description/result so it does not claim them.
- **Proof added:** `get_self_check` now returns `overallStatus` and typed `guardrails` with passed/warning/failed statuses for configured roots, runtime/Working/History/Staged placement, watched source existence, diff-tool availability, and runtime-under-watched-source collisions. MCP smoke creates a deliberate runtime-under-watched-source collision and verifies a failed guardrail row.

### CMB-PARITY-005 — Index Staleness And Counts

- **Owner:** Indexing/Data
- **Adapter surface:** MCP status tools, UI as applicable
- **Current status:** Initial restore implemented
- **Required proof:** Status exposes stale-file count and useful symbol/reference/call-site/relationship counts, backed by persisted facts.
- **Proof added:** `MonitorStatusResult` exposes symbol/reference/call-site/relationship/stale-file counts. Data tests mutate watched bytes after indexing to prove stale count changes, and direct MCP server smoke verifies the fields are visible through status tools.

### CMB-PARITY-006 — Scoped Index Query Semantics

- **Owner:** Data
- **Adapter surface:** MCP query tools
- **Current status:** Initial restore implemented
- **Required proof:** Folder scope uses path-aware matching and limits are clamped.
- **Proof added:** Shared `SolutionIndexQueryService.QueryIndex` owns scope filtering and clamps. Data tests prove `Features/Orders` does not over-match `Features/OrdersExtra`; direct MCP server smoke verifies clamp/envelope fields.

### CMB-PARITY-007 — Session-Scoped Staged Records

- **Owner:** Workflow
- **Adapter surface:** MCP workflow tools, possible CLI overlap
- **Current status:** Initial restore implemented
- **Required proof:** Staged records persist session identity; `list_session_staged_records` returns only that session's records.
- **Proof added:** `StagedEditRecord` and `StagedEditSummary` carry `SessionId`; MCP `stage_candidate_for_review` persists the supplied session id through shared Workflow; `list_session_staged_records` filters by persisted session. Workflow tests prove session filtering, and the MCP multi-file smoke proves another session's staged record is excluded.

### CMB-PARITY-008 — Source-Map Density And Budget

- **Owner:** Workflow/Roslyn
- **Adapter surface:** MCP source-map tool, CLI if exposed
- **Current status:** Restored for the C# Roslyn source-map surface
- **Required proof:** Modes actually shape payload; over-budget maps report explicit truncation/narrowing.
- **Proof added:** `RoslynEditService.GetSourceMap` resolves `auto` to concrete modes, shapes navigation/selector/detail/full payloads, returns `estimatedTokenProxy`/`budgetLimit`/`wasTruncated`, truncates oversized payloads with `suggestedNarrowing`, and emits `suggestedNextCalls`. Workflow unit tests prove mode shaping and truncation; MCP smoke proves mode metadata, selector next-call guidance, and full-project truncation through the server adapter.

### CMB-PARITY-009 — WinForms/Razor Source-Map Noise Filter

- **Owner:** Workflow/Roslyn
- **Adapter surface:** MCP source-map tool, CLI if exposed
- **Current status:** Restored for C# / `.razor.cs`; raw `.razor` markup remains an explicit boundary
- **Required proof:** Generated/designer noise is collapsed with visible elision markers; full/detail override can include generated members.
- **Proof added:** Source-map symbols now filter legacy AI-history attributes from the map only, preserve useful attributes such as `AIFileContext` and `FileVersion`, and mark WinForms designer plumbing with explicit `isElided`/`elisionReason` fields. Workflow unit tests prove AI-attribute filtering and designer elision. Raw `.razor` requests continue to return explicit MCP guidance to use text/file workflow tools or `.razor.cs` for Roslyn.

### CMB-PARITY-010 — Roslyn-Backed File Outline

- **Owner:** Workflow/Roslyn
- **Adapter surface:** MCP outline tool
- **Current status:** Restored
- **Required proof:** Outline comes from syntax/Roslyn declarations with kind/name/span/signature, not line text heuristics.
- **Evidence:** `RoslynEditService.GetFileOutline` returns structured Roslyn outline rows; `Mcp_get_file_outline_returns_roslyn_structured_members` proves the MCP tool returns `kind`, `name`, and `signature` fields and ignores comment/string declaration lookalikes.

### CMB-PARITY-011 — Superseded Staged-Record Lifecycle

- **Owner:** Workflow
- **Adapter surface:** MCP and CLI staging/decision overlap
- **Current status:** Initial restore implemented
- **Required proof:** Same-file restaging archives or blocks older staged records; accept/reject handles superseded records explicitly.
- **Proof added:** Same-file restaging marks older non-terminal records `superseded` with `SupersededByStagedRecordId` after the replacement staged record is ready. Superseded records are rejected by shared validation, launch, and decision paths. Workflow tests prove old records cannot launch or record decisions.

### CMB-PARITY-012 — Real Tool Manifest And Staging Guide

- **Owner:** Docs/MCP
- **Adapter surface:** MCP guidance tools
- **Current status:** Restored in MCP support
- **Required proof:** Tool manifest and staging guide are composed from current shipped docs/tool contracts, not generic architecture prose.
- **Proof added:** `get_tool_manifest` now reflects over live MCP tool methods and emits per-tool names, descriptions, and parameters plus safety notes. `get_staging_guide` now returns a current safe-edit sequence covering refresh/new, Working edits, source-map/symbol targeting, staging, pre-merge validation, WinMerge review, record decision, and post-accept index refresh. MCP smoke verifies the expected tool names and workflow safety terms.

## Larger Inventory Categories

The source finding also contains a larger machine-generated appendix. That appendix is not disposable, but it is not verified. Keep it as the intake queue for later reconciliation.

Summary of the appendix categories:

- Missing MCP-scope behavior: review-chain blocking across multi-file sessions.
- Degraded high-impact behavior: self-check guardrails, solution-index status richness, relationship/caller/reference tools, source-map budget/mode behavior, Roslyn outline, staged-record/session behavior, monitor-run lookup, and staging guide richness.
- Degraded medium-impact behavior: refresh timing/counts, file-fetch/session tracking, launch/stage response states, and per-file index refresh details.
- Degraded low-impact behavior: self-check metadata, monitor-status implementation-path fields, index-tree status details, source-map symbol metadata, parameter descriptions, session access bookkeeping, ledger/list shapes, smoke-test catalog richness, and future multi-project watched-project enumeration.
- Renamed/no-loss behavior: `new_file`, explicit staged-record/status tools, and the MCP stdio bridge rename are architectural changes rather than losses.

Every appendix item must eventually become one of:

- restored;
- intentionally replaced by an AIMonitor-specific equivalent;
- intentionally deferred;
- not applicable to the current architecture.

## Restore Order

1. Index richness and staleness: CMB-PARITY-001, 002, 003, 005, 006.
2. Session and staged-record lifecycle: CMB-PARITY-007, 011.
3. Source-map density, budget, and generated-noise filtering: CMB-PARITY-008, 009.
4. Roslyn outline and MCP guidance docs: CMB-PARITY-010, 012.
5. Self-check truthfulness: CMB-PARITY-004.
6. Per-edit feedback: restored as shared Workflow candidate-write validation with MCP-visible syntax/overlay diagnostics and operation metadata.

## Dataflow

```text
MSBuild project truth
  -> Roslyn/Indexing extraction
  -> durable index rows and workflow records
  -> shared query/workflow/Roslyn services
  -> MCP tool response shaping
  -> WinForms telemetry for live calls
```

Where the CLI exposes the same command behavior, it should route through the same shared service. Where a capability is Claude/MCP-only guidance or source navigation, the CLI does not need a parallel command unless it is useful for smoke tests or operator diagnostics.

## Invariants

- No CMB capability may be silently represented by an empty stub unless the result clearly says it is unavailable and a finding/decision tracks why.
- Tests must assert behavior and useful payload content for parity items. Existence, non-null JSON, or valid response shape is not enough.
- Descriptions are part of the contract. A tool description must not claim a capability that the implementation does not run.
- Restored behavior belongs in the owning shared service before adapter-specific response shaping.
- Any intentional divergence from CMB must be recorded in `docs/decisions/` or this map with operator rationale.

## Tests / Smokes

Parity fixes should add the narrowest useful test at the owning layer and, where practical, an adapter smoke that proves the visible tool/command no longer returns a compatibility-only shell.

Examples:

- relationship/call-site fixes: index fixture plus MCP query smoke;
- source-map fixes: Roslyn/service fixture plus MCP `get_source_map` mode smoke;
- staged-record session fixes: workflow record fixture plus MCP `list_session_staged_records` smoke;
- guidance fixes: exact doc/tool-manifest content assertions that prove the response includes current workflow commands and safety gates.

## Known Risks

- The appendix in `CmbMcpParityGap-2026-06-02.md` is unverified. Treat it as leads, not fact.
- Adding old CMB behavior blindly can violate AIMonitor's cleaner layering. Preserve the behavior when it still applies, but place it in the correct shared service.
- Some CMB host extras may not apply to AIMonitor. Mark those as explicit intentional differences instead of deleting them from the ledger.
