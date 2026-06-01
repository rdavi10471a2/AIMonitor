# AIMonitor Claude Mini Skills

These cards are short, model-facing reminders. They should stay small enough to paste into a prompt or load as focused context.

Use the AIMonitor workflow docs for current implementation details:

- `docs/feature-maps/CliWorkflowEditLoop.md`
- `docs/feature-maps/SharedAdapterSurface.md`
- `docs/workflows/SafeEditWorkflow.md`

## Cards

- `AIMonitorWorkflowQuickStart.md`: current AIMonitor MCP workflow, live bridge binding, review gates, and expected telemetry.
- `AIMonitorSkillPack.md`: compact master prompt and card index.
- `SkillRouter.md`: choose the smallest relevant card instead of loading the whole doctrine.
- `RoslynFirstNavigation.md`: semantic discovery before grep/text search.
- `SystemMonitorStaging.md`: watched-source staging and decision flow.
- `SessionOverlayValidation.md`: multi-file session staging and pre-merge validation behavior.
- `ReviewQueueAndGates.md`: WinMerge, pre-merge validation, and queue stop/unblock.
- `FormattingOracle.md`: insertion/replacement/removal layout rules.
- `AsyncPropagation.md`: async/signature caller propagation.
- `PartialClassRefactor.md`: human-guided companion partial extraction.
- `TroubleshootingDashboard.md`: reading live dashboard traffic.

## Layering

- Tool descriptions answer: how do I call this tool right now?
- Mini skills answer: what operating mode am I in?
- Long docs and fixture corpus answer: why does this rule exist, and what proved it?

Start with `AIMonitorWorkflowQuickStart.md` and `SkillRouter.md`, then load only the cards required by the active task.
