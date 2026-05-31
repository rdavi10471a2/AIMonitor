# AIMonitor Agent Instructions

This repository is the clean V2 monitor implementation.

- Keep `src`, `tests`, `samples`, and `docs` as top-level peers.
- Do not place product source at repository root.
- Do not use C# top-level statements.
- Keep MCP, CLI, and UI as adapters over shared Core/Workflow/MSBuild/Indexing services.
- Add tests in parallel with workflow behavior.
- Prefer MSBuild-loaded project truth over filesystem guessing.
- Keep generated runtime state under `runtime/` and out of watched projects.
- Do not hide data storage row/result classes inside repositories; storage records get their own files.
- Treat MSBuild project/document loading as language-neutral; C# is the first semantic provider, not the whole architecture.
- Prefer smoke/regression tests for end-to-end monitor behavior. Use unit tests sparingly for small contracts that smoke tests would make slow or vague, such as path derivation and SQLite row mapping.

## Edit Safety

The V2 workflow must preserve these V1 invariants:

- no direct protected watched-source mutation by the agent;
- bounded staged candidates;
- stable diff review;
- vote-plus-hash accept/reject classification;
- explicit dirty/unexpected recovery;
- regression tests for every fixed finding when feasible.
