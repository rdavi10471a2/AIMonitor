# AIMonitor

AIMonitor is the V2 safe edit monitor for AI-assisted .NET development.

The goal is to keep the proven Monitor workflow while starting from a cleaner architecture:

- MSBuild-first project loading.
- One shared workflow engine for Claude and Codex.
- MCP as the Claude adapter, not the core API.
- CLI as the Codex-friendly adapter.
- Tests, samples, and docs as top-level peers of product source.
- First-class watched-project support for Blazor/Razor, WinForms, and console apps.

## Initial Scope

Primary targets:

- Blazor/Razor component projects.
- WinForms projects.
- Console projects.

Expected to work through normal MSBuild loading when SDKs are installed:

- ASP.NET/Web API C# project loading and normal C# indexing.

Not a V2 focus:

- ASP.NET routing, middleware, auth-policy, hosting, deployment, or OpenAPI-specific semantic workflows.

## Project Layout

```text
src/       product code and adapters
tests/     unit, integration, smoke, and fixtures
samples/   human-readable watched-solution examples
docs/      architecture, decisions, findings, workflows, feature maps
config/    templates only; local config is ignored
runtime/   generated monitor state; ignored
```

## Build

```powershell
dotnet build .\AIMonitor.slnx
```

## Test

```powershell
dotnet test .\AIMonitor.slnx
```

## Architecture Rule

The safe edit workflow is not MCP. MCP is one adapter for Claude. Codex gets CLI/process-friendly access to the same engine.

```text
Claude -> MCP adapter -> shared workflow engine
Codex  -> CLI adapter -> shared workflow engine
App    -> host/UI     -> shared workflow engine
```
