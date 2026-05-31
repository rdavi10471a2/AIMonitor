# CLI Workflow Edit Loop

## Human Notes

This feature is the Codex-first safe edit path. Codex can use normal local file editing against monitor-owned working candidates while AIMonitor owns staging, WinMerge review records, telemetry, and vote-plus-hash decision classification.

The real accept path is WinMerge save/no-save plus `record-decision`. The CLI must not silently copy a candidate into watched source as its normal accept behavior.

## AI-Maintained Map

- `AIMonitor.Workflow.WorkflowEditService` owns refresh, status, replace-text, stage, launch bookkeeping, and record-decision operations.
- `AIMonitor.Workflow.WorkflowEditPaths` maps watched files into monitor-owned working files under the watched solution runtime workspace.
- `AIMonitor.Workflow.EditSessionManifest` stores original file identity and candidate paths.
- `AIMonitor.Workflow.StagedEditRecord` stores immutable staged candidate metadata, hashes, launch state, and decision results.
- `AIMonitor.Workflow.EditSessionStatus` is the CLI/adapter response model for the edit loop.
- `AIMonitor.Cli` exposes the workflow as `edit refresh`, `edit new`, `edit replace-text`, `edit status`, `edit stage`, `edit launch-diff`, and `edit record-decision`.
- `AIMonitor.Runtime.WinMergeDiffToolLauncher` opens WinMerge for staged candidate review.

## Dataflow

```text
Watched source file
  -> edit refresh
  -> runtime/watched-solutions/<solution>/working/<relative-file>
  -> Codex edits working file, preferably with edit replace-text for exact replacements
  -> edit status
  -> staged/original/watched hashes
  -> edit stage
  -> immutable staged candidate + ledger/run/telemetry records
  -> edit launch-diff
  -> full pre-merge validation copy/build gate
  -> if validation fails, user approves or cancels the override dialog
  -> operator saves staged candidate in WinMerge or leaves watched source unchanged
  -> edit record-decision --decision accepted|rejected
  -> accepted decisions rebuild the monitor-owned solution index
  -> accepted/rejected/dirty-unexpected classification by hashes
  -> accepted outcomes mark the session refresh-required
  -> edit refresh before the next edit to that watched file
```

For new files:

```text
Future watched source path
  -> edit new
  -> empty runtime/watched-solutions/<solution>/working/<relative-file>
  -> Codex writes the new-file candidate in Working
  -> edit stage
  -> immutable staged candidate + blank review baseline
  -> edit launch-diff
  -> operator saves staged candidate into the runtime review target or leaves it unchanged
  -> edit record-decision --decision accepted|rejected
  -> accepted decision creates the watched file from the reviewed runtime target
```

## Commands

```text
aimonitor edit refresh --file <watched-file>
aimonitor edit new --file <future-watched-file>
aimonitor edit replace-text --file <watched-file> --old-text <text> --new-text <text> --expected-matches <n>
aimonitor edit replace-text --file <watched-file> --old-text-file <path> --new-text-file <path> --expected-working-hash <hash>
aimonitor edit status --file <watched-file>
aimonitor edit stage --file <watched-file> --ledger-summary "<short summary>"
aimonitor edit launch-diff --staged-record-id <id>
aimonitor edit launch-diff --staged-record-id <id> --force-validation
aimonitor edit record-decision --staged-record-id <id> --decision accepted --expected-staged-hash <hash>
aimonitor edit record-decision --staged-record-id <id> --decision rejected
```

The `refresh` response includes `workingFilePath`. Codex should edit that monitor-owned path, not the watched source file.

`edit replace-text` operates only on the monitor-owned Working file. It does not mutate watched source. It counts exact matches, can enforce `--expected-matches`, can guard against stale candidates with `--expected-working-hash`, and normalizes replacement text to the Working file's dominant line ending. This is mainly the Codex-safe local command path; Claude can continue to use its MCP/editor edit surface when that remains stable. Agents may make multiple tool-driven edits to the Working file before staging. After `edit stage`, the staged candidate and recorded hashes are immutable review evidence; further candidate changes should be made in Working and staged again before `edit launch-diff` or accept. When agents edit Working files directly, they should preserve existing line endings. New files should follow `.editorconfig` or nearby project files.

`edit launch-diff` runs the pre-merge validation gate by copying the watched solution root into monitor runtime validation storage, overlaying the staged candidate, and running `dotnet build` against that validation copy. Validation itself does not mutate the watched source tree, and runtime/validation storage is excluded from the validation copy. For new files, WinMerge reviews monitor-owned runtime files; the watched file is not created until `record-decision accepted --expected-staged-hash <hash>` verifies the reviewed runtime content. If validation fails on an interactive Windows desktop, AIMonitor shows a human OK/Cancel override dialog before WinMerge opens. If no interactive dialog is available, the command blocks launch and tells the agent to ask the user in chat. `--force-validation` is the non-interactive equivalent and should only be used after explicit human approval.

`accepted-normalized` is a successful accept where exact bytes differed but normalized content matched, usually because WinMerge or an editor normalized line endings. The next operation must still be `edit refresh` so future edits use the bytes and line endings actually saved in watched source.

## Invariants

- Direct watched-source mutation is not the clean path.
- WinMerge is the expected human review/save surface for watched-source mutation.
- Pre-merge validation runs before WinMerge launch. Failed validation blocks review unless the user approves the override dialog, or the agent asks in chat and the caller passes `--force-validation` after explicit human approval.
- Existing files must enter through `edit refresh`; missing files must enter through explicit `edit new`.
- `edit new` never creates the watched source file. It creates an empty monitor-owned Working candidate and stages against a blank review baseline.
- `edit launch-diff` must not create watched-source placeholders for new files. New-file review stays in runtime until accepted.
- `record-decision accepted` requires `--expected-staged-hash`; existing-file accepts require the watched file hash to match the staged candidate hash, while new-file accepts create the watched file from the reviewed runtime target only after that verification.
- `record-decision rejected` requires the watched file hash to match the original refresh hash.
- Accepted decisions set `requiresRefresh=true`; `edit status` reports `refresh-required`, and further edit/stage/replace operations fail until `edit refresh` captures the saved watched-source bytes.
- Accepted decisions trigger a post-accept solution index rebuild and return an `indexRefresh` object. The rebuild emits `index.refresh-after-accept.started` and completed/failed telemetry.
- For new files, `record-decision rejected` requires the watched source file to remain absent.
- Mismatched vote and file state becomes `dirty-unexpected`.
- Status classifies unchanged and pending edit state before review; staged records classify accepted, rejected, accepted-normalized, and dirty-unexpected after review.
- Future MCP tools should call the same workflow service rather than implement a second edit path.

## Runtime Cleanup

Workflow history, staged candidates, validation workspaces, logs, and index artifacts are runtime state. They should stay under `runtime/` and out of watched projects. Retention should be an explicit cleanup/prune action, preferably from the UI or a dedicated command, rather than automatic pruning on every workflow command. Agents should still clean obvious partial test artifacts deliberately by exact path, especially `.bak` files created during manual review.

## Tests / Smokes

- `tests/integration/AIMonitor.Integration.Tests/CliIndexQueryTests.cs` covers the CLI edit round trip:
  refresh a watched file, edit the working candidate, read pending status, stage the candidate, simulate WinMerge save, record the accepted decision, and verify the watched file changed.
- The same integration test file covers new-file accepted and rejected paths.
- The same integration test file covers `edit replace-text`, CRLF preservation, and the refresh-required guard after accepted decisions.
