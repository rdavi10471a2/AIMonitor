# Architecture Diagramming Skill Eval

This folder is a repeatable test project for the `architecture-diagramming` skill.

The eval asks an agent to use the skill against AIMonitor's own worktree and produce portable architecture diagrams grounded in code, docs, and MCP evidence when available.

## Run

Use `prompt.md` as the task prompt. Attach either:

- Codex skill: `C:\Users\rdavi\.codex\skills\architecture-diagramming\SKILL.md`
- Claude card: `docs/claude-skills/ArchitectureDiagramming.md`

Tell the agent the workspace is the AIMonitor repository root and that the run is read-only.

## Score

Use `rubric.md` to review the output.

Store notable outputs in `runs/` using a dated filename.
