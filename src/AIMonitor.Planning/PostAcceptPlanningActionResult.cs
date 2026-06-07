namespace AIMonitor.Planning
{
    public sealed class PostAcceptPlanningActionResult
    {
        public bool Applied { get; set; }

        public string Action { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public PlanningIterationCompletionResult? IterationCompletion { get; set; }

        public PlanningIterationAppendResult? IterationAppend { get; set; }

        public PlanningIterationUpdateResult? IterationUpdate { get; set; }

        public PlanningTaskRow? TaskStatusChange { get; set; }
    }
}
