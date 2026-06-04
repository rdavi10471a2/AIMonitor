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

## Storage flaw: the virtual memory workspace is gitignored / unbacked

The plan stores virtual memory under `runtime/watched-solutions/<solution-id>/memory/...` (plan lines 195-201) and
makes authoring there the default (Option A / Phase 1). But `runtime/` is **gitignored** (`.gitignore` → `runtime/`)
because it was designed for **ephemeral, regenerable** state — indexes, Working candidates, logs — disposable by design
and intentionally excluded from version control.

Virtual memory is the opposite of that: **durable, curated content**, including hand-authored `Human Notes`, that
represents real intellectual work and is meant to persist and be shared. Storing it under `runtime/` means it:

- has **no off-machine backup** (disk loss = every unpublished draft and Human Note is gone — and publish is gated
  behind human review, so valuable drafts can sit unpublished and unbacked for a long time);
- is **not shareable** between machines or people before publish;
- inherits **"disposable / regenerable" semantics it does not deserve** — a `runtime/` clean could wipe it.

The plan elevates authoring-in-the-unbacked-area to a near-requirement, so it institutionalizes an unbacked store for
the one content type that can least afford it. This **stacks with the no-objective-gate point above**: memory is the
single artifact that is neither mechanically validatable (no build/test) **nor** (as planned) backed up.

**Recommendation — decide this in Phase 0, before Phase 1 builds on the runtime path:** give the memory workspace a
**persisted, version-controlled store distinct from `runtime/`'s disposable state**. Options: (a) a committed memory
location (e.g. a tracked `memory/` per solution, or under a non-ignored path) so drafts are backed up and shareable
*before* publish; (b) have the structured-memory store (records + Markdown projections) write to a backed/synced store
rather than ephemeral runtime. The principle: **durable curated memory must not inherit ephemeral-runtime persistence
semantics.** (Related, operator-owned: the watched solution itself is a local git repo with checkpoint commits but no
remote — a separate backup gap. The memory feature should define its own persistence/backup contract rather than
assuming the runtime area is safe to keep authored work in.)

## Candidate resolution — docs-only branch (UNDER CONSIDERATION, not decided)

Operator idea (2026-06-04, still being thought through): **each watched solution carries a docs-only branch** (e.g.
`aimonitor-memory`) that gets pushed. The virtual memory lives on that branch instead of in gitignored `runtime/`. This
is better than a backup patch — it upgrades the plan's virtual-first storage layer: the branch becomes a **backed,
versioned, co-located virtual store** replacing the disposable runtime workspace. It solves backup (it's pushed),
co-location (memory travels with the solution it describes — serves the self-describing north-star), build-isolation
(a separate branch never touches `master`/the build/checkpoint flow), and gives git-native diffable history.

Things to settle before adopting it:

1. **A branch isolates the *build* risk, not the *steering* risk.** A docs branch guarantees bad memory can't break the
   app (never merged, never built) — but memory's real danger is misguiding future agents, which has no objective gate
   on any branch. So the docs branch does **not** license skipping human review of memory *content*, especially docs
   the no-monitor agent will read. "It's just a docs branch" must not become "so don't review it."
2. **The north-star wants the *published* docs in `master`, not the docs branch.** A normal no-monitor checkout sees
   `master`, not `aimonitor-memory`. So host-discoverable folder docs (`src/X/AGENTS.md`) must land in the main tree.
   Clean two-stage shape, mapping onto the plan's virtual→publish: **docs branch = backed virtual/draft store**
   (low-friction, build-isolated, backed); **main-tree folder docs = reviewed, host-discoverable published end-state.**
   "Virtual" becomes a real backed branch; "publish" becomes a branch→tree promotion.
3. **Depends on the watched repo having a remote** (currently it has local checkpoint commits but no remote). Fixing the
   watched-repo backup gap and backing the memory are then the same fix.
4. **Mechanics (Phase 0):** worktree vs separate clone for the docs branch; and does committing to the docs branch run
   through the safe-edit floor, or is it the legitimate lower-friction zone (quarantined from source) with the reviewed
   gate applied at publish-to-`master`? Likely the latter for drafts.

Status: operator is weighing this; recorded as the leading candidate to resolve the storage flaw, not yet a decision.

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
