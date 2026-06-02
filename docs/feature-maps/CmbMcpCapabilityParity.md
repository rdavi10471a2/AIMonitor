# CMB MCP Capability Parity

## Human Notes

This map exists because AIMonitor was expected to carry forward the useful MonitorBaseClaude MCP behavior unless the operator explicitly approved a replacement or removal.

Do not treat a compatibility-shaped stub, a successful JSON response, or a passing smoke test as proof of parity. A parity item is complete only when the behavior, richness, and safety contract are implemented or the operator has recorded an intentional difference.

## AI-Maintained Map

The verified source finding is `docs/findings/CmbMcpParityGap-2026-06-02.md`.

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
- **Current status:** Missing
- **Required proof:** Index rows for partial/inherits/implements/overrides plus MCP query returning real relationship rows.

### CMB-PARITY-002 — Indexed Call Sites And True Callers

- **Owner:** Indexing/Data
- **Adapter surface:** MCP query tools
- **Current status:** Missing/thin
- **Required proof:** Call-site table or equivalent persisted caller identity. `find_indexed_callers` cannot infer callers by string-matching reference kind.

### CMB-PARITY-003 — Rich Indexed References

- **Owner:** Indexing/Data
- **Adapter surface:** MCP query tools
- **Current status:** Thin
- **Required proof:** References include enough caller/file/hash/partial context for agent navigation, or an explicit accepted replacement.

### CMB-PARITY-004 — Real Self-Check Guardrails

- **Owner:** Core/Runtime or MCP shared support
- **Adapter surface:** MCP `get_self_check`
- **Current status:** Stub/misleading
- **Required proof:** Either run guardrail/collision checks or change tool description/result so it does not claim them.

### CMB-PARITY-005 — Index Staleness And Counts

- **Owner:** Indexing/Data
- **Adapter surface:** MCP status tools, UI as applicable
- **Current status:** Thin
- **Required proof:** Status exposes stale-file count and useful symbol/reference/call-site/relationship counts, backed by persisted facts.

### CMB-PARITY-006 — Scoped Index Query Semantics

- **Owner:** Data
- **Adapter surface:** MCP query tools
- **Current status:** Thin
- **Required proof:** Folder scope uses path-aware matching and limits are clamped.

### CMB-PARITY-007 — Session-Scoped Staged Records

- **Owner:** Workflow
- **Adapter surface:** MCP workflow tools, possible CLI overlap
- **Current status:** Missing
- **Required proof:** Staged records persist session identity; `list_session_staged_records` returns only that session's records.

### CMB-PARITY-008 — Source-Map Density And Budget

- **Owner:** Workflow/Roslyn
- **Adapter surface:** MCP source-map tool, CLI if exposed
- **Current status:** Missing
- **Required proof:** Modes actually shape payload; over-budget maps report explicit truncation/narrowing.

### CMB-PARITY-009 — WinForms/Razor Source-Map Noise Filter

- **Owner:** Workflow/Roslyn
- **Adapter surface:** MCP source-map tool, CLI if exposed
- **Current status:** Missing
- **Required proof:** Generated/designer noise is collapsed with visible elision markers; full/detail override can include generated members.

### CMB-PARITY-010 — Roslyn-Backed File Outline

- **Owner:** Workflow/Roslyn
- **Adapter surface:** MCP outline tool
- **Current status:** Thin
- **Required proof:** Outline comes from syntax/Roslyn declarations with kind/name/span/signature, not line text heuristics.

### CMB-PARITY-011 — Superseded Staged-Record Lifecycle

- **Owner:** Workflow
- **Adapter surface:** MCP and CLI staging/decision overlap
- **Current status:** Missing
- **Required proof:** Same-file restaging archives or blocks older staged records; accept/reject handles superseded records explicitly.

### CMB-PARITY-012 — Real Tool Manifest And Staging Guide

- **Owner:** Docs/MCP
- **Adapter surface:** MCP guidance tools
- **Current status:** Thin
- **Required proof:** Tool manifest and staging guide are composed from current shipped docs/tool contracts, not generic architecture prose.

## Larger Inventory Categories

The source finding also contains a larger machine-generated appendix. That appendix is not disposable, but it is not verified. Keep it as the intake queue for later reconciliation.

Summary of the appendix categories:

- Missing MCP-scope behavior: per-edit overlay feedback, superseded-record lifecycle, source-map narrowing/next-call hints, AI-attribute filtering, and review-chain blocking across multi-file sessions.
- Degraded high-impact behavior: self-check guardrails, solution-index status richness, relationship/caller/reference tools, source-map budget/mode behavior, Roslyn outline, staged-record/session behavior, monitor-run lookup, and staging guide richness.
- Degraded medium-impact behavior: status DTO counts, refresh timing/counts, query envelopes/clamps, richer symbol/reference metadata, source-map file metadata, text/span edit result richness, structured diagnostics, manifest/operation-count persistence, file-fetch/session tracking, launch/stage response states, per-file index refresh details, and tool-manifest content.
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
6. Optional early feedback: per-edit overlay compile if the operator later wants faster iteration before the pre-merge build gate.

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
