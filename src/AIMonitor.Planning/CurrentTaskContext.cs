namespace AIMonitor.Planning
{
    public sealed class CurrentTaskContext
    {
        public bool HasCurrentTask { get; set; }

        public string TaskId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string Goal { get; set; } = string.Empty;

        public string Constraints { get; set; } = string.Empty;

        public string AcceptanceCriteria { get; set; } = string.Empty;

        public PlanningIterationRow? CurrentIteration { get; set; }

        public string CurrentIterationGoal { get; set; } = string.Empty;

        public string IterationSummary { get; set; } = string.Empty;

        public string ReviewEvidenceSummary { get; set; } = string.Empty;

        public string TaskMemoryMarkdownPath { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }
}
