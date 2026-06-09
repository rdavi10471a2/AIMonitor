# AIMonitor.Documentation

## Purpose

Plan and eventually implement generated source documentation for selected folders.

## Inputs

- Selected folder.
- `.cs` source files in that folder.
- Fresh solution index evidence when available.
- Source search evidence for weak caller/consumer discovery.
- Existing tests and feature maps when relevant.

## Outputs

- `Docs/Folder.aim.md`.
- One `Docs/<FileName>.aim.md` file per documented source file.
- `Docs/manifest.aim.json` freshness and evidence records.

## Data Flow

```text
selected folder
  -> documentation evidence gathering
  -> Docs/*.aim.md Working candidates
  -> existing safe edit staging/review workflow
  -> operator accept/reject in WinMerge
  -> manifest freshness evidence
```

## Owns

- Folder-local generated documentation shape.
- Documentation evidence labels.
- Freshness manifest planning.
- Best-effort caller/consumer documentation rules.

## Does Not Own

- Watched-source mutation.
- Staging, WinMerge launch, or decision classification.
- Solution index construction.
- Generic MCP base extraction.

## Key Tests

- Planned: manifest path mapping and freshness classification.
- Planned: no direct watched-source mutation for generated docs.
- Planned: caller evidence labels for indexed, grep-backed, and weak evidence.
