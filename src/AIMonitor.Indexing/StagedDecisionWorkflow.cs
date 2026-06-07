using AIMonitor.Core;
using AIMonitor.Logging;
using AIMonitor.Planning;
using AIMonitor.Workflow;

namespace AIMonitor.Indexing;

public sealed class StagedDecisionWorkflow
{
    public ReviewDecisionWithIndexRefreshResult Record(
        MonitorSettings settings,
        IMonitorLogger logger,
        WorkflowEditService workflowService,
        string stagedRecordId,
        string decision,
        string? expectedStagedHash,
        string source,
        bool verbose = false)
    {
        StagedEditRecord existing = workflowService.GetStagedRecord(stagedRecordId);
        WorkflowEditService.EnsureRecordNotDecided(existing);

        StagedEditRecord record = workflowService.RecordDecision(stagedRecordId, decision, expectedStagedHash);
        PostAcceptIndexRefreshResult? indexRefresh = null;
        if (record.Classification is "accepted" or "accepted-normalized")
        {
            indexRefresh = new PostAcceptIndexRefreshService().RebuildAfterAcceptedDecision(
                settings,
                logger,
                record,
                source);
        }

        PlanningEvidenceAttachmentResult planningEvidence = new PlanningService(settings)
            .AttachWorkflowDecisionToCurrentTask(CreatePlanningEvidence(record, indexRefresh));
        StagedEditSummary summary = workflowService.CreateSummary(record);
        return new ReviewDecisionWithIndexRefreshResult
        {
            StagedRecordId = record.StagedRecordId,
            WatchedFilePath = record.WatchedFilePath,
            RelativePath = record.RelativePath,
            Decision = record.Decision,
            Classification = record.Classification,
            Status = record.Status,
            Message = record.Message,
            StagedRecordSummary = summary,
            StagedRecordPath = summary.RecordPath,
            StagedRecord = verbose ? record : null,
            IndexRefresh = indexRefresh,
            PlanningEvidence = planningEvidence,
            NextStep = CreateNextStep(record, indexRefresh)
        };
    }

    private static string CreateNextStep(StagedEditRecord record, PostAcceptIndexRefreshResult? indexRefresh)
    {
        if (indexRefresh?.IsError == true)
        {
            return "Accept recorded, but the index rebuild failed. Index rows are stale. Re-run refresh_solution_index before trusting index queries.";
        }

        return record.Classification is "accepted" or "accepted-normalized"
            ? "Index was rebuilt after accept. Run edit refresh before further edits to this watched file."
            : "Decision recorded. Do not rely on changed index rows unless an accepted decision rebuilt the index.";
    }

    private static PlanningDecisionEvidence CreatePlanningEvidence(
        StagedEditRecord record,
        PostAcceptIndexRefreshResult? indexRefresh)
    {
        return new PlanningDecisionEvidence
        {
            StagedRecordId = record.StagedRecordId,
            SessionId = record.SessionId,
            RelativePath = record.RelativePath,
            WatchedFilePath = record.WatchedFilePath,
            StagedHash = record.StagedHash,
            Decision = record.Decision,
            Classification = record.Classification,
            Status = record.Status,
            Message = record.Message,
            DecidedAtUtc = record.DecisionAtUtc,
            IsNewFile = record.IsNewFile,
            PreMergeValidationStatus = record.PreMergeValidationStatus,
            PreMergeValidationDiagnosticCount = record.PreMergeValidationDiagnosticCount,
            PreMergeValidationForceApproved = record.PreMergeValidationForceApproved,
            IndexRefreshStatus = indexRefresh?.Status ?? string.Empty,
            IndexRefreshIsError = indexRefresh?.IsError == true
        };
    }
}
