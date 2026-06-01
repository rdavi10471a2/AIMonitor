# Smoke Coverage TODO

This list tracks standalone smoke and workflow coverage that should be ported or adapted from the MonitorBaseClaude/MonitorBaseClaudeTests lineage into AIMonitor.

## Common Surface

These tests apply to the shared monitor/index/workflow surface regardless of whether the caller is Claude, Codex, MCP, CLI, or WinForms.

- [x] Extend `AIMonitor.LanguageCorpusSmokeTests` to assert `expectedCallerCount` from corpus `expected.json`.
- [x] Extend `AIMonitor.LanguageCorpusSmokeTests` to assert `expectedRelationshipKinds` from corpus `expected.json`.
- [ ] Add a fixture index matrix smoke equivalent to MonitorBaseClaude `--fixture-index-matrix`, using a generated disposable C# fixture and an independent Roslyn comparator. Stacked tests PR #3 contains this implementation; leave unchecked here until that PR lands.
- [ ] Add a WebViewer file-by-file smoke equivalent to MonitorBaseClaude `--webviewer-file-by-file`, comparing selected real files against index and grep sanity counts. Stacked tests PR #3 contains this implementation; leave unchecked here until that PR lands.
- [ ] Keep current local watched-solution smoke coverage for Razor/code-behind indexing and representative real-project references.
- [ ] Add or preserve smoke summaries under `runtime/smoke/...` so failures leave reviewable artifacts.

## Codex-Specific Surface

These tests focus on the Codex CLI workflow and should not depend on Claude MCP/session behavior.

- [ ] Add CLI workflow smoke for full safe edit loop on multiple files where all candidates validate through the build gate.
- [ ] Add CLI workflow smoke for multiple staged files where one candidate fails validation and does not silently mutate watched source.
- [ ] Add CLI workflow smoke for `.razor` full-file edit path through `edit refresh`, Working candidate edit, `edit stage`, `edit launch-diff`, and `edit record-decision`.
- [ ] Add CLI workflow smoke for new-file review where WinMerge/runtime review is followed by the human/operator creating the future watched file before `record-decision accepted` verifies the staged hash.
- [ ] Add CLI workflow coverage for `accepted-normalized` as an integration path, not only classifier unit coverage.
- [ ] Add CLI workflow coverage for `dirty-unexpected` as an integration path, not only classifier unit coverage.
- [ ] Add non-interactive dialog decision seams so validation override approval/cancel behavior can be tested without a real Windows dialog.

## Not Directly Applicable

These MonitorBaseClaude-era smoke areas are Claude/MCP/model specific and should not be ported directly unless AIMonitor grows the same surface.

- Claude MCP tool-routing and session queue smokes.
- Model routing drills.
- Legacy source-map budget/tool smokes tied to old Claude MCP responses.
- Serial human review queue enforcement, unless AIMonitor later adds a Codex queue abstraction.
