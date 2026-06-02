# Smoke Coverage TODO

This list tracks standalone smoke and workflow coverage that should be ported or adapted from earlier safe-edit monitor test suites into AIMonitor.

The corpus and tool-surface checks are intentionally indebted to prior Roslyn/MCP experiments and compiler-library-style known-answer tests. AIMonitor uses those patterns as regression memory while keeping the workflow, staging, and review contracts local to this repository.

## Common Surface

These tests apply to the shared monitor/index/workflow surface regardless of whether the caller is Claude, Codex, MCP, CLI, or WinForms.

- [x] Extend `AIMonitor.LanguageCorpusSmokeTests` to assert `expectedCallerCount` from corpus `expected.json`.
- [x] Extend `AIMonitor.LanguageCorpusSmokeTests` to assert `expectedRelationshipKinds` from corpus `expected.json`.
- [x] Add a fixture index matrix smoke equivalent to the earlier `--fixture-index-matrix` workflow, using a generated disposable C# fixture and an independent Roslyn comparator.
- [x] Add a WebViewer file-by-file smoke equivalent to the earlier `--webviewer-file-by-file` workflow, comparing selected real files against index and grep sanity counts.
- [x] Keep current local watched-solution smoke coverage for Razor/code-behind indexing and representative real-project references.
- [x] Add or preserve smoke summaries under `runtime/smoke/...` so failures leave reviewable artifacts.
- [x] Port the earlier external language corpus: 42 known-answer cases covering calls, construction, members, metadata, multi-project-shaped sources, operators, project-system global usings, resources, and types.
- [x] Add live WinForms-visible MCP smoke coverage for normal status/index calls through the stdio bridge and WinForms-owned proxy hub.

## Codex-Specific Surface

These tests focus on the Codex CLI workflow and should not depend on Claude MCP/session behavior.

- [x] Add MCP session workflow smoke for full safe edit loop on multiple files with both candidates staged under the same `sessionId`, launched, simulated accepted, and recorded.
- [x] Add CLI workflow smoke for multiple staged files where one candidate fails validation and does not silently mutate watched source.
- [x] Add CLI workflow smoke for `.razor` full-file edit path through `edit refresh`, Working candidate edit, `edit stage`, `edit launch-diff`, and `edit record-decision`.
- [x] Add CLI workflow smoke for new-file review where WinMerge/runtime review is followed by the human/operator creating the future watched file before `record-decision accepted` verifies the staged hash.
- [x] Add CLI workflow coverage for `accepted-normalized` as an integration path, not only classifier unit coverage.
- [x] Add CLI workflow coverage for `dirty-unexpected` as an integration path, not only classifier unit coverage.
- [ ] Add non-interactive dialog decision seams so validation override approval/cancel behavior can be tested without a real Windows dialog.
- [x] Add MCP workflow smoke for creating a new file, adding paired members, removing `_removed` members, staging, and rejecting without creating watched source.
- [x] Add live WinForms-visible MCP smoke for all non-human file/Roslyn edit tools exposed to Claude/Codex surfaces.
- [x] Add Claude-skill-style MCP sequence coverage using source maps, symbol reads, Roslyn edits, staging, and rejected decision without requiring Claude itself.

## Not Directly Applicable

These earlier smoke areas are Claude/MCP/model specific and should not be ported directly unless AIMonitor grows the same surface.

- Claude model/tool-routing smokes.
- Model routing drills.
- Legacy source-map budget/tool smokes tied to old Claude MCP responses.
- Serial human review queue enforcement, unless AIMonitor later adds a Codex queue abstraction. MCP session staging is covered; old Claude queue semantics are not a current AIMonitor contract.
- Ollama-specific routing and fake-router drills.
