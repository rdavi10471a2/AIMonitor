# AIMonitor.Workflow — component architecture (generated)

> Generated from the solution index via the AIMonitor MCP server.
> **rank 1** in the solution layering. Solution-wide view: [`../Architecture.aim.md`](../Architecture.aim.md).

## Index evidence (project-scoped)

- **Files:** 19
- **Symbols:** 415 (public: 263)
- **Named types:** 39 across 1 namespace(s); public API surface: 34 type(s) + 229 public member(s)

## Place in the layering

```mermaid
flowchart TB
  Workflow -->|28 refs| Core
  Runtime -->|54 refs| Workflow
  Indexing -->|62 refs| Workflow
  App -.->|declared only| Workflow
  Cli -->|32 refs| Workflow
  McpServer -->|158 refs| Workflow
```

### Depends on (outbound)

| Dependency | Live refs (index) | Verdict |
|---|---|---|
| Core | 28 | live (caller/index-verified) |

### Depended on by (inbound)

| Consumer | Live refs into this project | Verdict |
|---|---|---|
| Runtime | 54 | live (caller/index-verified) |
| Indexing | 62 | live (caller/index-verified) |
| App | 0 | declared-only (no public ref — structural/transitive or candidate-dead) |
| Cli | 32 | live (caller/index-verified) |
| McpServer | 158 | live (caller/index-verified) |

## Namespaces & types

- **AIMonitor.Workflow**
  - `CandidateEditValidator` _internal_
  - `CompareSnapshotResult`
  - `EditOverlayDiagnostic`
  - `EditOverlayValidationResult`
  - `EditSessionManifest`
  - `EditSessionStatus`
  - `EditSyntaxDiagnostic`
  - `EditSyntaxValidationResult`
  - `ExternalValidationInputs` _private_
  - `FileHash`
  - `FileLedgerWriter`
  - `ManifestLock` _private_
  - `PreMergeValidationResult`
  - `PreMergeValidationService`
  - `ProcessResult` _private_
  - `ReplaceTextResult`
  - `ReviewDecisionClassifier`
  - `ReviewDecisionInput`
  - `ReviewDecisionResult`
  - `RoslynEditResult`
  - `RoslynEditService`
  - `RoslynFileOutlineItem`
  - `RoslynFileOutlineResult`
  - `RoslynSourceMapAttribute`
  - `RoslynSourceMapDiagnostic`
  - `RoslynSourceMapFile`
  - `RoslynSourceMapNarrowingSuggestion`
  - `RoslynSourceMapNextCall`
  - `RoslynSourceMapResult`
  - `RoslynSourceMapSymbol`
  - `RoslynSymbolReadResult`
  - `RoslynSymbolSelector`
  - `StagedEditRecord`
  - `StagedEditSummary`
  - `TextPosition` _private_
  - `TextSpanResult`
  - `WorkflowEditPaths`
  - `WorkflowEditService`
  - `WorkflowRunRecorder`

## Public API surface (cross-project anchors)

34 public type(s) other projects can bind to:

- `CompareSnapshotResult`
- `EditOverlayDiagnostic`
- `EditOverlayValidationResult`
- `EditSessionManifest`
- `EditSessionStatus`
- `EditSyntaxDiagnostic`
- `EditSyntaxValidationResult`
- `FileHash`
- `FileLedgerWriter`
- `PreMergeValidationResult`
- `PreMergeValidationService`
- `ReplaceTextResult`
- `ReviewDecisionClassifier`
- `ReviewDecisionInput`
- `ReviewDecisionResult`
- `RoslynEditResult`
- `RoslynEditService`
- `RoslynFileOutlineItem`
- `RoslynFileOutlineResult`
- `RoslynSourceMapAttribute`
- `RoslynSourceMapDiagnostic`
- `RoslynSourceMapFile`
- `RoslynSourceMapNarrowingSuggestion`
- `RoslynSourceMapNextCall`
- `RoslynSourceMapResult`
- `RoslynSourceMapSymbol`
- `RoslynSymbolReadResult`
- `RoslynSymbolSelector`
- `StagedEditRecord`
- `StagedEditSummary`
- `TextSpanResult`
- `WorkflowEditPaths`
- `WorkflowEditService`
- `WorkflowRunRecorder`

## Internal coupling (public-surface, intra-project reference sites)

```mermaid
flowchart LR
  f_WorkflowEditService_cs["WorkflowEditService.cs"]
  f_EditSessionManifest_cs["EditSessionManifest.cs"]
  f_WorkflowEditService_cs -->|162| f_EditSessionManifest_cs
  f_StagedEditRecord_cs["StagedEditRecord.cs"]
  f_WorkflowEditService_cs -->|122| f_StagedEditRecord_cs
  f_EditSessionStatus_cs["EditSessionStatus.cs"]
  f_WorkflowEditService_cs -->|76| f_EditSessionStatus_cs
  f_RoslynEditService_cs["RoslynEditService.cs"]
  f_RoslynEditModels_cs["RoslynEditModels.cs"]
  f_RoslynEditService_cs -->|65| f_RoslynEditModels_cs
  f_RoslynEditService_cs -->|44| f_EditSessionStatus_cs
  f_FileHash_cs["FileHash.cs"]
  f_WorkflowEditService_cs -->|38| f_FileHash_cs
  f_PreMergeValidationService_cs["PreMergeValidationService.cs"]
  f_PreMergeValidationResult_cs["PreMergeValidationResult.cs"]
  f_PreMergeValidationService_cs -->|34| f_PreMergeValidationResult_cs
  f_WorkflowEditPaths_cs["WorkflowEditPaths.cs"]
  f_WorkflowEditService_cs -->|31| f_WorkflowEditPaths_cs
  f_CompareSnapshotResult_cs["CompareSnapshotResult.cs"]
  f_WorkflowEditService_cs -->|28| f_CompareSnapshotResult_cs
  f_PreMergeValidationService_cs -->|25| f_StagedEditRecord_cs
  f_CandidateEditValidator_cs["CandidateEditValidator.cs"]
  f_CandidateEditValidator_cs -->|22| f_EditSessionManifest_cs
  f_ReplaceTextResult_cs["ReplaceTextResult.cs"]
  f_WorkflowEditService_cs -->|17| f_ReplaceTextResult_cs
  f_EditValidationResult_cs["EditValidationResult.cs"]
  f_CandidateEditValidator_cs -->|16| f_EditValidationResult_cs
  f_StagedEditSummary_cs["StagedEditSummary.cs"]
  f_WorkflowEditService_cs -->|15| f_StagedEditSummary_cs
  f_TextSpanResult_cs["TextSpanResult.cs"]
  f_WorkflowEditService_cs -->|11| f_TextSpanResult_cs
  f_WorkflowRunRecorder_cs["WorkflowRunRecorder.cs"]
  f_WorkflowEditService_cs -->|10| f_WorkflowRunRecorder_cs
  f_RoslynEditService_cs -->|9| f_WorkflowEditPaths_cs
  f_ReviewDecisionClassifier_cs["ReviewDecisionClassifier.cs"]
  f_WorkflowEditService_cs -->|9| f_ReviewDecisionClassifier_cs
  f_CandidateEditValidator_cs -->|5| f_WorkflowEditPaths_cs
  f_WorkflowEditService_cs -->|5| f_PreMergeValidationResult_cs
  f_CandidateEditValidator_cs -->|4| f_FileHash_cs
  f_RoslynEditService_cs -->|4| f_FileHash_cs
  f_RoslynEditService_cs -->|4| f_WorkflowEditService_cs
  f_WorkflowEditService_cs -->|4| f_EditValidationResult_cs
  f_WorkflowEditService_cs -->|3| f_CandidateEditValidator_cs
```

_Showing the top 25 of 31 intra-project edges by weight. Edge `A --> B` = file A references a public symbol defined in file B._

## Caveats

- **Public-surface only:** coupling and internal edges are built from references whose *target* symbol is `Public`. Intra-project use through `internal`/`private` symbols is not probed, so the internal graph is a **partial** view (real cohesion is higher).
- **Reference-site semantics:** a file consumed only by assembly load / DI / config shows no edge despite a runtime relationship.
- **Self-index, not live-watch:** produced by indexing AIMonitor as a *target* solution. Regenerate-and-diff is stable (same index → byte-identical doc).

