# Decision 0002: Safety Enforcement Philosophy

## Context

AIMonitor is a harness for the model the industry is moving toward: **the AI does the work; the human governs by
voting, accepting, and driving iteration.** "AI-assisted" here means the agent does it all and the operator steers — not
the agent suggests and the human writes.

Lived experience of operating the harness shaped this decision:

- **Accept-and-iterate is the rule.** Rejection is rare. The operator is not a defect-hunting gatekeeper; they are a
  steering loop that keeps a coherent change moving.
- **The human's check at WinMerge is a blast-radius glance, not a correctness audit.** In practice the operator verifies
  *"did the whole file get moved around, or is this the small localized change it claimed to be?"* — diff **stability**,
  cheaply — not line-by-line semantic correctness.
- **The monitor cannot protect itself.** It cannot safely run its own watched-project workflow on its own source while
  that source is being changed.

## Decision

Adopt a layered enforcement model with three rules.

### Rule 1 — Hard floor, soft human ceiling; partitioned by what the human can actually see

- **Hard structural floor (cannot be bypassed by any agent, hostile or buggy):** watched-source immutability, the
  monitor-owned Working/staged file ownership, and the content-hash accept classification
  (`accepted` / `accepted-normalized` / `rejected` / `dirty-unexpected`). This is where containment lives.
- **Soft human-contact ceiling:** WinMerge review. The operator's presence both *proves* diff stability (an unstable
  diff looks like churn and the loop visibly breaks) and *indirectly enforces* it (the whole pipeline exists to put a
  small, coherent diff in front of that human).

The partition rule:

> **The human eyeball only catches diff churn / blast radius. Failure modes that show up as an unstable diff may be left
> soft — the operator will see them. Failure modes that are invisible in the diff must be hard or test-covered, because
> neither the glance nor "is this the right change" will ever surface them.**

### Rule 2 — Three-way ownership of "is this change safe?"

Correctness is delegated and layered, never pinned on the WinMerge glance. For a **watched project**:

| Owner | Verifies | Mechanism |
| --- | --- | --- |
| **Human eyeball (WinMerge)** | diff shape / blast radius | operator presence at a stable diff |
| **Pre-merge build + running the app** | semantic/behavioral correctness of a clean diff | full-solution build gate (it compiles), then **run the watched app after the change and verify the behavior in-app**; accept-and-iterate (the next loop catches a subtle miss) |
| **Hard structure** | invisible side-effects (index state, telemetry, persisted state) | code-enforced invariants; no human/diff signal exists for these |

Note: **tests-as-regression-memory belongs to the monitor's own code (regime B below), not to the watched app.** A
watched project's correctness is verified by *running it*, not by AIMonitor's test suite.

### Rule 3 — Diff stability is the enabling condition for cheap human governance

Smallest-safe-edit, complete-edit-context, and staged immutability exist so that *"the file didn't thrash"* is a
**trustworthy** accept signal. Without bounded diffs the operator would be forced into expensive line-by-line review,
which would destroy the "human just votes" economics. Diff stability is therefore architectural, not cosmetic.

### Two operating regimes (a consequence, recorded here)

- **Editing a watched project:** monitor present → hard floor + soft human ceiling; the agent does it all, the human
  votes on diff stability, and **correctness is confirmed by running the watched app after the change and checking the
  behavior in-app** (behind the pre-merge build gate); accept-and-iterate. AIMonitor's own tests do **not** verify the
  watched app.
- **Editing the monitor itself:** *no monitor* (it cannot dogfood itself mid-change) → human-in-the-middle directly +
  **tests as the regression floor.** This is the deliberate, old-fashioned loop (human + AI + tests, no harness),
  because there is no honest way for the watcher to watch itself. This is why the monitor's own suite is large and
  load-bearing rather than belt-and-suspenders. **Tests are the monitor's safety net; running-the-app is the watched
  project's.**

### Multi-agent means portability, not concurrency

The MCP (Claude) and CLI (Codex) adapters over one shared engine exist so that **whichever single agent you reach for
drives the same safe workflow** — agent interchangeability, one active agent per project at a time. It is **not** a bet
on many agents editing one project concurrently. Single-writer-per-file is the intended mode, not a limitation.

## Rationale

This model puts the hard boundary exactly where adversarial or accidental AI behavior must be contained (watched source),
and leaves enforcement soft exactly where a present human is a cheaper, better verifier than code (judging whether a
stable diff is the right change). It matches how the harness is actually operated and keeps the operator's role cheap
enough to scale as agent throughput rises.

## Implications for findings (triage)

This decision is the rule for sorting review findings. A finding is real and must be hardened **only if its failure mode
is invisible in the diff and uncovered by tests/validation.**

- **INDEXREFRESH-2** (a degraded load silently wiping the index) was correctly hardened: zero diff churn, no human or
  test signal — exactly the invisible-side-effect class Rule 1 says must be hard. (See
  `docs/findings/SystemReview2-2026-06-02.md`.)
- **`forceValidation`** self-approval and **REG-BRIDGE-1** (the skipped headless bridge relay test) are **by-design soft**
  — the human is present at the decision/telemetry surface. Do not harden them. (See
  `docs/findings/McpElicitationVsWinFormsPrompts-2026-06-01.md` and
  `docs/findings/SystemReview2-RemediationVerification-2026-06-02.md`.)
- **LIFECYCLE-5** (single-writer concurrency) is **not** a defect: single active agent per project is the intended mode
  (Rule: multi-agent = portability). Do not add concurrency control to "fix" it.

## Non-goals — do not "fix" these

- Do not add a technical token to make `forceValidation` un-self-approvable; chat-approval + WinMerge presence is the
  approval mechanism.
- Do not add a headless stub for the bridge relay test; the running WinForms exe is the verification surface.
- Do not add multi-writer concurrency control for parallel agents on one project.
- Do not try to make the monitor monitor itself.

## Where the next investment goes

The harness is strong on containment, process transparency, and the hard/soft split. The frontier (not a current defect)
is **review-bandwidth amplification**: as agent throughput rises, help the operator vote on *triaged signal* — risk/blast-
radius ranking, intent summaries, and test/validation evidence attached at the vote — rather than raw per-file diffs.
This extends the model without changing the floor.
