# MSBuild-First Indexing

V2 starts from MSBuild because filesystem enumeration was not enough.

The MSBuild layer owns:

- solution and project loading;
- project identity;
- compile items;
- target frameworks;
- conditional symbols;
- project references;
- package/reference context;
- workspace diagnostics.

Indexing must consume MSBuild-loaded projects rather than inventing project membership from folders.

## Language Model

MSBuild loading is not a C#-only concept. AIMonitor should treat the solution/project/document graph as the broad project-system truth for any language that `MSBuildWorkspace` can load.

Semantic indexing is different. Symbols, references, callers, generated-code interpretation, and source maps are language-provider work. The first semantic provider is C# because the current monitor use cases are Blazor/Razor, WinForms, console, and normal .NET C# projects.

Do not encode the architecture as "all files are C#." The intended model is:

```text
MSBuild solution model
  projects
    documents for supported project languages
    references/framework/package/project facts
    semantic rows when a language provider supports that document
```

C# symbol/reference indexing ships first. Razor is part of that C# semantic provider when Razor-generated C# maps back to the original user-authored `.razor` source. Generated framework/render-tree spans that do not map back to user source are not indexed as navigable code.

Legacy combined `.razor.cs` files are treated as Razor input only when the file actually contains Razor syntax such as markup or `@code`. Normal `.razor.cs` code-behind remains ordinary C#.

This is not a full Razor language-server replacement. V2 should not claim that every component parameter, event handler string, or Blazor UI binding is represented as a semantic reference. Those shapes require a dedicated Razor binding layer if they become important. Until then, the monitor relies on MSBuild/C# truth, mapped Razor references, grep-verified smoke tests, and normal build feedback.

Other languages should remain visible as projects/documents with diagnostics and project facts, even when they do not yet have semantic rows.

## Initial Regression

`AIMonitor.MSBuild.Tests` opens a generated SDK-style project through `MSBuildWorkspace`. This is the first heartbeat test for the V2 direction.

Representative local smoke tests cover:

- real SchemaStudioWebViewer C# and Razor references;
- all real SchemaStudioWebViewer `.razor.cs` code-behind files;
- a generated local Blazor detector sample with clean split code-behind and legacy mixed `.razor.cs`.
