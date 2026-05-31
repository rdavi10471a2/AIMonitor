namespace AIMonitor.Runtime;

public sealed class DiffLaunchRequest
{
    public string OriginalFilePath { get; set; } = string.Empty;

    public string ProposedFilePath { get; set; } = string.Empty;

    public string? ExplicitToolPath { get; set; }
}
