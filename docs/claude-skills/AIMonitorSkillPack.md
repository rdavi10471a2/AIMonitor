# AIMonitor Claude Skill Pack

This is the compact master prompt for using AIMonitor with Claude against watched source.

## Core Rule

Use AIMonitor solution-index/source-map tools for semantic discovery and AIMonitor workflow tools for protected staged edits. Do not directly edit watched source.

## Load Order

1. Start with `AIMonitorWorkflowQuickStart.md` and `SkillRouter.md`.
2. Load only the cards needed for the active task.
3. Use live MCP tool descriptions for exact argument names.
4. Use long docs only for rationale or fixture evidence.

## Required First Calls

```text
get_monitor_status
get_workflow_status
get_self_check
get_tool_manifest
get_staging_guide
```

## Card Index

- `AIMonitorWorkflowQuickStart.md`: binding, first calls, safe edit loop, review gate.
- `RoslynFirstNavigation.md`: find symbols/references/callers before grep.
- `SystemMonitorStaging.md`: stage watched-source changes safely.
- `SessionOverlayValidation.md`: validate coupled staged files together.
- `ReviewQueueAndGates.md`: WinMerge, pre-merge validation gate, queue stop/unblock.
- `FormattingOracle.md`: placement, trivia, generated-file layout.
- `AsyncPropagation.md`: async/signature propagation through callers/contracts.
- `PartialClassRefactor.md`: human-guided companion partial extraction.
- `TroubleshootingDashboard.md`: verify live traffic and diagnose drift.

## Golden Path

```text
AIMonitor solution-index/source-map discovery
start_monitor_session for coupled work
refresh_file/new_file into monitor-owned Working candidates
use get_source_map/get_symbol/submit_symbol or typed edit tools
stage all coupled candidates with the same sessionId
review pre-merge validation
launch one WinMerge diff at a time
record each decision
stop on any blocked/not-launched result
```

## Stop Conditions

- Direct watched-source write temptation.
- Ambiguous symbol selector.
- Unknown MCP argument name.
- Pre-merge validation errors not explicitly force-reviewed.
- Review launch not started.
- Dirty-unexpected.
- Review-chain-blocked.
