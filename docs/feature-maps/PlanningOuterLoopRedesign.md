# Planning Outer Loop Redesign

## Purpose

AIMonitor Planning should be the living task context around the safe edit workflow. The safe edit workflow remains the inner engine. MCP is the primary conversational adapter for Codex and Claude; CLI is fallback/recovery.

This note captures the redesign direction after the MCP elicitation spike on `codex/planning-outer-loop`.

## Decisions From The Spike

- Do not add MCP sampling to the Planning loop. Normal Codex/Claude chat reasoning is the reasoning layer; AIMonitor should not create nested model calls.
- Keep `record_diff_decision` as the Workflow-to-Planning back door. It is the first point where reviewed evidence, decision, classification, staged hash, and index refresh status are durable facts.
- Treat the back door as post-decision, not only post-accept. Accepted, accepted-normalized, rejected, and recovery states all need a Planning next-step answer.
- Do not capture every MCP tool call as Planning history. Planning intent is the Current task plus the Current iteration or the last confirmed post-decision next step.
- Keep Planning context compact by default. Full history is available by task id through explicit drill-down tools; it should not be dumped into every startup response.
- Defer GitHub branch/PR automation. The operator will create/select branches manually for now.

## Front Door

At the start of a chat or before executing work, the agent should:

1. Verify that AIMonitor MCP tools are visible in the client.
2. Call `get_monitor_status` to prove the live server path.
3. Call a compact Current task tool.
4. If no Current task exists, stop and ask the operator to create/select one in the Plan Board UI.
5. If a Current task exists, discuss the current intent and Current iteration before editing.
6. Add, update, or select the next iteration only after explicit operator confirmation.
7. Execute only the confirmed Current iteration.

The front door should not mutate watched source. It prepares intent for the existing workflow.

Before the first watched-source edit, the agent should create or reuse a monitor session and set the planned edit file list on the session DTO:

```json
{
  "sessionId": "<monitor session id>",
  "taskId": "<current task id>",
  "iterationId": "<current iteration id>",
  "filesPlanned": [
    {
      "path": "<watched file path>",
      "owningProjectPath": "<MSBuild project path>",
      "role": "edit",
      "reason": "<why this file is expected to change>"
    }
  ]
}
```

This captures the agent's "I need to edit these files" statement as session intent. It should stay compact, use MSBuild/index truth for project ownership, and flow through the existing `sessionId` on refresh/edit/stage/decision calls. Each planned file is an ordered file in the session, so `record_diff_decision` can report progress such as 1 of N and defer the Planning next-step gate until the final planned file is decided. The next practical use is project-targeted index refresh after accepted decisions: rebuild the owning projects for the session's edited/staged/decided files instead of rebuilding the whole watched solution.

## Middle: Safe Edit Engine

The existing MCP safe edit tools stay intact:

```text
refresh_file / new_file
  -> Working candidate edit
  -> stage_candidate_for_review
  -> launch_staged_diff
  -> WinMerge human review/save
  -> record_diff_decision
```

This loop is not replaced by Planning. Planning tells the agent what to execute; Workflow owns how source changes are safely proposed, reviewed, and classified.

## Back Door

`record_diff_decision` should remain the durable integration point:

1. Classify the reviewed staged record.
2. Attach reviewed evidence to the Current task when one exists.
3. Include index refresh status for accepted/accepted-normalized decisions.
4. Trigger or return a post-decision Planning gate.

The gate asks what the decision means for the living task context:

- accepted / accepted-normalized: complete current iteration, keep it open, add next iteration, replace current iteration, pause/close/cancel task, or stop.
- rejected: retry current iteration, revise/replace current iteration, add a different next iteration, pause/close/cancel task, or stop.
- dirty/unexpected or recovery-required: do not advance Planning until workflow recovery is resolved.

If MCP elicitation is unavailable, declined, canceled, or fails, `record_diff_decision` should return a pending Planning decision. A separate explicit resolver tool applies any Planning mutation.

## Compact Living Context

`get_current_task_context` should become a short working brief:

- `taskId`
- title/status
- compact current intent
- Current iteration id and one-line goal
- last decision summary
- next expected action
- flags showing that deeper history is available

It should not include the full iteration list or full review evidence by default. Full task history should be available through explicit bounded MCP tools such as:

- `get_task_iterations(taskId, maxRows)`
- `get_task_decisions(taskId, maxRows)`
- `get_task_events(taskId, maxRows)`

Default row limits should be small. Rich payloads should require an explicit verbose/rich mode.

## Implementation Direction

Prefer a shared Planning outer-loop owner over adapter-heavy MCP logic:

```text
PlanningService / PlanningOuterLoopService
  owns task context, iteration completion, post-decision actions, compact summaries

StagedDecisionWorkflow
  owns safe decision orchestration and calls Planning attachment/gate services

AIMonitor.McpServer
  owns MCP schemas, elicitation, pending resolver tool, and compact response shaping
```

The spike proved useful pieces but put too much orchestration in `Program.cs`. Rebuild the clean version with a thin MCP adapter and shared service behavior.

## First Actionable Slice

1. Keep or recreate the harmless `test_elicitation` probe and integration smoke.
2. Add first-class Planning iteration completion in `AIMonitor.Planning`.
3. Make Current task context compact by default.
4. Add bounded task-history drill-down MCP tools.
5. Add post-decision Planning result shape to `record_diff_decision`.
6. For accepted/rejected terminal decisions, attach evidence and return/apply a Planning next-step gate.
7. If elicitation is unavailable or not accepted, return pending state and require `resolve_post_decision_planning`.
8. Add Planning unit tests and MCP integration smokes for accepted, rejected, and pending resolver paths.

## Index Refresh Follow-Up

Full post-accept solution rebuilds make the outer loop too slow. The next indexing redesign should use the session DTO plan and staged records:

1. `record_diff_decision` always has the decided staged file.
2. The `sessionId` gives access to the planned file set for the task/iteration.
3. Planned files include `owningProjectPath`.
4. Accepted decisions should refresh the union of owning projects for files with roles such as `edit`, `new-file`, `test`, or `config`.
5. Fall back to full solution rebuild for solution/project file edits, unknown ownership, unsupported Razor/generated boundaries, or failed project refresh.

Do not capture all MCP calls as Planning history. Capture the planned file/project set explicitly at the front door.

## Fresh Chat Restart Note

If this work resumes in a new chat, start here:

1. Read `docs/agent-memory/RestartContext.md`.
2. Read this file.
3. Verify live AIMonitor MCP with `get_monitor_status`.
4. Call `test_elicitation` if available.
5. Treat `codex/planning-outer-loop` as a spike branch. Preserve lessons, but prefer rebuilding a clean design on a fresh branch.
