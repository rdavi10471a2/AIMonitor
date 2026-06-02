# AIMonitor.McpServer

## Purpose

Claude-facing MCP tool adapter over shared AIMonitor services.

## Inputs

- MCP JSON-RPC tool calls.
- Claude session arguments.
- Watched file paths and workflow/session IDs.

## Outputs

- MCP tool responses.
- Adapter telemetry.
- Calls into workflow, index, Roslyn, runtime, and logging services.

## Data Flow

```text
Claude MCP tool call
  -> AIMonitor.McpServer tool method
  -> shared service
  -> compact MCP response
  -> persisted detail when needed
```

## Owns

- MCP tool schema and descriptions.
- MCP response shaping.
- Claude-visible guidance for wrong tool/key surfaces.
- MCP parity exposure for prior MonitorBaseClaude capabilities after the shared owning service implements or intentionally replaces the behavior.

## Does Not Own

- Safe edit workflow logic.
- Record-decision/post-accept index orchestration.
- Roslyn editable-session guard semantics.
- Index storage.
- WinMerge process launch.
- Index, Roslyn, Workflow, or Runtime behavior that belongs in shared services.

## Key Tests

- `McpServerSmokeTests`
- Tool smoke tests
- Parity tests must prove behavior/richness, not only that a compatibility-shaped JSON response exists.
