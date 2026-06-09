# Architecture Diagramming Rubric

Score each item 0-2.

## Evidence

- Uses repository docs/source or MCP output rather than inventing behavior.
- Names the evidence surface used.
- Flags uncertainty when evidence is unavailable.
- Separates implemented code, published docs, branch/proposal docs, and inference.

## Diagram Quality

- Shows time/order clearly.
- Separates actors, artifacts, gates, state, and durable mutation.
- Separates predictive checks from authoritative checks.
- Shows failure/replan/stale paths when they affect safety.
- Avoids mushy labels or defines them immediately.

## Portability

- Keeps behavior host-neutral where possible.
- Does not mix MCP and CLI names in the same operator step.
- Uses MCP tool names only for Claude/MCP evidence or explicit MCP flows.

## AIMonitor Fit

- Captures safe edit invariants.
- Captures adapter-over-shared-service architecture.
- Captures index/MSBuild/Razor refresh boundaries without promising full Visual Studio Razor semantics.
- Captures Plan Board context retrieval through the Planning surface, not task-memory files.

## Skill Critique

- Identifies which skill instructions helped.
- Identifies missing instructions or ambiguous wording.
- Suggests concrete improvements.

## Pass Bar

A useful run scores at least 16/22 and has no major invented behavior.
