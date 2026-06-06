---
status: open
type: analysis
created: 2026-06-06
audience: operator + Codex
scope: full-coverage (every indexed symbol) token-cost baseline, MCP semantic index vs grep+read
harness: tests/unit/AIMonitor.Data.Tests/McpVsGrepTokenBenchmarkTests.cs (local-only, File.Exists-gated)
data: runtime/token-benchmark/baseline.csv + baseline-summary.txt
relates: McpVsGrepTokenAnalysis-2026-06-06.md (the 16-symbol sampled precursor)
note: this is the BEFORE-fix baseline; re-run after the two index fixes land and diff the summary
---

# MCP index vs grep+read — full-coverage token baseline

## What this is

The sampled analysis (16 symbols) is here generalized to **every symbol in the index** — 2,688 symbols on the
isolated `SchemaStudioBench` copy — to remove any "cherry-picked" objection. For each symbol the harness measures the
context an agent must ingest to answer "where is X defined + all its references":

- **MCP arm** — the real tool path: `FindSymbols` (FindIndexedSymbols) + `ListReferences().Take(500)`
  (FindIndexedReferences), serialized as compact camelCase JSON. For members it queries the qualified `Type.Member`
  first and only falls back to the bare name if that returns nothing (today's behavior), modeling the real agent flow.
- **grep arm** — in-process case-sensitive `\b` word-boundary search reproducing `rg -n -w` (match lines) and `rg -C3`
  (context) byte output, plus the size of every distinct hit file. Runs without an external ripgrep binary so the test
  is portable.

**The grep arm was validated against real ripgrep** on three symbols — byte counts matched within ~0.2% (CRLF/separator
formatting only):

| symbol | harness grep-full | real `rg` grep-full | files |
|---|--:|--:|:--:|
| SchemaObjectColumnDefinition | 467,795 | 467,815 | 17=17 |
| DatabaseDomainDeleteResult | 15,914 | 15,916 | 2=2 |
| IViewDefinitionProvider | 29,938 | 29,942 | 4=4 |

## Baseline results (before fixes) — all 2,688 symbols, 0 errors

| metric | value |
|---|--:|
| MCP total | 8,376,909 tok |
| grep-full total | 167,602,982 tok → **20.0× aggregate** |
| grep-min total | 48,332,417 tok → 5.8× aggregate |
| median per-symbol full ratio | **17.8×** |
| MCP cheaper than grep-full | **95.6%** of symbols (2569/2688) |
| MCP cheaper than grep-min | 75.0% (2016/2688) |

tokens = bytes / 4.

### Honest reading
- **grep-full is an upper bound** — it assumes you read every file a grep hit. Ultra-common identifiers (`Name`,
  `Include`, `Text`, `Columns`) push grep-full to ~2.2M tokens each (grepping `Name` ≈ reading half the repo), which
  inflates the *aggregate*. The robust per-symbol figure is the **median ≈ 18×**.
- The conservative floor — **grep-min** (the cheap `rg -C3` candidate view that isn't even a real answer) — is still
  **5.8× aggregate**, and MCP is cheaper than it on **75%** of symbols.
- Defensible headline: **MCP costs ~10× fewer tokens at minimum, ~18–20× on a realistic read-the-files workflow,
  across every symbol in the project — and is correct where grep structurally can't be** (member nav, homonyms,
  word-boundary/plural, relationship edges, reflection — see the sampled analysis for the correctness evidence).

## The before/after is primed (fix headroom)

- **Fix #1 (qualified `Type.Member` lookup): 100% headroom.** Every one of the **2,533 members** has its qualified
  `Type.Member` query return **0 today**, forcing a fallback to the bare member name that ingests up to 100 homonyms
  (`DatabaseId` exists on 10 types, `GetByIdAsync` on 8, etc.). After the fix these resolve directly; the 8.38M MCP
  total should fall and the "qualified returns 0" counter should drop toward ~0.
- **Fix #2 (leaner reference rows)** shrinks the references payload, which dominates MCP cost for high-fanout symbols.

## After fixes (commit e185fb5) — MEASURED

Both fixes merged; identical harness rebuilt + rerun (only harness change: mirror the tool's new default **lean**
reference shape, since fix #2 trims in the tool layer the harness bypasses). All 2,688 symbols:

| metric | before | after | Δ |
|---|--:|--:|--:|
| MCP total tokens | 8,376,909 | 4,346,062 | **−48%** (nearly halved) |
| aggregate grep-full / MCP | 20.0× | 38.6× | ~2× |
| aggregate grep-min / MCP | 5.8× | 11.1× | ~2× |
| median per-symbol full ratio | 17.8× | 32.0× | |
| MCP cheaper than grep-full | 95.6% | 96.3% | |
| MCP cheaper than grep-min | 75.0% | 91.0% | |
| members forcing homonym fallback | 2,533 (100%) | 91 (3.6%) | fix #1 resolved 96.4% |

- **Fix #1 (qualified `Type.Member` lookup)** cut forced-homonym fallbacks 2,533 → 91; a `Name` member dropped from
  ~30k tok to ~289 tok.
- **Fix #2 (lean reference rows)** trimmed `projectPath` + `fileContentHash` from every row (now default
  `responseShape: "lean"`).
- **Combined: MCP context cost ~halved, advantage ~doubled.** Conservative floor (vs grep's candidate view) is now
  11× aggregate, MCP winning 91% of symbols outright.
- **Residual:** 91 members (3.6%) still fall back — qualified lookup did not resolve them (nested types / matching edge
  cases). Optional follow-up toward ~0.

After-fix data: `runtime/token-benchmark-afterfix/`. Reproduce either side with
`dotnet test tests/unit/AIMonitor.Data.Tests/AIMonitor.Data.Tests.csproj --filter FullCoverage_mcp_vs_grep_token_baseline`
(set `BENCH_OUT` to keep both).
