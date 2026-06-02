---
status: open
type: work-order
created: 2026-06-02
audience: Codex
baseline-commit: b5d87b2 (build green 0/0 on AIMonitor.slnx)
source-finding: docs/findings/SystemReview2-2026-06-02.md
scope: remediate the open + new items from system review #2
watched-target: C:\SchemaStudioWebViewer V 1.1 - Monitor\SchemaStudioWebViewer.sln
---

# Codex Work Order — System Review #2 Remediation

This is a self-contained remediation brief for the items confirmed open in
`docs/findings/SystemReview2-2026-06-02.md`. All line numbers are against `b5d87b2`. Verify before editing — the
review was code-grounded but re-confirm each anchor.

## Ground rules (read first)

- **`forceValidation` is BY-DESIGN. Do NOT "fix" it.** It is the documented operator-approval affordance (chat approval
  + WinMerge save) and tests depend on it. The tracked improvement is MCP elicitation
  (`docs/findings/McpElicitationVsWinFormsPrompts-2026-06-01.md`) — out of scope here.
- **System-memory contract rule** (`docs/system-memory/README.md`): if a change affects a boundary item (adapter
  boundaries, classifications, index rebuild/query, telemetry, component ownership/data-flow), update the matching
  `docs/components/*.md` and `docs/system-memory` doc AND add/adjust tests **in the same change**.
- **Never write watched source directly**; this is product-code work under `src/` + `tests/`, not a watched-project edit.
- Codex host contract is `AGENTS.md`. Keep adapters thin; shared behavior goes in services.
- Acceptance baseline: `dotnet build AIMonitor.slnx -c Debug` stays **0 warnings / 0 errors**; new tests pass. (Do not
  run the full suite as a smoke — it is long; build + the targeted new tests are enough.)

## Suggested branch & sequencing

Branch: `codex/system-review2-remediation`.

Recommended order (later tasks depend on earlier homes):

1. **Task A** — extract the shared decision workflow (creates the single home).
2. **Task B** — fold INDEXREFRESH-1 (NextStep truthfulness) + LIFECYCLE-4 (terminal-record guard) into the new homes.
3. **Task C** — INDEXREFRESH-2 (store abort-on-degraded) + INDEXREFRESH-3 (index-stale flag).
4. **Task D** — PARITY-1 / CLARITY-2 (unify `EnsureSession`, guard Roslyn) + its tests.
5. **Task E** — the lows / coverage / cleanup.

---

## Task A — Extract a shared `StagedDecisionWorkflow` (COHESION-1 / LAYER-2 / PARITY-DECISION-1, Medium)

**Problem.** The launch flow was extracted into `AIMonitor.Runtime/StagedDiffLaunchWorkflow.cs`, but the sibling
`record-decision` + post-accept-refresh orchestration was copy-pasted into both adapters and has **already drifted**.

Current MCP — `src/AIMonitor.McpServer/Program.cs:883-910` (note the **double** `CreateSummary` at 903-904):
```csharp
StagedEditRecord record = workflowService.RecordDecision(stagedRecordId, decision, expectedStagedHash);
PostAcceptIndexRefreshResult? indexRefresh = null;
if (record.Classification is "accepted" or "accepted-normalized")
{
    indexRefresh = new PostAcceptIndexRefreshService().RebuildAfterAcceptedDecision(settings, logger, record, "AIMonitor.McpServer");
}
return new ReviewDecisionWithIndexRefreshResult
{
    ...
    StagedRecordSummary = workflowService.CreateSummary(record),
    StagedRecordPath = workflowService.CreateSummary(record).RecordPath,   // ← second CreateSummary call
    StagedRecord = verbose ? record : null,
    IndexRefresh = indexRefresh,
    NextStep = record.Classification is "accepted" or "accepted-normalized"
        ? "Index was rebuilt after accept. Run edit refresh before further edits to this watched file."
        : "Decision recorded. Do not rely on changed index rows unless an accepted decision rebuilt the index."
};
```
CLI counterpart — `src/AIMonitor.Cli/Program.cs:274-301` (`CreateDecisionResponse`) is the same logic, computing
`summary` once (284) and reusing it. Byte-identical `NextStep` strings.

**Fix.**
- Create `StagedDecisionWorkflow` with a single method that does: `RecordDecision` → conditional
  `RebuildAfterAcceptedDecision` → assemble `ReviewDecisionWithIndexRefreshResult` (compute `CreateSummary` **once**) →
  `NextStep`. Suggested signature:
  ```csharp
  public ReviewDecisionWithIndexRefreshResult Record(
      MonitorSettings settings, IMonitorLogger logger, WorkflowEditService workflowService,
      string stagedRecordId, string decision, string? expectedStagedHash, string source, bool verbose = false)
  ```
- **Placement: put it in `AIMonitor.Indexing`, not Runtime.** `Indexing` already owns
  `PostAcceptIndexRefreshService` and `ReviewDecisionWithIndexRefreshResult`, and already references `Workflow` (its
  `RebuildAfterAcceptedDecision` takes `AIMonitor.Workflow.StagedEditRecord`). That lets the new type see
  `WorkflowEditService.RecordDecision`, the rebuild service, and the result type with **no new project reference and no
  cycle**. (Putting it in Runtime would force a new `Runtime→Indexing` reference — verify there is no
  `Indexing→Runtime` edge before considering that; Indexing is the cleaner home.)
- Both adapters call it and wrap thinly. MCP `RecordDiffDecision` and CLI `CreateDecisionResponse`/`Accept` become
  pass-throughs.
- Delete the second `CreateSummary` call.

**Contract docs:** update `docs/components/AIMonitor.Indexing.md` (new Owns entry) and
`docs/components/AIMonitor.McpServer.md` / `AIMonitor.Cli.md` (Does-not-own: decision orchestration). Note in
`docs/architecture/Architecture.md` that decision + launch orchestration are both shared workflows.

**Tests:** add an integration test asserting MCP and CLI `record-decision` return the **same** `NextStep`,
`Classification`, and `IndexRefresh` shape for an identical accepted record (parity lock). Existing
`CliIndexQueryTests`/`McpServerSmokeTests` decision tests must still pass.

---

## Task B — Index-refresh truthfulness NextStep (INDEXREFRESH-1 / CLARITY-1) + terminal-record guard (LIFECYCLE-4)

### B1 — NextStep must reflect `indexRefresh.IsError` (Medium)
In the new `StagedDecisionWorkflow` (single home after Task A), branch `NextStep` on the rebuild outcome **first**:
```csharp
NextStep =
    indexRefresh?.IsError == true
        ? "Accept recorded, but the index rebuild FAILED — index rows are stale. Re-run refresh_solution_index before trusting index queries."
    : record.Classification is "accepted" or "accepted-normalized"
        ? "Index was rebuilt after accept. Run edit refresh before further edits to this watched file."
        : "Decision recorded. Do not rely on changed index rows unless an accepted decision rebuilt the index."
```
This satisfies CLAUDE.md step 8 ("check `indexRefresh.status`"). The structured `IndexRefresh` payload is already
correct; only the human string was lying. Confirm `PostAcceptIndexRefreshResult` exposes `IsError`/`Status`.

### B2 — Guard launch (and decision) against terminal records (LIFECYCLE-4, Medium)
`src/AIMonitor.Runtime/StagedDiffLaunchWorkflow.cs:19` calls `GetStagedRecord` with no precondition on
`record.Decision`. Re-invoking on an accepted/rejected record overwrites validation/launch telemetry, and
`PrepareReviewFileForLaunch` (line 72) recreates the blank watched file a reject intentionally removed.

**Fix.** Add a terminal-decision guard at the top of `StagedDiffLaunchWorkflow.Launch` (and in
`StagedDecisionWorkflow.Record`): throw when `record.Decision` is already a terminal value
(`accepted` / `accepted-normalized` / `rejected`), e.g.
`"This staged record already has a final decision (<decision>). Refresh and stage a new candidate before launching/recording again."`
Best placed centrally — add a `WorkflowEditService` helper (e.g. `EnsureRecordNotDecided(record)`) the workflows call,
so MCP + CLI + both workflows share one guard.

**Tests:** `WorkflowEditServiceSafetyTests` — launching/recording on an already-accepted and an already-rejected record
throws; assert a rejected **new-file** re-launch does NOT recreate the blank watched file.

---

## Task C — `SolutionIndexStore` must not wipe the index on a degraded load (INDEXREFRESH-2/-3, Medium)

### C1 — Abort the clear-then-insert on a 0-project / degraded snapshot (INDEXREFRESH-2)
`src/AIMonitor.Data/SolutionIndexStore.cs:15-48`, the destructive line is **22**:
```csharp
public SolutionIndexSummary SaveSnapshot(MSBuildSolutionSnapshot snapshot)
{
    database.EnsureCreated();
    using SqliteConnection connection = database.OpenConnection();
    using SqliteTransaction transaction = connection.BeginTransaction();
    ClearCurrentState(connection, transaction);          // ← unconditional wipe, even for a 0-project snapshot
    SaveSolutionState(connection, transaction, snapshot);
    ...
```
A non-throwing degraded MSBuild load (0 projects) therefore wipes the existing index and commits an empty snapshot,
reported as a successful rebuild.

**Fix.** Before `ClearCurrentState`, guard on a floor:
```csharp
if (snapshot.Projects.Count == 0 /* && previously had > 0 */)
{
    // do NOT clear; abort the swap and signal failure
}
```
Decide the failure signal and **thread it up** so callers see it:
- Option 1 (preferred): return a result that carries an aborted/`IsError` flag, propagated through
  `SolutionIndexBuilder.RebuildAsync` → `PostAcceptIndexRefreshResult.IsError`, so Task B1's NextStep fires.
- Option 2: throw a typed exception that `RebuildAfterAcceptedDecision` catches and maps to `IsError=true`.
Optionally back up the sqlite file before `ClearCurrentState` for recoverability. Consider also a diagnostics-based
floor (if `snapshot.Diagnostics` indicate a load failure, treat as degraded).

### C2 — Per-file index-stale flag so accept+reindex is crash-safe (INDEXREFRESH-3)
`src/AIMonitor.Workflow/WorkflowEditService.cs:739-758`: the accept decision + manifest are persisted and returned
**before** the adapter-invoked rebuild runs; a crash between leaves a durable accept with a permanently stale index, and
`Refresh()` clears `RequiresRefresh` without rebuilding. **Fix.** Persist a per-file `IndexStale` flag on the manifest
at accept time (alongside `RequiresRefresh` at line 753), cleared only by a successful rebuild; have index-query paths
surface/repair when it is set. (Lower urgency than C1; can be a follow-up commit.)

**Contract docs:** update `docs/components/AIMonitor.Data.md` (SaveSnapshot now aborts on degraded load) and the
index-rebuild semantics note in `docs/system-memory/README.md`.

**Tests:** `AIMonitor.Data.Tests` — `SaveSnapshot` with a 0-project snapshot does NOT clear an existing populated index
and signals failure; `AIMonitor.Integration.Tests` — a forced 0-project / failing-loader rebuild yields
`indexRefresh.IsError == true` and the failure-shaped `NextStep` (INDEXREFRESH-4 coverage).

---

## Task D — Unify `EnsureSession`; guard the Roslyn typed-edit surface (PARITY-1 / CLARITY-2 / COV-4, Medium)

**Problem.** There are two `EnsureSession` implementations with different guard semantics. The Roslyn one skips the
post-accept `RequiresRefresh` guard the others enforce.

`src/AIMonitor.Workflow/RoslynEditService.cs:283-291` (current — no `RequiresRefresh` check):
```csharp
EditSessionStatus status = workflowService.GetStatus(fullPath);
if (status.HasSession)
{
    return status;                       // ← bypasses RequiresRefresh
}
return File.Exists(fullPath) ? workflowService.Refresh(fullPath) : workflowService.NewFile(fullPath);
```
and `WriteRoot` (294-298) does a raw `File.WriteAllText(status.WorkingFilePath, ...)` with no manifest lock. All 11
Roslyn tools route through these. Contrast `WorkflowEditService.EnsureSessionCanEdit` (private static), which throws on
`RequiresRefresh`, and the MCP `AIMonitorTools.EnsureSession` (`Program.cs:~1134-1148`) which also throws.

**Fix.**
- Introduce **one** shared `WorkflowEditService.EnsureEditableSession(path)` that: creates the session when absent
  (refresh existing / new-file), AND throws on `RequiresRefresh` with the canonical message
  `"Previous decision was accepted. Run refresh_file before editing or staging this file again."`
- Make `RoslynEditService.EnsureSession` and the MCP `AIMonitorTools.EnsureSession` both delegate to it (collapse the
  copies — closes CLARITY-2 and PARITY-1 together).
- Route `RoslynEditService.WriteRoot`'s write through a lock-taking `WorkflowEditService` seam (e.g.
  `WriteWorkingCandidate(status, content)` that takes `AcquireManifestLock`), so typed edits match the locking the rest
  of the engine uses.

**Contract docs:** `docs/components/AIMonitor.Workflow.md` (single session-gate seam) +
`docs/components/AIMonitor.McpServer.md` (Roslyn tools obey RequiresRefresh).

**Tests (COV-4):** `McpServerSmokeTests` — after an accepted decision, calling a Roslyn tool (e.g. `add_method` /
`submit_symbol`) and `stage_candidate_for_review` each **throw** until `refresh_file` runs. (Current after-accept test
only covers `submit_file`/`replace_span`.)

---

## Task E — Lows, coverage, cleanup

- **LIFECYCLE-LOW-1** (`WorkflowEditService.cs:766-798`): `Accept()`/`Reject()` call `LoadManifest` (769/789) outside
  `AcquireManifestLock` (unlike `RecordDecision`, which locks at 746). Wrap the read in `AcquireManifestLock(fullWatchedPath)`.
- **REG-COV-1**: add a unit test that `RecordDecision(accepted)` throws ("...before a successful diff review launch",
  `WorkflowEditService.cs:~696`) when no launch was recorded.
- **REG-ACCEPT-4**: add the force-approved **success** test — failed validation + `forceApproved=true` + launched +
  matching hash → accept succeeds and index refreshes (only the blocked path is currently asserted).
- **PARITY-DEADCODE-1**: delete the unused `SemanticEditNotImplemented` helper at `McpServer/Program.cs:~1191-1202`.
- **PARITY-ACCEPT-1** (`Cli/Program.cs:205-206,262`, help 35-41): CLI `accept`/`reject` are undocumented and `accept`
  uses `--expected-hash` while `record-decision` uses `--expected-staged-hash`. Either document + rename for consistency,
  or remove the shortcuts in favor of `record-decision`. Update `CliIndexQueryTests` accordingly.
- **PARITY-STAGE-SHAPE-1**: align the MCP stage result's top-level fields with the CLI's
  (`watchedFilePath`/`relativePath`/`launchStatus`), or declare `StagedEditSummary` the canonical contract; add a CLI
  compact-default test mirroring `McpServerSmokeTests:196`.
- **PARITY-REBUILD-1**: add `SolutionIndexRebuildService.Rebuild(settings)` in `AIMonitor.Indexing` and have all four
  inline composition sites (`Cli:381-383`, `McpServer:159-161`, App `SolutionIndexControl`, `PostAcceptIndexRefreshService`)
  call it.
- **REG-BRIDGE-1** (`McpServerSmokeTests.cs:~143`): implement a headless proxy-hub relay + telemetry test, or change the
  `[Fact(Skip)]` reason to point at the concrete `--mcp-live-workflow` ToolSmokeTests harness that does cover it.

---

## Acceptance criteria

- `dotnet build AIMonitor.slnx -c Debug` → 0/0.
- New/changed tests pass: Task A parity lock, Task B terminal-record + NextStep-on-IsError, Task C 0-project abort +
  failed-rebuild surfacing, Task D Roslyn/stage after-accept guard, Task E REG-COV-1/REG-ACCEPT-4.
- `forceValidation` behavior unchanged.
- Each boundary-affecting change ships with its `docs/components/*` + `docs/system-memory` update (contract rule).
- No new cross-adapter duplication; `record-decision` and `launch-diff` orchestration each live in exactly one shared
  workflow.

See `docs/findings/SystemReview2-2026-06-02.md` for the full finding context and the resolved/open ledger.
