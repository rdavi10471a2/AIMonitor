---
status: open
type: handoff
created: 2026-06-03
audience: Codex (primary) + operator
scope: consolidated handoff for the Phase 8 closure + harness review + authoring-simulation session
note: Claude is the review+test gate; Codex owns production. Tests/samples-edits stay local unless noted; findings/docs are pushed.
---

# Session handoff — Phase 8 closure, harness review, authoring smokes (2026-06-03)

Single place for Codex to pick up. **Headline: the parity restore is documented-gold.** Parity closure PASS (no
false-pass), safety floor PASS, MCP descriptions truthful. Remaining work is consciously deferred with rationale — do
**not** start the deferred refactors without re-confirming with the operator.

## State (verified this session)
- **Phase 8 closure review** (6 dimensions, adversarially verified): `Phase8HarnessClosureReview-2026-06-03.md`.
  Parity / safety-floor / description-truthfulness all PASS.
- **LSP + hooks fundamentals**: answered and recorded in `../decisions/0003-harness-fundamentals-lsp-and-hooks-equivalence.md`.
  No LSP to add (navigate+validate subset, exceeds on durable index); cross-file rename is moot 3 ways; in-band
  enforcement is structural.
- **Code size** (product vs test, samples excluded): product `src/` ≈ 14,141 lines / 81 files; tests ≈ 10,691 / 42
  (0.76:1 — lean, not bloated). ~73% of committed test code is end-to-end (smoke harnesses + dual-adapter integration).
  Claude's ClaudeSmokes = 1,833 lines / 16 files, **kept LOCAL**.

## Done (pushed) — no action needed
- **HIGH-1 closed for the Claude host**: `.claude/hooks/guard-watched-source.ps1` blocks native Edit/Write/Bash under
  the live watched root (resolved from `config/appsettings.json`, so it follows a solution swap). Setup step in
  `../setup/LocalSetup.md`. **Codex action:** add the equivalent guard for the Codex host (AGENTS.md surface).
- **New skill card** `../claude-skills/BlazorPageTriadAuthoring.md`; fixed stale `SkillRouter.md` (was "two-file");
  added cited "Extraction semantics you can rely on" to `RoslynFirstNavigation.md`; registered in `CLAUDE.md`.
- **Authoring-simulation ClaudeSmokes** (LOCAL, 5 green): see `AuthoringSimulationSmokesAndSkillPatterns-2026-06-03.md`.
- **AIMonitor.Bridge**: already dead — NOT in repo/solution, no `.csproj`/source, already addressed 2026-06-01. Only
  local gitignored `bin/obj` cruft remains. `AIMonitorBridgeDeadProjectStatus-2026-06-03.md`. **Codex action: none in
  the repo** — local `rm -rf src/AIMonitor.Bridge` only (do not report repo changes; there are none).

## Deferred — DO NOT start without operator re-confirmation
Tracked in `../feature-maps/CmbMcpCapabilityParity.md` → "Tracked Deferrals". All are layering/ownership or future
features, **not safety or correctness**.
- **HIGH-2** — durable monitor-session lifecycle lives in `AIMonitor.McpServer` instead of a shared `AIMonitor.Workflow`
  service. Codex's proposed fix ripples through the workflow engine; operator deferred it to avoid churning the green
  test surface. **When unparked: add a Workflow-layer session test first** (regression net), then extract.
- **MEDIUM-1** — self-check guardrail evaluation lives in the adapter (no owning-layer test). Revisit with HIGH-2.
- **Phase 7** (monitor history / prune) — future feature; `prune_monitor_history` honestly reports a no-op.
- **Degraded-low tail** — accepted low-impact.

## Actionable for Codex (when the operator greenlights)
1. **Triad enforcement** (the operator's actual intent for the new-Razor rule — it must be *forced*, not remembered):
   a scaffold tool (e.g. `new_razor_component`) that atomically creates the 3 Working candidates, and/or a stage-time
   check that refuses to stage a new `.razor` without its `.razor.cs` + `.razor.css` in the same session. Card
   `BlazorPageTriadAuthoring.md` is the interim reminder until this lands.
2. **Proposed skill cards** (operator call): `AugmentedSampleIndexProbe.md` (materialize-and-augment test pattern +
   the four extraction gotchas as a checklist) and `SameSignatureIsolationAssertions.md` (key assertions off StableKey;
   pair positive + regression + negative + isolation). Plus a note in `SessionOverlayValidation.md` that a coupled stage
   yields one shared `SessionId` with distinct `StagedRecordId`s and a broken `.cs` throws before any record exists.
3. **Codex-host watched-source guard** (mirror of HIGH-1).

## Design heuristic worth keeping (from the dual-adapter observation)
The CLI and MCP are two thin adapters over one shared engine, so CLI integration tests exercise the engine from a second
entrance — **shared-engine logic earns dual-adapter validation for free; logic that leaks into an adapter forfeits it.**
This is a concrete, non-safety argument for the "MCP is not the workflow" rule and for eventually unparking HIGH-2
(moving sessions into the engine would hand us CLI cross-coverage of session behavior we can't get today). Recorded on
the HIGH-2 deferral rationale in the ledger.
