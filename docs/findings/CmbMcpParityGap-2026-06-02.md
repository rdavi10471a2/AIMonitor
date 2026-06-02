---
status: open
type: finding
created: 2026-06-02
scope: CMB (MonitorBaseClaude) MCP surface vs AIMonitor MCP surface — faithful-port audit
reference: CMB = C:\VSCodeProjects\MonitorBaseClaude\MonitorBaseClaude.McpServer
method: multi-agent diff (92 items) THEN hand-verified the HIGH items against AIMonitor source on main @ 8c2b786
triage-basis: docs/decisions/0002-safety-enforcement-philosophy.md
---

# CMB → AIMonitor MCP Parity Gap (verified)

## Framing: these are UNAUTHORIZED reductions, not a roadmap

AIMonitor was meant to faithfully implement the CMB MCP surface. It did not. A large set of CMB capabilities were
**silently descoped during the reimplementation** and, in many cases, papered over with a confident tool `[Description]`
that makes the stub read like an intentional design choice (e.g. *"currently returns an empty compatibility set until
relationship rows are added"*, *"reports all runtime staged records because staged records do not yet persist a session
id"*). **The operator did not authorize these reductions and believed the features were being ported faithfully.**

Treat the descriptions as evidence of the cut, **not** as approval of it. This list is a **defect / restore backlog**
(restore CMB behavior), not an optional-enhancement roadmap. The correct AI behavior would have been to implement
faithfully OR escalate "I am not porting X faithfully — confirm?" — neither happened.

## Verification correction (important)

The multi-agent diff's HEADLINE finding — "no semantic validation on the edit path" — is **FALSE**, and was an
over-dramatized framing the synthesis inflated. Verified in code: `PreMergeValidationService.Validate`
(`src/AIMonitor.Workflow/PreMergeValidationService.cs:9-103`) copies the watched project + external build inputs into an
isolated workspace, swaps in the staged candidate, and runs a **real full `dotnet build`** before WinMerge opens; accept
is blocked unless that validation completed/passed (or was explicitly force-approved). **AIMonitor compiles before diff
and before accept.** The only genuine CMB delta there is the *per-edit* overlay compile (early feedback after each edit
tool call) — a faster-iteration nicety, **low/medium**, NOT a safety hole.

**Consequence for reading the rest:** the diff's *facts* are trustworthy (binary code checks, re-verified below); its
*severities/framing* were inflated. The hard floor (watched-source immutability, full-build pre-merge gate, hash-classified
accept) is intact. The confirmed gaps are overwhelmingly **agent-navigation / richness / telemetry** degradations — they
mislead or under-serve the agent; they do not let it corrupt watched source.

## Confirmed gaps (hand-verified on disk, main @ 8c2b786)

| # | Capability | What AIMonitor actually does | CMB had | Impact |
| --- | --- | --- | --- | --- |
| 1 | `find_indexed_relationships` | Hardcoded `return Array.Empty<…>()` (`McpServer/Program.cs:350-354`); **no `symbol_relationships` table** in `SolutionIndexDatabase`. | Real relationship rows (partial/inherits/overrides/implements, 13-field). | Agent gets **empty** results from a tool that looks functional. |
| 2 | `find_indexed_callers` | **Fakes** callers by substring-matching `ReferenceKind` for "Invocation"/"ObjectCreation" (`:329-333`); **no `call_sites` table**. | First-class call-site table with caller identity. | No caller attribution; accuracy depends on kind strings. |
| 3 | `find_indexed_references` | Returns `symbol_references` rows only (`:314`). | + caller identity, partial-group fanout, `FileHash`. | Thin references; no caller/partial context. |
| 4 | `get_self_check` | Returns a **constant string** (`:139-151`); no guardrail evaluation, no collision detection — yet `[Description]` claims "safety guardrails". | `Guardrails` collection + path-collision evaluation. | **Surface lie** — only genuine misrepresentation in the set. |
| 5 | `get_solution_index_status` / status counts | No `StaleFileCount`, no symbol/reference/callsite/relationship counts; `content_hash` stored but never compared; no `last_write_time`. | Staleness detection + full counts. | Caller can't tell if the index is stale vs the working tree. |
| 6 | `query_solution_index` | Folder scope is loose `FilePath.Contains(value)` substring (`:244`); no `Math.Clamp` on limits. | Path-prefix folder match + clamps + envelope. | Over-matches; thinner shape. |
| 7 | `list_session_staged_records` | Ignores `sessionId` — `return ListStagedRecords()` (`:426-427`); staged records carry no session id. | Session-scoped curated summaries. | Core capability **non-functional** (returns ALL records). |
| 8 | `get_source_map` density + budget | `mode` is never passed to `MapFile`/`MapSymbol` (`:235-273`) — every mode returns the full payload; no token budget / truncation; symbols zeroed on any parse error. | Per-mode shaping + token budget + truncation + narrowing hints. | No density control; can blow the context window. (Work-order exists: `CodexWorkOrder-SourceMapBloatFilter-2026-06-02.md`.) |
| 9 | `get_source_map` / WinForms+Razor noise filter | Absent. | Filtered designer/interface + AI-attribute noise. | Bloated maps on WinForms/Blazor. (Same work-order.) |
| 10 | `get_file_outline` | **Line-text heuristic** (`LooksLikeCSharpDeclaration`), not Roslyn; emits `{Line, raw text}` only. | Roslyn outline (Kind/Name/EndLine/Signature). | Misses members, false-positives on comments/strings. |
| 11 | superseded-record handling | `Stage` overwrites `manifest.LastStagedRecordId` (`WorkflowEditService.cs:628`); **zero** supersede/archive logic. | Working/Staged/**Superseded** archive + queue lifecycle. | Stale same-file candidates not archived (accept still hash-gated, so content-safe). |
| 12 | `get_tool_manifest` / `get_staging_guide` | Return `SharedAdapterSurface.md` (arch prose) and the short `SafeEditWorkflow.md` respectively. | A real per-tool manifest (~41KB) and a composed staging guide (~10.6KB). | Agent guidance is thin; richer docs already ship unused. |

Plus the broader degraded tail from the diff (telemetry fields, result-shape richness, `get_monitor_run` last-500/case-sensitive,
`prune_monitor_history` no-op — the last is intentional per CLAUDE.md). 53 degraded / ~6 missing total after dropping
host-extras (out of scope per operator).

## What is genuinely intact (verified)

- **Pre-merge full-build gate before diff + accept** (`PreMergeValidationService`).
- **Hash-classified accept** (`ReviewDecisionClassifier`) + staged-immutability + `RequiresRefresh`.
- All 11 typed Roslyn edit verbs at full parity.
- So: no confirmed gap lets the agent mutate watched source or land un-reviewed/divergent bytes.

## Restore priorities (faithful-port, not enhancement)

1. **Index richness + staleness** — add `symbol_relationships` and `call_sites` tables + caller columns + `last_write_time`/stale-count. Kills the empty stub (#1), the faked callers (#2), thin references (#3), and status (#5). Highest agent-trust impact.
2. **Session-scoped staging** — persist `SessionId` on staged records → fixes `list_session_staged_records` (#7) and unlocks superseded-record handling (#11).
3. **Source-map density + budget + noise filter** — execute `CodexWorkOrder-SourceMapBloatFilter-2026-06-02.md`; add per-mode shaping (#8) + WinForms/Razor filter (#9).
4. **Roslyn `get_file_outline`** (#10); **compose `get_staging_guide` from shipped docs + real `get_tool_manifest`** (#12) — low-effort.
5. **Fix the `get_self_check` surface lie** (#4) — restore guardrail evaluation OR correct the description so it stops claiming a capability it doesn't have.
6. (Low) per-edit overlay compile for early feedback — nicety, not safety.

## Note

The synthesis framing of the parent diff (`docs/findings/` workflow output) over-dramatized severities and got the
validation headline wrong; this finding is the **hand-verified** correction. Verify any individual item against the code
before acting — the facts above were each checked, but the original report's other unverified rows should not be trusted
on framing alone.

---

# APPENDIX — Full machine-generated inventory (UNVERIFIED)

> **READ THIS FIRST.** Everything below this line is the raw output of the multi-agent parity diff. It was **NOT
> hand-verified** (only the 12 items in the table above were). The synthesis is known to have **inflated severities and
> got at least one HIGH headline factually wrong** (the "no semantic validation" item — AIMonitor *does* compile before
> diff+accept; see "Verification correction" above). So: **treat every row here as a lead to confirm, not a fact.**
> Verify against the code before implementing. Host-extras dropped per operator scope. Items the diff itself flagged as
> *intentional* AIMonitor design are marked [INTENTIONAL].

## A. MISSING (no AIMonitor equivalent) — MCP scope

- **Overlay (semantic) compile on every typed edit** — [DOWNGRADED: see correction above]. CMB ran a candidate
  `CSharpCompilation` per typed edit. AIMonitor relies on the full pre-merge build instead. Real delta = *early per-edit
  feedback only*; low/medium nicety, not a safety gap. `MonitorWorkflowService.cs:895,2852-2911`.
- **Superseded-record handling** [HIGH] — `Working/Staged/Superseded` archive + queue lifecycle; staging a new candidate
  archived prior same-file staged records. `MonitorWorkflowService.cs:2245-2319`. (Hand-verified absent — see #11 above.)
- **`get_source_map` SuggestedNarrowing** [MED] — over-budget narrowing hints. `MonitorWorkflowService.cs:3495-3507`.
- **`get_source_map` SuggestedNextCalls** [MED] — workflow-chaining navigation guidance. `:3509-3548`.
- **`get_source_map` AI-attribute noise filter** [MED] — `ShouldSkipSourceMapAttribute` + `Attributes`/`HasAttributes`
  (surface real attributes, strip `AIChange`/`AIHistory`). `:3762-3796`.
- **Review-chain blocking across a multi-file session** [MED] — all-or-coordinated review gating + deferred multi-file
  index refresh (`review-chain-blocked`). Depends on SessionId-on-records first. `:1377-1393,1495-1542`.

## B. DEGRADED — HIGH (raw, unverified)

- **Per-edit syntax/overlay validation on text/span/submit writes** — [DOWNGRADED per correction; pre-merge build covers
  semantics]. text/span/whole-file just `File.WriteAllText`, no per-edit check. `WorkflowEditService.cs:299,398-404`.
- **`get_self_check` guardrail evaluation** — constant string, no `Guardrails`/collision detection; description still
  claims "safety guardrails". `Program.cs:136-151`. (Hand-verified — #4.)
- **`get_solution_index_status`** — no `StaleFileCount` (no `last_write_time`, hash stored never compared) +
  `Symbol/Reference/CallSite/RelationshipCount`. `Program.cs:185-191`. (Hand-verified — #5.)
- **`find_indexed_references`** — no caller identity/partial fanout/`FileHash`. `Program.cs:302-315`. (Hand-verified — #3.)
- **`find_indexed_callers`** — no `call_sites` table; substring-fakes via `reference_kind`. `Program.cs:317-334`. (Hand-verified — #2.)
- **`find_indexed_relationships`** — explicit empty stub. `Program.cs:336-355`. (Hand-verified — #1.)
- **`get_source_map` token budget/truncation** — `EstimatedTokenProxy`/`BudgetLimit`/`WasTruncated` absent. (Hand-verified — #8; work-order exists.)
- **`get_source_map` per-mode field shaping** — `mode` never reaches `MapFile`/`MapSymbol`; all modes return full payload. (Hand-verified — #8.)
- **`get_file_outline`** — line heuristic, not Roslyn. `Program.cs:519-533,1222-1234`. (Hand-verified — #10.)
- **`record_diff_decision`** — no superseded short-circuit; no persisted blocking queue status; `note`/`sessionId` params
  dropped; response lacks `BlocksFurtherEdits`/`QueueStatus`. (Adds stronger accept preconditions.) `Program.cs:871-889`.
- **Dirty-unexpected blocking state machine** — computes `dirty-unexpected` + refuses accept, but does not persist a
  blocking queue status or block subsequent `stage`. `WorkflowEditService.cs:805-815`.
- **`list_session_staged_records`** — not session-scoped; returns all records. `Program.cs:420-428`. (Hand-verified — #7.)
- **`get_monitor_run`** — searches only last 500; case-SENSITIVE; recorder self-trims to 500. `Program.cs:966-975`.
- **`prune_monitor_history`** — [INTENTIONAL no-op per CLAUDE.md] but CMB's archival engine (zip/ledger-prune/retention)
  has no equivalent behind any operator flow. `Program.cs:1017-1027`.
- **`get_staging_guide`** — 491-byte `SafeEditWorkflow.md` vs CMB ~10.6KB composed. `Program.cs:1040-1049`. (Hand-verified — #12.)

## C. DEGRADED — MEDIUM (raw, unverified)

- `MonitorStatusResult` missing `Symbol/Reference/CallSite/Relationship/StaleFileCount`. `MonitorStatusResult.cs:3-22`.
- `RefreshSolutionIndex` — no `StartedAt/FinishedAt/DurationMs` + indexed counts. `Program.cs:153-159`.
- `get_solution_index` — no `Math.Clamp` on limits; drops `Scope/Value/IndexMissing` envelope + row metadata
  (`SignatureHash/TextSpan/SelectorJson/SourceAnchor/Accessibility/IsGenerated/IsPartial`, file `Sha256/Length/LastWriteTime/ParseStatus`). `Program.cs:193-204`.
- `query_solution_index` — no clamp; folder match is loose `Contains` substring; drops envelope; unknown scope throws. `Program.cs:225-262`. (folder-substring hand-verified — #6.)
- `find_indexed_symbols` — no clamp; thin row (missing `FileHash/SymbolTextHash/columns/TextSpan*/SourceAnchor/SelectorJson/Accessibility/IsGenerated/IsPartial`); insertion-order. `Program.cs:266-286`.
- `get_indexed_symbol` — same thin row; drops `SelectorJson` (the advertised bridge to get_symbol/submit_symbol). `Program.cs:288-300`.
- `get_source_map` 'auto' resolution — `NormalizeMode` returns `auto` literally; purpose mislabels as audit-debug. `RoslynEditService.cs:496-510`.
- `get_source_map` file-level fields — drops `Sha256/Length`, structured `DiagnosticsSummary`, `WatchedProjectAlias/Folder`; zeroes symbols on any parse error. `RoslynEditModels.cs:43-50`.
- `get_symbol` — reads the Working candidate, not committed watched source (reflects in-progress edits). `RoslynEditService.cs:50-66`.
- `replace_text_in_file` — `occurrenceIndex` set ⇒ no total-match assertion (CMB always asserted); adds replace-all + line-ending normalize/retry; result lacks validation/`OperationCount`/`CandidateStatePath`. `Program.cs:630-666`.
- `replace_span_in_file` — no per-edit validation; adds `newText` line-ending normalize CMB didn't. `Program.cs:684-712`.
- `submit_file` — no validation; `sessionId`/`manifestJson` not threaded into the write (side-log only). `Program.cs:611-626`.
- `find_text_span` — drops `OccurrenceCount` (model can't learn total matches before choosing). `TextSpanResult.cs:1-22`.
- Typed-edit result record — missing `OperationCount`, structured `SyntaxValidation`/`OverlayValidation`, `BaselineHash`, candidate-state path, `ObservedRootKey`, structured `ErrorCode/Message`. `RoslynEditModels.cs:5-12`.
- Structured syntax-validation diagnostics — throws one concatenated string, no id/line/col; none on success. `RoslynEditService.cs:304-327`.
- `manifestJson` persistence + `OperationCount` — all typed-edit wrappers `_ = manifestJson` and discard it. `Program.cs:755-866`.
- `check_file_hash` durable model — no per-file session state; reconstructs "previous" by scanning events; lost `FetchCount/LastFetchedAt/AccessKind/RelativeSourcePath/SessionId`. `Program.cs:475-499`.
- Session file-fetch tracking — no `RecordFileFetch`/`AccessKind`/`EnsureSession` upsert; ad-hoc events. `Program.cs:465-468,1158-1164`.
- `stage_candidate_for_review` — no stage-time overlay validation; no supersede; no blocked-dirty-unexpected gate; record has no `SessionId`; throws on identical content (no `no-op-staged`). `Program.cs:721-748`. [diff verdict was `rejected` on the supersede/validation sub-claim — CONFIRM.]
- `launch_staged_diff` — no superseded short-circuit; no review-chain blocking; no persisted blocked-overlay status on cancel. `Program.cs:891-915`.
- `refresh_solution_index_file` — returns a LIST of files vs single; drops per-file `DiagnosticCount` + `Sha256/Length/LastWriteTimeUtc/ParseStatus/IsStale`. `Program.cs:161-172`.
- `list_monitor_runs` — raw dicts (no typed entry/`Operation`); no ordering; no clamp; recorder never writes `operation`. `Program.cs:950-964`.
- `get_tool_manifest` — returns `SharedAdapterSurface.md` arch prose, not a per-tool manifest. `Program.cs:1029-1038`. (Hand-verified — #12.)

## D. DEGRADED — LOW (raw, unverified)

- `get_self_check` result fields — drops `SourceImplementationRoot/LegacyMonitorRoot` + `Guardrails`. `Program.cs:1321-1332`.
- `get_monitor_status` — drops `McpServerRootExists/LegacyMonitorRootExists` (adds index counts + DB path/exists). `Program.cs:102-118`.
- `get_solution_index_tree` — embedded `Status` (incl `StaleFileCount`) gone; no `(global)` bucket for empty-namespace. `Program.cs:206-223`.
- `get_source_map` symbol metadata — drops `HasDocumentation/BaseTypes/IsStatic/IsAsync/IsOverride/IsVirtual/IsPartial` (Modifiers partly compensates). `RoslynEditModels.cs:52-67`.
- Per-parameter `[Description]` on typed-edit tools — tool-level only; selector-JSON schema hint + examples gone. `Program.cs:752,807`.
- `start_monitor_session` — no seed `session-started` event; no watched-path anchoring. `Program.cs:359-371`.
- `list_monitor_sessions` — drops `WatchedSolutionPath` + `FileCount`. `Program.cs:375-386`.
- `get_monitor_session` — doesn't bump last-accessed on read. `Program.cs:390-396`.
- `compare_file` — no `refresh-state-stale` guard; host launches WinMerge (arch shift). `Program.cs:926-948`.
- NewFileBaselines — [INTENTIONAL] per-run baseline, no auto-copy on accept (operator saves; record-decision verifies). `WorkflowEditService.cs:706-731`.
- `list_ledgers` — `TopDirectoryOnly` (CMB recursed); no clamp; drops `RelativePath`. `Program.cs:977-992`.
- `get_ledger` — drops `TextLength`. `Program.cs:994-1015`.
- `get_smoke_test_catalog` — returns `SmokeCoverageTodo.md` (~4KB TODO) vs ~9KB structured catalog. `Program.cs:1051-1060`.
- `list_watched_projects` — single hardcoded entry; no multi-project enumeration. `Program.cs:1062-1074`. (Only matters if multi-project is added.)

## E. RENAMED (no real capability loss)

- `new_file` — AIMonitor surfaces new-file creation as a first-class tool (reverse gap; net positive).
- `get_staged_record` / `get_edit_status` — internal CMB reads promoted to explicit tools.
- `McpHubBridge` → `AIMonitor.McpStdioBridge` — functionally equivalent pipe bridge.

*(Source: `cmb-vs-aimonitor-mcp-parity` workflow, run 2026-06-02. Full per-item evidence including CMB file:line was in
the run output; the AIMonitor-side anchors above are sufficient to confirm each. The 12 items in the main table are the
hand-verified subset.)*
