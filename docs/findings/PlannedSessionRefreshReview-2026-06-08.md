---
status: open
type: review
created: 2026-06-08
audience: Codex + operator
scope: deep review of branch codex/planned-session-refresh (HEAD 63f9620) vs origin/main — functional correctness + do the skills justify it
reviewer: Claude (review gate) — build+test in isolated worktree, 3 parallel review dimensions, adversarial verification of HIGH findings
verdict: DO NOT MERGE AS-IS — 2 verified HIGH regressions + 1 MED deadlock; core safety floor preserved; skills only partially justify the necessity
---

# Review — codex/planned-session-refresh (63f9620)

## Verdict

The branch's core idea is coherent and the **watched-source safety floor is preserved** (no accept without staging + recorded launch; syntax-validated before any candidate write; a Roslyn overlay semantic compile across all planned files before merge; operator-in-loop for every merge). It ships **one verified HIGH (index data loss)**, plus **two MEDs** (pre-merge overlay fidelity vs the intended post-merge full build, and a launch deadlock); its own targeted tests are red in a clean build environment, and the riskiest new paths are untested. **Recommend rework before merge.**

> **Correction (2026-06-08, after reading `CandidateEditValidator.ValidateCandidateOverlayCompilation`):** an earlier draft rated the deferred build as a second HIGH ("no build before merge"). That was overstated. There **is** a pre-merge gate — a Roslyn compile of the whole project with every planned working file overlaid, fired once all planned candidates exist — which catches cross-file C# breaks before any merge. The real gap is narrower (full-build-only errors + no rollback) and is downgraded to MED below.

Reviewed at branch HEAD `63f9620` ("Preserve Razor rows during scoped refresh") — no rework commit is published on origin as of this review.

## Design intent (operator clarification, 2026-06-08) — credited

The goal is sound and the review credits it: `main` ran a full-solution build on **every** overlay validation and around **each merge** — a compile-storm of several builds per multi-file change. The branch turns edits into **verifiable batches**: the agent develops a full up-front plan via the MCP tools, and the batch is validated as a coherent unit instead of compiling after every file. That is the right direction and the per-launch build is **not** sacred.

The HIGH safety finding below is **not** "don't defer / go back to per-file compiling." It is a **placement** problem: the branch validates the batch build at the *terminal accept*, i.e. **after** the operator has already merged the earlier files into watched source. By the operator's own framing, a batch is only "verifiable" if it is verified **before** it is committed. The fix preserves the perf win **and** the safety floor: run the single batch build **up front (at launch, over the staged overlay), before any WinMerge save** — then the operator merges an already-verified batch. Same number of builds (one per batch), but nothing reaches watched source until the whole plan compiles.

## Functional correctness vs main

### HIGH — verified real (adversarially confirmed)

**1. Scoped refresh cascade-deletes inbound cross-project references → silent index data loss.**
`SolutionIndexDatabase.cs` now declares `symbol_references` / `call_sites` / `symbol_relationships` target keys as `references symbols(stable_key) ON DELETE CASCADE`, and `OpenConnection` sets `pragma foreign_keys=on`. The scoped path (`ReplaceProjectFiles` → `DeleteProjectRows`) deletes only the refreshed project A's symbol rows — but the cascade then deletes **project B's** reference/call-site/relationship rows whose `target_stable_key` points at an A symbol (cross-project refs are resolved solution-wide). Re-insertion only restores A's own rows, so **B→A references are permanently orphaned until a full `RebuildAsync`.** Reachable in the *common* case: a single-file edit in a multi-project solution takes the scoped path (`projectPaths.Length==1`). `find_indexed_references`/`callers` silently under-report until a full rebuild. **No test covers a multi-project scoped refresh.** (`SolutionIndexStore.cs` DeleteProjectRows; `SolutionIndexDatabase.cs` FK defs)

_(The deferred-build concern moved to MED — see "Deferred full-build placement" below. There is a pre-merge semantic gate; the gap is only the delta to the full build.)_

### MED
- **Deferred *full*-build placement (the corrected HIGH).** Two compiles exist: GATE 1 = `CandidateEditValidator.ValidateCandidateOverlayCompilation`, a Roslyn compile of the whole project with all planned working files overlaid, fired once every planned candidate exists — runs **before** merge and catches cross-file C# breaks. GATE 2 = `PreMergeValidationService.Validate`, the full `dotnet build`, now deferred to `ValidateTerminalPlannedOverlay` at the **terminal accept**, i.e. **after** the operator has merged the files. GATE 1 is semantic-only — it skips `.razor` markup and runs no MSBuild/analyzers/source-or-Razor generators. So an error only GATE 2 catches (Razor markup, source-gen, analyzer, MSBuild/cross-project) clears the pre-merge gate, gets merged, and fails at the terminal build — leaving watched source non-compiling with **no rollback**; same on abandonment before the terminal accept. Narrow (the common cross-file C# case is caught by GATE 1) but real. **GATE 2's post-merge placement is intended and correct** — it must build the *real* watched tree the operator committed, which only exists after merge. The issue is **fidelity, not placement**: GATE 1 is a weaker (semantic) predictor than GATE 2, so a green overlay can still hit a red real build, leaving the tree transiently non-compiling until the operator acts (recoverable via version control; in-loop). Fix levers: (a) raise GATE 1 toward a full overlay build (cover Razor/MSBuild/analyzers/source-gen) so it faithfully predicts GATE 2; (b) make a GATE 2 failure loud and ensure an abandoned session can't skip it silently. Do **not** relocate GATE 2. (`PreMergeValidationService.ValidateStagedOverlay` hash-only at launch; `CandidateEditValidator.ValidateCandidateOverlayCompilation` semantic GATE 1; `StagedDecisionWorkflow.ValidateTerminalPlannedOverlay` full build GATE 2)
- **Launch lockout / deadlock.** `ShouldDeferBuildValidationUntilAccept` requires *every* planned file to have an active (undecided) staged record at launch time. Once any planned file is decided, launching review for the remaining files throws — so interleaving launch/decide (the per-file flow the branch's own quickstart describes) **deadlocks** the session. Only "launch all, then decide all" works. (`McpServer/Program.cs` ~1475)
- **Schema-upgrade drop window.** `DropSymbolDependentTableIfMissingCascade` drops the three tables on upgrade; if the first post-upgrade op is a scoped refresh (not a full rebuild), every other project's refs stay empty while status reports a plausible-looking but reference-incomplete index.
- **"Scoped/incremental" is actually a whole-project rebuild; file-scoped overloads are dead code.** The production path rebuilds the entire owning project (which is *why* Razor preservation works). The unused `includedFilePaths` overloads + `RefreshSolutionDocumentsFromDiskAsync` are a latent trap — wiring them later would turn the whole-project delete into data loss. Remove or guard.
- **Stale cross-project refs after a signature change** (project-scoped refresh doesn't re-extract inbound refs); **terminal build failure has no force-override** (inconsistent with single-file launch); **accepted `.razor.cs` sibling is reindexed but flagged stale**; **`WorkflowEditServiceSafetyTests` doesn't actually pin the deferred-build invariant** (shallow relative to the branch theme).

### Verified correct (not regressed)
Core accept gate ordering, syntax-before-write, and "no edit reaches watched source without staging + recorded launch + watched==staged" all hold. Razor-row preservation is correct **for the single-project scope it runs in**. Symbol-identity round-trip, multi-file overlay validation (copies all staged), and the project→solution full-rebuild fallback are sound.

## Build + tests (clean isolated worktree)

Build **green** (0 errors). But **5 targeted tests fail** at `63f9620`:
- `RefreshProjectFilesAsync_preserves_razor_generated_reference_rows` — **environment artifact, not a fix defect.** It fails at the *initial-rebuild* precondition (line 220, before the scoped-refresh step), because the Razor source generator didn't emit generated docs via `MSBuildWorkspace` in the hermetic worktree (ASP.NET Core 10 pack *is* installed). The review confirms the preservation *logic* is correct; the author should confirm this test is green in their full-build/CI environment.
- `McpServerSmokeTests`: `…callers_and_relationships…` (expects `fileContentHash`), `…refresh_and_stage_use_monitor_working_copy` (`Assert.False`→`True`), `…tool_manifest_and_staging_guide…` (staging guide missing `"pre-merge validation"`), plus 1 `MSBuild.Tests` failure. Several look like the branch's intentional changes (lean shape / deferred-validation guide rewrite / planned-session gate) **not reconciled with its own integration tests** — i.e., the branch was pushed with red tests here.

## Do the skills explain why this is necessary? — **PARTIAL**

No meaningful drift; docs match code on every load-bearing claim (and `SessionOverlayValidation.md` correctly *removed* a now-false "future work" line). The skills justify the **what** and the **safety-why of requiring planned sessions** well (system-memory README: planned rows so validation/indexing don't re-derive intent from filesystem guesses; decisions before the expensive index pass). But:
- **Scoped/incremental refresh rationale is undocumented** — no skill explains *why* refresh is now project-scoped, what `refreshMode` means, or that a solution-fallback exists.
- **Razor-row preservation is entirely absent** from the skills despite being the branch's named headline — nothing explains why `.razor.cs`/`Directory.Build.*` are excluded from the cheap path.
- **`SetMonitorSessionEditPlan`** (new MCP tool) is undocumented.
- **Solution-fallback** silently changes the meaning of a "successful" `indexRefresh.status` — no operator guidance.

So an operator reading only the skills understands the rules and the safety motivation, but **not the performance/correctness engineering rationale** behind the refresh/Razor work — which is exactly the "why is this necessary" the changes most need to justify.

## Recommendation

Rework before merge:
1. **Cross-project cascade (HIGH):** don't let project-scoped delete cascade-drop other projects' inbound refs — drop the `ON DELETE CASCADE` for the scoped path, re-extract inbound refs, or fall back to full rebuild when the refreshed project has inbound cross-project references. Add a **multi-project scoped-refresh test**.
2. **Overlay fidelity (MED) — keep GATE 2 post-merge.** The post-merge full build on the real tree is intended and correct; don't move it. Instead raise GATE 1 (the pre-merge overlay) toward a full overlay build so a green prediction reliably matches the real build, surface a GATE 2 failure loudly, and ensure an abandoned session can't skip GATE 2 silently. Make the "safety test" actually pin the deferred-build behavior.
3. **Lockout (MED):** treat already-decided planned files as satisfied so the per-file flow doesn't deadlock.
4. **Remove dead file-scoped overloads** or guard them.
5. **Skills:** document the scoped-refresh + Razor-preservation rationale, `SetMonitorSessionEditPlan`, and the solution-fallback (close the "explain why" gap).

Reviewed against `origin/main` @ `46699c5`; branch @ `63f9620`. Full per-finding evidence + adversarial verdicts in the workflow transcript.
