# Plan Board UI Concept

## Core Idea

The Plan Board is not a generic Kanban board.

It is a single-thread execution cockpit:

```text
many task targets -> one current task -> evidence capture -> review -> closure memory
```

The left task list can show many tasks, but AIMonitor only executes against one current task at a time.

Planning is the outer workflow loop. The safe edit workflow remains the inner loop.

```text
planning task
  -> current task context
  -> agent/session attachment
  -> one or more safe edit loops
  -> review decisions
  -> docs and closure memory
```

The existing safe edit loop still owns refresh, candidate editing, staging, validation, WinMerge review, decision classification, and index refresh. Planning owns the task lifecycle around that loop.

## Current Task Rule

Only one task is Current for a watched solution.

Changing the Current task should force a context write before the switch completes. That write captures enough restart memory for the task being left behind:

- current status;
- human context and constraints;
- linked sessions;
- files read;
- files changed;
- pending staged records;
- decisions;
- tests or validation evidence;
- next step / why the task is being paused.

This prevents "we changed tasks in the UI and the chat forgot why."

## First Screen Shape

```text
Plan Board

Left rail: Task Targets
  Backlog
  Ready
  Current
  Paused
  Done

Center: Current Task
  title
  goal
  human context
  constraints
  acceptance criteria
  current agent/session note

Right rail: Evidence + Review
  linked session
  files read
  files changed
  staged records
  review decisions
  doc targets
  closure readiness
```

## Why Not Full Drag-And-Drop First

Drag-and-drop implies multiple lanes of work moving at once. AIMonitor's safe workflow is intentionally stricter:

- many task targets can be prepared;
- only one task is the execution target;
- evidence attaches to the current task;
- switching tasks is a deliberate context boundary.

Buttons are clearer for the first version:

```text
New Task
Mark Ready
Make Current
Write Context + Pause
Refresh Memory
Close Task
```

## Task Switch Flow

When the operator selects a different task and clicks `Make Current`:

1. AIMonitor checks whether the current task has unsaved context or pending evidence.
2. AIMonitor writes or refreshes that task's memory Markdown.
3. AIMonitor records a pause/switch event.
4. AIMonitor activates the selected task.
5. AIMonitor shows the new task's memory and current evidence.

The UI should make this feel normal, not punitive. The operator is not being blocked; AIMonitor is saving the thread.

## Workflow Evidence Role

Planning replaces the old idea of embedding AI change/history attributes in source.

Source stays source. Accepted workflow evidence goes into the Current task memory Markdown and planning database:

- staged record id and hash;
- watched file path / relative path;
- review decision classification;
- validation and post-accept index refresh result;
- closure notes and documentation targets.

The safest first integration point is after a durable workflow decision, especially accepted or accepted-normalized decisions. At that point the workflow already knows the real staged record, file, hash, classification, and index-refresh result. Planning can append task evidence and refresh Markdown as a best-effort follow-up without changing the safe edit path.

```text
record decision
  -> workflow persists decision and refreshes index as it already does
  -> planning appends evidence to Current task if one exists
  -> planning refreshes task memory Markdown
```

If Planning fails during this follow-up, the workflow decision remains durable and the normal safe-edit behavior must remain intact.

The AI-facing doorway into planning is `get_current_task_context` over MCP, with CLI parity through `plan current`. Agents must not browse `runtime/**/planning/task-memory/` directly for active context. Task memory Markdown is storage and review evidence; Planning service responses are the contract. The Current task context intentionally excludes private Human Notes unless a future curated AI context field is added.

The initial task evidence packet should stay small. It is not a replacement for normal source comments and should not try to narrate implementation details line-by-line.

Fields available from the shared decision seam:

```text
stagedRecordId
sessionId
watchedFilePath
relativePath
decision
classification
status
message
decisionAtUtc
stagedHash
stagedNormalizedHash
originalHash
originalNormalizedHash
isNewFile
preMergeValidationStatus
preMergeValidationDiagnosticCount
preMergeValidationForceApproved
indexRefresh status/result when applicable
```

Task memory should summarize those as:

```text
when
what file
accepted/rejected/normalized/dirty-unexpected
staged record id
validation/index outcome
optional task-level why
```

The optional "why" belongs to the task context, acceptance criteria, or closure note. It does not need to be inferred for every file. Source comments remain responsible for explaining non-obvious code behavior.

Accepted workflow decisions do not close tasks.

A task stays open and keeps recording work until the operator explicitly closes it. The operator may test the UI or watched project manually between accepted records, add feedback, and ask the agent to continue. Planning does not need a separate iteration model for this; the task event log is the sequence.

```text
task current
  -> accepted/rejected record
  -> optional manual test note
  -> more accepted/rejected records
  -> optional docs/closure note
  -> operator closes task
```

The task log can grow naturally. If it becomes too noisy, the operator can later prune or summarize it as a separate maintenance action. The first workflow should preserve the evidence instead of deciding too early what to discard.

## Task Memory Sections

The task Markdown file is the human/agent memory surface. The database stores the minimal structured facts needed to keep the board stable across shutdowns.

Initial task memory shape:

```markdown
# Task: Plan Board Foundation

## Goal

## Constraints

## Acceptance Criteria

## Human Notes + Status Updates
### Human Notes
<!-- HUMAN:BEGIN notes -->
Operator-authored context, manual test notes, and feedback.
<!-- HUMAN:END notes -->

### Status Updates
<!-- SYSTEM:BEGIN status-updates -->
System-generated task transition headers with required operator comments.
<!-- SYSTEM:END status-updates -->

## Review Evidence
<!-- TASK-GIT:BEGIN -->
Machine-synced reviewed workflow evidence.
<!-- TASK-GIT:END -->

## AI Decision History
<!-- AI:BEGIN decisions -->
Agent-authored decisions worth preserving while the task is open.
<!-- AI:END decisions -->

## AI General Notes
<!-- AI:BEGIN notes -->
Agent resume notes, risks, next steps, and non-decision observations.
<!-- AI:END notes -->

## Closure
<!-- HUMAN:BEGIN closure -->
Operator-authored closure summary.
<!-- HUMAN:END closure -->
```

Task switch/status notes render under `Human Notes + Status Updates` because the operator supplied the comment, but they are not part of the editable human-notes block. The timestamp and move description are system-generated; the operator only supplies the required comment.

```markdown
### yyyy-MM-dd HH:mm:ss : Moved task to Ready

Operator comment.
```

Machine refreshes preserve human sections and AI note sections, while regenerating `Review Evidence` from structured workflow facts.

Future pruning/truncation should be explicit operator action, likely from right-click/context-menu commands on the relevant task memory section:

- truncate or summarize `Human Notes + Status Updates`;
- truncate or summarize `Review Evidence`;
- require confirmation before deleting evidence-like history.

## MCP Bridge Role

The MCP bridge can carry some planning behavior without changing every MCP tool message.

Useful bridge-level behavior:

- expose or inject the Current task context at session/tool boundaries;
- record broad MCP activity against the Current task;
- warn when a session has no Current task;
- keep the WinForms proxy hub visible as the operator control point.

Precise workflow evidence should still come from shared services where the facts are known:

- `refresh_file` / file read evidence;
- candidate edits / files changed;
- staged records;
- validation results;
- review decisions;
- post-accept index refresh events.

That keeps the bridge as the outer-loop coordinator without making it responsible for interpreting every workflow-specific payload.

Bridge-level planning is secondary to post-decision workflow evidence. Use the hub for session visibility later; use the workflow commit point first for trustworthy task memory.

## First Version Acceptance

The first UI is useful when it can:

- create task targets;
- show exactly one Current task;
- switch Current task only through a context-write step;
- show review/evidence attached to Current;
- refresh task memory Markdown;
- close a task with a closure summary.

## Implementation Rules

This feature is being built on the local `codex/planning-outer-loop` branch with normal Codex repository edits. It must not reconfigure AIMonitor to watch itself and must not require AIMonitor MCP/edit workflow for AIMonitor source changes.

For newly authored Planning C# source:

- use block-scoped namespaces with braces;
- do not use C# top-level statements;
- do not use `using var`, `using` declarations, or `await using` declarations;
- use `using (...) { }` resource scopes;
- always use braces for control-flow bodies.

The planning service must be test-first around drift-sensitive invariants:

- exactly one Current task per watched solution;
- switching Current tasks writes context memory first;
- human Markdown sections are preserved on refresh;
- planning runtime state stays under the watched-solution runtime workspace.
