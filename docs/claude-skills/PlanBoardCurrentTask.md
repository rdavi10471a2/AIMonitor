# Plan Board Current Task

Use when a prompt asks for the current task, task memory, planning context, or workflow evidence attachment.

## Rule

Do not inspect `runtime/**/planning/task-memory/` directly for active task context.

Use the Planning surface:

```text
get_current_task_context
```

The returned payload is the AI-facing contract. It intentionally excludes private Human Notes. Use the Plan Board UI for operator-only notes.

## Workflow Evidence

`record_diff_decision` is the durable integration point after WinMerge review. When a Current task exists, AIMonitor attaches the staged record decision to Planning after the workflow decision is classified. Planning failures must not undo or invalidate the recorded safe-edit decision.

## If No Current Task Exists

Warn the operator that workflow evidence will not attach to task memory until a task is made Current.
