# AIMonitor.Cli

## Purpose

Codex-friendly command adapter over shared AIMonitor services.

## Inputs

- Command-line arguments.
- Repository root and config path.
- Watched file paths.

## Outputs

- JSON command responses.
- Adapter telemetry.
- Workflow/index/runtime calls through shared services.

## Data Flow

```text
Codex / shell command
  -> AIMonitor.Cli Program
  -> shared service
  -> JSON response + telemetry
```

## Owns

- CLI command parsing.
- CLI response shape.
- Non-interactive command ergonomics.

## Does Not Own

- Workflow rules.
- Index query semantics.
- WinMerge launch implementation.

## Key Tests

- CLI integration tests in `AIMonitor.Integration.Tests`
