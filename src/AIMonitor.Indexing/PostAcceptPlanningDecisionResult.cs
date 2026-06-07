using AIMonitor.Planning;

namespace AIMonitor.Indexing;

public sealed class PostAcceptPlanningDecisionResult
{
    public bool Required { get; set; }

    public bool ElicitationAttempted { get; set; }

    public bool ElicitationAccepted { get; set; }

    public string ElicitationAction { get; set; } = string.Empty;

    public bool Pending { get; set; }

    public string PendingDecisionId { get; set; } = string.Empty;

    public string ResolverTool { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public PostAcceptPlanningActionResult? AppliedAction { get; set; }
}
