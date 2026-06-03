---
status: open
type: finding
created: 2026-06-03
audience: Codex + operator
scope: Phase 8 (final parity closure) + whole-harness review, incl. LSP-equivalence and hooks-equivalence
method: 6-dimension multi-agent review (13 agents) with adversarial verification of every blocker/high/medium finding
note: review + findings only; Codex owns production fixes. Phase 7 (history/prune) is intentionally DEFERRED by operator decision.
---

# Phase 8 — Harness Closure Review (parity + architecture + LSP/hooks equivalence)

Six independent reviewers, each finding adversarially re-checked. Severities below are the **post-verification**
ratings (the verifier downgraded four findings that were by-design, and confirmed three real ones).

## Bottom line by dimension

| Dimension | Verdict |
| --- | --- |
| Parity closure (CMB-PARITY-001..012 + appendices) | **PASS** — all 12 genuinely restored, content-tested, no false-pass; Phase 7 honestly deferred |
| Safety floor (decision 0002) | **PASS** — all four pillars intact and test-asserted; override cannot fire silently |
| MCP description truthfulness | **PASS** — no poisonous stub-with-confident-description; two minor wording nits |
| Architecture / layering | **CONCERNS** — safety lifecycle + index + edits route through shared services correctly, but two subsystems leaked behavioral logic into the MCP adapter |
| LSP equivalence (operator hypothesis) | **PARTIALLY TRUE** — navigate/reference axis MEETS/EXCEEDS an LSP; rename/completion/as-you-type are MISSING **by design** |
| Hooks equivalence (operator hypothesis) | **MOSTLY STRUCTURAL** — in-band enforcement is stronger than a hook; one out-of-band path is genuinely unguarded |

The hard floor is intact and the parity restore is genuinely closed. The actionable items are **one real protection
gap, two architecture-ownership defects, and a short docs/ledger tail.**

## Confirmed gaps (post adversarial verification)

### HIGH-1 — The agent's own native Edit/Write/Bash can mutate the LIVE watched source (the real hooks answer)
AIMonitor's structural confinement (`ResolveWatchedPath` → `WorkflowEditPaths`, rejecting any path outside the watched
root) governs **only AIMonitor's own MCP tools**. Claude Code's built-in Edit/Write/Bash never traverse that engine.
- Live watched solution: `C:\SchemaStudioWebViewer V 1.1 - Monitor\SchemaStudioWebViewer.sln` (`config/appsettings.json:3`, wired via `.mcp.json`).
- `.claude/settings.json` deny covers only `//c/Schema Studio - DBV2/**` (`:43-45`) — a **different, secondary path**.
- The live watched root has a **Read allow** (`:33`) and **no Edit/Write/NotebookEdit deny**; `defaultMode` is `bypassPermissions` (`:47`); no `.claude/hooks/` exist.
- Net: under bypassPermissions a native Edit/Write to a watched `.razor`/`.cs` succeeds, bypassing refresh→stage→diff→decision and the pre-merge build gate — the exact invisible-in-the-diff failure mode decision 0002 says must be hardened.

**Fix:** derive an Edit/Write/NotebookEdit (and destructive-Bash) deny — or a `PreToolUse` hook — from
`MonitorSettings.WatchedProjectFolder` so it tracks whatever solution is watched. At minimum, correct the stale deny to
cover `C:/SchemaStudioWebViewer V 1.1 - Monitor/**`. **This is the single concrete place a hook earns its keep.**

### HIGH-2 — Durable monitor-session lifecycle is implemented in the MCP adapter, not a shared service
`StartMonitorSession`/`RecordMonitorSessionEvent`/`GetFile`/`CheckFileHash` plus the `AIMonitorSessionState` /
`AIMonitorSessionFileAccess` records, the JSON store under `workflow/sessions`, and the real lifecycle logic
(fetch-count accrual, first/last access timestamps, access-row remove/re-add, changed-since-last-fetch detection) all
live in `src/AIMonitor.McpServer/Program.cs` (`335-394`, `433-474`, `1194-1276`). A repo-wide grep for those types
returns **only** Program.cs — there is no `MonitorSessionService` in `AIMonitor.Workflow`.
- Violates the core rule "safe-edit lifecycle belongs in Workflow; MCP only exposes and shapes responses."
- The CLI cannot share it (MCP-only), even though sessions are conceptually workflow state.
- Masked by adapter-only tests: fetch-count/access-kind/changed-unchanged are asserted, but **only** through the MCP smoke (`McpServerSmokeTests.cs:1465-1488`); the documented owning layer (Workflow) has zero session tests. The ledger records CMB-PARITY-007 as "Owner: Workflow," which only the staged-record half satisfies.

**Fix:** extract a `MonitorSessionService` into `AIMonitor.Workflow`; have the MCP tools delegate to it (like staging
already does); add owning-layer tests for fetch-count accrual, access kind, and changed/unchanged detection.

### MEDIUM-1 — Self-check guardrail evaluation lives in the adapter, with no owning-layer test
`BuildSelfCheckGuardrails` + `CheckPathExists`/`CheckPathUnderRoot`/`CheckPathOutsideRoot`/`IsPathUnderRoot`
(`Program.cs:1111-1163`) do real behavioral evaluation (root existence, working/history/staged containment, the
runtime-under-watched-source collision) entirely in the adapter. The Phase 5 plan explicitly required a **shared
guardrail service** plus two service-level tests (normal layout + deliberate collision); neither test exists — only the
MCP smoke (`McpServerSmokeTests.cs:178-191`) exercises it.

**Fix:** extract a shared guardrail/diagnostics service; add the two service-level tests (especially the deliberate
runtime-under-watched-source collision).

### LOW tail
- **LOW-1 (ledger reconciliation):** the degraded-low tail in `CmbMcpCapabilityParity.md:138` (smoke-test catalog richness, `list_watched_projects` single hardcoded entry, `get_monitor_run` last-500/case-sensitive, `list_monitor_runs` shapes) has no explicit per-item restored/replaced/deferred/NA status. Closure's own invariant wants each marked. `get_smoke_test_catalog` still serves the ~4KB `SmokeCoverageTodo.md` (`Program.cs:1004-1007`) — honest description, just thin.
- **LOW-2 (description wording):** `find_indexed_relationships` description says "including incoming and outgoing relationship direction" (`Program.cs:315`) but the row (`IndexedRelationshipRow.cs`) carries **no Direction field** — direction is only a query filter. `refresh_file_and_index` (`Program.cs:195`) implies per-file incremental refresh but performs a **full rebuild**; its sibling `refresh_solution_index_file` is honest about this — give it the same hedge.
- **LOW-3 (adapter-resident history reads):** `ListMonitorRuns`/`GetMonitorRun`/`ListLedgers`/`GetLedger` read `HistoryRoot` files directly in the adapter (`Program.cs:909-972`). Tied to deferred Phase 7; fold into a shared history service when that lands.

## By-design — do NOT "fix" these (verifier refuted them)
- **Overlay validation is error-only / no as-you-type diagnostics.** Documented intentional design (`Program.cs:1106`); no doc anywhere claims LSP diagnostic parity. The full pre-merge build is ground truth. Not a gap.
- **No rename / completion / semantic-tokens.** Deliberate scoping consequence of the safety floor (watched-source immutability, one Working candidate per file). The skills docs already treat cross-file rename as a manual, operator-orchestrated, multi-staged task. Documentation-clarity only.
- **Proxy-hub telemetry is bridge-path-only.** A server-side audit log already exists regardless of launch path: `MonitorLogPipeClientLogger` falls back to direct-file write when the hub pipe is absent, and `adapter.mcp.tool.called` is emitted per tool (tested at `McpServerSmokeTests.cs:141-146`). Only a granularity nuance, not a missing trail.
- **No recursive/destructive-command guardrail.** An explicit "AIMonitor should *eventually* express…" wishlist item in `HarnessEngineeringNotes.md:30-36`, out of parity scope, not part of decision 0002's floor. Reasonable future hardening (a `PreToolUse` Bash deny on `rm -rf`/`Remove-Item -Recurse`/`git clean -fd` under the watched root), but not a closure defect.

## Answering the two operator hypotheses

**"MCP essentially wraps what a language server could do?"** — *Partially, and on the part it covers it exceeds an LSP.*
- **MEETS / EXCEEDS:** go-to-definition, find-all-references, callers, relationships, document/workspace symbols are real Roslyn `SemanticModel` facts (`GetSymbolInfo`, `OverriddenMethod`, `ExplicitInterfaceImplementations`, `FindImplementationForInterfaceMember`) **persisted as durable, queryable, hash-stamped SQLite rows** — something a volatile in-process LSP does not offer. Plus content-hash safety and staged review.
- **MISSING (by design):** rename, completion, semantic tokens, call/type-hierarchy-as-a-tool (the edges exist as rows; transitive expansion is the caller's job), and live as-you-type diagnostics (only on-demand overlay + full build).
- **Position it as** a *durable semantic index + safe-edit harness covering the navigate+validate subset of an LSP* — not "LSP equivalent."

**"Have we achieved hook-equivalence via other means?"** — *Yes for the in-band edit path — structurally, which is stronger than a hook — but no for the out-of-band path.*
- Watched-source immutability, monitor-owned Working/Staged ownership, content-hash accept classification, and the build-then-WinMerge accept gate are **code-enforced invariants** the agent literally cannot bypass through AIMonitor's tools — a hook here would be redundant.
- The proxy hub is a faithful PostToolUse-telemetry analog (with a server-side fallback log too).
- **The one place a hook (or corrected deny) is genuinely needed is HIGH-1:** AIMonitor cannot confine Claude's *native* tools, so the agent's own Edit/Write/Bash against the live watched root are unguarded today.

## Suggested order for Codex
1. HIGH-1 deny/hook fix (cheapest, closes a live safety gap — note this also benefits any Codex session).
2. HIGH-2 extract `MonitorSessionService` into Workflow + owning-layer tests.
3. MEDIUM-1 extract shared guardrail service + the two Phase 5 service tests.
4. LOW tail: ledger status reconciliation + two description rewords.
