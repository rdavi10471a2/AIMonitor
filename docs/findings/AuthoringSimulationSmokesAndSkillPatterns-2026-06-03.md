---
status: open
type: finding
created: 2026-06-03
audience: Codex + operator
scope: ClaudeSmokes authoring-simulation battery (new WinForms/Blazor pages & repos) + skill-pattern harvest
note: the 4 ClaudeSmokes test files are kept LOCAL (Claude-owned verifiable surface); only docs/skill-cards/this note are pushed
---

# Authoring-simulation ClaudeSmokes + skill-pattern harvest

A 3-phase workflow (author → verify → reflect; 5 agents, ~249k tokens, ~10.7 min) authored and verified four
ClaudeSmokes that simulate **adding new pages and repositories over the in-repo samples**, then mined the run for
skill-card patterns. All four pass (**Data 2/2, Workflow 3/3, independently re-verified green**). Tests stay LOCAL.

## What was authored (LOCAL, tests-only, no src/ or samples/ edits)
- `ClaudeSmokesAuthoringWinFormsTests` — materialize a WinFormsSample copy, author a new `Order` repo + `OrderForm`
  over it, re-index, assert the new graph by exact StableKey + **isolation** (two same-signature `GetByIdAsync`
  overrides resolve to the right concrete type) + **regression** (Customer graph intact) + **negative**.
- `ClaudeSmokesAuthoringRazorTests` — materialize a BlazorSample copy, author a new **3-file `OrderList` page**
  (`.razor` + `.razor.cs` + `.razor.css`) over a new repo, assert code-behind indexed, `razor:*` refs map back,
  repo graph, regression, isolation, and the **negative that scoped `.css` yields no index symbols**.
- `ClaudeSmokesAuthoringWorkflowTests` — author the 3-file page through the safe-edit workflow as **one coupled
  session** (shared `SessionId`, distinct `StagedRecordId`s); assert new-file creates no watched source, backing store
  byte-identical, and the **per-extension gate**: a broken `.razor.cs` throws before stage while `.razor`/`.css` pass.
- `ClaudeSmokesAuthoringSingleFileTests` — the degenerate single-file authoring case (one new `.cs` via
  new_file→submit→stage), valid C# submits without throwing, no watched source created, backing store untouched.

## Extraction facts confirmed (grep third-eye against the extractor) — NOT new quirks for Codex
The run produced `newQuirks: []`. These are the known behaviors, now cited so future authors skip the grep round-trip
(folded into `docs/claude-skills/RoslynFirstNavigation.md` → "Extraction semantics you can rely on"):
- `inherits_from` is emitted for **both** base class and each interface (any base-list named type).
- `overrides` is emitted **only when the overridden target resolves** → **no `overrides` row** for an override of a
  **generic base** virtual (`RepositoryBase<T>.GetByIdAsync`).
- `ObjectCreationExpression` is captured for a `new` in a **local-variable declaration in a method body** (explicit
  ctor) — **not** field initializers, field-assignment RHS, or compiler-default ctors.
- `ContainingType` is fully-qualified; razor `ReferenceKind` is `"razor:" + node.Kind()`; one `@code` block emits
  several razor refs.
- **Symbol-present ≠ references-extracted**: a body that fails to bind (missing `using`) still indexes its symbol but
  emits **no call sites** (`GetReferencedSymbol` returns null). Assert call sites explicitly.

## Self-awareness (where authoring drifted, and what caught it)
- Trusting prose over the extractor was the recurring failure mode; grep + a throwaway diagnostic `[Fact]` was the
  recurring fix. The ObjectCreation rule ("method body") was too loose in prose — only a runtime row-dump revealed it
  is specifically a *local-variable declaration*.
- The deepest near-miss: a missing `using` left symbols indexed but call-sites silently absent — "the file looks
  processed" almost passed a hollow assertion. Only running the extractor surfaced it.
- Razor refs: assumed 1, actual 7 — assert the *specific* row via `Contains`, never a cardinality you didn't measure.
- Same-name collisions (two `OnLoadClicked`, two `LoadAsync`) forced keying every `Assert.Single` off
  `CallerStableKey`, not `CallerName`.

## Skill-card actions
**Done (pushed):**
- New card `docs/claude-skills/BlazorPageTriadAuthoring.md` — encodes the operator rule: **new Razor = 3-file triad**,
  authored as one coupled session, per-extension validation, scoped `.css` not indexed (only merged). Existing Razor =
  edit as-is.
- Fixed stale `SkillRouter.md` line (it told authors to start new Razor in *two-file* form — now points to the triad).
- Added the cited extraction-semantics subsection to `RoslynFirstNavigation.md` (the token lever: most of this run's
  spend went to the diagnostic-dump round-trips that a citable reference removes).
- Registered the card in `CLAUDE.md`.

**Recommended ENFORCEMENT (Codex/production — the operator's actual intent):**
The triad rule exists so the operator does not have to remember it, and since the AI creates the files it must be
**forced, not documented** (same logic as decision 0003: enforce what the agent may forget — a skill card is only a
reminder). Durable enforcement options, in order of value:
- A scaffold tool (e.g. `new_razor_component`) that atomically creates the three Working candidates (`.razor` +
  `.razor.cs` + `.razor.css`) for a new component, so the agent makes one call and cannot emit a partial triad.
- A stage-time companion check: refuse to stage a **new** `.razor` unless its `.razor.cs` and `.razor.css` are present
  in the same session (mirrors how the safety floor enforces structure rather than trusting recall).
Both live in production (Codex). The `BlazorPageTriadAuthoring.md` card is the interim reminder until one lands.

**Proposed skill cards (not auto-authored — operator/Codex call):**
- `AugmentedSampleIndexProbe.md` — the materialize-and-augment test pattern (CopyTree skip bin/obj → write new source
  into the copy → OpenProjectAsync + SaveSnapshot) with the four extraction gotchas as a checklist.
- `SameSignatureIsolationAssertions.md` — the mean-teacher discipline: key assertions off StableKey, and always pair
  positive checks with regression + negative + isolation checks.
- Note in `SessionOverlayValidation.md` that a coupled stage yields one shared `SessionId` with distinct
  `StagedRecordId`s, and a broken `.cs` throws *before* any staged record exists (assert `File.Exists==false`).

## Coordination
Codex owns production; these are Claude's local tests + shared docs. No safety-floor impact. The samples are Claude's
owned verifiable surface; a future **live-MCP / proxy-hub** batch (the surface Codex's CLI can't reach) will need the
WinForms host running with MCP connected — operator will enable on request.
