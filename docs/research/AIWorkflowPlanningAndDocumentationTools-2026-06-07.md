---
status: research
type: landscape
created: 2026-06-07
audience: operator + Codex
scope: planning, session, approval, documentation, semantic-code, and debugger-adjacent tools relevant to AIMonitor/AISolutionDocumenter
---

# AI Workflow Planning And Documentation Tools

This note preserves the reading list from the planning/documentation discussion so it does not get lost in chat scrollback.

The strategic lens is not "compete with generic agent dashboards." The goal is a local, expert-amplifying workflow where a human supplies domain/business context, agents operate through safe evidence-bound workflows, and generated or unfamiliar code remains understandable through maintained documentation.

## Best Planning / Spec Workflow Reads

### OpenClaw Code Agent

Read for:

- Claude Code and Codex as managed background coding sessions.
- Structured plan artifacts and approval before implementation.
- Session lifecycle, worktree decisions, resume/recover, and PR/merge/discard follow-through.
- A concrete example of making plan approval first-class across multiple coding agents.

AIMonitor takeaway:

OpenClaw is closest on shared agent control-plane and approval UX. Borrow the explicit plan/session decision model, not the whole chat/worktree product shape.

### autospec

Link: https://ariel-frischer.github.io/autospec/

Read for:

- `spec -> plan -> tasks -> implement`.
- YAML-first artifacts for programmatic validation.
- How a spec workflow can become machine-readable instead of prose-only.

AIMonitor takeaway:

Useful inspiration for a backing store shape for plans/tasks/doc targets.

### Agent OS Workflow

Link: https://buildermethods.com/agent-os/v2/workflow

Read for:

- Product/spec/task planning flow.
- Command-driven planning workflow that agents can follow.
- How a planning surface turns human intent into agent-executable tasks.

AIMonitor takeaway:

Good vocabulary for the plan lifecycle, but AIMonitor should bind plan items to real workflow evidence.

### SpecDD

Link: https://specdd.ai/

Read for:

- Specs as durable intent, architecture, behavior, and boundary docs.
- Specs for both humans and AI coding agents.
- Keeping docs/specs updated with implementation, not as a follow-up.

AIMonitor takeaway:

Strong support for treating documentation as a first-class artifact of the work loop.

### Spec This

Link: https://specthis.ai/

Read for:

- MCP-driven spec/plan creation.
- Scope, constraints, and acceptance criteria as explicit planning fields.

AIMonitor takeaway:

Useful for designing a "Start Plan" flow that does not require the human to type a giant prompt into chat.

### SpecD

Link: https://getspecd.dev/

Read for:

- Approval and signoff gates.
- Agent-independent spec workflow.

AIMonitor takeaway:

Good reminder that approval/signoff belongs in the workflow object, not only in chat memory.

## Control Plane / Plan UI Ideas

### Cogpit

Link: https://cogpit.dev/

Read for:

- Session control UI.
- Permission modes.
- MCP server selection per session.

AIMonitor takeaway:

The UI value is not "cards"; it is making session state and permissions visible enough that humans do not have to manage everything through a tiny VS Code chat box.

### Handler.dev

Link: https://handler.dev/

Read for:

- Local/self-hosted agent sessions.
- Persisted terminal output, scrollback, and running processes.
- Sandbox control plane for Claude Code, Codex, Gemini, OpenCode, and others.

AIMonitor takeaway:

Worth mining for local-first session persistence and "what is this agent doing now?" UI patterns.

### Agent Cockpit

Link: https://agent-cockpit.dev/

Read for:

- Session manager.
- Timeline.
- Approval queue.
- Human-in-the-loop control room.

AIMonitor takeaway:

Timeline plus approval queue maps naturally to `plan -> session -> staged records -> decisions -> docs updated -> closure`.

### Spettro

Link: https://spettro.eyed.to/

Read for:

- Plan agent vs coding agent.
- `/approve` modes.
- Hooks, permission requests, and project-local allowed commands.

AIMonitor takeaway:

Role separation is useful: planning, editing, reviewing, and document-refreshing should be distinct modes even if the same underlying agent performs them.

### ECA

Link: https://eca.dev/

Read for:

- Editor-agnostic agent protocol.
- MCP integration.
- Approval controls and browser remote control.

AIMonitor takeaway:

Useful for thinking about local UI surfaces that are not tied to one IDE.

## Adjacent Workflow / Monitoring Reads

### AgentGlance

Link: https://agentglance.app/

Read for:

- Lightweight monitoring overlay.
- Agent status visibility.
- Implementation-plan visibility.

AIMonitor takeaway:

Small status surfaces can be valuable. AIMonitor's Plan Pane does not need to become a full project management app.

### AgenticQueue

Link: https://agenticqueue.ai/

Read for:

- Approval queues.
- Lineage.
- Plan -> implement -> test lanes.

AIMonitor takeaway:

Lineage is the interesting part: link plan items to sessions, staged records, accepted decisions, and documentation updates.

### Optio

Link: https://optio.host/

Read for:

- Workflow queue.
- Review-requested states.
- Multi-agent execution.

AIMonitor takeaway:

Queue/status states are useful, but AIMonitor should avoid becoming a SaaS task runner.

## Earlier Close Comparisons

### Bernstein

Link: https://github.com/sipyourdrink-ltd/bernstein

Read for:

- Deterministic multi-agent CLI orchestration.
- Audit chain.
- Signed agent cards.
- Per-artifact lineage.

AIMonitor takeaway:

Worth borrowing from lightly for evidence chains. Do not import a full compliance framework unless the need becomes real.

### vibe-kanban

Link: https://github.com/BloopAI/vibe-kanban

Read for:

- Task/workspace UI.
- Diff review.
- Agent workspaces.
- PR/merge workflow.

AIMonitor takeaway:

Review ergonomics are worth studying. Generic Kanban should not become AIMonitor's center.

### agtx

Link: https://github.com/fynnfluegge/agtx

Read for:

- Multi-agent board.
- Brainstorm/sweep style workflow.
- MCP orchestrator pattern.

AIMonitor takeaway:

The brainstorm -> sweep idea is useful: discuss freely, then commit a structured plan when the human is ready.

### cc-sdd

Link: https://github.com/gotalab/cc-sdd

Read for:

- Cross-agent spec-driven templates.
- Phase gates.
- Boundary-first implementation tasks.

AIMonitor takeaway:

Plan items should carry boundaries, dependencies, and acceptance criteria.

### Trellis

Link: https://github.com/mindfold-ai/trellis

Read for:

- Repo-local memory/spec/task substrate across agents.
- Shared standards and workflow artifacts.

AIMonitor takeaway:

Repo-local durable memory is central to AISolutionDocumenter: human context plus generated evidence should live where agents naturally discover it.

## Semantic Substrate

### Roam Code

Link: https://github.com/Cranot/roam-code

Read for:

- SQLite code intelligence.
- Call graph.
- Architecture simulation.
- MCP surface.
- Verdict-first agent envelopes.
- Preflight/blast-radius/risk checks.
- Graph health, architecture, smell, security, and multi-agent partitioning ideas.

AIMonitor takeaway:

Relevant to architecture-atlas ideas. It appears closer to broad multi-language codebase intelligence/governance than AIMonitor's C#-first safe edit lifecycle.

Closer notes:

- Roam stores a local SQLite-backed graph under `.roam/index.db`.
- Its README claims a graph of symbols, calls, imports, layers, git history, runtime traces, smells, clones, security flows, and algorithmic patterns across 28 languages.
- Its core verbs are intentionally small despite a huge command/tool surface: `understand`, `context`, `retrieve`, `preflight`, and `critique`.
- `roam preflight <symbol>` is the most relevant idea for AIMonitor: it returns a verdict before editing, combining blast radius, affected tests, complexity, coupling, conventions, and fitness/rules.
- `roam context <symbol>` is also relevant: AI-ready context with definition, callers, callees, and files-to-read with line ranges.
- `roam retrieve <task>` combines full-text search with structural reranking inside a token budget. This is a useful pattern for AISolutionDocumenter when a user asks a free-form business/domain question that does not map cleanly to one symbol.
- `roam critique` verifies a patch against the graph: clone-not-edited, blast radius, and semantic-diff/intent checks. This overlaps with AIMonitor's post-candidate validation ideas but is graph/risk focused rather than WinMerge/hash-classification focused.
- It exposes many MCP tools but keeps the default preset smaller, with a meta-tool to expand the surface. That is a useful answer to MCP tool bloat.
- It has "cold-start envelopes": missing index, stale index, partial failure, and not-found states return canonical actionable JSON instead of empty output or tracebacks. This maps directly to AIMonitor's rule that valid response shape is not enough; responses need real operator guidance.
- It emphasizes local/no-cloud operation and evidence packets: HMAC-chained run ledger, graph attestation, PR bundle, and policy receipts. AIMonitor already has staged records/hashes/ledgers; Roam's evidence vocabulary may help name higher-level plan/session proof.

SQLite depth comparison:

- AIMonitor currently persists solution/project/document/symbol/reference/call-site/relationship/package/framework/global-using/diagnostic rows. The index is MSBuild/Roslyn-loaded and C#-accurate for the supported semantic surface.
- Roam appears broader: multi-language symbols, graph metrics, churn/history, clone/smell/security/taint/architecture detectors, affected tests, co-change/coupling, health scores, and multi-agent leases/memory.
- AIMonitor is deeper on the safe edit lifecycle: monitor-owned Working candidates, immutable staged records, WinMerge review, hash-classified accept/reject, retrieval backups, post-accept index rebuild, and adapter parity over a shared workflow engine.
- Roam is deeper on pre-change graph risk and broad codebase governance. Its `preflight`, `health`, `weather`, `critique`, `retrieve`, and `context` concepts are worth adapting.

Ideas worth borrowing:

- Add a verdict-first `preflight` style query for AIMonitor plans: "Before this edit, what is the likely blast radius, affected tests, complexity/coupling risk, and documentation impact?"
- Add a plan/session `closure` or `critique` step that checks accepted changes against declared plan intent and target docs.
- Add codebase/documentation `health` summaries for AISolutionDocumenter: stale docs, undocumented generated folders, missing human context sections, high-churn undocumented surfaces.
- Consider a small default MCP tool preset plus an expand-toolset pattern if AIMonitor/AISolutionDocumenter grows many planning/documentation tools.
- Use canonical error/not-found/stale envelopes everywhere. Empty results should say whether the corpus was empty, stale, unsupported, not found, or genuinely clean.
- For free-form task discovery, consider a hybrid `retrieve` path: exact MSBuild/Roslyn graph facts first, optional vector/FTS/fuzzy ranking second.

Actual schema comparison:

Roam's public schema in `src/roam/db/schema.py` is compact and graph-metric oriented. It has tables for `files`, `symbols`, `edges`, `file_edges`, `git_commits`, `git_file_changes`, `git_cochange`, `file_stats`, `graph_metrics`, `clusters`, `git_hyperedges`, `git_hyperedge_members`, `symbol_metrics`, `math_signals`, and `snapshots`.

AIMonitor's current MSBuild/Roslyn SQLite schema has `solution_state`, `projects`, `documents`, `symbols`, `symbol_references`, `call_sites`, `symbol_relationships`, `project_references`, `package_references`, `framework_references`, `global_usings`, and `diagnostics`.

What Roam persists that AIMonitor mostly does not:

- Graph-wide metrics: PageRank, in/out degree, betweenness.
- File health metrics: churn, authorship count, complexity, health score, co-change entropy, cognitive load.
- Symbol complexity metrics: cognitive complexity, nesting, parameter count, returns, boolean ops, callback depth, Halstead metrics.
- Algorithm-pattern signals: loop depth, nested loops, calls in loops, subscripts in loops, self calls, string concatenation in loops, loop invariants.
- Git history and co-change: commits, file changes, pairwise co-change, n-ary hyperedges.
- Clustering/community detection: cluster id/label per symbol.
- Snapshot trends over time: health, cycles, god components, bottlenecks, dead exports, layer violations.
- Unified generic graph tables (`edges`, `file_edges`) over many languages.

What AIMonitor persists that Roam's public schema does not emphasize:

- MSBuild project truth: target frameworks, output type, SDK, assembly/root namespace, nullable, implicit usings, language version, preprocessor symbols.
- Package/framework/project references from real `.csproj`/solution load.
- C#-specific semantic row shape: stable symbol keys, namespace, containing type, accessibility, static/abstract/sealed/virtual/override/method-kind flags.
- Reference rows with snippets plus target metadata and caller metadata.
- Explicit call-site table separate from generic edges.
- Explicit symbol-relationship rows for inheritance/implements/overrides/partials-style relationships.
- Stale-file detection through persisted content hashes.
- Diagnostics from the MSBuild/Roslyn load.
- Separate runtime workflow state outside the index: Working candidates, staged records, ledgers, review decisions, retrieval backups, validation copies, and index-refresh state.

Schema-level conclusion:

- Roam has a broader graph-analysis database. It is designed to support architecture health, blast radius, affected tests, churn/coupling, smells, algorithms, security/taint, and multi-language governance.
- AIMonitor has a narrower but more compiler-truthful C#/.NET solution database plus a much stronger safe-edit workflow database/runtime layer.
- A useful next AIMonitor schema direction is not to copy all Roam detectors. The highest leverage additions would be small graph metric tables or materialized views over existing `symbols`, `call_sites`, and `symbol_relationships`: fan-in/fan-out, PageRank-ish centrality, blast-radius cache, affected-test mapping, file churn/co-change, and doc-staleness/doc-coverage tables for AISolutionDocumenter.

### Ory Lumen

Link: https://github.com/ory/lumen

Read for:

- Local semantic search for Claude Code, Codex, and OpenCode.
- Token-reduction framing.
- SQLite/sqlite-vec style local indexing.
- Cross-host plugin packaging for Claude, Codex, Cursor, and OpenCode.
- Reproducible benchmark harness against real GitHub bug-fix tasks.

AIMonitor takeaway:

Useful adjacent framing for reducing context clutter, though AIMonitor's strongest evidence remains MSBuild/Roslyn semantic truth.

Closer notes:

- Lumen is a local MCP semantic search engine. It uses Ollama or LM Studio for code embeddings, stores vectors in SQLite plus `sqlite-vec`, and exposes `semantic_search`, `health_check`, and `index_status` to agents.
- It does not require comments to work. It splits files into semantic chunks such as functions, types, and methods using Go's native AST for Go and tree-sitter grammars for other languages. It embeds the code chunks themselves: identifiers, signatures, literals, nearby control flow, and local structure are the retrieval surface.
- That means it can still work on generated/comment-light code when names and structure carry meaning. It will be weaker when generated code uses meaningless identifiers, giant homogeneous files, or repeated boilerplate with little semantic distinction.
- It uses Merkle tree change detection so only changed files are re-chunked and re-embedded. Worktrees can seed from sibling indexes, which avoids full re-indexing for branch/worktree sessions.
- Index data lives outside the repo under a local data directory keyed by project path, model, and binary version.
- It filters indexing through built-in ignores, `.gitignore`, `.lumenignore`, `.gitattributes` `linguist-generated`, and supported extensions. The `.lumenignore` hook is especially relevant for generated-code-heavy repos where some generated files are noise and others are the product.
- Their benchmark is stronger than a synthetic token estimate: it runs Claude on real GitHub bug-fix tasks with and without Lumen, captures raw JSONL, cost, time, output tokens, cache reads, tool calls, patch diffs, and judge ratings.
- Reported headline from their README/docs: across 9 language tasks, cost dropped in every language, average bug-fix cost dropped 37%, time dropped 37%, output tokens dropped 42%, and patch quality was maintained. Results vary by language/task; C++ feature work had smaller gains and higher output tokens.

Generated-code implications:

- Lumen's vector approach answers "what code looks semantically related to this natural-language request?" It is useful when the agent does not know the exact symbol, file, or vocabulary.
- AIMonitor's Roslyn/MSBuild approach answers "what is the exact symbol/reference/caller/project truth?" It is stronger once the relevant C# symbol or file is known.
- For AISolutionDocumenter, the two ideas are complementary:
  - use Roslyn/MSBuild for exact source evidence, ownership, references, callers, and data-flow facts;
  - consider vector search as a fuzzy discovery layer over generated code and Markdown human sections, especially when business terms do not match source identifiers.
- For generated code with no comments, the durable fix is still human-owned Markdown context. Vector search may find structurally relevant generated chunks, but it cannot invent the missing business meaning reliably.

### RoslynMcp

Link: https://github.com/MadQ/RoslynMcp

Read for:

- Roslyn MCP server.
- C# semantic tool surface.
- Rename/refactoring ideas.

AIMonitor takeaway:

Useful comparison for C# semantic MCP scope.

### SharpLens MCP

Link: https://github.com/pzalutski-pixel/sharplens-mcp

Read for:

- Roslyn-powered .NET/C# analysis and refactoring tool ideas.

AIMonitor takeaway:

Useful as a semantic tooling neighbor, not as a full safe-edit monitor.

### Visual Studio MCP AI Server

Link: https://github.com/LadislavSopko/mcp-ai-server-visual-studio

Read for:

- Visual Studio/Roslyn MCP integration.
- Semantic navigation through Visual Studio.
- Debugger MCP tools: breakpoints, stepping, locals, call stack, expression evaluation, output/error windows.
- Codex/Claude/Gemini/OpenCode MCP configuration examples.

AIMonitor takeaway:

This validates the Roslyn-over-MCP direction and covers debugging from the IDE side. It does not appear to cover AIMonitor's safe edit lifecycle: Working candidates, staging, WinMerge review, hash-classified decisions, ledgers, and post-accept index rebuilds.

## Product Direction Notes

### Plan Pane

The planning feature should be a real UI/backing-store object because humans should not have to author serious plans in a tiny chat box.

Minimal useful plan record:

```text
planId
title
description
humanContext
status
docTargets
linkedSessionIds
filesRead
filesChanged
stagedRecordIds
decisions
testsRun
closureSummary
```

The plan should attach evidence as the work proceeds:

```text
intent -> source evidence -> staged candidates -> accepted decisions -> generated docs -> human notes -> closure
```

### AISolutionDocumenter

AISolutionDocumenter should be repo-local and documentation-focused:

- Source files are read-only evidence.
- Markdown is the writable artifact.
- Every generated Markdown file is born with human-owned sections.
- AI sections are refreshable from build/index truth.
- Human sections are preserved byte-for-byte unless explicitly edited.

Recommended Markdown marker style:

```markdown
## Human Context

<!-- HUMAN:BEGIN context -->
<!-- Add domain/business context here. This section is preserved on regeneration. -->
<!-- HUMAN:END context -->

## AI Structure

<!-- AI:BEGIN structure -->
Generated from build-tree analysis.
<!-- AI:END structure -->

## AI Documentation History

<!-- AI-HISTORY:BEGIN -->
- 2026-06-07: Created from solution analysis. Human sections initialized.
<!-- AI-HISTORY:END -->
```

Core split:

- Humans provide business/domain context and framework land-mine knowledge.
- AI provides continuously refreshable data-flow, symbol, dependency, and evidence analysis.

This is the general version of what low-code/domain tools already do internally: maintain a higher-level project model. The difference is that this model is repo-local, Markdown-visible, and built over AI-authored or generated code.
