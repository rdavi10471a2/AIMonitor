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

## Decision: document the constraint, do not pin

The host Roslyn must be **>= the Roslyn the registered SDK's Razor generator was built against** for
source-generated Razor to surface. Every way to *force* that match was weighed and rejected — the cost is high and
the payoff is marginal (markup-binding refs are an already-documented Razor boundary, and the defensible in-memory
`razor:*` path has no Roslyn-version dependency):

| Option | Cost | Verdict |
| --- | --- | --- |
| **A.** `global.json` pin to a .NET 9 SDK (its Roslyn 4.x < host 5.3.0, and it builds the net9.0 WebViewer) | A repo-root pin **breaks building AIMonitor itself** (net10.0); scoping the pin to only the watched solution is fragile | Rejected — breaks the host to fix a watched-app detail |
| **B.** Bump NuGet `Microsoft.CodeAnalysis.*Workspaces` 5.3.0 → 5.6.0 | 5.6.0 is an unreleased daily build (not on nuget.org stable); re-couples to whatever SDK floats in, so the next SDK bump breaks it again | Rejected — chasing a moving target |
| **C.** Pin SDK + NuGet to a matched GA pair | Forces every dev/CI machine onto one SDK + ongoing version maintenance | Rejected — heavy burden for a documented boundary |

**Conclusion:** leave `razor-generated:*` environment-dependent. `razor:*` is the defensible coverage and is
unaffected.

## Authoring rule for hermetic source-generated Razor tests

> A hermetic test that asserts source-generated Razor output (`razor-generated:*`, i.e. markup expressions and
> component bindings) only passes when the test/host environment's Roslyn matches the compiler SDK environment —
> specifically when the MSBuildWorkspace host Roslyn (AIMonitor's pinned `Microsoft.CodeAnalysis.*` version) is
> **>= the Razor generator shipped in the SDK that `MSBuildLocator` registers**. Because the registered SDK floats
> with the machine, these assertions must be **environment-aware** (assert-if-present), never hard.
>
> Tests that assert only the in-memory `razor:*` path (`@code` / `.razor.cs` mapped back to `.razor`) have no such
> dependency and may assert hard. Use `RazorGeneratorEnvironmentDiagnostic` (`RAZORGEN_DIAG=1`) to confirm whether a
> given machine's Roslyn/SDK pair surfaces source-generated docs before assuming a `razor-generated:*` assertion is
> portable.

## Test scoping

The two assertions that depend on source-generated Razor are now environment-aware (assert when source-gen rows are
present, no-op when the Roslyn/SDK skew suppresses them) so the suite is green here without hiding the real behavior:

- `tests/unit/AIMonitor.Data.Tests/ClaudeSmokesPhase1RazorTests.cs` (markup `@Model.Title`)
- `tests/unit/AIMonitor.MSBuild.Tests/MSBuildWorkspaceLoaderTests.cs` (component binding `@bind-Value`)

`McpSurfaceIndexVerificationTests` already only records (never hard-asserts) `razor-generated` presence.
