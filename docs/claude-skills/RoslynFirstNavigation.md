# Roslyn First Navigation

Use when a C# task mentions symbols, references, callers, implementations, diagnostics, dependencies, or change impact.

## Rules

- Prefer AIMonitor solution-index, source-map, and symbol tools over text or grep search for C# symbol discovery.
- Use text search only for literal text, comments, strings, generated artifacts, non-C# files, or Roslyn failure fallback.
- Do not guess argument names. Read `get_tool_manifest` or the live MCP schema.
- Start broad, then narrow: symbol search, type overview, references/callers/impact.
- Apply this per target file or edit cycle. Do not shortcut with "I already discovered this earlier" when the target file or coupled edit set changes.
- Treat empty `find_indexed_references` / `find_indexed_callers` as a result to verify, not proof of absence, before API renames or signature changes.
- Before writing a call site to a referenced type, load that type's real callable surface with `find_indexed_symbols`, `get_source_map`, or `get_symbol`.
- If pre-merge validation diagnostics expose a missed call site, use text search only as a diagnostic fallback, then return to AIMonitor structure and stage the missed file in the same session.

## Usual Flow

```text
query_solution_index
find_indexed_symbols(text)
get_indexed_symbol(stableSymbolKey)
find_indexed_references(stableSymbolKey)
find_indexed_callers(stableSymbolKey), when behavior/signature changes
find_indexed_relationships(stableSymbolKey), when partials/inheritance/contracts may matter
get_source_map(scope: "file", mode: "selector")
get_symbol(symbolSelectorJson)
```

The index is broad discovery. Source-map and symbol tools are the precise edit surface.
