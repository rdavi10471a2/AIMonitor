---
status: new
type: finding
created: 2026-06-01
scope: end-to-end evaluation of the AIMonitor safe-edit workflow, from lived session use
confidence: high (based on direct multi-cycle use, not inspection)
related: CliVsMcpAdapterReview-2026-06-01.md, WorkflowCostAndIncrementalRebuild-2026-06-01.md, RazorComponentBindingReferences-2026-06-01.md
---

## Summary

Evaluation of the safe-edit workflow as actually exercised on 2026-06-01 across a real session: a two-file Razor page
(`DatabaseDomainTest.razor` + `.razor.cs`) on `SchemaStudioWebViewer.sln`, an earlier WinForms page + routing edit on
`Schema Studio.sln`, Roslyn-first navigation, reference/caller probing, a watched-solution switch, and a hub restart.

**Verdict:** the workflow does its core job well — it never permitted direct watched-source mutation, every change
compiled before human review, and decisions are hash-verified and durable. The safety model is sound. The problems are
ergonomics and per-cycle cost, plus one real correctness gap (razor component-binding references). For a V0.1 it held up
under a genuine multi-cycle, multi-file, cross-solution session. The skeleton is right and safe; it needs the engine to
own the gate, go incremental, and tighten sharp edges — not a redesign.

## What worked (earned confidence)

- **Safety invariant held end-to-end.** `new_file`/`refresh_file` -> Working candidate -> `stage_candidate_for_review`
  -> pre-merge validation gate -> WinMerge -> hash-classified `record_diff_decision`. No path to mutate watched source
  directly; the project `deny` rule backstopped it.
- **The validation gate is the strongest feature.** A full-solution build before WinMerge proved both new pages compiled
  (including Radzen markup against the code-behind) before review — real pre-merge assurance, not a diff-shape guess.
- **Roslyn-first navigation is genuinely token-cheap** when used correctly: `get_source_map` (selector) + `symbol:` keys
  located the repository APIs and member structure without whole-file reads.
- **Hash classification + accept verification** behaved exactly as designed: `accepted-normalized` correctly flagged the
  LF->CRLF normalization instead of silently accepting.

## Friction (all observed live)

## Resolution update - 2026-06-01

Addressed after this evaluation:

- Stale edit sessions now return clearer recovery guidance that names refresh/start-session expectations.
- Per-file workflow manifests use advisory locks around read-modify-write paths.
- `submit_file` preserves the Working candidate's dominant line endings, reducing accidental normalized-only accepts.
- Wrong indexed key shapes now produce visible guidance instead of silent empty reference/caller results.
- Razor markup passed to Roslyn source-map/symbol tools now returns visible guidance to use `.razor.cs` or text/file tools.
- Engine-owned pre-merge validation is shared by CLI and MCP launch paths.
- Compact staged responses plus explicit staged-record fetch-back reduce repeated transcript payloads.

Still deferred by design:

- Dependency-aware/incremental build and index.
- MCP elicitation.
- First-class coupled multi-file accept/validation as one unit.

1. **Stale session after solution switch.** `new_file` errored until a fresh `start_monitor_session` was created; the old
   handle was bound to the previous watched solution. Error was opaque ("An error occurred invoking 'new_file'").
2. **No manifest lock.** Parallel `new_file` calls errored; had to serialize. Matches the "non-atomic / last-writer-wins"
   item in the CLI/MCP review.
3. **Coupled two-file pages are manual.** `.razor` + `.razor.cs` are mutually dependent, so to keep each validation pass
   compilable I placed `[Inject]` in the code-behind and accepted it first, then the markup. The per-file path forced the
   ordering by hand; `SessionOverlayValidation` exists but the happy path did not use it.
3a. Authoring note: the standalone `.razor.cs` compiles as a partial `ComponentBase` even before the `.razor` exists,
    which is what made the sequential-accept ordering viable.
4. **Slow, every cycle.** Full-solution build on each `launch_staged_diff` plus a full index rebuild on each accept
   (10-21s each, measured). No incremental build or index. See WorkflowCostAndIncrementalRebuild-2026-06-01.md.
5. **`accepted-normalized` every time.** `submit_file` writes raw LF; the watched projects are CRLF. Benign but constant
   churn; honoring existing EOL on new files would remove it.
6. **Silent `[]` on wrong key format.** `find_indexed_references`/`find_indexed_callers` need the `symbol:<hash>` keys
   from `find_indexed_symbols`/`query_solution_index`/`get_indexed_symbol`; passing the `::`-path selector keys from
   `get_source_map` returns empty with no error. Cost real investigation time.
7. **Opaque tool errors.** `get_symbol`/`get_source_map` return "An error occurred" when there is no Working candidate or
   when pointed at `.razor` markup, with no actionable detail.

## Correctness gap (filed separately)

- **Razor component-attribute binding references are not indexed.** C#-block references index; `@bind-Value`/`Click=`/
  component-parameter references do not, and the source-generated path is wired to the wrong Roslyn API. Full detail and
  fix in RazorComponentBindingReferences-2026-06-01.md.

## Priorities (by leverage)

1. **Engine-owned validation gate + incremental build/index.** Fixes the safety-enforcement gap (engine currently trusts
   an adapter `launched` bool — see CLI/MCP review) AND the per-cycle latency in one stroke. Highest leverage; touches
   every cycle.
2. **Razor path-2 fix** (`GetSourceGeneratedDocumentsAsync`) — restores a whole capability class for two-file/component
   pages.
3. **Ergonomics**: clear "stale session / wrong watched solution" error on solution switch; advisory manifest lock; honor
   EOL on `submit_file`; distinguish empty-result from error on key mismatch; richer error text on `get_symbol`/
   `get_source_map` failures.
4. **First-class coupled multi-file staging** for `.razor` + `.razor.cs` (and partial-class sets) so mutually dependent
   new files validate and accept as a unit instead of by manual ordering.

## Notes

- This evaluation is observational (lived use), not a static audit; it complements the three related findings rather than
  restating them. Where a point is covered in depth elsewhere, the related doc is cited.
- Token-cost observations and the index-rebuild timings are in WorkflowCostAndIncrementalRebuild-2026-06-01.md.
