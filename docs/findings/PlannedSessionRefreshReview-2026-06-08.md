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
1. **Cross-project cascade (HIGH):** see the detailed fix below — a plan-time inbound-reference closure + a store backstop + a multi-project test.
2. **Overlay fidelity (MED) — keep GATE 2 post-merge.** The post-merge full build on the real tree is intended and correct; don't move it. Instead raise GATE 1 (the pre-merge overlay) toward a full overlay build so a green prediction reliably matches the real build, surface a GATE 2 failure loudly, and ensure an abandoned session can't skip GATE 2 silently. Make the "safety test" actually pin the deferred-build behavior.
3. **Lockout (MED):** treat already-decided planned files as satisfied so the per-file flow doesn't deadlock.
4. **Remove dead file-scoped overloads** or guard them.
5. **Skills:** document the scoped-refresh + Razor-preservation rationale, `SetMonitorSessionEditPlan`, and the solution-fallback (close the "explain why" gap).

Reviewed against `origin/main` @ `46699c5`; branch @ `63f9620`. Full per-finding evidence + adversarial verdicts in the workflow transcript.

---

## HIGH #1 fix — detail (plan-time inbound-reference closure)

**Root cause.** Scoped refresh of project A deletes A's symbol rows; `ON DELETE CASCADE` then drops *other* projects' `symbol_references`/`call_sites`/`symbol_relationships` rows that **target A's symbols**, and the re-insert only restores A's own rows. So inbound cross-project references vanish until a full rebuild. (A second symptom: even without the cascade, if an edit changes a symbol's `stable_key`, inbound refs in unrefreshed projects go stale.)

**Direction matters.** The endangered rows belong to projects that **reference into A** (inbound dependents) — *not* A's own dependencies. Fixing this is about finding "who references A," not "what A references."

### Planning model: a new engine-derived section

`filesPlanned` stays the agent's **edit scope**. The plan gains a separate, **engine-populated** section — the **refresh closure / `InboundReferencingProjects`** — distinct because a project can need its index rows regenerated *without* any source edit. Compute it at plan time in two grains:

- **Cheap gate (MSBuild `ProjectReference` graph):** if A has *zero* reverse dependencies (a true leaf nothing references), the closure is empty → scoped refresh is always safe; skip the index query.
- **Precise set (index — authoritative):** which projects actually hold references to A's *symbols* (the query below). Use the index, **not** the raw `ProjectReference` edges: .NET project references are transitive, so a project can reference A's symbols without a *direct* `ProjectReference` to A — direct build-graph dependents would under-count. That set ∪ A = the refresh closure (project-granular; reverse direction).

The agent's only new duty: during planning, check `find_indexed_references` for cross-project sites and add a consumer to `filesPlanned` **only if it must be edited**. The closure itself is derived, never hand-typed.

### Code + locations

**Detection — `src/AIMonitor.Data/SolutionIndexStore.cs`** (uses the existing `projects` id↔path join):
```csharp
public IReadOnlyList<string> GetInboundDependentProjectPaths(string projectPath)
{
    using SqliteConnection connection = database.OpenConnection();
    using SqliteCommand command = connection.CreateCommand();
    command.CommandText = """
        select distinct rp.project_path
        from symbol_references r
        join symbols  s  on s.stable_key = r.target_stable_key
        join projects sp on sp.id = s.project_id      -- declares the symbol
        join projects rp on rp.id = r.project_id      -- references it
        where sp.project_path = $p and r.project_id <> s.project_id
        -- UNION the same shape over call_sites(target_stable_key)
        -- and symbol_relationships(target_stable_key, source_stable_key)
        """;
    command.Parameters.AddWithValue("$p", projectPath);
    // read distinct project_path into a List<string>
}
```

**Guard — `src/AIMonitor.Indexing/PostAcceptIndexRefreshService.cs`**, at the existing `useFileRefresh` decision (~line 22):
```csharp
bool useFileRefresh = projectPaths.Length == 1 && filePaths.Length > 0;
if (useFileRefresh)
{
    var store = new SolutionIndexStore(new SolutionIndexDatabase(databasePath));
    if (store.GetInboundDependentProjectPaths(projectPaths[0]).Count > 0)
    {
        useFileRefresh = false;   // MVP: full rebuild.
        // optimization: refresh the CLOSURE = projectPaths[0] ∪ inbound dependents
    }
}
```

**Carry-forward — `src/AIMonitor.Indexing/PostAcceptIndexRefreshPlan.cs`**: add `string[] InboundReferencingProjects` (the closure beyond A), populated at plan build so it's inspectable in `indexRefresh` telemetry and the refresh reads it instead of recomputing.

**Backstop — `src/AIMonitor.Data/SolutionIndexStore.cs` `ReplaceProjectFiles`**: wrap the single-project delete+reinsert in `pragma foreign_keys=off` (reinsert restores A's symbols at the same `stable_key`, so other projects' inbound rows stay valid), **or** drop `ON DELETE CASCADE` on the cross-project target FKs and delete only by `project_id`. Defense-in-depth even if planning misjudges.

**Test — `tests/unit/AIMonitor.Data.Tests`**: a two-project fixture where B references a symbol in A, scope-refresh A, assert B's inbound references survive.

### Skill instruction (agent behavior — the documented gap)

Add to the planning/blast-radius skill: *before finalizing the plan, run `find_indexed_references` on each symbol you intend to change; if referencing sites exist in other projects, (a) add any file you must edit to `filesPlanned`, and (b) know the engine will refresh those dependent projects' index rows via the closure.* The tool to do this already exists; the instruction to do it at plan time does not.

---

## All suggested fixes — implementation checklist

Code sketches are illustrative (exact signatures to be confirmed against the branch); file locations are precise.

### HIGH
- **#1 Cross-project cascade** — see the detailed section above (plan-time inbound-reference closure, index-authoritative; guard in `PostAcceptIndexRefreshService`; FK-cascade backstop in `SolutionIndexStore.ReplaceProjectFiles`; two-project test).

### MED
- **Overlay fidelity (keep GATE 2 post-merge on the real tree).** Pick one:
  - **(A) High-fidelity predictor:** in `src/AIMonitor.Runtime/StagedDiffLaunchWorkflow.cs`, when deferred *and all planned files are staged*, call the full overlay build instead of the hash-only check:
    ```csharp
    PreMergeValidationResult validation = deferBuildValidationUntilAccept
        ? validationService.Validate(settings, record, stagedOverlayRecords) // full overlay build (was ValidateStagedOverlay, hash-only)
        : validationService.Validate(settings, record, stagedOverlayRecords);
    ```
    Keeps the terminal real-tree build as the authoritative confirm → **2 builds/batch**, faithful prediction.
  - **(B) Cheap + safe-fail:** keep the semantic GATE 1, but make a terminal `GATE 2` failure loud and **flag a session abandoned after merges** so the operator knows GATE 2 never ran. Accepts rare post-merge surprises (recoverable via VCS).
- **Launch lockout/deadlock** — `src/AIMonitor.McpServer/Program.cs` `ShouldDeferBuildValidationUntilAccept`: treat already-decided planned files as satisfied; require an *active* staged record only for files **not yet decided**:
  ```csharp
  bool everyPlannedFileReady = plannedFiles.All(f =>
      IsDecided(sessionRecords, f) ||                 // accepted/rejected already → satisfied
      HasActiveStagedRecord(sessionRecords, f));      // else must be staged & undecided
  ```
- **"Scoped" is a whole-project rebuild; dead file-scoped overloads** — `src/AIMonitor.MSBuild/MSBuildWorkspaceLoader.cs`: delete the never-called `BuildDeclarationsAsync(...,IReadOnlySet<string> includedFilePaths,...)`, `BuildReferencesAsync(...,IReadOnlySet<string>,...)`, and `RefreshSolutionDocumentsFromDiskAsync`, **or** add a guard test; add a doc-comment that scoped refresh is project-granular.
- **Accepted `.razor.cs` flagged stale** — `src/AIMonitor.Indexing/PostAcceptIndexRefreshService.cs` `MarkRefreshFilesFresh`: when `useFileRefresh` rebuilt the whole owning project, mark **every** accepted session file in that project fresh, not just the Razor-filtered `filePaths` (the `.razor.cs` rows *were* reindexed by the whole-project rebuild).
- **Terminal build has no force-override** — `src/AIMonitor.Indexing/StagedDecisionWorkflow.cs` `ValidateTerminalPlannedOverlay`: honor an operator force path like single-file launch — when `validation.IsError` and the record carries `PreMergeValidationForceApproved`, record instead of throw.
- **Shallow safety test** — add cases (in `WorkflowEditServiceSafetyTests` or Indexing) pinning: the lockout fix (interleave launch/decide on 2 planned files), the chosen fidelity behavior, and abandonment surfacing an un-validated state.

### Refresh/validate scope = affected projects only (build vs index)

Intent (should be documented; it wasn't): refresh and validate only the **affected project set** — edited projects ∪ their inbound dependents (the closure) — not the whole solution.

- **Build (GATE 2):** MSBuild does affected-only **for free if built in place** on the real tree — its incremental up-to-date check rebuilds only changed projects + their dependents and skips the rest. MSBuild's incremental "affected" = changed + dependents = the **same direction** as the closure. The current `PreMergeValidationService.Validate` builds a **fresh copy** each run, which has no prior `obj/bin` and therefore **can't be incremental → full rebuild** (the compile-storm). Fix: build the real tree in place (post-merge GATE 2), or explicitly target the affected projects, so incrementality kicks in.
- **Index refresh:** no automatic incrementality (MSBuildWorkspace/Roslyn load + extract). Scope re-extraction to the closure explicitly — that's HIGH #1.

### Skills (the "explain why" gap)
In `docs/claude-skills/AIMonitorWorkflowQuickStart.md`, `SessionOverlayValidation.md`, `SystemMonitorStaging.md`, `docs/system-memory/README.md`: document the scoped-refresh rationale + `refreshMode`, the `InboundReferencingProjects` closure, `SetMonitorSessionEditPlan`, the silent solution-fallback (a "rebuilt" status may have fallen back), and the **verbatim-merge rule** (no hand-editing in WinMerge — it breaks overlay≡watched). Also fold the two-gate model + GATE 1 noise classes from `docs/PlannedSessionEditFlow.md` into the skill.

### Host contracts — `CLAUDE.md` / `AGENTS.md` (MED — they're the first thing an agent reads)

The branch left **`CLAUDE.md` untouched** and added only **one unrelated line to `AGENTS.md`**, so both top-level host contracts still describe the pre-planned-session workflow and now misdirect agents. Required edits:

**`CLAUDE.md` — "Watched-Source Safety" steps + the pre-merge paragraph:**
1. Step 1 "Start or reuse the intended monitor session" → **"Start a *planned* session: `start_monitor_session` with `filesPlanned` listing every file you intend to change — required before any `refresh_file`/`new_file`/edit; mutations to unplanned files are rejected."**
2. Add a planning sub-step: **"While planning, run `find_indexed_references` on each symbol you'll change; add any cross-project consumer you must edit to `filesPlanned`. The engine refreshes dependent projects' index rows via the inbound-reference closure."**
3. Reconcile line 58 (`"If pre-merge validation fails, launch_staged_diff must not be treated as a warning…"`) with the **two-gate model**: GATE 1 (overlay semantic compile, pre-merge) is a *predictor that can be noisy* (Razor / duplicate-inclusion false positives) — a failing overlay is operator judgment and **may be merged anyway**; GATE 2 (full `dotnet build` on the real watched tree, post-accept) is the authoritative gate. The "hard-stop, not a warning" language should attach to GATE 2 / genuine breaks, not to noisy overlay errors.
4. Add the **verbatim-merge rule**: "Merge the staged bytes verbatim in WinMerge — do not hand-edit during merge; the overlay's validity only transfers to watched source if watched ends up byte-equal to the staged candidate."

**`AGENTS.md` — mirror in Codex/CLI vocabulary** (keep host-specific names per the CLAUDE.md "do not mix host names" rule). The existing AGENTS.md line still says `edit launch-diff` "must run the full pre-merge validation gate before WinMerge … the user must explicitly approve the validation override dialog" — the old single-gate model. **Resolve the cross-adapter inconsistency the review found:** planned sessions + deferred/two-gate validation are currently **MCP-only**; the CLI keeps build-at-launch with no planned requirement. So either (a) AGENTS.md must state plainly that the CLI path is build-at-launch / no planned sessions (so Codex doesn't assume parity), or (b) the CLI gains planned-session + closure parity and AGENTS.md documents it the same as CLAUDE.md. Pick one and make it explicit — today the two contracts silently describe different engines.

### LOW / NIT
- Remove the dead `using AIMonitor.App.Controls;` in `src/AIMonitor.App/Program.cs`.
- Optional: deterministic tie-break for identical-tick staged-record timestamps (practically unreachable; superseding prevents two active records per path).
