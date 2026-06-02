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
