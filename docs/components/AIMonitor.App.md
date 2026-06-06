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

## WinForms Layout Notes

Load `docs/feature-maps/WinFormsLayout.md` before changing nested WinForms layout, source-browser panes, tree command rows, or splitter behavior.

- Prefer one stateful tree command button over paired `Expand All` / `Collapse All` buttons. The button text should reflect the current tree state.
- Put tree command buttons in a small padded command row with a stable width instead of stretching multiple buttons across the full panel.
- Add `ToolTip` text to dense tree views so direction and selection behavior are discoverable without crowding the surface.
- Keep short explanatory labels beside status/summary text when a panel has domain-specific directionality. For the Solution Index source browser, use `Uses` for things the selected item points at and `Used By` for source locations that point at symbols declared by the selected item.
- Initialize splitter distances after layout when control sizes are real, and give splitters friendly widths and panel minimums. Avoid relying on designer/default splitter distances for nested WinForms layouts.
