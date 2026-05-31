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

C# symbol/reference indexing may ship first. Other languages should remain visible as projects/documents with diagnostics and project facts, even when they do not yet have semantic rows.

## Initial Regression

`AIMonitor.MSBuild.Tests` opens a generated SDK-style project through `MSBuildWorkspace`. This is the first heartbeat test for the V2 direction.
