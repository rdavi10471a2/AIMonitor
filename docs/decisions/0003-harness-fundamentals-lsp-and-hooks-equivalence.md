# 0003 — Harness Fundamentals: LSP and Hooks Equivalence

Status: accepted
Date: 2026-06-03
Related: [0002-safety-enforcement-philosophy.md](0002-safety-enforcement-philosophy.md),
[../findings/HarnessEngineeringNotes.md](../findings/HarnessEngineeringNotes.md),
[../findings/Phase8HarnessClosureReview-2026-06-03.md](../findings/Phase8HarnessClosureReview-2026-06-03.md),
[../../.claude/hooks/guard-watched-source.ps1](../../.claude/hooks/guard-watched-source.ps1)

## Context

The current "agent harness" discourse names a handful of fundamentals — **skills/skill-cards, hooks, context
management, and a language-server-like semantic layer**. AIMonitor arrived at these primitives by building a
safe-edit monitor from scratch, and only afterward found the reference material (the `harness-engineering-demo`
video, captured in `HarnessEngineeringNotes.md`) that names them. This note records the conclusion of the Phase 8
closure review's two equivalence questions — "does MCP wrap a language server?" and "have we achieved hooks via other
means?" — so they are not relitigated. The short version: **the fundamentals are met, and where a named fundamental
does not apply, it is because it was designed for a human-in-an-editor, not for an agent consumer.**

## Decision

### 1. Language-server equivalence: we have the agent-relevant subset, made durable and safe

An LSP is a volatile, in-memory, single-query, **human-facing** service. AIMonitor provides the navigation/diagnostic
half that matters to an agent — go-to-definition, find-references, callers, relationships, outline — as **durable,
queryable, hash-stamped index rows**, which is a superset of an LSP on the axis that counts (persistence, scoped/bulk
query, staleness, content-hash safety). The remaining LSP surface does not apply to this consumer:

- **Completion / IntelliSense** — irrelevant; an agent emits whole members, it does not autocomplete char-by-char.
- **Semantic tokens / coloring** — irrelevant; there are no colors in a tool call.
- **Hover/signature** — the agent generates signatures; it does not hover.
- **Live as-you-type diagnostics** — the agent's diagnostic authority is the pre-merge full `dotnet build` (ground
  truth) plus an on-demand overlay, not a streamed best-effort analysis.

Therefore there is **no language-server to "add."** Position AIMonitor as a *durable semantic index + safe-edit
harness covering the navigate+validate subset of an LSP*, not as an LSP equivalent.

### 2. Cross-file rename is moot three ways; propagation is not rename

The one LSP feature that initially looked like a gain — atomic, solution-wide semantic **rename** — is moot, and the
reasons stack:

1. **Structurally impossible atomically** — one Working candidate, one `stage_candidate_for_review`, one WinMerge
   review, and one `record_diff_decision` **per file**. There is no multi-file atomic commit primitive.
2. **Rejected by the safety model** — an atomic multi-file rewrite is exactly the invisible-in-the-diff churn
   decision 0002 exists to prevent (the human eyeballs each changed file).
3. **Against the agent's grain** — a global top-down sweep violates the "small plan, bounded edit, focused test"
   discipline. Unprompted, the agent does not generate a cross-file rename; if asked, it performs semantic
   find-references + per-file edits — the same way a careful human does, except the "find" step is **more correct**
   than grep because `find_indexed_references`/`find_indexed_callers` resolve the actual symbol, not text matches.

Keep the distinction sharp:

- **Rename** = a *global* operation imposed top-down (atomic sweep). Refused by design.
- **Propagation** = a *local* edit that ripples outward — change a signature, then fix callers; make a method async,
  then await up the chain. This arises naturally from a small edit and **is** supported, as a **multi-file session**
  with overlay validation, each touched file still individually reviewed (`SessionOverlayValidation`,
  `AsyncPropagation` skill cards).

The difference is direction: propagation grows outward *from* a small local edit (bottom-up); rename descends *onto*
the codebase as one sweep (top-down). The harness supports the first and structurally refuses the second.

### 3. Hooks equivalence: in-band enforcement is structural; the one out-of-band gap is now closed

Hooks exist to enforce rules the agent may forget. For AIMonitor's **in-band** edit path, the rules are not reminders
— they are **structural invariants** the agent cannot bypass through AIMonitor's tools: watched-source immutability,
monitor-owned Working/Staged ownership, content-hash accept classification, and the build-then-WinMerge accept gate
(all verified intact in the Phase 8 review). That is stronger than a hook. The proxy hub is a faithful
PostToolUse-telemetry analog, with a server-side fallback log regardless of launch path.

The single place a hook genuinely earns its keep is the **out-of-band** path: AIMonitor cannot confine the agent's
*native* Edit/Write/Bash tools, only its own MCP tools. That gap (HIGH-1 in the Phase 8 finding) is closed by a
`PreToolUse` guard (`.claude/hooks/guard-watched-source.ps1`) that resolves the watched root **live** from
`config/appsettings.json` on every call — so it follows a solution swap and never goes stale — and blocks native
mutations under the watched root (destructive shell opt-in). Enabling it is a documented setup step
(`docs/setup/LocalSetup.md`).

### 4. The floor runs with the agent's grain

The recurring reason the LSP/atomic features keep not applying is the meta-point worth recording: **the safety floor
is not fighting the agent's natural behavior — it runs with the grain.** Small local edits, reviewed per file,
propagation modeled as a session of small edits. The named harness fundamentals were designed around a consumer (a
human in an editor) whose working style differs from the one this harness actually serves (an agent that already
prefers bounded local edits and has a persisted index + a build gate).

## Consequences

- Do not chase a language-server integration, completion, semantic tokens, or atomic cross-file rename. They are
  either already covered, irrelevant to an agent, or deliberately refused by the floor.
- Cross-file work is expressed as **propagation over a multi-file session**, never as an atomic rewrite.
- The "hooks" fundamental is met: structural for in-band edits, plus the watched-source guard hook for the
  native-tool out-of-band path. A future destructive-command guard (opt-in today) can harden the remaining
  out-of-band shell case if real usage warrants it.
- The "skills" and "context" fundamentals are already expressed as the `docs/claude-skills/` cards and the
  index/source-map context tools; this note closes the LSP/hooks pair.
