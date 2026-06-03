# Harness Engineering Notes

## Source

- Video/repo reviewed: `coleam00/harness-engineering-demo`
- Useful reference areas:
  - `.claude/skills/*`
  - `.claude/hooks/*`
  - `.claude/context/*`

## What To Steal Now

### Skill Cards

The strongest near-term idea is not the exact command structure, but the discipline of making workflow cards explicit, small, and testable.

For AIMonitor, the skill-card patterns from prior Claude workflow work should live in this repo and be referenced from `CLAUDE.md`. They should describe the monitor workflow as the public Claude contract:

- find/read context through solution index and source-map tools;
- refresh watched files before editing;
- edit monitor-owned Working files or use bounded MCP edit tools;
- stage candidates for review;
- launch WinMerge/human review;
- record accepted or rejected decisions;
- check index refresh/status before continuing;
- use multi-file session rules when edits are coupled.

The cards should stay conversational enough for Claude/operator handoff. Do not turn them into a rigid script that hides judgment, human approval, or WinMerge review.

### Hooks

Hooks are valuable because they enforce rules the agent may forget. AIMonitor should eventually express the following as hook-like guardrails for Claude-facing workflows:

- block direct watched-source mutation outside the monitor workflow;
- block recursive destructive commands in watched projects;
- require refresh/stage/diff/decision for protected edits;
- surface pre-merge validation failures as blockers unless the human explicitly approves launch;
- keep success quiet and failure verbose.

Hook behavior should mirror the UI principle: errors and blockers are loud, routine success is compact.

## What To Defer

The demo repo's plan/report folders, context modules, and Ralph-style multi-session loop are useful patterns, but they should wait until the core AIMonitor workflow is stable.

Likely later work:

- durable plan/report artifacts for larger Claude workflows;
- smoke tests that assert skill-card call sequences without needing Claude;
- fresh-session orchestration loops;
- automated branch/worktree experiments.

Do not import these pieces just because they exist in the demo. Add them only when they support a current AIMonitor workflow failure or a testable operator need.

## Fundamentals Equivalence (verified 2026-06-03)

AIMonitor was built from scratch and then checked against the harness fundamentals the demo/video names. Conclusion:
each is already present as **equivalent-or-better**, and where a named fundamental does not apply it is because it was
designed for a human-in-an-editor, not for an agent consumer. Detail and the for/against reasoning for the LSP and
hooks pair are in `../decisions/0003-harness-fundamentals-lsp-and-hooks-equivalence.md`; the closure evidence is in
`Phase8HarnessClosureReview-2026-06-03.md`.

| Fundamental | Where it lives in AIMonitor |
| --- | --- |
| Skills / skill-cards | `docs/claude-skills/` cards + `SkillRouter.md` |
| Context management | durable, queryable, hash-stamped index + source-map tools |
| Language server (LSP) | navigate+validate subset, made durable and safe; **exceeds** an LSP on persistence; completion/semantic-tokens/atomic-rename are deliberately out of scope (human-editor features). Cross-file rename is moot 3 ways — see 0003 |
| Hooks | in-band enforcement is **structural** (watched-source immutability, hash classification, pre-merge build gate — stronger than a hook); the one out-of-band gap (the agent's native Edit/Write/Bash vs the live watched root) is closed by `.claude/hooks/guard-watched-source.ps1`, which resolves the watched root live from `config/appsettings.json` |
| Sub-agents | the Claude **Workflow tool** — used for **review / verify / research fan-out only**, NOT writes |
| Plans | the **human end of the loop**: operator drives the plan step (ask → review → sign off), then the principal agent executes |

**Operating-model rule that falls out of this:** writes/execution are owned by the two **principal agents (Claude vs
Codex)**, which are each other's adversarial check; sub-agents fan out for review. Parallel sub-agents writing code is
exactly the chaos AIMonitor's safety floor (one Working candidate per file, content-hash classification, supersede /
dirty-unexpected) exists to serialize away — so the rule is not just taste, it is what the monitor architecture assumes.
A non-safety **testing dividend** reinforces it: shared-engine logic earns dual-adapter validation for free (CLI and MCP
tests exercise the same engine from two entrances), while logic that leaks into an adapter forfeits it.
