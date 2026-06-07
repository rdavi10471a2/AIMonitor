namespace AIMonitor.Indexing;

public sealed class ReviewDecisionSessionProgress
{
    public string SessionId { get; set; } = string.Empty;

    public bool HasPlan { get; set; }

    public string TaskId { get; set; } = string.Empty;

    public string IterationId { get; set; } = string.Empty;

    public int PlannedFileCount { get; set; }

    public int DecidedFileCount { get; set; }

    public int CurrentFileSequence { get; set; }

    public string CurrentFileRelativePath { get; set; } = string.Empty;

    public string CurrentFileStatus { get; set; } = string.Empty;

    public bool CurrentFileMatchedPlan { get; set; }

    public bool IsSessionComplete { get; set; }

    public string Message { get; set; } = string.Empty;
}
