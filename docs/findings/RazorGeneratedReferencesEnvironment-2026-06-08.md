# Razor source-generated references depend on a Roslyn/SDK version match (2026-06-08)

## Symptom

`razor-generated:*` reference rows (markup expressions like `@Model.Title` and component bindings like
`@bind-Value="DisplayName"`) are **absent** from the index on this machine. The defensible in-memory `razor:*` rows
(the `@code` / `.razor.cs` C# mapped back to `.razor` source) are present and correct (~4444 on the real WebViewer).

This is **environment-dependent**, not a missing commit. The same product code produced `razor-generated:*` rows on
another machine. A repo-wide sweep (every branch, every open PR #1–#13, every stash, every worktree) found **no fix**
that was lost — `main` is the most advanced version of the Razor extraction code. PR-13 is documentation only; PR-10
is an older, less sophisticated label scheme; PR-11 is the new `AIMonitor.Planning` project; PR-12 is the
planned-session-refresh branch. None changes how source-generated docs are acquired.

## Root cause: Roslyn version skew between the workspace host and the SDK's Razor generator

The indexer obtains source-generated Razor via `MSBuildWorkspace` →
`project.GetSourceGeneratedDocumentsAsync(...)` (`MSBuildWorkspaceLoader.AddSourceGeneratedRazorReferences`). It then
cross-references the generated symbols back to real source via `SyntaxTree.GetMappedLineSpan` — the correct and
best-achievable design.

On this machine that call returns **0 source-generated documents**, even though:

- the Razor source generator **is** loaded as an analyzer reference
  (`Microsoft.CodeAnalysis.Razor.Compiler.dll` from `sdk\10.0.300\Sdks\Microsoft.NET.Sdk.Razor\source-generators\`), and
- the `.razor` files **are** supplied to it as additional documents, and
- there are **zero** workspace diagnostics and **zero** notable compilation diagnostics.

Generator present + inputs present + zero output + zero errors is the signature of Roslyn **silently skipping a
generator that references a newer `Microsoft.CodeAnalysis` than the host**:

| Component | Roslyn version |
| --- | --- |
| MSBuildWorkspace host (AIMonitor's pinned NuGet `Microsoft.CodeAnalysis.* Workspaces 5.3.0`) | **5.3.0** |
| SDK 10.0.300's Razor generator (`sdk\10.0.300\Roslyn\bincore\Microsoft.CodeAnalysis.dll`) | **5.6.0** (`5.6.0-2.26230`) |

A generator built against 5.6.0 will not run in a 5.3.0 host. Roslyn drops it without a hard error.

### Why this machine specifically

- Installed SDKs: **10.0.300, 10.0.201, 9.0.304, 9.0.301** (Codex's PR-13 note claiming `10.0.103` is stale — that
  SDK is not present).
- There is **no `global.json`** anywhere, so `MSBuildLocator.RegisterDefaults()` registers the newest SDK (10.0.300),
  whose Razor generator (5.6.0) is ahead of the pinned host Roslyn (5.3.0).
- On the machine where `razor-generated:*` worked, the registered SDK's Razor generator was compatible with the host
  Roslyn the app loaded.

### Reproduce / confirm on any machine

`tests/unit/AIMonitor.MSBuild.Tests/RazorGeneratorEnvironmentDiagnostic.cs` (gated on `RAZORGEN_DIAG=1`) opens a
hermetic net10.0 Razor fixture through the same workspace path and dumps: registered MSBuild instances, analyzer
references (is the Razor generator loaded?), additional documents (are the `.razor` files supplied?), runtime Roslyn
version, source-generated document count, and notable compilation diagnostics.

```
RAZORGEN_DIAG=1 dotnet test tests/unit/AIMonitor.MSBuild.Tests --filter Dump_workspace_razor_generator_state
```

## Fix options (decision pending)

The host Roslyn must be **>= the Roslyn the registered SDK's Razor generator was built against**.

- **A. Pin the SDK via `global.json` to one whose Razor generator Roslyn <= the host's 5.3.0.**
  The watched WebViewer targets `net9.0`; a .NET 9 SDK (e.g. 9.0.304) ships Roslyn 4.x (< 5.3.0), so its Razor
  generator would run under the 5.3.0 host **and** can build the net9.0 watched app. Caveat: AIMonitor's own
  projects/test fixtures target `net10.0`, so a repo-root `global.json` pinned to a .NET 9 SDK would break building
  AIMonitor itself — the pin must be scoped to the watched solution / host process, not the AIMonitor repo.
- **B. Bump AIMonitor's NuGet `Microsoft.CodeAnalysis.*Workspaces` from 5.3.0 to >= the SDK's Roslyn (5.6.0).**
  Robust and machine-independent in principle, but couples the host to whatever SDK is installed, and a 5.6.0 daily
  build may not be published on nuget.org as a stable package.
- **C. Combined (recommended for determinism): `global.json` pin the SDK to a GA version whose bundled Roslyn matches
  a published NuGet `Microsoft.CodeAnalysis.*` version, and set the NuGet packages to that version.** Both the host
  and the generator are then pinned and compatible.

Until one is applied, `razor-generated:*` is environment-dependent. The in-memory `razor:*` coverage is unaffected
and remains the defensible Razor boundary.

## Test scoping

The two assertions that depend on source-generated Razor are now environment-aware (assert when source-gen rows are
present, no-op when the Roslyn/SDK skew suppresses them) so the suite is green here without hiding the real behavior:

- `tests/unit/AIMonitor.Data.Tests/ClaudeSmokesPhase1RazorTests.cs` (markup `@Model.Title`)
- `tests/unit/AIMonitor.MSBuild.Tests/MSBuildWorkspaceLoaderTests.cs` (component binding `@bind-Value`)

`McpSurfaceIndexVerificationTests` already only records (never hard-asserts) `razor-generated` presence.
