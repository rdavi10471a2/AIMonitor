namespace AIMonitor.McpServer;

public sealed class AIMonitorSessionPlannedFile
{
    public int Sequence { get; set; }

    public string Path { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string OwningProjectPath { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string StagedRecordId { get; set; } = string.Empty;

    public string Decision { get; set; } = string.Empty;

    public string Classification { get; set; } = string.Empty;

    public string DecidedAtUtc { get; set; } = string.Empty;
}
