# Plan Board Current Task

Use when a prompt asks for the current task, task memory, planning context, or workflow evidence attachment.

## Rule

Do not inspect `runtime/**/planning/task-memory/` directly for active task context.

Use the Planning surface:

```text
get_current_task_context
```

The returned payload is the AI-facing contract. It intentionally excludes private Human Notes. Use the Plan Board UI for operator-only notes.

## Iteration Goals

Planning uses iteration goals as the v1 substitute for subtasks: one Current task, many explicit iteration rows. Use `currentIterationGoal` from `get_current_task_context` as the next executable slice when present.

Do not turn ordinary chat into a task or iteration automatically. The operator must explicitly confirm that a line should become planning state. If the operator's intent is ambiguous, ask a direct confirmation such as: "Should I add this as the next iteration goal?"

When the operator confirms a new iteration, append exactly one compact goal line through the Planning surface before executing it:

```text
append_current_task_iteration
```

CLI/Codex command equivalent:

```text
plan add-iteration --goal "<one-line iteration goal>"
```

This creates a row in Planning, not just prose in the task goal.

When staging workflow edits, provide a compact ledger summary that explains both why the edit was made and what changed. That summary is attached to task review evidence after `record_diff_decision`, so future agents can continue from evidence without reading runtime task-memory files.

## Workflow Evidence

`record_diff_decision` is the durable integration point after WinMerge review. When a Current task exists, AIMonitor attaches the staged record decision to Planning after the workflow decision is classified. Planning failures must not undo or invalidate the recorded safe-edit decision.

## If No Current Task Exists

Warn the operator that workflow evidence will not attach to task memory until a task is made Current.
