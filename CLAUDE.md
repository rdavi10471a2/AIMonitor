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

Do not hide data storage row/result classes inside repository classes. Storage records get their own files so schema-shaped data stays visible in reviews.

Treat MSBuild project/document loading as language-neutral. C# is the first semantic indexing provider because it is the current product focus; do not describe the whole architecture as C#-only.

Prefer workflow smoke/regression tests for monitor behavior. Tiny unit tests are acceptable when they pin a narrow contract that would be noisy in a smoke test.
