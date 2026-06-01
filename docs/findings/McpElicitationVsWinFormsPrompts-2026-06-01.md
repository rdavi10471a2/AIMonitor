---
status: new
type: finding
created: 2026-06-01
scope: replace server-owned WinForms prompts with MCP elicitation; dual-client (Claude + Codex) implications
confidence: medium-high (Claude Code support confirmed; Codex support recently landed and evolving)
---

## Summary

AIMonitor currently asks the operator yes/cancel-style questions (notably the pre-merge validation override) with a
**native Windows Forms / TaskDialog** drawn by the WinForms hub (`PreMergeValidationOverridePrompt`). The MCP-standard way
to do this is **elicitation**: the server sends an `elicitation/create` request and the *client* renders the prompt natively,
returning the user's structured response. This removes server-owned GUI from the decision path and works across clients.

## What is possible

- **MCP elicitation** lets a server request structured user input mid-call. The client renders the UI (form fields, enum
  dropdowns, boolean checkboxes) and returns accept/decline/cancel + content validated against the server's `requestedSchema`.
- **Claude Code supports it** (added v2.1.76, 2026-03-14). No `initialize` capability declaration is required from the server;
  Claude Code renders the dialog when an `elicitation/create` arrives. Schema is **flat** (top-level primitive properties only:
  string / enum / boolean / number — no nesting, no arrays of objects). An `Elicitation` hook can auto-respond for automation.
- **Codex CLI supports it too**, recently landed / still maturing (openai/codex PRs #17043 "support server-driven elicitations",
  #13425; issue #6992). Codex also gates modifying MCP tools behind a boolean elicitation approval when approval-policy is
  on-request.
- Because elicitation is a **client capability**, the same server-side request renders natively in any conforming client
  (Claude Code, Claude Desktop, Codex, Agents SDK). Write once on the server.

## Implication for the dual-client harness

- **Claude path = MCP.** Replacing the WinForms override prompt with `elicitation/create` gives Claude a native, non-blocking
  prompt and keeps GUI out of the stdio/bridge path. Direct win.
- **Codex path = CLI adapter (no MCP) — the catch.** In the current architecture Codex drives the edit loop through the CLI,
  not MCP, so it would **not** receive MCP elicitation on that path. Elicitation only reaches an agent connected as an MCP client.
  Decision required:
  - (a) route Codex through the MCP server as well (gains native elicitation, but changes the "CLI is Codex's path" design), or
  - (b) keep the CLI's terminal-interactive prompt (or chat-approval + `--force-validation`) as Codex's channel, and use
    elicitation only for MCP clients.
  - (c) **hybrid (worth thinking about):** keep the CLI as Codex's edit surface, but expose the *single human-gate step*
    (validation override) as an MCP tool Codex calls. Elicitation only fires during an in-flight MCP tool call, so Codex
    must be mid-call to be prompted — registering the AIMonitor MCP server in `~/.codex/config.toml` and routing just the
    override through it gives Codex a native, terminal-free prompt while ~95% of the loop stays on the CLI. Bonus: Codex
    auto-gates *modifying* MCP tools behind an approval elicitation when approval-policy is on-request, so marking that tool
    as modifying can trigger the prompt on its own. Preserves "CLI is Codex's edit surface" with native prompts only where
    a human decision is required.
- Either way, the WinForms TaskDialog should stop being the *primary* decision surface for agent-driven flows; it remains a
  reasonable fallback for a human operating the WinForms hub directly.

## Repro / Expected / Actual

- **Actual:** `PreMergeValidationOverridePrompt` (AIMonitor.Runtime) draws a native Win32 TaskDialog; `launch_staged_diff`
  (MCP) and `edit launch-diff` (CLI) both depend on that GUI for the override decision.
- **Expected (target):** MCP clients receive an `elicitation/create` (flat schema: e.g. boolean `forceValidation` + message
  summarizing failed validation) and render it natively; the CLI path keeps a terminal prompt for Codex.

## Notes / open questions

- Confirm the exact Codex CLI version where elicitation is stable before relying on it for the Codex path.
- Elicitation schema is flat — the override prompt is a good fit (one boolean + explanatory message); richer review choices
  would need to be decomposed into flat fields.
- Sources: Claude Code MCP docs (code.claude.com/docs/en/mcp), MCP client-concepts (modelcontextprotocol.io),
  openai/codex PRs #17043 / #13425 / issue #6992.
