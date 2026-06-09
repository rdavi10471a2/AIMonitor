# AIMonitor Documentation

## Purpose

AIMonitor.Documentation is the proposed generated documentation capability for AIMonitor.

The first version should generate per-file Markdown documentation for a selected folder and review those generated docs through the existing AIMonitor safe edit workflow. This is not the `AIMonitorMCPBase` extraction. That idea is tabled.

## Product Thesis

AI-generated code tends to create stable naming and repeated structural patterns. AIMonitor can turn those patterns into useful source-adjacent documentation if the generated prose is tied to current source evidence, source hashes, and review decisions.

Generated docs should be evidence, not lore.

## Scope Decision

Start with selected folders.

Do not start with project-wide generation, class-picker UI, or a standalone generic MCP base. Folder scope is large enough to prove the workflow and small enough to keep review practical.

Initial output shape:

```text
SourceDocs/
  <relative path from repo root>/
    SomeClass.cs.md
    AnotherClass.cs.md
  manifest.json
```

The `SourceDocs` root mirrors the source tree without placing generated docs beside product source files. The manifest records source paths, source hashes, generated timestamps, evidence level, and freshness status.

## Workflow

Use the existing safe edit workflow for generated docs.

```text
SELECT   operator selects a folder
           |
READ     documentation service gathers source, index, tests, docs, and naming evidence
           |
WRITE    generate SourceDocs candidates as monitor-owned Working files
           |
STAGE    stage generated docs as immutable review records
           |
REVIEW   launch WinMerge immediately
           |
DECIDE   operator accepts/rejects generated docs
           |
STATE    accepted docs land in SourceDocs; manifest/freshness evidence updates
```

Key point: "show proposed docs" means go straight to merge/review. The diff tool is the preview surface.

## Authority Boundaries

- Source files and tests are evidence.
- Solution index rows are evidence when fresh.
- Generated docs are derived evidence.
- `SourceDocs/manifest.json` records freshness; it is not a substitute for source.
- WinMerge review remains the human acceptance point.
- `record-decision` remains the durable classification point.
- Documentation generation must not introduce a second mutation path.

## Initial Document Shape

Each generated file doc should be short and consistent:

```markdown
# SomeClass

Source: `src/.../SomeClass.cs`
Source hash: `...`
Generated: `...`
Confidence: source verified

## Purpose

## Owns

## Does Not Own

## Dataflow

## Key Methods

## Invariants

## Evidence
```

The document should prefer dataflow and ownership over broad prose. It should label weak claims instead of smoothing over missing evidence.

## Evidence Levels

Use explicit labels:

```text
source-verified       source file was read and hashed
index-verified        fresh solution index evidence was used
test-backed           matching tests or smoke coverage were found
doc-contract          repo docs state the behavior
name-inferred         meaning inferred from naming/call shape only
weak                  evidence gap remains
stale                 source hash no longer matches manifest
```

## Skill Requirements

The generation skill should:

- identify actors, artifacts, gates, state, authority, and dataflow;
- extract repeated semantic terms from names;
- keep labels short and glossary-backed;
- avoid full-system claims in per-file docs;
- separate implemented evidence from inferred or doc-only claims;
- include an evidence gap section when tests, index freshness, or semantic data are missing.

## MCP / Adapter Surface Ideas

The eventual MCP surface should route through shared services:

```text
generate_source_docs
check_source_docs_freshness
list_source_docs_manifest
explain_source_doc
stage_generated_docs
```

For the first implementation, these tools should still use the normal workflow services for candidate creation, staging, WinMerge launch, and decision classification.

## First Vertical Slice

1. Add `AIMonitor.Documentation` service/project or namespace with SourceDocs manifest models.
2. Implement folder scan for `.cs` files.
3. Generate deterministic Markdown cards from source text and available index/test evidence.
4. Write candidates through the existing Working/new-file workflow.
5. Stage generated docs and launch WinMerge immediately.
6. Record accept/reject decisions through the existing workflow.
7. Add freshness check from manifest source hashes.
8. Add focused tests for manifest mapping, freshness classification, and no direct watched-source mutation.

## Known Risks

- Generated prose can overclaim semantic meaning if evidence labels are not enforced.
- Large folder selections can produce noisy review batches.
- SourceDocs manifest updates must go through the same reviewed path or freshness evidence can drift.
- Razor/Blazor docs must preserve the existing boundary: C# and source-map evidence are strong; markup binding claims need grep/smoke-backed labels.
- A generic `AIMonitorMCPBase` extraction could distract from the first useful product slice and is intentionally deferred.
