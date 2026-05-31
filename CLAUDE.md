# AIMonitor Claude Rules

Claude should treat this repository as the V2 implementation of an AI safe edit monitor.

Use the project structure:

- `src/` for product code.
- `tests/` for tracked regression/unit/integration/smoke tests.
- `samples/` for watched-project examples.
- `docs/` for architecture, findings, decisions, workflows, and feature maps.

The core design rule:

> MCP is not the workflow. MCP is the Claude adapter over the shared workflow engine.

Prefer MSBuild project truth over directory enumeration. When adding behavior, add tests beside it.
