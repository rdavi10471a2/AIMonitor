# AIMonitor Components

These files are the monitor's own component memory. Use them when changing a project or debugging a data-flow boundary.

Each component note should answer:

- Purpose
- Inputs
- Outputs
- Data flow
- Owns
- Does not own
- Key tests

## Components

- `AIMonitor.Core.md`
- `AIMonitor.MSBuild.md`
- `AIMonitor.Data.md`
- `AIMonitor.Indexing.md`
- `AIMonitor.Workflow.md`
- `AIMonitor.Runtime.md`
- `AIMonitor.Logging.md`
- `AIMonitor.Cli.md`
- `AIMonitor.McpServer.md`
- `AIMonitor.McpStdioBridge.md`
- `AIMonitor.App.md`

## Boundary Rule

Adapters should stay thin. Core behavior belongs in shared services so CLI, MCP, and WinForms do not drift.
