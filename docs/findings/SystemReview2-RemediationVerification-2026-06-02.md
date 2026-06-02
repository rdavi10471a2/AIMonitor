---
status: verified-pending-merge
type: finding
created: 2026-06-02
scope: independent verification of the Codex remediation PR for system review #2
pr-branch: codex/system-review2-remediation @ 8a3d816 ("Remediate system review workflow findings")
base: origin/main @ ce05216
work-order: docs/findings/CodexWorkOrder-SystemReview2-2026-06-02.md
source-finding: docs/findings/SystemReview2-2026-06-02.md
verified-by: read the actual branch code (not the diffstat); dotnet build AIMonitor.slnx = 0/0; targeted new unit tests green
---

## Verdict: ship it (merge)

The remediation cleanly closes **every open Medium from system reviews #1 and #2** plus the new construction
finding, is properly tested, and updates the contract docs in the same change (system-memory rule honored). Build is
**0 warnings / 0 errors** on `AIMonitor.slnx`. Targeted new unit tests pass: **Workflow 15/15** (was 6 → +9),
**Data 7/7**, **Runtime 1/1** (new `AIMonitor.Runtime.Tests` project). The long integration suite was not re-run
(reliably green except the one permanently skipped test); it builds clean.

Verification was code-grounded — each item below was confirmed against the branch source at `8a3d816`, not inferred
from the diffstat.

## Item-by-item ledger (vs CodexWorkOrder-SystemReview2-2026-06-02.md)

| Work-order item | Status | Evidence on branch |
| --- | --- | --- |
| **A** — extract shared `StagedDecisionWorkflow` in `AIMonitor.Indexing`; kill the double `CreateSummary` | ✅ | `src/AIMonitor.Indexing/StagedDecisionWorkflow.cs` (new); computes `summary` once; both adapters call `new StagedDecisionWorkflow().Record(...)` — `Cli/Program.cs:247,267`, `McpServer/Program.cs:880`. Correct placement (Indexing already owns the result type + rebuild service and references Workflow; no new project ref, no cycle). |
| **B1** — `NextStep` reflects `indexRefresh.IsError` | ✅ | `StagedDecisionWorkflow.CreateNextStep` returns the "rebuild failed / index rows stale" string when `indexRefresh?.IsError == true`. |
| **B2 / LIFECYCLE-4** — terminal-record guard | ✅ | `WorkflowEditService.EnsureRecordNotDecided` (throws on accepted/accepted-normalized/rejected); called inside `RecordDecision` itself and at the top of `StagedDecisionWorkflow.Record` (defense in depth). |
| **C1 / INDEXREFRESH-2** — don't wipe the index on a degraded load | ✅ | `SolutionIndexStore.SaveSnapshot` now throws "Refusing to replace an existing solution index with a degraded zero-project snapshot." when `snapshot.Projects.Count == 0 && previousSummary.ProjectCount > 0` (before `ClearCurrentState`). |
| **C2 / INDEXREFRESH-3** — crash-safety | ✅ (exceeded brief) | New `IndexStale` flag on `EditSessionManifest`/`EditSessionStatus`, set at accept alongside `RequiresRefresh`; surfaced as an `index-stale` classification in `GetStatus`; carried forward across `Refresh`; cleared by `MarkIndexFresh`, which is invoked on the **rebuild success path** (`PostAcceptIndexRefreshService.cs:48`). Self-healing on success, persists on failure. |
| **D / PARITY-1 / CLARITY-2** — unify `EnsureSession`, guard Roslyn surface | ✅ | New `WorkflowEditService.EnsureEditableSession` (creates session when absent, else takes `AcquireManifestLock` + `EnsureSessionCanEdit`) and `WriteWorkingCandidate` (lock + guard + line-ending normalization). `RoslynEditService.EnsureSession`/`WriteRoot` now delegate to both — the post-accept `RequiresRefresh` guard now covers all 11 Roslyn tools. |
| **E — LIFECYCLE-LOW-1** | ✅ | `Accept()`/`Reject()` now read the manifest inside `AcquireManifestLock`. |
| **E — PARITY-REBUILD-1** | ✅ | New `SolutionIndexRebuildService.RebuildAsync`; all 4 inline composition sites now call it (App `SolutionIndexControl:359`, `Cli:362`, `McpServer:158`, `PostAcceptIndexRefreshService:35`). |
| **E — PARITY-DEADCODE-1** | ✅ | `SemanticEditNotImplemented` deleted from `McpServer/Program.cs` (0 matches). |
| **E — PARITY-ACCEPT-1** | ✅ | `edit accept` documented in help as a shortcut; uses `--expected-staged-hash`; `record-decision` accepts `--expected-staged-hash` with `--expected-hash` as a backward-compatible fallback. |
| **E — REG-COV-1 / REG-ACCEPT-4 / COV-4** | ✅ | `WorkflowEditServiceSafetyTests` grew +9 (accept-before-launch throw, force-approved success, Roslyn/stage after-accept guard, MarkIndexFresh), `SolutionIndexStoreTests` +31 (0-project abort), plus new `StagedDecisionWorkflowTests` / `StagedDiffLaunchWorkflowTests`. |
| **forceValidation** | ✅ | Untouched (by-design). |
| Contract docs | ✅ | `docs/components/{Indexing,Data,Workflow,McpServer,Cli}.md`, `docs/system-memory/README.md`, `docs/architecture/Architecture.md`, `AIMonitor.slnx` (new test project) all updated in the same commit. |

## Remaining / optional (non-blocking)

- **REG-BRIDGE-1 still open** — the proxy-hub relay + telemetry test remains `[Fact(Skip)]` in
  `McpServerSmokeTests.cs` (1 skip). The work order permitted leaving it skipped; the live `--mcp-live-workflow`
  ToolSmokeTests path still exercises it operator-driven. Lowest-priority carryover; worth a real headless test
  eventually.
- **Minor (style/concurrency):** `PostAcceptIndexRefreshService` calls `new WorkflowEditService(settings).MarkIndexFresh(...)`
  on a fresh instance rather than the injected singleton. Harmless in the single-operator flow, but if
  `AcquireManifestLock` is instance-scoped it would not coordinate with the singleton's lock under concurrent same-file
  activity (touches the prior LIFECYCLE-5 concurrency theme). Consider reusing the injected service or making the lock
  process-wide. Not a blocker.

## Net

All four still-open items from review #1 (PARITY-1, INDEXREFRESH-1/2/3, LIFECYCLE-4) and the new review-#2 construction
finding (record-decision orchestration duplication) are resolved and tested. `record-decision` and `launch-diff`
orchestration now each live in exactly one shared workflow. Recommend merge.
