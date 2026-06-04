---
status: open
type: review
created: 2026-06-04
audience: Codex (plan author) + operator
scope: review of plans/WatchedProjectMemorySurface.md (commit 09e5608)
reviewer: Claude (review gate)
---

# Review — Watched Project Memory Surface plan

## Verdict

Strong, safety-consistent plan. It independently lands on the principles recorded in decisions 0002/0003/0004:
floor preserved (publishing into watched source goes through the safe-edit workflow); memory is a context feature, not
an edit-workflow feature; new shared `AIMonitor.Memory` service with adapters on top (explicitly *not* folded into
Workflow — avoids the HIGH-2 adapter-leak mistake); facts outrank memory; the sub-agent is a scout/author, not a
mutation authority (= 0004); hooks/LSP not required and the existing watched-source guard hook is retained as a
direct-write tripwire (= 0003); deterministic spine proven before LLM drafting; structured records with Markdown as a
*projection*. No new silent-mutation path. The plan is doc-only; no `AIMonitor.Memory` project exists yet.

## Two operator clarifications to fold into the plan

1. **Watched-project memory is an additional CONTEXT SOURCE surfaced via MCP — not a host-instruction authority for the
   monitor-agent.** AIMonitor's own `CLAUDE.md`/`AGENTS.md` remain Claude's host contract. The watched project's
   `CLAUDE.md`/`AGENTS.md`/feature docs are resolved as bounded *evidence* (domain language, feature ownership, local
   conventions, test selection) — they do not instruct the monitor-agent and cannot relax safety rules. The plan's
   *Authority And Truth* section lists watched-memory in an *instruction-authority* ladder above the agent request,
   which reads as if a watched doc steers (or overrides) the agent. Recommend re-casting that section: separate
   **instruction authority** (host files + system memory + the live operator request — watched memory is NOT in this
   chain) from **resolved context** (watched memory as MCP-surfaced evidence, ranked under project facts). This removes
   the "stale watched doc overrides a live request" ambiguity entirely — memory is evidence; the host files and the
   operator's live word stay authoritative.

2. **Foreground the north star: make the watched project AI-capable WITHOUT the monitor.** The eventual goal is a
   *self-describing* watched project — published `AGENTS.md`/`CLAUDE.md`/folder docs that normal agent host-discovery
   finds when AIMonitor is not attached. The virtual-memory + publish/sync flow is the **bootstrap** that graduates a
   watched project to standalone AI-navigability. The plan mentions this as one goal among many; it should be the
   stated purpose of the publish path. Honest caveat to state alongside it: running the watched project monitor-free
   means the safety floor is gone — the published memory makes it *possible*, not *safe* ("if someone's crazy enough to
   try it"). The memory feature is how a project absorbs the AI-navigability the monitor provides so it can stand alone.

## The load-bearing review point: memory is the only artifact with no objective gate

Our whole model trusts what runs: code → build, behavior → run/tests. **Memory has nothing to run.** A wrong memory
doc compiles, passes every test, and silently misguides future sessions — worse than a bad code change (which fails the
build) because it passes every gate *and compounds* (it steers generation, gets published, steers again). The north
star makes this sharper: published memory exists precisely to steer an agent that has **no monitor and no floor**, so
publish-time human review is the *only* protection that agent will ever have. Consequences for the plan:

- **Keep human review on publish — longer than for code, not shorter.** Answer the open question "should `memory_publish`
  ever bypass WinMerge for agent-maintained sections?" with **no** until well-proven. This is the one workflow where
  the "you don't have to review, just test-and-iterate" model does *not* apply, because there is no objective gate to
  fall back on and (per the north star) the published doc may be steering a floor-free agent.
- **Drift detection is the closest thing to an objective gate — elevate it, but scope it honestly.** `memory_check_drift`
  (memory claims vs MSBuild/index facts) is the only mechanical correctness check. It catches *factual staleness*, not
  *bad advice*. Name that limit so drift-green is not read as memory-correct.
- **Name the feedback loop as the central risk** (generated guidance steering future generation), not just an Option-B
  con. Provenance markers + mandatory publish review are the right mitigations; the plan has them — call the loop out.

## Smaller flags

- **Host duplication (`CLAUDE.md` vs `AGENTS.md`):** resolve the open question toward one structured source projected to
  host-specific files, to avoid the two-host drift this repo itself fights. The structured model already supports it.
- **Hold the `AIMonitor.Memory` line:** keep resolution/drift logic in the shared service, not the MCP adapter (HIGH-2
  lesson), and route CLI + MCP through it so memory resolution earns dual-adapter validation for free.
- **System-memory contract:** the context-source-vs-instruction-authority distinction and the north star should
  eventually be reflected in `docs/system-memory/README.md` — but per the plan's Phase 0, that waits for plan acceptance.

## Where Claude plugs in (role)

Phase 0/3 (sample memory layouts + hand-authored sample memory docs for the committed watched samples) land on Claude's
owned sample surface (`WinFormsSample`/`BlazorSample`). Natural fit: Claude authors the sample memory docs + the
resolver **ClaudeSmokes**. The plan's test list is already mean-teacher-shaped and reuses patterns Claude has: resolver
returns nearest folder memory and does **not** over-match sibling features (the `Features/Orders` vs `Features/OrdersExtra`
isolation trap), conflicts reported not merged, `Human Notes` preserved across regeneration, publish uses the workflow
path, drift marks stale. Same cadence as the parity restore: Codex executes per phase; Claude reviews + ClaudeSmokes
each, with extra weight on the resolver-determinism and publish-review phases (the no-objective-gate risk).
