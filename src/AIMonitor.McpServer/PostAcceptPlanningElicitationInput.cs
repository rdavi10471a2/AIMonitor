using System.ComponentModel;

namespace AIMonitor.McpServer;

public sealed class PostAcceptPlanningElicitationInput
{
    [Description("Whether the current Planning iteration is complete after the accepted review.")]
    public bool CurrentIterationComplete { get; set; }

    [Description("What Planning should do after the accepted review.")]
    public PostAcceptPlanningNextAction NextAction { get; set; }

    [Description("One compact goal line for append-next-iteration or replace-current-iteration.")]
    public string NextIterationGoal { get; set; } = string.Empty;

    [Description("Required note for completing an iteration or moving the task to paused, closed, or canceled.")]
    public string StatusNote { get; set; } = string.Empty;
}
