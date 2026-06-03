# 0004 — Operator Workflow and Agent Autonomy Contract

Status: accepted
Date: 2026-06-03
Related: [0002-safety-enforcement-philosophy.md](0002-safety-enforcement-philosophy.md),
[0003-harness-fundamentals-lsp-and-hooks-equivalence.md](0003-harness-fundamentals-lsp-and-hooks-equivalence.md)

## Context

The operator's human-side workflow, observed and stated over the AIMonitor build, is deliberately review-light:

- **No PR, no branch on the watched project.** The operator works in their own sandbox; the agent's accepted change
  goes straight to real.
- **The "PR" is a checkpoint commit on a successful build** (e.g. `Checkpoint successful build SchemaStudioWebViewer`)
  — a known-good rollback anchor triggered by an *objective* signal, not a human approval ritual.
- **Validation is behavioral:** run the app, and if it is broken, iterate. The WinMerge step is a *churn glance*
  ("did the whole file get rearranged"), not a strict line-by-line review.
- Weeks of work have been produced this way without a rollback.

This works because the safety lives in objective gates, not subjective review (decision 0002): watched-source
immutability + content-hash classification + the pre-merge full build gate guarantee the change is exactly the staged
candidate and that it compiles; running the app confirms behavior; the checkpoint commit is the undo. Git is the
**iteration substrate** (known-good anchors), not the approval workflow.

The remaining throughput throttle is the agent asking the operator "OK?" for nearly every step. That per-step
confirmation is **redundant with the structural floor** — it asks a human to approve work an objective gate already
governs. Removing it (within the floor) is what converts the agent's speed into the operator's stated goal:
generate-and-test weeks of work in days.

## Decision: the autonomy contract

**Principle: do not ask a human to approve what an objective gate already governs.** Per-step "OK?" on reversible,
floor-gated work is the same advisory friction as the PR review the operator already dropped.

**Proceed autonomously (no confirmation)** — reversible and governed by the floor or durable authorization:

- Generate / edit monitor-owned Working candidates, stage, launch validation/build, run tests, iterate.
- Checkpoint-on-green-build commits; author/commit/push findings, docs, and local tests (the durably-authorized
  pattern this session).
- Reversible local config (e.g. the watched-source guard hook).
- Anything the structural floor already catches — it cannot be bypassed, so asking is redundant.

**Stop and ask** — the genuine gates the floor itself defers to, or that the floor cannot govern:

- `forceValidation` after a **failed** pre-merge build — the one real override gate; CLAUDE.md requires explicit chat
  approval ("yes, launch anyway") for that staged record. Silence/ambiguity is not approval.
- Outward-facing, irreversible, or destructive actions not already authorized (publishing externally, deleting things
  the agent did not create, force-push, etc.).
- Genuine ambiguity where the answer changes **what** gets built — a real fork, not a courtesy check.

Anything not in the "stop and ask" list defaults to **proceed**.

## Consequences

- Both principal agents (Claude and Codex) act autonomously within the floor and reserve human attention for the few
  real gates. This is the throughput lever: agent speed only becomes operator throughput when the per-step throttle is
  removed.
- It is safe **because** the floor is structural and the work is reversible (sandbox + checkpoint + run-and-iterate).
  The same autonomy pointed at irreversible/outward-facing targets would re-trigger the "stop and ask" rules — the
  contract is scoped to floor-governed, reversible work, not blanket.
- This is the human-side complement to decision 0002: 0002 says *where* safety lives (objective floor, not the glance);
  0004 says *the agent should therefore not seek per-step approval* for what that floor already covers.
- Promoting this contract into the `CLAUDE.md` / `AGENTS.md` operating rules would make it the binding default for both
  hosts; recorded here as the decision, available for promotion when the operator chooses.
