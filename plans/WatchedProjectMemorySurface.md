# Watched Project Memory Surface Plan

## Purpose

AIMonitor needs a watched-project memory feature that can author, maintain, resolve, and use local project guidance through a shared memory model, even when that guidance is drafted outside the watched source tree first.

The missing piece is local Markdown memory: root and folder-level `AGENTS.md`, `CLAUDE.md`, `README.md`, feature maps, and similar files that describe project intent, ownership, data flow, invariants, tests, and local conventions. Agent harness guidance increasingly assumes this kind of local memory exists near the code. AIMonitor should be able to draft that memory externally, use it during monitored development, keep it aligned with observed project facts, and optionally publish it into the watched project without giving agents a new path to silently mutate watched source.

This is a feature in its own right. It is not part of the safe edit workflow engine.

## Core Position

Watched-project memory is a context feature, not an edit workflow feature.

It may depend on the workflow when publishing Markdown files into watched source, but workflow safety must not depend on memory. Memory can make edits smarter. Workflow remains the boundary that makes watched-source mutation safe.

The internal model should not be "Markdown files plus an agent." The durable model should be structured AIMonitor memory with Markdown projections. Markdown is the human-facing and agent-facing representation; the service should still track document identity, scope, section ownership, status, provenance, and publish/drift state mechanically.

## Goals

- Let AIMonitor survey a watched solution and identify useful memory boundaries.
- Let agents author memory drafts in monitor-owned space before anything is written to the watched project.
- Emulate folder-local agent memory even when the files do not yet physically exist in the watched project.
- Surface the right memory context for a watched file, folder, namespace, feature, or project.
- Support later publishing/syncing of stable memory docs into the watched source tree.
- Let the watched project become self-describing when AIMonitor is not attached.
- Provide a maintenance path so memory docs can be kept current after monitored edits.

## Non-Goals

- Do not bypass the safe edit workflow for watched-source writes.
- Do not make workflow safety depend on memory docs.
- Do not require hooks or language-server integration for the initial feature.
- Do not load every memory doc by default.
- Do not let generated memory override AIMonitor's host instructions, system memory, or safety rules.
- Do not claim that Markdown memory is compiler truth.

## Layering

Recommended ownership:

```text
AIMonitor.Core
  watched solution identity, path identity, shared records

AIMonitor.MSBuild
  MSBuild-loaded project, document, folder, and namespace truth

AIMonitor.Indexing
  semantic facts, symbol references, callers, relationships, and source-map evidence

AIMonitor.Memory
  watched-project memory workspace
  virtual memory tree
  memory discovery and resolution
  draft generation inputs
  drift detection
  publish/sync planning

AIMonitor.Workflow
  optional publish gate when memory docs are written into watched source

AIMonitor.Cli / AIMonitor.McpServer / AIMonitor.App
  adapter surfaces for memory commands, context packets, and operator UI
```

The new feature should live in a dedicated shared service area, probably `AIMonitor.Memory` or `AIMonitor.Context`. It should not be folded into `AIMonitor.Workflow`.

## Hard Guardrails

- Memory services may write to monitor-owned memory workspace files.
- Memory services may resolve and surface context packets.
- Memory services may draft or propose watched-project memory docs.
- Memory services may create a publish/sync plan.
- Only the safe edit workflow may write published memory docs into watched source.
- Publishing must be explicit.
- Initial publishing should be diffable and human-visible.
- Watched-project memory must never override AIMonitor host/system safety instructions.
- The memory scout may explore broadly, but mutation remains gated.
- The resolver must be bounded: return relevant memory, not the entire memory corpus.
- Memory docs must distinguish human-owned notes from agent-maintained maps.
- Memory cannot authorize broader mutation scope.
- Memory cannot suppress validation or tests.
- Memory drafts and generated sections must carry provenance.
- Memory claims do not outrank MSBuild, compiler, index, or accepted-source facts.
- Virtual and published memory conflicts must be visible, not silently merged.

## Authority And Truth

Instruction and safety authority should be explicit:

```text
System/developer instructions
-> AIMonitor host file: AGENTS.md / CLAUDE.md
-> AIMonitor system memory
-> watched-project root memory
-> watched-project folder/feature memory
-> current agent request
```

Watched-project memory can guide domain language, local conventions, feature ownership, and test selection. It cannot relax monitor safety rules or grant direct watched-source mutation.

Implementation truth has a separate precedence model:

```text
MSBuild / compiler / source index facts
-> current accepted source
-> current Working candidate
-> published memory docs
-> virtual memory drafts
-> agent-authored summaries
```

If memory claims conflict with project facts, the facts win and the memory should be flagged as stale or conflicting.

## Structured Memory Model

The first implementation can keep the schema small, but the plan should reserve the deterministic spine early.

Suggested records:

- `MemoryDocument`: identity, solution id, relative path, virtual path, optional published path, source kind, scope, status, sections, provenance.
- `MemoryScope`: solution, project, folder, namespace, feature, or test-area scope plus relative path, projects, namespace prefix, symbols, and related tests where known.
- `MemorySection`: stable section id, title, ownership, preserve-on-regeneration flag, content hash, and last generated/reviewed metadata.
- `MemoryStatus`: draft, current, possibly stale, confirmed stale, unknown, conflict.
- `MemoryProvenance`: MSBuild snapshot, index query, source-map query, text search, agent summary, operator note, or imported published doc.
- `MemoryResolutionPacket`: bounded context packet with included docs, source labels, stale/conflict warnings, and inclusion reasons.
- `MemoryPublishPlan`: create/update/no-op/conflict classifications for publishing virtual memory into watched source.
- `MemoryDriftMarker`: changed scope, related docs, stale status, and reason.

Markdown files should be projections of these records wherever practical. Early phases may store simple Markdown drafts, but section ownership and provenance should be designed before automatic drafting becomes powerful.

## Root Router Files

Root watched-project `AGENTS.md` and `CLAUDE.md` should be router documents plus a project brief. They should not become large manuals.

Recommended root contents:

- project purpose;
- active host guidance;
- safety/domain constraints;
- memory routing table;
- doc ownership rules;
- which docs are human-owned vs agent-maintained;
- reminder not to load every memory file by default;
- reminder that AIMonitor workflow rules still apply when AIMonitor is attached.

The root files should point to local folder or feature memory instead of embedding every detail.

## Folder And Feature Memory

Folder-level or feature-level memory should be local and specific.

Recommended sections:

- Purpose
- Owned workflows
- Important types/files
- Data flow
- Invariants
- Local style or domain conventions
- Tests / smokes
- Known risks
- Human Notes
- Agent-Maintained Map

Human-owned sections must be preserved unless explicitly requested. Agent-maintained sections can be updated by the memory feature, but publishing still follows the chosen safety path.

Section ownership should be mechanically detectable, not only implied by headings.

Example:

```html
<!-- AIMONITOR:SECTION HumanNotes ownership=human preserve=true -->
## Human Notes
...
<!-- /AIMONITOR:SECTION -->
```

```html
<!-- AIMONITOR:SECTION AgentMap ownership=agent-maintained preserve=false -->
## Agent-Maintained Map
...
<!-- /AIMONITOR:SECTION -->
```

Exact marker syntax can change during design, but the requirement should remain: automated maintenance must be able to preserve human-owned content reliably.

## Virtual Memory Workspace

Initial authoring should happen in monitor-owned space, not directly in the watched project.

Suggested runtime layout:

```text
runtime/watched-solutions/<solution-id>/memory/
  AGENTS.md
  CLAUDE.md
  src/Billing/AGENTS.md
  src/Billing/FeatureMap.md
```

The virtual memory tree should mirror watched-project relative paths.

Example mapping:

```text
watched source:
C:\RealApp\src\Billing\InvoiceService.cs

virtual memory:
runtime/watched-solutions/<solution-id>/memory/src/Billing/AGENTS.md

optional published memory:
C:\RealApp\src\Billing\AGENTS.md
```

This solves the "one step away" problem. AIMonitor can emulate folder-local memory for Claude/Codex while keeping drafts outside the watched source tree until the operator chooses to publish.

## Context Resolution

Given a watched file, folder, namespace, feature, or project, AIMonitor should resolve a bounded memory packet.

Resolution should consider:

- watched-project root memory;
- nearest virtual folder memory;
- nearest published folder memory, if present;
- feature memory explicitly associated with the path;
- project-level memory;
- relevant test memory, if known;
- stale/drift status.

The packet should be small, source-labeled, and explain what was included.

Example packet shape:

```text
Memory context for src/Billing/InvoiceService.cs

Included:
- virtual root AGENTS.md: project overview and routing
- published src/Billing/AGENTS.md: billing invariants and owned workflows
- indexed test relation: PaymentRetryTests

Must preserve:
- posted invoices are append-only
- retry operations must be idempotent

Likely tests:
- BillingWorkflowSmoke
- PaymentRetryTests

Warnings:
- local memory is stale relative to the last accepted edit
- virtual src/Billing/AGENTS.md has unpublished changes
```

The resolver should explain why each item was included and should report virtual/published conflicts instead of silently merging them.

## Exploration Strategy

The memory scout should prefer AIMonitor's semantic tools when semantic context matters.

Use MCP/index/source-map tools for:

- project ownership;
- namespace and type ownership;
- symbol references;
- callers;
- relationships between files;
- compiler-backed source-map references;
- tests related by symbol or project facts.

Use grep/text search for:

- literal labels and strings;
- CSS classes;
- route text;
- config keys;
- Markdown files;
- non-C# assets;
- naming-convention relationships.

The scout should build memory from project truth first, then fill gaps with text evidence.

## Sub-Agent Model

This feature is worthy of a sub-agent, but the sub-agent should be a scout and authoring assistant, not a mutation authority.

Modes:

```text
Survey mode:
  inspect watched solution
  propose memory boundaries
  summarize ownership, data flow, tests, risks
  no file writes to watched source

Authoring mode:
  draft initial memory docs in the monitor-owned memory workspace
  no direct watched-source writes

Maintenance mode:
  after monitored edits, compare changes to existing memory
  propose updates to affected memory docs
  publish only through explicit sync/workflow
```

The scout can roam, summarize, and infer. AIMonitor decides how those drafts are stored and surfaced. Workflow decides what may be written into watched source.

## Publish And Sync

Publishing is promotion from monitor-owned memory into project-native memory.

With AIMonitor attached:

```text
virtual memory overlay resolves nearest docs
AIMonitor surfaces bounded context to the agent
```

Without AIMonitor attached:

```text
published AGENTS.md / CLAUDE.md / folder docs live in the watched tree
normal agent host discovery can find them
```

Possible sync commands:

```text
memory status
memory resolve-context
memory survey
memory draft
memory diff
memory publish-plan
memory publish
memory sync-from-watched
memory check-drift
```

Initial publishing should probably use the safe edit workflow and WinMerge review, because these docs steer future agent behavior. A later lower-friction path can be considered for clearly marked agent-maintained sections after real use proves the pattern.

Publish planning should classify each target:

- create;
- update agent-maintained section only;
- update human-owned section requested;
- no-op;
- conflict;
- stale source;
- delete proposed.

Deletes should be proposed only at first. Removing memory can remove safety context.

## Maintenance Integration

The main integration question is not "how does this become workflow?" It is "how does memory maintenance become a natural follow-up to workflow?"

Possible integration points:

- After accepted/accepted-normalized decisions, mark nearby memory docs as possibly stale.
- Include memory-stale hints in next-step guidance.
- Let the agent ask `memory_check_drift` for changed files.
- Let the scout draft memory updates into the virtual workspace.
- Let the operator publish those updates when they are worth keeping.

This keeps memory maintenance attached to real edits without making every edit pay a heavy documentation tax.

## Hooks And Language Server Position

Initial implementation should not require hooks or a language server.

AIMonitor already has:

- watched-source mutation gate;
- Working/stage/review/decision loop;
- MSBuild-loaded project truth;
- semantic index;
- MCP/CLI/UI adapter surfaces;
- telemetry.

That covers most of what generic hooks would enforce for this use case. The existing watched-source guard hook remains useful as a direct-write tripwire, but memory resolution and maintenance should be AIMonitor features, not external hook policy.

Language-server style integration may be useful later for editor UX, but it is not required to prove the memory model.

## Possible Implementation Paths

### Option A: Runtime-Only Virtual Memory

Create and maintain memory docs only under the monitor runtime workspace.

Pros:

- safest first step;
- no watched-project pollution;
- easy to regenerate and compare;
- useful while AIMonitor is attached.

Cons:

- not discoverable by normal Claude/Codex when AIMonitor is absent;
- requires memory resolver tools.

### Option B: Direct Watched-Project Memory

Create `AGENTS.md`, `CLAUDE.md`, and folder docs directly in the watched project.

Pros:

- native agent discovery works immediately;
- versioned with the watched project;
- useful without AIMonitor.

Cons:

- higher risk;
- generated docs can steer future agents badly;
- requires careful review and sync behavior.

### Option C: Hybrid Virtual-First With Explicit Publish

Draft and maintain memory in monitor-owned runtime space, resolve it virtually during monitored work, and publish selected docs into the watched project when stable.

Pros:

- best safety/usefulness balance;
- supports both monitored and non-monitored agent use;
- allows drift detection;
- lets memory mature before becoming project-native.

Cons:

- more moving parts;
- requires clear precedence and sync rules.

Recommendation: implement Option C in phases, beginning with Option A behavior.

## Phased Plan

### Phase 0 - Contract, Schema, Markers, And Samples

- Create this plan.
- Add or update system-memory language only after the plan is accepted.
- Decide the minimal structured memory schema.
- Decide section marker convention.
- Decide authority/truth precedence rules.
- Decide generated-marker and provenance conventions.
- Define sample watched-project memory layouts for Codex and Claude sample solutions.
- Decide naming conventions for folder memory files.

### Phase 1 - Runtime Memory Workspace

- Add a shared memory service project or namespace.
- Derive memory workspace path from watched solution identity.
- Store virtual memory docs under mirrored relative paths.
- Add models for memory document identity, scope, source, and status.
- Prove deterministic storage and retrieval before LLM drafting.
- Do not publish into watched source yet.

### Phase 2 - Resolver

- Given a watched path, resolve root plus nearest virtual memory docs.
- Include published memory docs only as read evidence, if present.
- Return a bounded context packet with source paths and stale flags.
- Explain why each memory source was included.
- Exclude unrelated sibling docs by default.
- Handle virtual and published duplicates deterministically.
- Expose through CLI/MCP.

### Phase 3 - Manual And Sample Memory Docs

- Create hand-authored sample memory docs for committed watched samples.
- Prove resolver behavior before automated survey or drafting.
- Use these samples to define what good root router and folder memory docs look like.

### Phase 4 - Structured Survey Support

- Use MSBuild, indexing, source maps, and text search to propose memory boundaries.
- Generate a survey result, not docs yet.
- Include projects, folders, namespaces, important symbols, tests, and risks.
- Keep output bounded and reviewable.

### Phase 5 - Draft Authoring

- Generate draft memory docs into the runtime memory workspace.
- Support root router docs and folder/feature docs.
- Preserve human-owned sections if drafts are regenerated.
- Regenerate section-by-section where markers allow it.
- Do not write to watched source.

### Phase 6 - Publish Planning

- Compare virtual memory docs to intended watched-project target paths.
- Produce a publish plan with create/update/delete/no-op classifications.
- Show diffs before publishing.
- Let operator select which docs to publish.

### Phase 7 - Safe Publish

- Route selected publish writes through the safe edit workflow.
- For new watched-project memory docs, use the existing new-file path.
- For existing docs, refresh, edit candidate, stage, launch diff, and record decision.
- Consider a lower-friction path only after this is proven.

### Phase 8 - Drift Detection

- After accepted edits, identify related memory docs.
- Mark docs stale when changed files overlap their scope.
- Prefer soft stale states such as possibly stale before forcing maintenance.
- Add `memory_check_drift` to compare current project facts with memory claims.
- Draft maintenance updates into the runtime memory workspace.

### Phase 9 - Agent UX

- Add MCP/CLI tools for context resolution and memory maintenance.
- Add WinForms display for resolved memory context and stale docs.
- Add telemetry for memory resolution, draft generation, publish planning, and drift checks.

## Initial Tool Surface

Minimal first pass:

```text
memory_status
memory_list_docs
memory_get_doc
memory_resolve_context
memory_explain_resolution
memory_survey
memory_draft_doc
memory_publish_plan
```

Later:

```text
memory_publish
memory_sync_from_watched
memory_check_drift
memory_mark_stale
memory_list_docs
memory_get_doc
```

## Test And Smoke Ideas

- Runtime memory workspace path is derived from watched solution identity.
- Resolver returns root memory plus nearest folder memory for a watched file.
- Resolver does not return unrelated sibling feature docs by default.
- Published and virtual docs are both detected and source-labeled.
- Resolver explains inclusion and exclusion decisions.
- Virtual and published conflicts are reported, not silently merged.
- Survey proposes memory boundaries from MSBuild projects and indexed symbols.
- Draft regeneration preserves `Human Notes`.
- Draft regeneration preserves marker ownership rules.
- Publish plan correctly classifies create/update/no-op.
- Publish uses workflow path for watched-source writes.
- Drift check marks local memory stale after accepted edits in its scope.
- CLI and MCP return consistent memory context packets.

## Open Questions

- Should the default folder memory file be `AGENTS.md`, `CLAUDE.md`, `README.md`, `FeatureMap.md`, or configurable?
- Should root `CLAUDE.md` and `AGENTS.md` be generated as separate files or one source rendered into host-specific variants?
- Should published memory docs include a generated marker, or should only specific sections be marked agent-maintained?
- Should memory docs be versioned in the watched project immediately after operator approval, or kept runtime-only until a separate promotion decision?
- How much of the survey should be deterministic service output versus sub-agent-authored prose?
- Should `memory_publish` ever bypass WinMerge for agent-maintained sections, or is human diff review always required?
- How should memory stale state be surfaced without nagging after every tiny edit?

## Acceptance Shape

The feature is useful when:

- AIMonitor can resolve local memory for a watched file even when no physical docs exist in the watched tree.
- The resolver returns a bounded, evidence-labeled context packet.
- The packet explains why each memory source was included.
- An agent can use that resolved context while editing through AIMonitor.
- Draft memory docs can be authored safely outside watched source.
- Human-owned sections are preserved across draft regeneration.
- Stable memory can be explicitly published into the watched project so future agent sessions can discover it without AIMonitor.
- Virtual and published memory conflicts are visible.
- Drift can be detected and maintained without making every edit feel like a documentation chore.
- Memory never overrides AIMonitor safety rules.
