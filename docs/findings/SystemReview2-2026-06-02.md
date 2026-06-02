---
status: reviewed
type: finding
created: 2026-06-02
scope: second deep review — consistency/safety + overall system construction & design, after the 17-commit Codex doc+fix drop (origin/main bd24d13 -> 91d934f)
confidence: high on the construction verdict and the resolved/open classification; per-finding confidence in the workflow output
watched-target: C:\SchemaStudioWebViewer V 1.1 - Monitor\SchemaStudioWebViewer.sln
method: multi-agent workflow (6 dimensions — 3 consistency/safety + 3 architecture/design, adversarial per-finding verification, synthesis); 43 raised, 39 confirmed; build green 0/0 on 91d934f
---

## Verdict

The system is **soundly constructed and internally consistent** after the drop. `dotnet build AIMonitor.slnx` is **0 warnings / 0 errors** across all 11 projects + tests. The load-bearing safety guards (staged-candidate immutability, hash-based accept classification, post-accept `RequiresRefresh`, pre-merge validation gate) are enforced **once, centrally in `WorkflowEditService`** — adapters no longer re-implement them. Dependency direction is clean (no inversion; Core is a true leaf), the stdio bridge holds its thin boundary at the project-reference level, and the V2→AIMonitor rename landed completely (no stale `V2` in any active `.md`/`.cs`). **0 confirmed High.** Counts: 0 High / 10 Medium / 10 Low / 19 Info (Info = positive confirmations).

`forceValidation` (by-design, tracked under [[McpElicitationVsWinFormsPrompts-2026-06-01]]) and the environmental Razor source-generator test ([[RazorComponentBindingReferences-2026-06-01]]) are excluded per scope.

## StagedDiffLaunchWorkflow placement — explicit verdict: correctly placed in Runtime

The new `src/AIMonitor.Runtime/StagedDiffLaunchWorkflow.cs` is a thin launch orchestrator: it calls Workflow's `PreMergeValidationService.Validate`, persists via `RecordPreMergeValidation`/`RecordDiffLaunch`, and launches WinMerge via the Runtime-owned `WinMergeDiffToolLauncher` — performing **no** hash classification and **no** accept decision (those stay in `WorkflowEditService`). Dependency direction is correct and non-inverting (`Runtime.csproj` references `Workflow`; not vice-versa). It also closed the prior "engine trusts an adapter-set `launched` bool" crack by recording the launcher's real `Launched` result.

## Resolved since review #1 (docs/findings/WorkflowDeepDiveReview-2026-06-01.md)

| prior item | now | proof |
| --- | --- | --- |
| Engine couldn't enforce its own validation gate | **RESOLVED** — engine persists validation status/force-approval; `RecordDecision` refuses accept on not-launched / empty / errored-without-force | `WorkflowEditServiceSafetyTests`, `McpServerSmokeTests`, `CliIndexQueryTests` launch tests |
| `submit_file` raw-writes / bypasses normalization | **RESOLVED** — delegates to `WorkflowEditService.SubmitFile` (`EnsureSessionCanEdit` + line-ending normalization) | `Mcp_submit_file_preserves_existing_line_endings` |
| CLI/MCP launch-diff drift (prior PARITY-2) | **RESOLVED** — both route through shared `StagedDiffLaunchWorkflow`; `CanShow()`-aware no-dialog guidance now shared | `StagedDiffLaunchWorkflow.cs:66-68`; callers `Cli:214`/`Mcp:922` |
| MCP-only span byte logic | **RESOLVED** — span positioning engine-owned (`FindTextSpan`/`ReplaceSpan`, CRLF-aware) | `WorkflowEditServiceSafetyTests:80-101` |
| `accepted-normalized` never exercised (COV-2) | **PARTIALLY RESOLVED (CLI)** — full LF-working/CRLF-watched accept asserts `accepted-normalized` + rebuilt | `CliIndexQueryTests.cs:1045-1104` |
| stdio bridge boundary; V2 naming; manifest-lock hardening | **RESOLVED / hardened** | csproj refs; `AcquireManifestLock` |

## Still OPEN (carried from review #1)

### PARITY-1 — Roslyn typed-edit tools still skip the post-accept refresh guard (Medium)
`src/AIMonitor.Workflow/RoslynEditService.cs:275-307`. `EnsureSession` returns on `HasSession` without checking `RequiresRefresh`; `WriteRoot` does raw `File.WriteAllText` with no manifest lock. All 11 Roslyn tools silently clobber the stale Working candidate after an accept (bounded — blocked downstream at stage, no watched-source breach). This is the one review #1 fix that did **not** get applied. **Fix:** make `EnsureSession` throw on `RequiresRefresh` (or delegate to `WorkflowEditService.EnsureSessionCanEdit`); route `WriteRoot` through a lock-taking seam. Then add the after-accept regression test (REG-COV-4) — current tests cover `submit_file`/`replace_span` after accept but no Roslyn tool and no `stage_candidate_for_review`.

### INDEXREFRESH-1/2/3 — index-refresh truthfulness chain (Medium)
- **-1 / CLARITY-1** (`McpServer:884-910`, `Cli:298-300`): `NextStep` is keyed only on `record.Classification`; a failed rebuild (`Status="failed"`) still reports "Index was rebuilt after accept" — contradicts CLAUDE.md "check indexRefresh status." Structured payload is correct; only the human string is wrong.
- **-2** (`src/AIMonitor.Data/SolutionIndexStore.cs:15-48,245-257`): `SaveSnapshot` clears-then-inserts unconditionally; a non-throwing degraded/0-project MSBuild load **wipes the index and commits an empty snapshot reported as `rebuilt`/`IsError=false`**. Data-destructive. **Fix:** abort the swap (don't Clear) and return `IsError=true` on 0-project/below-floor or load-diagnostic failure; optionally back up the sqlite file pre-Clear.
- **-3** (`WorkflowEditService.cs:739-758`): accept + manifest persisted and returned before the adapter-invoked rebuild; a crash between leaves a durable accept with a permanently stale index, and `Refresh()` clears `RequiresRefresh` without rebuilding. **Fix:** per-file index-stale flag set at accept, cleared only by a successful rebuild.
- **-4** (coverage): no test drives a throwing/0-project rebuild and asserts `IsError`/`failed`.

### LIFECYCLE-4 — launch re-runnable on a terminal record (Medium)
`src/AIMonitor.Runtime/StagedDiffLaunchWorkflow.cs:19-72`. `Launch` has no precondition on `record.Decision`; re-invoking on an accepted/rejected record overwrites validation/launch telemetry, and `PrepareReviewFileForLaunch` recreates the blank watched file a reject intentionally removed. **Fix:** guard `Launch` (or the three service methods) to throw when `record.Decision` is terminal.

## New construction findings

### COHESION-1 / LAYER-2 / PARITY-DECISION-1 — decision flow not extracted like the launch flow was (Medium)
`Cli/Program.cs:267-302`, `McpServer/Program.cs:876-911`. The launch flow was extracted into shared `StagedDiffLaunchWorkflow`, but the **sibling `record-decision` + post-accept-refresh orchestration** (classification gate → `RebuildAfterAcceptedDecision` → `ReviewDecisionWithIndexRefreshResult` → identical `NextStep` strings) was copy-pasted into both adapters. **Already drifted: MCP calls `CreateSummary` twice (903-904); CLI computes once.** The one remaining significant drift seam. **Fix:** extract a shared `StagedDecisionWorkflow` (sibling to `StagedDiffLaunchWorkflow`) both adapters wrap thinly; gives INDEXREFRESH-1's fix one home and kills the double `CreateSummary`.

### PARITY-REBUILD-1 — index-rebuild composition inlined in 4 places (Low)
`Cli:381-383`, `McpServer:159-161` (+ App `SolutionIndexControl` and `PostAcceptIndexRefreshService`) each independently compose `SolutionIndexStore`+`SolutionIndexBuilder`+`MSBuildWorkspaceLoader`. **Fix:** add a `SolutionIndexRebuildService.Rebuild(settings)` facade in Indexing; all sites call it.

### CLARITY-2 — auto-refresh / EnsureSession applied unevenly; two copies (Low)
Span tools auto-refresh; `submit_file`/`replace_text` don't; Roslyn uses a *second* `EnsureSession` (`RoslynEditService.cs:275`) with different guard semantics; CLI never auto-refreshes. Safe but uneven/undocumented. **Fix:** one `WorkflowEditService.EnsureEditableSession` (auto-create when appropriate AND enforce `RequiresRefresh`); collapse the two copies (also closes PARITY-1).

## Lows / Info (no urgent action)
LIFECYCLE-LOW-1 (`Accept()/Reject()` read manifest outside `AcquireManifestLock` — bounded, RecordDecision re-verifies), REG-COV-1 (accept-before-launch guard untested), REG-ACCEPT-4 (force-approved *success* path untested), PARITY-STAGE-SHAPE-1 (CLI exposes top-level `watchedFilePath`/`relativePath`/`launchStatus`; MCP doesn't — cosmetic, shared `StagedEditSummary` identical), PARITY-ACCEPT-1 (CLI `accept`/`reject` undocumented; `--expected-hash` vs documented `--expected-staged-hash` — safety resolved, ergonomics persist), PARITY-DEADCODE-1 (unused `SemanticEditNotImplemented` at `McpServer:1191-1202` — delete), REG-BRIDGE-1 (proxy-hub relay test still `[Fact(Skip)]`).

## Top 3 next actions
1. **Index-refresh truthfulness (INDEXREFRESH-1/2/3)** — highest leverage; currently data-destructive + operator-misleading.
2. **Roslyn guard gap (PARITY-1)** + the after-accept regression test (COV-4) — closes the last unguarded post-accept write path.
3. **Extract a shared `StagedDecisionWorkflow`** (mirror the launch extraction) — one home for INDEXREFRESH-1's fix + the LIFECYCLE-4 terminal-record guard; kills the double `CreateSummary`. Fold in `SolutionIndexRebuildService` (PARITY-REBUILD-1) and the single `EnsureEditableSession` (CLARITY-2) as the broader de-dup pass.

## Notes
- All four still-open items trace to review #1; PARITY-1 specifically is the fix that was not carried through this drop. The recurring theme is the **adapter-duplicated orchestration around `record-decision`** — extracting `StagedDecisionWorkflow` resolves COHESION-1, gives INDEXREFRESH-1/LIFECYCLE-4 a single home, and prevents the next drift.
- Related history: [[WorkflowDeepDiveReview-2026-06-01]], [[CliVsMcpAdapterReview-2026-06-01]], [[CompactResponseVerbosePersist-2026-06-01]].
