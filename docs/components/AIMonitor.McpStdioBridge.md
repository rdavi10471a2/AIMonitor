# AIMonitor.McpStdioBridge

## Purpose

Thin stdio bridge from Claude Code to the WinForms-owned MCP proxy hub.

## Inputs

- Claude Code MCP stdio stream.
- Repository root and config path.

## Outputs

- Forwarded JSON-RPC requests/responses.
- Live telemetry visible in WinForms Monitor Status.

## Data Flow

```text
Claude Code stdio
  -> AIMonitor.McpStdioBridge
  -> WinForms MCP proxy hub
  -> AIMonitor.McpServer
```

## Owns

- Stdio-to-pipe transport.
- Bridge session lifetime.

## Does Not Own

- MCP tools.
- Workflow logic.
- WinMerge launch.

## Key Tests

- Live ToolSmokeTests through WinForms-visible telemetry
