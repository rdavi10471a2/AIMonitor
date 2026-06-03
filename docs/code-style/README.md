# AIMonitor Code Style

This is the repository code-generation style contract for Claude, Codex, and human-authored C# going forward. It is not a demand to churn existing projects just to satisfy style cleanup.

## Rules

1. Do not use C# top-level statements. Every executable entry point gets an explicit `Program` type and `Main` method.
2. Always use braces for control-flow bodies.
3. Do not add `using var`, `using` declarations, or `await using` declarations for resource lifetime. Use explicit `using (...) { ... }` / `await using (...) { ... }` blocks so disposal scope is visible.
4. Generated projects and new sample projects may also disable SDK implicit namespace imports when that keeps imports clearer, but that is a project hygiene choice, not the rule meant by "no implied using."

## Enforcement

- `.editorconfig` marks brace preference as an error.
- `RepositoryShapeTests` scans source, tests, and committed samples for top-level statements, unbraced control-flow bodies, and growth beyond the existing `using` / `await using` declaration baseline.
- Existing code contains `using` declarations and legacy `<ImplicitUsings>enable</ImplicitUsings>` entries. Do not churn them during unrelated work.
- New source and new sample projects such as `CodexWindows` and `CodexBlazor` use explicit imports and avoid `using` declarations.

## Rationale

These rules make generated edits easier to review and keep diffs stable:

- explicit entry points avoid hidden program shape;
- braces reduce accidental one-line control-flow rewrites;
- explicit disposal scopes make resource lifetime easier to review in generated diffs.
