# V2 Architecture

AIMonitor V2 starts from the lessons of MonitorBaseClaude:

- one Claude-facing monitor path;
- one authoritative watched solution path;
- stable staged diffs;
- vote-plus-hash decisions;
- generated state outside watched projects;
- tests as regression memory.

## Boundaries

| Project | Responsibility |
| --- | --- |
| `AIMonitor.Core` | Shared settings, identities, and plain domain records. |
| `AIMonitor.Data` | SQLite solution index built from the single watched solution path. |
| `AIMonitor.Logging` | Unified structured log paths and JSON-lines event logging. |
| `AIMonitor.Workflow` | Candidate staging, review classification, queues, ledgers, and recovery rules. |
| `AIMonitor.MSBuild` | Solution/project loading, project graph, compile items, target frameworks, and diagnostics. |
| `AIMonitor.Indexing` | Symbols, references, callers, relationships, and source maps from MSBuild-loaded projects. |
| `AIMonitor.Runtime` | Build, test, process, diff, and external tool execution adapters. |
| `AIMonitor.McpServer` | Combined MCP tool server hosted behind the WinForms-owned MCP proxy hub for interactive sessions, or launched directly by deterministic server tests. |
| `AIMonitor.Cli` | Codex-friendly command adapter. |
| `AIMonitor.App` | Operator host/UI and first recipient for interactive MCP traffic through its MCP proxy hub. |
| `AIMonitor.McpStdioBridge` | Thin MCP stdio-to-WinForms-pipe adapter for Claude Code and live smoke tests. It owns no workflow logic and does not launch tools directly. |

## Initial MSBuild Goal

The first real capability is loading SDK-style projects through `MSBuildWorkspace` and preserving project identity before indexing.

The first persisted capability is rebuilding the monitor-owned solution index from `Monitor:WatchedSolutionPath`.

`AIMonitor.Data` owns the active SQLite solution index and durable monitor stores that exist today. Do not carry empty boundary projects; add new persistence boundaries only when there is real behavior and test coverage to justify them.

## Semantic Boundary

The V2 index should be conservative. It stores project-system truth from MSBuild and source-symbol facts from providers that can prove their mappings.

For C# and Razor work:

- regular C# and clean `.razor.cs` code-behind are C# provider inputs;
- user `.razor` files are indexed only through reliable Razor/compiler source mappings;
- legacy mixed `.razor.cs` files are accepted only when Razor syntax and source mappings prove they are Razor input;
- full Blazor UI binding semantics are not part of the initial index contract.

This boundary is intentional. The current monitor workflow is already useful with MSBuild truth, C# symbols/references, representative Razor mappings, grep-verified smoke tests, and compiler/build feedback. A future Razor binding provider can extend the model without rewriting the core architecture.

## Logging Boundary

The host process owns log serialization. UI controls, workflow components, MCP, CLI, and future runtime adapters should emit events through `IMonitorLogger` instead of opening their own durable log writers. WinForms can expose a live log view by subscribing to the host-owned `IMonitorLogEventSource`; file reads and writes must allow shared access so diagnostics tools, the UI, and background work do not fight over the JSON-lines file.

## MCP Hosting Boundary

Interactive Claude Code MCP bindings should launch `AIMonitor.McpStdioBridge`, not `AIMonitor.McpServer` directly. The stdio bridge connects the MCP client session to the WinForms-owned MCP proxy hub. WinForms is therefore the first monitor recipient for live MCP traffic, records request/response telemetry, and then relays JSON-RPC to the combined `AIMonitor.McpServer`.

Deterministic integration tests may still launch `AIMonitor.McpServer` directly when they are testing server/tool behavior without a live WinForms process. Live smoke tests that need operator-visible telemetry should launch `AIMonitor.McpStdioBridge`.

The checked-in MCP template uses relative paths because it is a repo template. Real Claude Code bindings should use the safer `dotnet` command with absolute paths to `AIMonitor.McpStdioBridge.dll`, the repository root, and the appsettings file until Claude Code's launch working directory is verified.
