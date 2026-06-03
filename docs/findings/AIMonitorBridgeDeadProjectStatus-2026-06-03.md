---
status: informational
type: finding
created: 2026-06-03
audience: Codex + operator
scope: AIMonitor.Bridge "dead project" — accurate current state before any removal work
note: surfaced by a product-vs-test line count that showed a phantom 0-line AIMonitor.Bridge "project"
---

# AIMonitor.Bridge — already dead, only local build cruft remains (no repo action needed)

A line-count of product code showed `src/AIMonitor.Bridge` as a 0-line project. Investigation shows it is **not a
project in the repo at all** — it was already cleaned. Posting the accurate state so removal work is not duplicated or
falsely reported as done.

## Verified current state (2026-06-03)

- **Not in version control:** `git ls-files src/AIMonitor.Bridge` returns nothing. No `.csproj`, no `.cs`, no tracked
  file under that path.
- **Not in the solution:** `AIMonitor.slnx` does not reference `AIMonitor.Bridge` (only the real
  `src/AIMonitor.McpStdioBridge/AIMonitor.McpStdioBridge.csproj`).
- **Not referenced anywhere tracked:** the only `git grep "AIMonitor.Bridge"` hits are in the historical finding
  `docs/findings/CliVsMcpAdapterReview-2026-06-01.md` (lines 189–231), which already recorded it as
  "fully dead and still referenced by a broken task (medium), **Addressed 2026-06-01**" — the broken
  `.vscode/tasks.json` reference was removed then. No live task, csproj, project reference, or doc boundary-table row
  points at it today.
- **What actually remains:** only **local, gitignored build output** — `src/AIMonitor.Bridge/bin` and
  `src/AIMonitor.Bridge/obj` (confirmed gitignored). This leftover `bin/obj` is why the directory still appears on disk
  and shows up as a phantom "0-line project" in directory scans. The stale `obj/AIMonitor.Bridge.AssemblyInfo.cs` is the
  only thing that still names the old assembly.

## Recommendation

**Nothing to remove from the repo or the solution — it is already clean.** The dead project (created at inception) was
removed earlier, including its broken build task. The only remaining action is **local disk hygiene**: delete the
leftover `src/AIMonitor.Bridge/` directory (pure gitignored `bin/obj`) on any machine where it still exists:

```text
rm -rf src/AIMonitor.Bridge
```

This does not change version control (nothing there is tracked). If a teammate's IDE still shows the project, that is
stale local state, not a repo condition. No safety-floor or parity impact.
