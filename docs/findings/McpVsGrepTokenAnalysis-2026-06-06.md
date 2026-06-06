---
status: open
type: analysis
created: 2026-06-06
audience: operator + Codex
scope: empirical proof that answering code-navigation via the AIMonitor MCP semantic index costs far fewer context tokens (and is more correct) than grep+read
method: per-symbol measurement of context bytes ingested to answer "where is X defined + all its references", MCP arm vs grep arm, across two datasets
reproducible: yes — re-run the workflow (mcp-token-analysis) against any indexed solution; grep arm is plain ripgrep + file-size sums
---

# MCP semantic index vs grep+read — token-cost analysis

## Claim under test

"Using the MCP index actually saves tokens." Operationalized as: **the context (in bytes → tokens ÷4) an agent must ingest to fully answer a navigation question — where is symbol X defined, and where are all its references** — via MCP (`find_indexed_symbols` → `find_indexed_references`) versus without an index (ripgrep, then read the files to confirm references and see the definition).

Two datasets:
- **Real-world** — 8 symbols on the live SchemaStudio app (messy production code).
- **Bench** — 10 symbols on an isolated, freshly-indexed copy (`SchemaStudioBench`), spanning kinds (class, enum, interface, record, method, property) and high→low fanout. Index verified identical to live (2,688 symbols / 10,460 refs / 0 diagnostics).

Grep is measured two ways: **min** = `rg -C3` candidate output only (no definition body, still ambiguous); **full** = `rg` output + reading every file that has a hit (what you actually do to confirm references and read the definition).

## Headline

| Dataset | MCP tok | grep-min tok | grep-full tok | MCP vs min | MCP vs full |
|---|--:|--:|--:|--:|--:|
| Real-world (7 measured) | 8,690 | 17,657 | 119,384 | **2.0×** | **13.7×** |
| Bench (6 measured) | 23,029 | 35,466 | 232,181 | **1.5×** | **10.1×** |

**MCP is ~1.5–2× cheaper than even the barest grep output, and ~10–14× cheaper than the realistic read-the-files approach.** Per-symbol full-read ratios ranged 5× → 23×.

## But the token ratio understates it — grep is also *wrong*

The bigger result is that for many real navigation questions **grep cannot produce the correct answer at any token cost**, while MCP returns it exactly:

- **Member navigation is impossible for grep.** For `DatabaseModel.DatabaseId` and `SchemaObjectDefinition.DatabaseId`, the qualified `Type.Member` text returns **0 hits** (type and member are on different lines). The only fallback — the bare member name `DatabaseId` — returns **186 hits across 30+ files**, overwhelmingly SQL string literals (`WHERE DatabaseId = @databaseId`), Razor markup (`ValueProperty="DatabaseId"`), reflection strings (`nameof(...)`), and — critically — the **same member name on 10 different types**. MCP returned the exact **5** (DatabaseModel) and **6** (SchemaObjectDefinition) references, each with file:line:caller.
- **Homonym collisions.** `GetByIdAsync` exists on **8 types**, `GetByDatabaseAsync` on **4**, `DatabaseId` on **10**. Grep cannot bind a hit to the declaring type; MCP resolves to the one symbol. (`SchemaObjectRepository.GetByIdAsync` → MCP found the single real call site; grep would have to wade through all 8 homonyms.)
- **Word-boundary misses.** `SourceTable` (the class) is consumed mostly through the `SourceTables` (plural) collection property — ~25 real usages. `rg -w "SourceTable"` matches none of them (boundary breaks at the trailing `s`). MCP resolves them by type.
- **Relationship edges grep can't derive.** For interface `IViewDefinitionProvider`, MCP returned an `inherits_from` row (the implementing `ViewDefinitionProvider`) and the construction site where the concrete class is assigned to the interface field — references a text search for the interface name never finds.
- **Reflection / target-typed `new()` / Dapper mapping.** `typeof(X)`, `nameof(X.Member)`, `return new()`, and `QueryAsync<X>` member binding are all real type/member references grep can't see and the index resolves.
- **Log/string/comment noise.** On the real-world tree, `SourceTable` and `IViewDefinitionProvider` grep hits were polluted by build `.log` files (CS8618 compiler-warning text) and `[AIFileContext(...)]` attribute strings — all excluded by the index.

## Honest nuances (where grep "looked" cheaper, and an MCP cost to fix)

- For two **method** targets (`GetByDatabaseAsync`, `GetByIdAsync`), grep-**min** tokens were *lower* than MCP — but only because grep produced an **ambiguous** answer it couldn't actually resolve (3 hits among 4–8 same-named methods). Grep-**full** was still 5–14× more than MCP.
- The reason MCP cost more on those: `find_indexed_symbols` matches the **simple name**, so a `Type.Member` query first returns **0**, forcing a refine to the bare name that returns **all overloads** (e.g. 8 `GetByIdAsync` definitions, ~8 KB) just to pick one. This is a real, recordable **MCP improvement opportunity**: support a qualified `Type.Member` lookup (or filter by `containingType`) so member navigation doesn't ingest every homonym. Even so, MCP stayed far under grep-full.
- The MCP cost is dominated (~90–98%) by the **references payload**: each row carries `projectPath`, `filePath`, line/column, `referenceKind`, `snippet`, caller name/kind, and a 64-char `fileContentHash`. For very high-fanout symbols (e.g. `SchemaObjectColumnDefinition`, 83 refs → ~51 KB) the references response is large — still 9× cheaper than reading the 17 files grep touches, but a leaner row shape (drop the per-row content hash / project path) would cut MCP cost materially. **Second recordable improvement opportunity.**

## Method / reproducibility

- Both arms hit the same source tree. MCP arm = `find_indexed_symbols` + `find_indexed_references`, measured by total characters of the JSON tool responses. Grep arm = `rg -n -w` (and `-C3`) output bytes + summed `wc -c` of distinct hit files; no MCP, no file reads.
- Run via the `mcp-token-analysis` workflow (picker → per-symbol parallel MCP/grep measurement agents). Re-runnable against any indexed solution.
- Caveat: agent-reported MCP `responseChars` are precise-to-±escaping; grep byte counts are exact (`wc -c`). The effect sizes (1.5×–23×, plus the qualitative correctness gaps) dwarf any counting noise. 4 of the bench MCP-arm agents errored on the hardest queries and were re-measured by hand (results above).

## Bottom line

For real code-navigation questions, the MCP index is both **cheaper** (≈1.5–2× vs minimal grep, ≈10–14× vs realistic grep+read) and **correct where grep structurally cannot be** (member navigation, homonyms, word-boundary/plural, relationship edges, reflection, construction). The token saving is real; the correctness gap is the stronger argument. Two concrete MCP improvements fall out: (1) qualified `Type.Member` lookup to avoid ingesting all homonyms; (2) a leaner reference-row shape for high-fanout symbols.
