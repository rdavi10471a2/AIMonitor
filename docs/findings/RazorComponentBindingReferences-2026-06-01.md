---
status: addressed-in-code
type: finding
created: 2026-06-01
scope: solution-index reference extraction for Razor component-attribute bindings (two-file pages)
confidence: high on repro/symptom; medium-high on the exact code fix
watched-target: C:\SchemaStudioWebViewer V 1.1 - Monitor\SchemaStudioWebViewer.sln (Blazor)
---

## Summary

Resolution update - 2026-06-01:

- `MSBuildWorkspaceLoader` now feeds the Razor source-generated reference path from
  `Project.GetSourceGeneratedDocumentsAsync()` in addition to any generated Razor trees already present in the
  compilation.
- The generated-tree path de-duplicates trees already present in the compilation before adding missing trees for
  semantic binding.
- Added a regression test for a two-file Razor component with a component `@bind-Value` markup reference back to a
  code-behind property.

Indexed references from `.razor` **markup** back to user symbols work for **C# in `@code`/expression blocks**, but
**not for component-attribute bindings** (`@bind-Value="X"`, `Click="@Method"`, component parameters). A newly authored
two-file Razor page (`.razor` + `.razor.cs`, no `@code`) therefore indexes its **declarations** correctly but records
**no markup references** to its code-behind members. A full `dotnet build` + full index rebuild does **not** fix it.

This is not removed/in-transit code — both razor reference paths are present in
`AIMonitor.MSBuild/MSBuildWorkspaceLoader.cs`. The gap is in *how* they resolve component bindings.

## Repro (live, 2026-06-01)

1. Added `Components/Pages/DatabaseDomainTest.razor` + `.razor.cs` via the safe-edit workflow (both accepted; index
   rebuilt to 110 docs; pre-merge full-solution build passed).
2. Probes (using the correct `symbol:<hash>` keys from `find_indexed_symbols`, **not** the `::`-path keys from
   `get_source_map`):
   - `find_indexed_references` on `RunTestAsync` (used only via `Click="@RunTestAsync"`) → **`[]`**.
   - `find_indexed_references` on `SelectedDatabaseId` (markup `@bind-Value` + code-behind) → **3 refs, all in `.razor.cs`**, none for the markup binding.
   - `find_indexed_callers` on `DatabaseDomainRepository.GetByDatabaseIdAsync` → C# call from our `.razor.cs`, plus
     `razor:InvocationExpression` from **existing** files (`DomainObjectEditor.razor`, `ManageDatabases.razor`,
     `ParserLab.razor`) — but **nothing from our new `.razor`**.
3. Manual `dotnet build` (generated output now in `obj/`) + `refresh_solution_index` → **still `[]`** for the markup refs.

Note the existing `razor:` reference snippets are all C# statements (`Domains = (await ...GetByDatabaseIdAsync(...))`)
i.e. `@code`/expression-block C#, never component-attribute bindings.

## Root cause

`ProjectSymbolIndex.BuildReferencesAsync` runs two razor paths:

1. **`AddRazorReferences`** (`MSBuildWorkspaceLoader.cs:700-744`) — re-generates each `.razor` with the monitor's *own*
   `RazorProjectEngine.Create(RazorConfiguration.Default, fileSystem, …)` (`:1265-1268`). This engine has **no TagHelper /
   component descriptors** (no Radzen, no Blazor component metadata). Component attributes (`@bind-Value`, `Click=`,
   parameters) are only lowered into C# member references when the component's descriptor is known; without it they stay
   inert markup. So C#-block references are captured, component-binding references are not. This is what produced the
   existing files' `razor:` refs (their refs live in `@code`/expression C#) and what drops our new page's bindings.

2. **`AddSourceGeneratedRazorReferences`** (`:746-784`) — intended to catch the real generator output (the actual
   `RazorSourceGenerator` *does* know the components). But it enumerates
   `compilation.SyntaxTrees.Where(IsSourceGeneratedRazorTree)` (`:753`). **Source-generated documents are not part of
   `compilation.SyntaxTrees`** in Roslyn; they are exposed via `project.GetSourceGeneratedDocumentsAsync()` (or a
   `GeneratorDriver`). So this loop iterates an empty set and contributes nothing — the path is effectively dead.

Net: neither path resolves component-attribute bindings, so two-file pages whose only markup→code references are
component bindings record zero markup references.

## Why a build + reindex didn't help

`PostAcceptIndexRefreshService.RebuildAfterAcceptedDecision` (`AIMonitor.Indexing/PostAcceptIndexRefreshService.cs:35-37`)
calls `SolutionIndexBuilder.RebuildAsync` → `MSBuildWorkspaceLoader.OpenSolutionAsync`, which only reads
`project.GetCompilationAsync()` (no generated docs). The post-accept step is **index-only**; it never builds the watched
project, and even when the project *is* built, the generated trees still aren't read (path-2 uses the wrong API). So the
operator's expectation of "a full project rebuild of the watched project after accept" is both (a) not currently happening
and (b) insufficient on its own to surface these refs without the path-2 API fix.

## Suggested fix

Primary (makes component-binding references work):

- In `AddSourceGeneratedRazorReferences`, source the generated trees from
  `await project.GetSourceGeneratedDocumentsAsync(cancellationToken)` instead of
  `compilation.SyntaxTrees.Where(IsSourceGeneratedRazorTree)`. Build the semantic model from the **generator-augmented**
  compilation (`generatedDoc.Project.GetCompilationAsync()` / the document's `GetSemanticModelAsync()`), then keep the
  existing `GetMappedLineSpan` → `IsUserRazorMappedSpan` mapping to land references on the user `.razor`/`.razor.cs`.
  This requires the MSBuild workspace to load the Razor source generator (it does, via analyzer references) and to
  actually run it (Roslyn runs generators on demand for `GetSourceGeneratedDocumentsAsync`).
- Thread `project` (not just `compilation`) into `AddSourceGeneratedRazorReferences` so the generated-docs API is reachable.

Secondary / supporting:

- Give the monitor's standalone `RazorProjectEngine` (path-1) the project's TagHelper descriptors
  (`RazorProjectEngine` + `DefaultTagHelperContext` / discover from compilation references) if path-1 is to remain a
  fallback. Otherwise consider retiring path-1 once path-2 works, since path-2 with the real generator is authoritative.
- Implement the intended post-accept **watched-project build** before the index rebuild (the operator-expected behavior),
  so generator output is fresh; pair it with incremental build/index (see
  `WorkflowCostAndIncrementalRebuild-2026-06-01.md`) to bound the cost.

Regression coverage:

- Add a workflow smoke that creates a new two-file Razor page using component-attribute bindings (`@bind-Value`,
  `Click="@Method"`), accepts it, and asserts `find_indexed_references` on the bound members returns `razor*` reference
  rows in the `.razor`. The existing corpus only proves `@code`-block references, which is why this slipped through.

## Notes

- Probe-key gotcha worth documenting for agents: `find_indexed_references`/`find_indexed_callers` require the
  `symbol:<hash>` stable keys from `find_indexed_symbols`/`query_solution_index`/`get_indexed_symbol`; the `::`-path
  selector keys from `get_source_map` now return a visible MCP guidance payload instead of silently returning `[]`.
- Declarations are unaffected — the partial type and code-behind members index fine from both files
  (`BuildRazorDeclarationsAsync` maps generated declaration spans back correctly).
