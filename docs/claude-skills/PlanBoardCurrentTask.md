# Plan Board Current Task

Use when a prompt asks for the current task, task memory, planning context, or workflow evidence attachment.

## Rule

Do not inspect `runtime/**/planning/task-memory/` directly for active task context.

Use the Planning surface:

```text
get_current_task_context
```

The returned payload is the AI-facing contract. It intentionally excludes private Human Notes. Use the Plan Board UI for operator-only notes.

## Current Task Handshake

At the start of work that may use Planning, request the current task context first. Tell the operator only the compact useful parts:

- task title and status;
- `currentIteration.goal` when present;
- whether review evidence exists, summarized in one or two lines.

Do not paste full task memory, full evidence history, file paths, hashes, or Markdown unless the operator asks. If there is no Current task, say that evidence will not attach until one is made Current.

## Iteration Goals

Planning uses iteration goals as the v1 substitute for subtasks: one Current task, many explicit iteration rows. Use `currentIterationGoal` from `get_current_task_context` as the next executable slice when present.

Do not turn ordinary chat into a task or iteration automatically. The operator must explicitly confirm that a line should become planning state. Treat this as an elicitation pause: restate the exact one-line iteration goal you intend to write, then ask a direct confirmation such as: "Should I add this exact line as the next iteration goal?"

When the operator confirms a new iteration, append exactly one compact goal line through the Planning surface before executing it:

```text
append_current_task_iteration
```

CLI/Codex command equivalent:

```text
plan add-iteration --goal "<one-line iteration goal>"
```

This creates a row in Planning, not just prose in the task goal.

After appending, re-request `get_current_task_context` and continue from the returned `currentIteration` row. This keeps Claude/Codex aligned on the same shared iteration DTO without reading task-memory files.

If the operator corrects an iteration goal after it was written, use the correction surface instead of editing task-memory Markdown or appending a second cleanup row:

```text
update_task_iteration
```

CLI/Codex command equivalent:

```text
plan update-iteration --iteration-id "<iteration-id>" --goal "<replacement one-line iteration goal>"
```

When staging workflow edits, provide a compact ledger summary that explains both why the edit was made and what changed. That summary is attached to task review evidence after `record_diff_decision`, so future agents can continue from evidence without reading runtime task-memory files.

## Workflow Evidence

`record_diff_decision` is the durable integration point after WinMerge review. When a Current task exists, AIMonitor attaches the staged record decision to Planning after the workflow decision is classified. Planning failures must not undo or invalidate the recorded safe-edit decision.

## If No Current Task Exists

Warn the operator that workflow evidence will not attach to task memory until a task is made Current.
