---
status: open
type: work-order
created: 2026-06-02
audience: Codex
baseline-commit: f67619a (main; build green 0/0)
scope: port CMB's source-map bloat filter (WinForms + Razor generated noise) + optional size cap
watched-targets: SchemaStudioWebViewer (Blazor/Razor) and the SchemaStudio WinForms line
triage-basis: docs/decisions/0002-safety-enforcement-philosophy.md
---

# Codex Work Order — Source-Map Bloat Filter (WinForms + Razor)

## Context

The prior monitor (CMB / MonitorBaseClaude) filtered the source map to drop **common Windows Forms interface/designer
boilerplate** so a form's map wasn't drowned in framework noise. That filter was **not ported to AIMonitor**. The real
watched targets make this matter on two fronts:

- **WinForms** (the SchemaStudio desktop line): designer-generated `InitializeComponent`, designer field blocks,
  `Dispose(bool)`, and `ISupportInitialize`/`IComponent`/`IContainer` plumbing bloat the map.
- **Blazor/Razor** (SchemaStudioWebViewer — the *real* viewer): the source-generated render plumbing
  (`BuildRenderTree(RenderTreeBuilder __builder)`, `__builder.*` calls, generated lifecycle) is the analogous bloat.

## Ground truth (verified on `f67619a`)

- `RoslynEditService.GetSourceMap` (`src/AIMonitor.Workflow/RoslynEditService.cs:30-48`) resolves files via
  `ResolveSourceMapFiles`, then `files.Select(MapFile)` builds each `RoslynSourceMapFile` (its `.Symbols` list). **No
  member filtering happens** — every declaration is mapped.
- MCP `get_source_map` (`src/AIMonitor.McpServer/Program.cs:535-561`) exposes a **density `mode`**
  (`auto`/`navigation`/`selector`/`detail`/`full`) — this reduces *volume* but does **not** strip WinForms/Razor noise.
- `get_solution_index`/`query_solution_index` have `maxFiles`/`maxSymbols` caps; `get_source_map` has **no size cap**.

So the bloat-control today is density modes + index-query caps only. The targeted noise filter and a source-map size
cap are both missing.

## Goal

Add **noise filtering** to the source map so orientation maps show user-authored structure, not framework/generated
boilerplate — for both WinForms and Razor — plus an optional hard size cap as a backstop. Engine-owned in
`RoslynEditService` so MCP and CLI both benefit. **Never drop silently:** collapse filtered runs into a single
placeholder entry that states what/how much was elided (the no-silent-cap rule).

## Tasks

### Task A — WinForms designer/interface filter
In `MapFile` (the per-file symbol enumeration), detect and **collapse** (not silently drop) designer/interface
boilerplate:
- `InitializeComponent()` method body.
- Designer-generated field block (the control/`components` fields).
- `Dispose(bool disposing)` designer override.
- Explicit WinForms interface plumbing: `ISupportInitialize.BeginInit/EndInit`, `IComponent`/`IContainer` members.

Detect designer-generated code robustly, not by name alone: the `*.Designer.cs` partial file, the
`#region Windows Form Designer generated code`, and `[GeneratedCode]`/`[DebuggerNonUserCode]` attributes; use
`InitializeComponent` by name as a fallback. Replace each collapsed run with one placeholder symbol entry, e.g.
`InitializeComponent (designer-generated, 412 lines, filtered)`, so the map records the elision.

### Task B — Razor generated render-plumbing filter
Filter the source-generated Razor render noise while keeping user-authored code:
- Exclude `BuildRenderTree(RenderTreeBuilder __builder)` and the `__builder.*` render calls, generated lifecycle, and
  members originating in `*.razor.g.cs` / source-generated documents.
- **Keep** user-authored `@code` members and `.razor.cs` code-behind members.
Respect AIMonitor's conservative Razor boundary (`docs/architecture/Architecture.md` "Razor Boundary";
`docs/findings/RazorComponentBindingReferences-2026-06-01.md`) — this is map *presentation* filtering, not a change to
what is indexed. Collapse, don't silently drop.

### Task C — density-mode awareness / override
The filter should be **on by default for orientation modes** (`auto`/`navigation`/`selector`) and **overridable** so an
agent that genuinely needs the generated code can get it: either honor `mode=full`/`detail` to include everything, or
add an explicit `includeGenerated` (default false) parameter on `GetSourceMap` + the MCP `get_source_map` tool. Document
which knob wins.

### Task D — (optional) source-map size cap
Add a `maxChars` (or symbol) budget backstop to `get_source_map`. On overflow, truncate and append a marker entry
stating how many symbols/chars were elided (no silent truncation). Default high enough not to bite normal use.

## Where it slots

- `src/AIMonitor.Workflow/RoslynEditService.cs` — `MapFile` (filter), `GetSourceMap` signature (mode/`includeGenerated`).
- `src/AIMonitor.Workflow/RoslynEditModels.cs` — `RoslynSourceMapFile`/`RoslynSourceMapResult`: add an `elided`/`filteredCount` field so callers see what was filtered.
- `src/AIMonitor.McpServer/Program.cs` — `GetSourceMap` tool: expose the knob (and `maxChars` if Task D).
- Engine-owned → CLI source-map paths inherit it automatically.

## Tests

- **WinForms fixture:** a temp form + `*.Designer.cs` with `InitializeComponent` + designer fields → map omits/collapses
  the boilerplate (placeholder present with a line count) and **retains** user-authored members. A `full`/`includeGenerated`
  call returns the boilerplate.
- **Blazor fixture:** a `.razor` (+ `.razor.cs`) component → generated render plumbing filtered, `@code`/code-behind
  members retained.
- **Mode test:** `navigation`/`auto` filters; `full`/`includeGenerated` does not.
- **Size-cap test** (if Task D): oversized map truncates with an elision marker.

## Contract docs (system-memory rule — same change)

- `docs/components/AIMonitor.Workflow.md` — note the source-map filter + the override knob.
- `docs/architecture/Architecture.md` — one line under the source-map/Razor boundary that orientation maps filter
  WinForms/Razor generated noise by default.
- If this is treated as a behavior contract, note it in `docs/system-memory/README.md`.

## Acceptance criteria

- `dotnet build AIMonitor.slnx -c Debug` → 0/0.
- New WinForms + Blazor filter tests pass; `full`/`includeGenerated` round-trip test passes.
- Nothing is dropped silently — every collapse/truncation leaves a marker stating what was elided.
- Filter is engine-owned (shared by MCP + CLI); `forceValidation` and watched-source safety untouched (n/a here).

## Notes

- This is a map *quality/legibility* feature, aligned with decision 0002's "review-bandwidth amplification" frontier —
  give the agent/operator signal, not generated noise — without touching the hard floor.
- Rationale for "collapse, don't drop": a silently shorter map reads as "this file is simple" when it isn't; the
  placeholder keeps the map honest about what's hidden.
