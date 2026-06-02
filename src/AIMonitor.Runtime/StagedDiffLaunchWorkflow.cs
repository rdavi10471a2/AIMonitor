using AIMonitor.Core;
using AIMonitor.Logging;
using AIMonitor.Workflow;

namespace AIMonitor.Runtime;

public sealed class StagedDiffLaunchWorkflow
{
    public StagedDiffLaunchWorkflowResult Launch(
        MonitorSettings settings,
        IMonitorLogger logger,
        WorkflowEditService workflowService,
        string stagedRecordId,
        string source,
        string? diffToolPath = null,
        bool forceValidation = false,
        bool verbose = false)
    {
        StagedEditRecord record = workflowService.GetStagedRecord(stagedRecordId);
        PreMergeValidationResult validation = new PreMergeValidationService().Validate(settings, record);
        string validationPrompt = "";
        if (validation.IsError && !forceValidation && PreMergeValidationOverridePrompt.CanShow())
        {
            forceValidation = PreMergeValidationOverridePrompt.Prompt(validation.Diagnostics);
            validationPrompt = forceValidation ? "approved" : "cancelled";
        }

        record = workflowService.RecordPreMergeValidation(record.StagedRecordId, validation, forceValidation);
        logger.Write(
            validation.IsError ? MonitorLogLevel.Warning : MonitorLogLevel.Information,
            source,
            "premerge.validation.completed",
            validation.Message,
            new Dictionary<string, string>
            {
                ["stagedRecordId"] = record.StagedRecordId,
                ["watchedFilePath"] = record.WatchedFilePath,
                ["relativePath"] = record.RelativePath,
                ["validationStatus"] = validation.Status,
                ["diagnosticCount"] = validation.DiagnosticCount.ToString(),
                ["validationWorkspacePath"] = validation.ValidationWorkspacePath,
                ["forceValidation"] = forceValidation.ToString().ToLowerInvariant(),
                ["validationPrompt"] = validationPrompt,
                ["isError"] = validation.IsError.ToString().ToLowerInvariant()
            });

        if (validation.IsError && !forceValidation)
        {
            StagedEditRecord blocked = workflowService.RecordDiffLaunch(
                record.StagedRecordId,
                launched: false,
                "Pre-merge validation failed. WinMerge launch is blocked unless force validation is used after human approval.");
            return new StagedDiffLaunchWorkflowResult
            {
                StagedRecordSummary = workflowService.CreateSummary(blocked),
                StagedRecord = verbose ? blocked : null,
                PreMergeValidation = validation,
                DiffLaunch = new DiffLaunchResult
                {
                    Launched = false,
                    Tool = "WinMerge",
                    ToolPath = string.Empty,
                    ProcessId = 0,
                    Message = "Pre-merge validation failed. Human approval is required before force-launching WinMerge."
                },
                NextStep = PreMergeValidationOverridePrompt.CanShow()
                    ? "Human cancelled validation override. Fix and restage before launching WinMerge."
                    : "Validation failed and no interactive dialog is available. Ask the user whether to override; rerun with force validation only after explicit approval."
            };
        }

        record = workflowService.PrepareReviewFileForLaunch(record.StagedRecordId);
        DiffLaunchResult launch = new WinMergeDiffToolLauncher().Launch(new DiffLaunchRequest
        {
            OriginalFilePath = string.IsNullOrWhiteSpace(record.ReviewBaselineFilePath)
                ? record.WatchedFilePath
                : record.ReviewBaselineFilePath,
            ProposedFilePath = record.StagedFilePath,
            ExplicitToolPath = diffToolPath,
            CandidateToolPaths = settings.WinMergeCandidatePaths
        });
        StagedEditRecord updated = workflowService.RecordDiffLaunch(record.StagedRecordId, launch.Launched, launch.Message);
        return new StagedDiffLaunchWorkflowResult
        {
            StagedRecordSummary = workflowService.CreateSummary(updated),
            StagedRecord = verbose ? updated : null,
            PreMergeValidation = validation,
            DiffLaunch = launch,
            NextStep = record.IsNewFile
                ? "After WinMerge review, save the staged candidate into watched source for accept, or leave watched source absent for reject. Then record the diff decision."
                : "After WinMerge review, save the staged candidate into the watched source for accept, or leave watched source unchanged for reject. Then record the diff decision."
        };
    }
}
