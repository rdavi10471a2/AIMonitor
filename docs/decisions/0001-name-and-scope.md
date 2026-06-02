# Decision 0001: Name And Scope

## Decision

Use `AIMonitor` as the neutral product name.

## Rationale

The monitor is no longer Claude-specific. Claude uses MCP; Codex should use CLI/process-friendly access. Both adapters should share the same workflow engine.

## Scope

AIMonitor targets local .NET watched projects, initially:

- Blazor/Razor;
- WinForms;
- console.

ASP.NET/Web API projects are expected to load as normal SDK-style C# projects when required SDKs are installed, but ASP.NET-specific semantic workflows are not a current focus.
