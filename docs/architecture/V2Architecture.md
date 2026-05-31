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
| `AIMonitor.Storage` | Future durable workflow state that is not the source index. |
| `AIMonitor.Runtime` | Build, test, process, diff, and external tool execution adapters. |
| `AIMonitor.McpServer` | Claude-facing MCP adapter. |
| `AIMonitor.Cli` | Codex-friendly command adapter. |
| `AIMonitor.App` | Operator host/UI. |
| `AIMonitor.Bridge` | Stdio-to-host bridge when needed by MCP clients. |

## Initial MSBuild Goal

The first real capability is loading SDK-style projects through `MSBuildWorkspace` and preserving project identity before indexing.

The first persisted capability is rebuilding `runtime/data/solution-index.sqlite` from `Monitor:WatchedSolutionPath`.

## Logging Boundary

The host process owns log serialization. UI controls, workflow components, MCP, CLI, and future runtime adapters should emit events through `IMonitorLogger` instead of opening their own durable log writers. WinForms can expose a live log view by subscribing to the host-owned `IMonitorLogEventSource`; file reads and writes must allow shared access so diagnostics tools, the UI, and background work do not fight over the JSON-lines file.
