# AIMonitor.Runtime

## Purpose

Own runtime boundaries for launch orchestration, validation override prompts, and WinMerge process launch.

## Inputs

- `StagedEditRecord`.
- Monitor settings.
- Diff tool configuration.
- Validation override decisions.

## Outputs

- `DiffLaunchResult`.
- Launch and override telemetry.

## Data Flow

```text
StagedEditRecord
  -> StagedDiffLaunchWorkflow
  -> AIMonitor.Workflow.PreMergeValidationService
  -> PreMergeValidationOverridePrompt when needed
  -> WinMergeDiffToolLauncher
```

## Owns

- Launch orchestration around staged records.
- WinMerge process launch.
- Human validation override prompt.

## Does Not Own

- Build validation copy creation.
- `dotnet build` validation execution.
- Candidate staging.
- Decision classification.
- MCP/CLI command parsing.

## Key Tests

- CLI/MCP launch-diff integration tests
