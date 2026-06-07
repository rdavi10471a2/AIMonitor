namespace AIMonitor.Planning
{
    public sealed class PlanningIterationUpdateResult
    {
        public bool Updated { get; set; }

        public string TaskId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string IterationGoal { get; set; } = string.Empty;

        public PlanningIterationRow? Iteration { get; set; }

        public string TaskMemoryMarkdownPath { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }
}
