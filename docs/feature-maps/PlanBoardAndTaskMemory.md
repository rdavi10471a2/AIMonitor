# Plan Board And Task Memory

## Purpose

This map captures the new AIMonitor direction: make planning a first-class, human-friendly workflow surface instead of a chat convention.

The goal is not to compete with generic agent Kanban tools. The goal is to let one domain expert drive a safe, evidence-bound engineering session with Claude/Codex while preserving business context, source evidence, review decisions, and documentation closure.

## Product Thesis

Humans provide business/domain context and framework land-mine knowledge.

AI provides continuously refreshable structural evidence: files, symbols, calls, data flow, validations, accepted changes, and documentation updates.

The Plan Board is the bridge:

```text
human intent -> active task -> agent session -> safe edits -> review decisions -> docs update -> closure memory
```

Put another way, Planning is AIMonitor's outer workflow loop. The current safe edit workflow remains the inner loop that performs refresh, candidate edits, staging, validation, WinMerge review, decision classification, and index refresh.

## Research Source

Background reading lives in:

```text
docs/research/AIWorkflowPlanningAndDocumentationTools-2026-06-07.md
```

That research note includes `agtx`, `vibe-kanban`, `OpenClaw`, Roam Code, Lumen, and related tools. This file is the AIMonitor-specific direction, not another landscape list.

## Design Constraints

- Keep the board local-first and per watched solution.
- Keep AIMonitor's safety model: no direct protected source mutation by agents.
- Allow many tasks in Backlog/Ready/Done, but only one Active task per watched solution.
- All staged records and decisions should attach to the Active task unless explicitly overridden by the operator.
- The board is not generic project management. It is an operator cockpit for one active engineering thread.
- Task memory should be readable by humans and agents after restart.
- The database is authoritative for state and relationships; Markdown is the narrative/restart surface.

## Initial UI Shape

WinForms can host a small Kanban-style Plan Board:

```text
Backlog | Ready | Active | Review | Docs/Closure | Done
```

Rules:

- Backlog, Ready, and Done can contain many cards.
- Active can contain exactly one card.
- Review shows staged records waiting for WinMerge launch or decision.
- Docs/Closure shows target docs, AI documentation history, validation/test evidence, and close controls.
- Done requires no pending staged records and a closure summary.

Card detail pane:

```text
Title
Description
Human context / meeting notes
Goal
Acceptance criteria
Constraints / do-not-touch
Suggested doc targets
Linked monitor sessions
Files read
Files changed
Staged records
Decisions
Validation/test runs
Closure summary
Task memory Markdown path
```

Useful first buttons:

```text
New Task
Activate
Start/Attach Session
Suggest Doc Targets
Launch Pending Review
Update Target Docs
Close Task
Open Task Memory
```

## Backing Store

Each watched solution should get its own planning database under the existing runtime watched-solution folder:

```text
runtime/watched-solutions/<solution-id>/planning/board.sqlite
```

Initial schema:

```sql
create table board_state (
    id integer primary key check (id = 1),
    watched_solution_path text not null,
    watched_project_folder text not null,
    created_at_utc text not null,
    updated_at_utc text not null,
    active_task_id text
);

create table tasks (
    task_id text primary key,
    title text not null,
    description text not null default '',
    human_context text not null default '',
    status text not null,
    priority integer not null default 0,
    sort_order integer not null default 0,
    task_memory_md_path text not null default '',
    created_at_utc text not null,
    updated_at_utc text not null,
    closed_at_utc text not null default ''
);

create table task_doc_targets (
    id integer primary key autoincrement,
    task_id text not null references tasks(task_id) on delete cascade,
    doc_path text not null,
    source_scope text not null default '',
    status text not null default 'suggested',
    unique(task_id, doc_path)
);

create table task_sessions (
    id integer primary key autoincrement,
    task_id text not null references tasks(task_id) on delete cascade,
    session_id text not null,
    host text not null default '',
    created_at_utc text not null,
    unique(task_id, session_id)
);

create table task_files (
    id integer primary key autoincrement,
    task_id text not null references tasks(task_id) on delete cascade,
    file_path text not null,
    access_kind text not null,
    first_seen_at_utc text not null,
    last_seen_at_utc text not null,
    unique(task_id, file_path, access_kind)
);

create table task_staged_records (
    id integer primary key autoincrement,
    task_id text not null references tasks(task_id) on delete cascade,
    staged_record_id text not null,
    relative_path text not null,
    staged_hash text not null,
    status text not null,
    created_at_utc text not null,
    unique(staged_record_id)
);

create table task_decisions (
    id integer primary key autoincrement,
    task_id text not null references tasks(task_id) on delete cascade,
    staged_record_id text not null,
    decision text not null,
    classification text not null,
    decided_at_utc text not null
);

create table task_events (
    id integer primary key autoincrement,
    task_id text not null references tasks(task_id) on delete cascade,
    event_type text not null,
    summary text not null,
    payload_json text not null default '',
    created_at_utc text not null
);
```

The one-active-task invariant should be enforced in service code so the UI and MCP tools can return clear operator guidance.

## Task Memory Markdown

Each task should have a Markdown memory file, likely under a watched/project docs path or a configured task-memory root:

```text
docs/agent-plans/<task-id>-<slug>.md
```

The Markdown file is the agent-readable restart/context surface. The database remains authoritative for queryable state.

Initial shape:

```markdown
# Task: Add Generated Documentation Workflow

<!-- HUMAN:BEGIN context -->
Business/domain context goes here. This section is preserved.
<!-- HUMAN:END context -->

<!-- HUMAN:BEGIN constraints -->
Things not to touch, framework land mines, acceptance concerns.
<!-- HUMAN:END constraints -->

<!-- AI:BEGIN current-state -->
Status: Active
Linked session: session-...
Files read:
- ...
Files changed:
- ...
Pending review:
- ...
<!-- AI:END current-state -->

<!-- AI:BEGIN closure-history -->
- 2026-06-07: Task created from Plan Pane.
<!-- AI:END closure-history -->
```

Rules:

- Human sections are created with every task memory file.
- Human sections are preserved byte-for-byte on regeneration unless the operator explicitly edits them.
- AI sections can be refreshed from board/session/workflow evidence.
- Closure appends a compact AI history entry.

## MCP / CLI Surface Ideas

These should route through shared planning services if implemented:

```text
start_plan
list_plans
get_plan
activate_plan
attach_session_to_plan
record_plan_event
suggest_doc_targets
list_plan_staged_records
close_plan
refresh_task_memory
```

Existing edit workflow tools should be able to attach evidence to the active plan:

```text
refresh_file / get_file -> task_files read
submit_* / replace_* -> task_files changed candidate
stage_candidate_for_review -> task_staged_records
record_diff_decision -> task_decisions
post-accept index refresh -> task_events
```

## Open Questions

- Should task memory Markdown live in watched source docs, runtime, or both?
- Should source edits require an Active task, or only warn when absent?
- Should doc-target updates be part of task closure or a separate explicit step?
- Should task memory be generated before activation, or only when the first session attaches?
- How should rejected staged records appear in task memory?
- Should a task be allowed to close if accepted source changes exist but target docs are stale?

## First Vertical Slice

1. Add planning database and service under the watched-solution runtime folder.
2. Add WinForms Plan Board tab with Backlog, Ready, Active, Review, Docs/Closure, Done columns.
3. Enforce one active task.
4. Create task memory Markdown on task creation with human sections.
5. Attach existing MCP sessions and staged records to the active task.
6. Show pending staged records in Review.
7. Close task with a closure summary and refreshed task memory.

This first slice does not need automatic documentation generation. It establishes the plan/session/evidence backbone needed for AISolutionDocumenter later.

## Local Implementation Boundary

The planning feature is being built with normal Codex repository edits on `codex/planning-outer-loop`. Do not reconfigure AIMonitor to watch itself for this implementation work, and do not route AIMonitor repository source edits through AIMonitor's watched-source workflow until that is an explicit dogfood step.

New Planning C# source follows the stricter house style:

- block-scoped namespaces with braces;
- no C# top-level statements;
- no `using var`, `using` declarations, or `await using` declarations;
- resource lifetime uses `using (...) { }`;
- control-flow bodies always use braces.
