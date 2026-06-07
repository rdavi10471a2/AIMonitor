namespace AIMonitor.McpServer;

public sealed class PendingPostAcceptPlanningDecision
{
    public string PendingDecisionId { get; set; } = string.Empty;

    public string StagedRecordId { get; set; } = string.Empty;

    public string TaskId { get; set; } = string.Empty;

    public string TaskTitle { get; set; } = string.Empty;

    public string CurrentIterationId { get; set; } = string.Empty;

    public string CurrentIterationGoal { get; set; } = string.Empty;

    public string CreatedAtUtc { get; set; } = string.Empty;
}
