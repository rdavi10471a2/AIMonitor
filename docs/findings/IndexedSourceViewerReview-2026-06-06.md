---
status: open
type: review
created: 2026-06-06
audience: Codex + operator
scope: review of 3a624bf "Add indexed source navigation viewer" (merged to main via fdeb62a)
reviewer: Claude (review gate)
context: deep review for "does what's advertised + no fast-iteration landmines left in source"
---

# Review — indexed source navigation viewer (3a624bf)

## Verdict

The feature is real and works end-to-end: Monaco is genuinely bundled (not a stub), loaded via WebView2 with a
machine-independent asset path, and driven from the solution index. The **data layer and schema migration are solid** —
the single most likely fast-iteration breakage (an existing `solution-index.sqlite` throwing `no such column`) is
correctly avoided. **Every landmine is in the WinForms control** `src/AIMonitor.App/Controls/SolutionIndexControl.cs`:
an incomplete UI refactor that orphans (and leaks) the old detail widgets, a WebView2 init race + timeout wedge that
stacks event handlers, and a WebView2 user-data path that only resolves correctly on the dev box. None are blocking for
a dev demo; all should be fixed before this ships anywhere else.

## Verified clean (the stuff fast iteration usually breaks)

- **Schema migration done right.** `SolutionIndexDatabase.cs` adds 7 symbol columns (`accessibility`, `is_static`,
  `is_abstract`, `is_sealed`, `is_virtual`, `is_override`, `method_kind`) via explicit `AddColumnIfMissing` (pragma
  table_info + conditional `ALTER ... ADD COLUMN ... NOT NULL DEFAULT`), not bare `CREATE TABLE IF NOT EXISTS`. Verified
  against a real pre-change on-disk DB: old DBs upgrade in-place instead of throwing. Read paths call `EnsureCreated`
  before SELECT, so migration runs first. Existing rows backfill with `''`/`0`.
- **No SQL injection.** New INSERT/SELECT are parameterized; the one interpolation (`AddColumnIfMissing`) takes only
  compile-time literal column names.
- **Reader ordinals / nullability correct.** SELECT order maps 1:1 to constructor ordinals 10-16; all new columns are
  `NOT NULL DEFAULT`, so no `IsDBNull` needed; `IndexedSymbolRow` new fields non-nullable with matching defaults.
- **Monaco bundle is genuine** MS Monaco 0.52.2 — copyright banners intact, no offsite domains, no
  `XMLHttpRequest`/`WebSocket`/`sendBeacon`, `eval`/`fetch` only in the standard same-origin-gated worker loader.
- **csproj is correct** — `Content Include="Assets\monaco\**\*"` is scoped (~16 files), `CopyToOutputDirectory=PreserveNewest`
  (not `Always`), so steady-state incremental builds copy 0 files. WebView2 PackageReference added.
- **MSBuild loader change is correct** — new symbol-flag capture is pure in-memory `ISymbol` reads (zero added cost);
  new `IsRazorCodeBlockMappedLine` is try/catch-guarded and bounds-checked, cannot abort the index build.

## Flags — all in SolutionIndexControl.cs unless noted

### CRITICAL
1. **Orphaned dead UI.** The refactor swapped the detail panel to `sourceSplit` but left `detailTabs` + six
   `DataGridView`s + `rawBox` + `fileOverviewControl` (constructed ~lines 185-196) **never added to the visible control
   tree** → never disposed (GDI/window-handle leak) **and still populated on every selection** (`SetGrid(...)`,
   `rawBox.Text = ...`, `SetReferencesGrid(...)`) — dead work on the hot path. Delete them (and their feed code) or
   re-host them.

### HIGH
2. **WebView2 init race.** `sourceEditorInitialized = true` is set at ~line 1005, *after* `EnsureCoreWebView2Async` and a
   15s wait. A second tree click during init re-enters and re-subscribes `NavigationCompleted` / `ProcessFailed` /
   `WebMessageReceived` (`+=`, never `-=`) and fires a racing `NavigateToString`. Set the guard before the awaits and
   attach handlers exactly once (extract a one-time init).
3. **Init-timeout wedge.** On the 15s `TimeoutException`, state is left `initialized=true, shellLoaded=false`; the guard
   needs both, so every later load re-runs the whole init body, compounding #2's handler stacking.
4. **Non-deployable WebView2 user-data path.** `CreateWebViewEnvironmentAsync` uses
   `AppPathResolver.FindRepositoryRoot()`, which falls back to `Directory.GetCurrentDirectory()` when `AIMonitor.slnx`
   isn't present (any published/installed build), under a literal `"self-analysis-codex"` copy-paste segment. cwd-dependent
   and wrong off the dev box. Use `LocalApplicationData` or `runtime/` resolved from `AppContext.BaseDirectory` (as the
   Monaco asset path already correctly does).
5. **No `Dispose` override.** `uiToolTip` (a `Component`, not in `Controls`) and the orphaned widgets from #1 are never
   disposed; the WebView2 (owns a browser process + user-data lock) is only disposed transitively.

### MED
6. **Unbounded `File.ReadAllText` into Monaco** (`LoadSourceFile` ~line 911) — a large file is read fully, JSON-serialized,
   and pushed across the WebView2 bridge with no cap → UI hang / memory spike. Add a size/line cap + truncation notice.
7. **Unbounded reference-tree build** (`SetSourceReferenceTree`) — thousands of `IndexedReferenceRow`s each become a
   `TreeNode` with no cap/virtualization.
8. **Fragile signature formatting** (`SimplifyTypeExpression` / `SimplifySignatureTypes`) — `Split(',')` breaks generic
   params (`Dictionary<string,int>`) and `Replace("System.", "")` corrupts identifiers containing that substring.
   Display-only, but renders wrong text.
9. **Test gap.** Persistence round-trip *is* tested (`SolutionIndexStoreTests` writes/reads the new columns; the razor
   kind discrimination is covered). NOT asserted: the Roslyn→snapshot flag *extraction* (`CreateSymbolSnapshot` /
   `GetMethodKind`) and the viewer UI itself.

### LOW / NIT
10. `AreDevToolsEnabled = true` left on in a shipping read-only viewer (~line 974) — likely debug leftover.
11. `ready`/`error` detected by substring match on raw JSON (`Contains("\"ready\"")`) instead of parsing `kind`.
12. `AIMonitor.Data` (incl. new `AddColumnIfMissing`) and `MSBuildWorkspaceLoader.GetFileLineSnippet` use
    `using`/`using var` declarations, which CLAUDE.md forbids for new C# — **pre-existing** style debt, not introduced by
    this commit.
13. **LOW perf** — `IsRazorCodeBlockMappedLine` (`MSBuildWorkspaceLoader.cs:816`) does `File.ReadAllLines` per matched
    Razor reference, and the next line re-reads the same file for the snippet → 2 full reads per razor ref, no per-file
    cache. Bounded to razor refs; matters only on razor-heavy solutions.

## Recommended fix order (control only)

1. Delete/re-host the orphaned `detailTabs`/grids and stop feeding them (#1).
2. One-time WebView2 init: set guard before awaits, attach handlers once (#2/#3).
3. Deployable user-data path; drop the `self-analysis-codex` segment (#4).
4. Add a `Dispose` override (#5).
5. Bound file size and reference counts (#6/#7).

## Meta

Storage/migration discipline here is a clear step up from prior fast drops — the schema path is the right pattern. The
quality risk has moved entirely into the WinForms/WebView2 control, where the incomplete-refactor leak (#1) and the
init-race/wedge (#2/#3) are the kind of thing that looks done and demos fine but degrades over a session. The
`self-analysis-codex` user-data segment (#4) is a concrete "works on my machine" trap to fix before any non-dev build.
