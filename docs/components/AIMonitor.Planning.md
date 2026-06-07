# AIMonitor.Planning

## Purpose

Own the Plan Board outer workflow loop: task targets, one Current task per watched solution, task memory Markdown, and planning evidence relationships.

Planning starts the work thread. The safe edit workflow executes inside that thread.

Planning also replaces legacy AI change/history attributes as the durable memory surface. Workflow evidence is written to task memory Markdown and the planning database, not embedded into source files.

## Inputs

- `MonitorSettings`.
- Operator-created task targets.
- Human context, constraints, acceptance criteria, status updates, and closure summaries.
- Post-decision workflow evidence from shared services when a Current task exists.

## Outputs

- Planning SQLite database under the watched-solution runtime workspace.
- Task memory Markdown under the planning runtime workspace.
- Current task state for UI, MCP, CLI, and workflow evidence attachment.
- AI-facing Current task context through `get_current_task_context` / `plan current`. Human Notes are intentionally excluded from this payload.

## Data Flow

```text
Plan Board / MCP / CLI
  -> PlanningService
  -> PlanningDatabase
  -> board.sqlite + task memory Markdown
  -> Workflow evidence attachment when Current task exists
```

Agents should not scan task-memory Markdown folders directly. Task memory files are durable storage and human review artifacts; Planning service responses are the AI-readable contract.

## Owns

- Planning database path and schema.
- Task lifecycle state.
- Exactly-one-Current-task invariant.
- Context-memory write before switching Current tasks.
- Human-section-preserving task memory refresh.
- Task-level workflow evidence records and Markdown history that replace source-level AI change attributes.
- Open task event logs that keep recording until the operator explicitly closes the task.
- Task memory sections for Human Notes + Status Updates, Review Evidence, AI Decision History, AI General Notes, and Closure.
- Post-WinMerge decision evidence attachment from `record_diff_decision`.
- The AI-readable Current task context shape.

## Does Not Own

- Safe edit candidate creation.
- Staging, validation, WinMerge launch, or decision classification.
- Solution index storage.
- Adapter-specific command parsing or UI layout.
- Source attributes for AI change history.
- Deciding that an accepted staged record means the task is done.
- Exposing private Human Notes to AI-facing context by default.

## Key Tests

- `AIMonitor.Planning.Tests`
