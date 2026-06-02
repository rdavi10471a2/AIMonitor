# AIMonitor System Memory

This folder marks the monitor knowledge base that acts as AIMonitor's own system memory.

AIMonitor cannot safely use the watched-project workflow to monitor itself while it is being changed. These documents are therefore the contract memory for how the system works. They are not background reading; they are the source of truth for preventing accidental contract drift.

## Contract Rule

If a code change affects any item below, update the matching memory doc and add or update tests in the same change:

- adapter boundaries;
- watched-source safety;
- Working/staged/runtime file ownership;
- pre-merge validation;
- WinMerge launch/review/decision behavior;
- accepted, accepted-normalized, rejected, dirty-unexpected, and refresh-required classifications;
- index rebuild and query semantics;
- MCP/CLI/WinForms telemetry;
- Claude/Codex host instructions;
- component ownership or data flow.

Do not rely on conversation memory for these contracts. Put the durable rule in the docs and prove it with tests when feasible.

## Authoritative Entry Points

- `README.md`: repository overview and public workflow shape.
- `AGENTS.md`: Codex host contract.
- `CLAUDE.md`: Claude / Claude Code host contract.
- `docs/agent-memory/RestartContext.md`: restart and handoff recovery memory.
- `docs/components/README.md`: component ownership and data-flow index.
- `docs/claude-skills/README.md`: Claude skill-card routing.

## Component Memory

Use `docs/components/` when changing a project boundary. Each component note records:

- purpose;
- inputs;
- outputs;
- data flow;
- what the component owns;
- what it does not own;
- tests that prove the boundary.

## Workflow Memory

The safe edit workflow contract is:

```text
discover
  -> edit monitor-owned Working files
  -> stage
  -> pre-merge validation
  -> WinMerge review/save
  -> hash-classified accept/reject decision
  -> index refresh after accepted decisions
```

Adapters may expose different commands or tool names, but they must route through shared services where behavior overlaps.

## Local Composition Contract

Reason in the cloud; compose locally.

Agents may plan, inspect, and describe intended edits through index/source-map/tool calls, but monitor-owned local tooling owns file mutation, staging, hashing, validation, review artifacts, and decision classification. The clean path is never "agent writes watched source and explains what happened."

## Stable Diff Contract

After staging, staged runtime files are immutable review evidence. If a candidate needs another change, edit the monitor-owned Working file and stage again. Do not patch staged runtime files, and do not accept a staged record whose candidate changed after staging.

Accepted decisions are classified by operator decision plus staged hash:

- exact staged content saved into watched source -> `accepted`;
- normalized equivalent saved into watched source -> `accepted-normalized`;
- watched source left unchanged -> `rejected`;
- anything else -> dirty/unexpected recovery.

## Change Protocol

Before changing a boundary:

1. Read the relevant host file: `AGENTS.md` for Codex work, `CLAUDE.md` for Claude work.
2. Read the relevant component note in `docs/components/`.
3. Read the relevant workflow or skill card.
4. Make the code change through the shared service layer when behavior overlaps adapters.
5. Add or update regression coverage.
6. Update this memory surface if the contract changed.

If a finding contradicts system memory, treat it as a bug in either the code or the memory. Resolve the mismatch before moving on.

## Iteration Style

AIMonitor work should use tight planning and implementation loops:

```text
small plan -> user approval when needed -> small edit -> focused test -> inspect result -> next step
```

Do not require a large up-front plan for work whose edge cases are not knowable until code and tests expose them. The preferred style is conversational and empirical: keep a general direction, make a bounded change, test it, and let the next concrete implication shape the next step.

Use detailed plans only when the change is broad, risky, or crosses multiple ownership boundaries. Even then, keep the plan revisable and update it as tests and runtime behavior reveal new facts.

Success is defined by real workflow behavior, not by the elegance of the initial plan.

## Historical Docs

`docs/findings/` and `docs/decisions/` preserve investigation history. They can explain why a rule exists, but current behavior should be reflected in the active memory entry points above.
