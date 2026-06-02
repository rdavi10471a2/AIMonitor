# Phase 1 ClaudeSmokes Workorder

## Goal

Review Phase 1 CMB MCP parity as an external Claude/MCP consumer and add smoke coverage that proves the restored index surfaces are actually usable for safe edit planning.

This is not a general refactor. This is a parity and evidence pass.

## Scope

Inspect:

- `docs/feature-maps/CmbMcpCapabilityParity.md`
- `docs/feature-maps/CmbMcpParityRestorePlan.md`
- `src/AIMonitor.MSBuild`
- `src/AIMonitor.Indexing`
- `src/AIMonitor.McpServer`
- `src/AIMonitor.Workflow`
- existing smoke tests under `tests/`

## Required Coverage

Add ClaudeSmokes-style tests for Phase 1 high/medium parity items:

- indexed symbol relationships
- call sites and true callers
- rich indexed references
- index status/count/staleness reporting
- scoped index query behavior
- `get_solution_index_status`
- `find_indexed_references`
- `find_indexed_callers`
- `find_indexed_relationships`
- symbol/document metadata needed for Claude to safely plan edits

Use representative watched-solution files:

- at least one normal C# file
- at least one Razor page/component

## Razor Boundary

Do not claim full Blazor markup binding semantics.

Razor references are valid only where compiler/Razor source mappings expose clean user-authored source spans. Targeted grep may be used as an external sanity check, but not as the primary implementation truth.

## Failure Conditions

Tests should fail if MCP/index responses are:

- stubbed or placeholder-only
- missing expected top-level IDs/counts
- missing caller metadata where Roslyn should provide it
- too vague for Claude to safely plan an edit
- inconsistent with the documented Phase 1 parity contract

## Constraints

- Do not rewrite architecture.
- Do not add broad abstractions.
- Keep tests externally meaningful.
- If a gap is found, document it and add the smallest test that proves it.
- Preserve the adapter layering: MCP exposes shared workflow/indexing services; it is not the workflow itself.

## Deliverable

A focused test/update pass that proves Phase 1 parity from Claude's point of view.
