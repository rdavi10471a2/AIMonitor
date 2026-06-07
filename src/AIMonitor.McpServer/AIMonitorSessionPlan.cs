namespace AIMonitor.McpServer;

public sealed class AIMonitorSessionPlan
{
    public string TaskId { get; set; } = string.Empty;

    public string IterationId { get; set; } = string.Empty;

    public string CreatedAtUtc { get; set; } = string.Empty;

    public string UpdatedAtUtc { get; set; } = string.Empty;

    public IReadOnlyList<AIMonitorSessionPlannedFile> FilesPlanned { get; set; } = [];
}
