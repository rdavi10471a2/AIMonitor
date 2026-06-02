---
status: verified
type: finding
created: 2026-06-02
scope: Phase 1 CMB-parity restore — review verdict + ClaudeSmokes CI-gate verification
reviewed-commit: 6daf517 (main; Phase 1 index richness + outline + the ClaudeSmokes workorder)
workorder: docs/feature-maps/Phase1ClaudeSmokesWorkorder.md
ledger: docs/feature-maps/CmbMcpCapabilityParity.md
note: the ClaudeSmokes TESTS are kept LOCAL (uncommitted) per operator preference; this finding is their durable record
---

# Phase 1 ClaudeSmokes Verification

## Verdict

Phase 1 (index richness) is **genuinely restored — 6/6 items `restored-real`, no false-pass.** The parity-verify
`/workflow` (false-PASS hunt vs the ledger required-proof) confirmed real backing across all four layers (table →
Roslyn extraction → Data derivation → thin MCP shaping): real `symbol_relationships` + `call_sites` tables, callers
resolved via `FindContainingSymbol` (not `ReferenceKind.Contains`), `PathIsUnderFolder` prefix scoping, real Roslyn
outline. The old fakes (`Array.Empty`, kind-substring callers, missing tables) are gone.

## Gap the ClaudeSmokes close

The pre-existing Phase-1 unit/integration tests **hand-seed `MSBuildReferenceSnapshot` rows** and assert they
round-trip — they prove the Data/query/MCP layers but **bypass the extractor**, so they would still pass if extraction
regressed to a stub. The ClaudeSmokes drive the **real `MSBuildWorkspaceLoader` extraction end-to-end** and assert
content, so they **fail if Phase-1 extraction is faked**. They are the CI gate the workorder asked for.

## ClaudeSmokes gate (8 tests — LOCAL only, tests-only, no `src/` edits, `[Trait("Suite","ClaudeSmokes")]`)

| Test | Surface | Proves |
| --- | --- | --- |
| `ClaudeSmokesPhase1IndexTests` (Data.Tests) | hermetic real extraction | relationship kinds + call-site caller identity (`FindContainingSymbol`) + rich references; fails if extraction stubs out |
| `ClaudeSmokesPhase1RazorTests` (Data.Tests) | hermetic Razor | `.razor` `@code` references mapped to source + persisted |
| `ClaudeSmokesPhase1RepositoryShapeTests` (Data.Tests) | **real watched `DatabaseDomainRepository`** | symbol-find locates the real repository class + async methods; reference navigation returns real rows |
| `ClaudeSmokesRepositoryInstanceTests` ×2 (Data.Tests) | **real `SchemaStudioWebViewer` + `Schema Studio - DBV2`** | both real solutions populate every Phase-1 surface at scale (symbols/refs/relationships/call-sites > 0) |
| `ClaudeSmokesPhase1McpTests` ×3 (Integration.Tests) | **standard smoke pattern — real MCP server** | `find_indexed_callers`/`_relationships`/`_references` real rows incl. caller identity + Razor ref; `get_file_outline` Roslyn on `.cs`, refusal on `.razor`; `query_solution_index` folder scope is path-prefix not substring (sibling trap) |

Result: Data.Tests **5/5**, Integration **3/3**. `dotnet build AIMonitor.slnx` green.

## Notes carried forward

- **Razor tooling is fine on this machine** — a standalone Blazor `dotnet build` runs the source generator (no SDK
  download needed). `@code` C# references extract and map back to the `.razor` source. The only non-working path is
  **markup component-attribute binding** (`@bind-Value`) — the documented Razor boundary
  ([[RazorComponentBindingReferences-2026-06-01]]), an intentional limit, not a tooling gap; ClaudeSmokes deliberately
  do not assert it.
- **Authoring surfaces are out of Phase-1 scope.** Phase 1 = index/query/navigation (read). The edit/stage/decision
  workflow is later (Phase 2 session/staging, Phase 6 per-edit feedback) and is already covered by Codex's
  `WorkflowEditServiceSafetyTests` + the `submit_file`/`replace_span` `McpServerSmokeTests`.
- The tests are intentionally **kept local** (not committed). The reproducible authoring recipe is recorded so the
  next phases' ClaudeSmokes can be regenerated.
