---
status: verified
type: finding
created: 2026-06-03
scope: CMB-parity Phases 2-6 — review verdict + local ClaudeSmokes gate
reviewed-commit: 364044e (main; phases 2-5 + phase 6 per-edit validation + workflow harness sample)
note: ClaudeSmokes TESTS are kept LOCAL (uncommitted) per operator preference; this is their durable record
---

# Phases 2-6 ClaudeSmokes Verification

## Verdict

Reviewed Phases 2-6 (two parity-verify workflows, false-PASS + orphaned-field hunt). **Phases 3, 4, 5, 6 are
wired-real; Phase 2 is 6-of-7 wired.** The pre-merge full-build safety gate is **confirmed unchanged**.

- **Phase 2** — session-scoped staged records, superseded lifecycle, and typed session file tracking are all
  wired-real (populate→consume verified). The one flagged issue — the dirty-unexpected blocking state machine — was
  **over-stated by the read-only review and corrected by running it** (see below).
- **Phase 3** — source-map density/budget/noise-filter wired: `mode` shapes the payload (`ShapeSourceMapFile/Symbol`),
  token budget truncates (`WasTruncated`), noise collapses **with a marker** (`GetElisionReason`).
- **Phase 4** — `get_tool_manifest` reflects over `[McpServerTool]`; `get_staging_guide` composed from shipped docs.
- **Phase 5** — `get_self_check` builds 9 real guardrail rows from live FS/path math; collision row flips on placement.
- **Phase 6** — per-edit `CandidateEditValidator` runs on every candidate write and surfaces structured `id/line/col`
  diagnostics on the edit results, **correctly scoped as feedback, not a gate**: the overlay never throws (a CS0103
  edit still succeeds), only true syntax errors throw (`.cs`-only), and non-C# files are `skipped-non-csharp`. The
  pre-merge full `dotnet build` gate is the single, distinct, unchanged safety mechanism.

## Correction: the dirty-unexpected "silent proceed" claim was wrong

The Phase 2-5 review claimed a dirty-unexpected reject "silently proceeds — the next stage succeeds and supersedes
it." Authoring the ClaudeSmoke and **running it disproved that**: the next stage throws *"Watched file changed since
refresh. Refresh before staging."* — the file **is** protected (by the watched-changed guard). The real residual is
**low/ergonomic, not a safety hole**: the persisted `Status="dirty-unexpected"` is an orphaned field nothing consumes,
and the block message is generic rather than dirty-unexpected-specific. (Third time this session a read-only review
claim got walked back by actually running it.)

## ClaudeSmokes gate (now 12 cases — LOCAL, tests-only, no `src/` edits, `[Trait("Suite","ClaudeSmokes")]`)

New this batch:
- `ClaudeSmokesPhase2DirtyUnexpectedTests` — asserts the real safe behavior (dirty-unexpected file is blocked pending
  refresh, not silently re-staged).
- `ClaudeSmokesHarnessSampleTests` — **automated, CI-portable** index of the in-repo `WorkflowHarnessSample` (no
  external path, no `File.Exists` guard). This is what lets the ground-truth smokes stop depending on external
  `C:\SchemaStudioWebViewer`/`Schema Studio - DBV2` paths.
- `ClaudeSmokesPhase6ValidationTests` (×2) — semantic-error edit surfaces a `CS0103` overlay diagnostic yet still
  succeeds (feedback, not block); non-C# edit is `skipped-non-csharp`.

Plus the Phase-1 set. Result: Data.Tests 6/6, Workflow.Tests 3/3, Integration 3/3 — all green.

## CI-portability + the WinForms/Blazor end-state

The `WorkflowHarnessSample` is the in-repo, CI-portable watched target; the new harness-index smoke makes it an
automated index target. The operator's intended end-state: **meaningful in-repo WinForms and Blazor sample solutions**
(forms + designer files + repository classes; components + `@code` + bindings + repositories) that the full ClaudeSmokes
battery runs against — giving representative, CI-portable ground-truth that fully replaces the external-repo dependency.

## Low items for Codex (not safety)

- dirty-unexpected: consume the persisted `Status="dirty-unexpected"` as an explicit recovery state + give the block a
  dirty-unexpected-specific message (today the generic watched-changed guard protects the file).
- The `--sample-workflow-harness` live smoke `oldText` uses 8-space indent vs the sample's 4-space (review flag); fix
  when the live syntax-rejection mode is hardened.
