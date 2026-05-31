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
| `AIMonitor.Workflow` | Candidate staging, review classification, queues, ledgers, and recovery rules. |
| `AIMonitor.MSBuild` | Solution/project loading, project graph, compile items, target frameworks, and diagnostics. |
| `AIMonitor.Indexing` | Symbols, references, callers, relationships, and source maps from MSBuild-loaded projects. |
| `AIMonitor.Storage` | SQLite schema and durable state. |
| `AIMonitor.Runtime` | Build, test, process, diff, and external tool execution adapters. |
| `AIMonitor.McpServer` | Claude-facing MCP adapter. |
| `AIMonitor.Cli` | Codex-friendly command adapter. |
| `AIMonitor.App` | Operator host/UI. |
| `AIMonitor.Bridge` | Stdio-to-host bridge when needed by MCP clients. |

## Initial MSBuild Goal

The first real capability is loading SDK-style projects through `MSBuildWorkspace` and preserving project identity before indexing.
