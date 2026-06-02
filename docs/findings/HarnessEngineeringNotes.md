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
