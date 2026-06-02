# AIMonitor.Runtime

## Purpose

Own runtime boundaries such as pre-merge validation, validation override prompts, and WinMerge launch.

## Inputs

- `StagedEditRecord`.
- Monitor settings.
- Diff tool configuration.
- Validation override decisions.

## Outputs

- `PreMergeValidationResult`.
- `DiffLaunchResult`.
- Validation and launch telemetry.

## Data Flow

```text
StagedEditRecord
  -> StagedDiffLaunchWorkflow
  -> PreMergeValidationService
  -> PreMergeValidationOverridePrompt when needed
  -> WinMergeDiffToolLauncher
```

## Owns

- Build validation copy creation.
- `dotnet build` validation execution.
- WinMerge process launch.
- Human validation override prompt.

## Does Not Own

- Candidate staging.
- Decision classification.
- MCP/CLI command parsing.

## Key Tests

- CLI/MCP launch-diff integration tests
