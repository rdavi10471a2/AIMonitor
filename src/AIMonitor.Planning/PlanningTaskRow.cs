namespace AIMonitor.Planning
{
    public sealed class PlanningTaskRow
    {
        public string TaskId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Goal { get; set; } = string.Empty;

        public string HumanContext { get; set; } = string.Empty;

        public string Constraints { get; set; } = string.Empty;

        public string AcceptanceCriteria { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string TaskMemoryMarkdownPath { get; set; } = string.Empty;

        public string CreatedAtUtc { get; set; } = string.Empty;

        public string UpdatedAtUtc { get; set; } = string.Empty;

        public string ClosedAtUtc { get; set; } = string.Empty;
    }
}
