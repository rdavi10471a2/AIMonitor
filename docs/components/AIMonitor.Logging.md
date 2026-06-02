# AIMonitor.Logging

## Purpose

Provide shared structured logging and live log transport for adapters and the WinForms host.

## Inputs

- Monitor log entries from app, CLI, MCP, workflow, runtime, and indexing surfaces.

## Outputs

- JSON-lines log entries under `runtime/logs/aimonitor.ndjson`.
- Live in-process log events.
- Named-pipe log events for adapter telemetry.

## Data Flow

```text
Component event
  -> IMonitorLogger
  -> WinForms log pipe or JSON-lines logger
  -> runtime/logs/aimonitor.ndjson
  -> Monitor Status UI
```

## Owns

- Log event shape.
- Shared file access for durable logs.
- Pipe names and pipe logging.

## Does Not Own

- UI grid rendering.
- Workflow behavior.
- MCP JSON-RPC transport.

## Key Tests

- `AIMonitor.Logging.Tests`
