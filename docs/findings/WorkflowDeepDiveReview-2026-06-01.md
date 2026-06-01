---
status: reviewed
type: finding
created: 2026-06-01
scope: deep-dive review of the AIMonitor safe-edit workflow + adapter boundary, after the cf52655/3b81216 merge
confidence: high on the boundary verdict; per-finding confidence noted inline
watched-target: C:\SchemaStudioWebViewer V 1.1 - Monitor\SchemaStudioWebViewer.sln
method: multi-agent workflow (6 review dimensions, adversarial per-finding verification, synthesis); 28 findings raised, 24 survived verification
---

## Summary / verdict

Deep-dive review run immediately after Codex merged the adapter-safety + Razor-indexing work
(`cf52655` "Address adapter safety and Razor indexing findings", `3b81216` "Test full index snapshot replacement").

**The core safety invariant holds: agents never mutate watched source directly, and no path lets divergent or
un-reviewed bytes be classified `accepted`.** Every edit writes only to monitor-owned Working/staged files; the accept
decision is gated by content-hash classification (`ReviewDecisionClassifier`: watched-hash must equal staged-hash, else
`dirty-unexpected` throws). That gate is engine-owned and sits underneath every adapter. Nothing catastrophic.

**Adapter boundary verdict (the primary question):** the "MCP/CLI are thin adapters over the shared workflow engine"
rule mostly holds. The `submit_file` rework actively *improved* it — raw `File.WriteAllText` moved out of the MCP
adapter into the shared `WorkflowEditService.SubmitFile`. The real accept safety lives in the engine, not the adapters.
The remaining boundary issues are: one genuine guard-inconsistency (PARITY-1), one guidance-parity gap (PARITY-2), and
the `forceValidation` design tradeoff (PARITY-3 / ACCEPT-1, recalibrated below).

Counts: **1 originally-High (recalibrated to by-design), 11 Medium, 12 Low.** Clean: no confirmed
direct-watched-mutation, no accept-of-divergent-content, hash gate solid; bridge findings are telemetry/lifecycle
hygiene only. The known environmental Razor source-generator (`@bind-Value`) test failure is excluded (see
[[RazorComponentBindingReferences-2026-06-01]]).

## Recalibration: `forceValidation` self-approval is by-design, not a defect

The review's top finding (ACCEPT-1 / PARITY-3, originally High) was that `forceValidation` is an agent-supplied bool on
both adapters, so an agent can push a *failed* pre-merge validation past the human gate by passing `forceValidation:true`
— the same bool carries both "agent requested force" and "operator approved force", and no synthesizable-token check
distinguishes them.

**The mechanism is real, but the characterization as a safety hole is wrong.** It is a deliberate, documented affordance:

- **Tests depend on it.** `WorkflowEditServiceSafetyTests.cs` and `CliIndexQueryTests.cs` drive
  `forceValidation`/`forceApproved` directly. In headless CI `PreMergeValidationOverridePrompt.CanShow()` is false, so
  the flag is the only way the suite can exercise the launch→accept path without a WinForms dialog.
- **It is the documented operator-approval protocol**, gated socially (chat approval) rather than technically:
  - `docs/feature-maps/CliWorkflowEditLoop.md:83` — "Failed validation blocks review unless the user approves the
    override dialog, **or the agent asks in chat and the caller passes `--force-validation` after explicit human
    approval**."
  - `docs/feature-maps/SharedAdapterSurface.md:64`, `docs/claude-skills/AIMonitorWorkflowQuickStart.md:93`,
    `docs/claude-skills/ReviewQueueAndGates.md:22` — all repeat "ask the operator in chat before using
    `forceValidation`."

The reviewer matched the code against the literal CLAUDE.md phrase "forceValidation must require explicit operator
approval" and flagged the absence of *technical* enforcement, without crediting the chat-approval contract + WinMerge
save as the actual approval mechanism. Also note the bound: even a forced accept still goes through WinMerge save + hash
classification, so it cannot smuggle divergent bytes into watched source — it only skips the "are you sure the build is
broken?" checkpoint.

**Disposition:** downgrade ACCEPT-1/PARITY-3 from "High safety gap" to **by-design tradeoff, already tracked** by
[[McpElicitationVsWinFormsPrompts-2026-06-01]] — the intended improvement is to replace the WinForms-only prompt with MCP
`elicitation/create` so operator approval becomes a first-class gate for MCP clients (the CLI keeps its terminal/chat
channel). No new work item beyond that finding.

## Genuinely actionable findings

### PARITY-1 — Roslyn typed-edit surface skips the `RequiresRefresh` guard (Medium, confirmed)

`src/AIMonitor.Workflow/RoslynEditService.cs:275-292` (`EnsureSession`), write at `294-307`.

The `cf52655` fix added the post-accept `RequiresRefresh` guard to MCP `AIMonitorTools.EnsureSession`
(`McpServer/Program.cs:1137-1140`) and to `WorkflowEditService.EnsureSessionCanEdit` (used by
ReplaceText/SubmitFile/Stage/Compare). But the **typed-edit surface bypasses both**: all 11 Roslyn tools
(`submit_symbol`, `add_method`, `add_field`, `add_property`, `add_constructor`, `add_nested_type`, `add_symbol`,
`add_using`, `remove_using`, `set_type_partial`, `remove_symbol`) funnel through `RoslynEditService.EnsureSession`, which
returns on `status.HasSession` without inspecting `RequiresRefresh`. So a post-accept typed edit silently clobbers the
Working candidate the refresh should have recaptured.

Bound: caught downstream — `Stage`/`Compare` call `EnsureSessionCanEdit`, which throws on `RequiresRefresh`, so no
watched-source breach. The defect is the broken refresh-before-edit lifecycle for typed edits + silent loss of the
just-accepted bytes/line-endings.

**Fix:** make `RoslynEditService.EnsureSession` throw on `RequiresRefresh` (or delegate to the shared
`EnsureSessionCanEdit`). Add a regression test: `add_method`/`submit_symbol` throws after an accepted decision until
`refresh_file` is run.

### INDEXREFRESH-2 — degraded MSBuild load wipes the index and commits an empty snapshot reported as "rebuilt" (Medium, confirmed)

`src/AIMonitor.Data/SolutionIndexStore.cs:15-48,245-257,259-273`.

`SaveSnapshot` unconditionally `ClearCurrentState` then inserts the new snapshot, with no minimum-project/diagnostic
floor and no backup. A non-throwing degraded load (e.g. 0 projects resolved) therefore wipes the existing index and
commits an empty/partial snapshot — and the post-accept response still reports `"status":"rebuilt"`. Not unsafe to
watched source, but it silently blinds the index. This is the biggest *practical* surprise risk in the review.

**Fix:** abort the swap (don't `Clear`) and return `IsError=true` when project count drops to 0/below a floor or
diagnostics indicate a failed load; optionally back up the sqlite file pre-`Clear`.

### INDEXREFRESH-1 / -3 — rebuild error not surfaced; accept+reindex not crash-atomic (Medium, confirmed)

`src/AIMonitor.McpServer/Program.cs:845-871`, `src/AIMonitor.Cli/Program.cs:311-338`,
`src/AIMonitor.Workflow/WorkflowEditService.cs:583-597`.

Callers never gate on `indexRefresh.IsError`: a `Status="failed"` rebuild is attached to the response but ignored, and
`NextStep` always says "Index was rebuilt after accept." Separately, the accept classification is persisted *before* the
rebuild, so a crash in between leaves a durable accept with a permanently stale index (and `refresh_file` clears
`RequiresRefresh` without rebuilding the index).

**Fix:** branch `NextStep`/response shape on `indexRefresh.IsError`; persist a per-file "index-stale" flag at accept time
cleared only on a successful rebuild, so recovery forces a real rebuild before serving index queries.

### LIFECYCLE-4 — launch/validation re-runnable on an already-decided record (Medium, confirmed)

`src/AIMonitor.Workflow/WorkflowEditService.cs:458-478`; `McpServer/Program.cs:875-941`; `Cli/Program.cs:207-279`.

`RecordPreMergeValidation`/`RecordDiffLaunch` have no precondition on `record.Decision`; re-invoking `launch_staged_diff`
overwrites validation/launch telemetry on a terminal record, and re-launching a *rejected* new-file record recreates the
blank watched file (`PrepareReviewFileForLaunch:498-499`). Accept itself still re-verifies hashes, so not a bypass.

**Fix:** guard `RecordPreMergeValidation`/`RecordDiffLaunch`/`PrepareReviewFileForLaunch` to throw when `record.Decision`
is terminal (accepted/accepted-normalized/rejected).

### Coverage gaps (Medium/Low, confirmed)

The new guards are thin on tests — each of these would let a regression ship green:
- **COV-1** — accept-before-launch guard (`WorkflowEditService.cs:540-543`) has no test (every accept test launches
  first).
- **COV-2 / ACCEPT-4** — `accepted-normalized` classification is never exercised at any layer; the force-approved
  *success* path is never asserted (only the blocked paths are).
- **COV-4** — `stage_candidate_for_review`'s `RequiresRefresh` block is untested (overlaps PARITY-1).
- **INDEXREFRESH-4** — no test for a failed/degraded post-accept rebuild.
- **BRIDGE-1** — the only end-to-end proxy-hub relay + telemetry test
  (`McpServerSmokeTests.cs:113-132`) is permanently `[Fact(Skip)]`.

### PARITY-2 — MCP gives weaker no-dialog guidance than CLI (Low, confirmed)

`src/AIMonitor.McpServer/Program.cs:910-927` vs `src/AIMonitor.Cli/Program.cs:239-259`.

On a blocked validation the CLI returns a `CanShow()`-aware `nextStep` ("no interactive dialog → ask the user, rerun
with `--force-validation` only after approval"); the MCP blocked result returns only a generic message with no equivalent
guidance. Not a bypass — asymmetric guidance only. **Fix:** add the same `CanShow()`-aware guidance to the MCP
blocked-launch result.

## Lows (hygiene — no invariant impact)

LIFECYCLE-1/2/3 (terminal-decision guard missing; `RequiresRefresh` not consulted on the decision/launch path; staging
orphans prior undecided records — all content-hash-gated, so they degrade to a failed op, not an unsafe write),
LIFECYCLE-5 (no concurrency control; single-writer-per-file is an undocumented assumption), ACCEPT-2 (engine trusts an
adapter-set `launched` bool — advisory only; hash+validation are the real gate), COV-3/COV-5 (force-approved success and
`SubmitFile` new-file line-ending detection untested), BRIDGE-2/3/4 (orphaned child process on host kill; in-flight
correlation dropped on child crash; unbounded pending-telemetry map).

## Top 3 next actions

1. **PARITY-1** — add the `RequiresRefresh` guard to `RoslynEditService.EnsureSession` (+ regression test). Real
   guard-inconsistency introduced by the partial `cf52655` rollout.
2. **INDEXREFRESH-2** (+ -1/-4) — abort the snapshot swap on a degraded/0-project load instead of committing an empty
   index; gate the response/`NextStep` on `indexRefresh.IsError`; add the failed-rebuild tests.
3. **Backfill guard coverage** — COV-1/2/4 + the `accepted-normalized` and force-approved-success paths, so the
   `cf52655` guards can't silently regress.

(`forceValidation` / ACCEPT-1 is intentionally NOT an action item — see recalibration; tracked under
[[McpElicitationVsWinFormsPrompts-2026-06-01]].)

## Notes

- Verifier corrections worth keeping: COV-4's stated rationale ("Roslyn depends on the untested `EnsureSessionCanEdit`
  path") is factually wrong — Roslyn tools never touch that guard; the real issue is the *unguarded* Roslyn path
  (PARITY-1). Several reviewers inflated severity by testing against the literal CLAUDE.md wording without crediting the
  independent hash/validation gates; the adversarial pass downgraded those.
- Related: [[CliVsMcpAdapterReview-2026-06-01]] already flagged the "engine trusts adapter `launched` bool" crack
  (ACCEPT-2 here, still present) and the adapter-boundary framing.
