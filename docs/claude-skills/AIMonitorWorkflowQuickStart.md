# AIMonitor Workflow Quick Start

Use this card first when Claude is editing a watched project through AIMonitor.

## Binding

Claude Code should bind to `AIMonitor.McpStdioBridge`, not `AIMonitor.McpServer` directly. The bridge sends Claude's MCP traffic through the WinForms-owned proxy hub so the Monitor Status tab records live request/response telemetry.

Use absolute paths in the installed MCP config until relative working-directory behavior is verified:

```text
dotnet <absolute path>\src\AIMonitor.McpStdioBridge\bin\Debug\net10.0\AIMonitor.McpStdioBridge.dll --repo-root <absolute AIMonitor repo> --config <absolute AIMonitor config>
```

## First Calls

```text
get_monitor_status
get_workflow_status
get_self_check
get_staging_guide
get_tool_manifest
```

Use the Solution Index before loading bodies:

```text
get_solution_index_status
get_solution_index_tree
query_solution_index
find_indexed_symbols
get_indexed_symbol
find_indexed_references
find_indexed_callers
find_indexed_relationships
```

Use source-map tools before symbol edits. They are important, not legacy:

```text
get_source_map(scope: "file", mode: "selector")
get_symbol(symbolSelectorJson)
submit_symbol(path, symbolSelectorJson, replacement)
```

The index is a broad discovery surface. Source maps and symbols are the precise edit surface for C# member/type surgery. Before changing, removing, renaming, moving, or changing the signature/visibility of any symbol, perform a blast-radius check with indexed references/callers/relationships plus one cross-check signal. Declare every affected watched file in `start_monitor_session` before editing. Pre-merge validation is the guardrail for missed impact, not the discovery step.

## SchemaStudioWebViewer Razor Generated Build

Use this when Claude needs to build the real `SchemaStudioWebViewer` watched app or materialize Razor generator files for source-map/index evidence.

Important distinction:

- AIMonitor `main` already has Razor generation/indexing code in `MSBuildWorkspaceLoader`: it reads Roslyn source-generated Razor documents and also builds `RazorDocumentIndex` in memory with `RazorProjectEngine`.
- AIMonitor does **not** require an on-disk `obj\_generated` folder to index Razor. That folder is diagnostic evidence for humans/agents who need to inspect generated Razor C# directly.
- If `_generated` is missing, do not infer the indexer is missing Razor support. It usually means the external WebViewer app was built without `EmitCompilerGeneratedFiles=true`.

The real WebViewer checkout is external to AIMonitor:

```text
C:\SchemaStudioWebViewer\SchemaStudioWebViewer.csproj
C:\SchemaStudioWebViewer\SchemaStudioWebViewer.sln
```

The project currently targets `net9.0` and uses `Microsoft.NET.Sdk.Web`. On this machine, the verified SDK/runtime set is:

```text
.NET SDK 10.0.103
MSBuild 18.0.11
Microsoft.AspNetCore.App 10.0.3
```

Check the selected SDK before building:

```powershell
dotnet --info
dotnet --list-sdks
dotnet --list-runtimes
```

Run from the WebViewer root:

```powershell
Set-Location C:\SchemaStudioWebViewer
dotnet build .\SchemaStudioWebViewer.csproj -c Debug -p:UseSharedCompilation=false -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=obj\_generated
```

The important properties are `EmitCompilerGeneratedFiles=true` and `CompilerGeneratedFilesOutputPath=obj\_generated`. A normal `dotnet build` can succeed without leaving the diagnostic `_generated` folder behind.

Verify generated Razor files:

```powershell
Get-ChildItem C:\SchemaStudioWebViewer\obj\_generated -Recurse -File |
    Where-Object { $_.FullName -like '*.razor.g.cs' } |
    Select-Object -First 20 FullName
```

Expected folder shape:

```text
C:\SchemaStudioWebViewer\obj\_generated\
  Microsoft.CodeAnalysis.Razor.Compiler\
    Microsoft.NET.Sdk.Razor.SourceGenerators.RazorSourceGenerator\
      ... *.razor.g.cs
```

Verified locally on 2026-06-08: build succeeded with 20 existing warnings, 0 errors, and produced 36 files under `C:\SchemaStudioWebViewer\obj\_generated`.

Use `_generated` as evidence for Razor source mapping and generator behavior. Do not treat it as proof of full Visual Studio-level Razor binding semantics.

## Existing File Edit

```text
start_monitor_session(filesPlanned: [...]) with the planned watched file set, even for one-file edits
refresh_file(sourceFilePath, sessionId)
edit only the returned Working candidate using MCP tools, passing the same sessionId to every mutation tool
stage_candidate_for_review(path, sessionId)
launch_staged_diff(stagedRecordId) for every planned staged record before recording decisions
operator reviews/saves in WinMerge
record_diff_decision(stagedRecordId, "accepted", expectedStagedHash)
refresh_file before another edit to the same watched file
ask the user whether to run `dotnet build` for the watched solution
```

Safe editing tools include:

- `replace_text_in_file` with `expectedMatches`.
- `find_text_span` then `replace_span_in_file`.
- `get_source_map`, `get_symbol`, then `submit_symbol` for precise C# replacements.
- `add_symbol`, `remove_symbol`.
- `add_using`, `remove_using`, `set_type_partial`.
- `add_field`, `add_property`, `add_method`, `add_constructor`, `add_nested_type`.
- `submit_file` only for new files, generated files, or deliberate whole-file replacement.

CSS, JSON, config, markup, and other non-C# text assets use the same Working candidate, staging, WinMerge diff, and decision flow. They do not need semantic index rows to be diffable.

Do not edit watched source directly. Do not edit staged runtime files. After staging, further candidate changes must go back through the Working file and be staged again.

## New File Edit

```text
start_monitor_session(filesPlanned: [...]) with the planned watched file set
new_file(sourceFilePath, sessionId)
submit_file(path, content, sessionId)
stage_candidate_for_review(path, sessionId)
launch_staged_diff(stagedRecordId) for every planned staged record before recording decisions
operator creates/saves watched file in WinMerge
record_diff_decision(stagedRecordId, "accepted", expectedStagedHash)
ask the user whether to run `dotnet build` for the watched solution
```

Rejected new-file decisions leave watched source absent.

## Validation And Review

`launch_staged_diff` always runs pre-merge validation before WinMerge. If validation fails on an interactive Windows desktop, AIMonitor shows `Yes Launch` and `Cancel`.

- `Cancel`: WinMerge does not open; fix the Working candidate and stage again.
- `Yes Launch`: WinMerge opens despite failed validation; only record `accepted` if the operator deliberately saved the candidate into watched source.
- No dialog available: ask the operator in chat before using `forceValidation`.

For planned sessions, `launch_staged_diff` confirms staged overlay readiness before WinMerge. Launch/review every planned staged record before recording decisions. The full planned staged overlay build runs at the terminal planned accepted decision before the accepted set is treated as final and before post-accept index refresh proceeds.

Accepted or accepted-normalized decisions return `indexRefresh`. In planned sessions, early accepts can defer refresh until the terminal accepted decision. Check that status before relying on fresh index rows.
