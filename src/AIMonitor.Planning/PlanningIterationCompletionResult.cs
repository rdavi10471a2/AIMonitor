namespace AIMonitor.Planning
{
    public sealed class PlanningIterationCompletionResult
    {
        public bool Completed { get; set; }

        public string TaskId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string IterationId { get; set; } = string.Empty;

        public PlanningIterationRow? Iteration { get; set; }

        public string TaskMemoryMarkdownPath { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }
}
