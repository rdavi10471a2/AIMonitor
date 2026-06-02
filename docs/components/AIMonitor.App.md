# AIMonitor.App

## Purpose

Operator WinForms host and first recipient for live interactive MCP traffic.

## Inputs

- Monitor settings.
- Index query services.
- Live log events.
- MCP proxy hub traffic.

## Outputs

- Solution Index UI.
- Monitor Status telemetry UI.
- WinForms-owned MCP proxy hub.
- Operator-visible review/validation context.

## Data Flow

```text
WinForms app
  -> shared services for local views
  -> MCP proxy hub receives live Claude traffic
  -> log service records events
  -> Monitor Status displays request/response telemetry
```

## Owns

- Operator UI.
- Live MCP proxy hub.
- Monitor Status presentation.
- App-owned shared logging service.

## Does Not Own

- Workflow classification.
- Index schema.
- CLI command surface.

## Key Tests

- UI smoke/manual verification.
- Tool smoke tests that require WinForms-visible telemetry.
