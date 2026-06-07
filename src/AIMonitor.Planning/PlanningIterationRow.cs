namespace AIMonitor.Planning
{
    public sealed class PlanningIterationRow
    {
        public string IterationId { get; set; } = string.Empty;

        public string TaskId { get; set; } = string.Empty;

        public int Sequence { get; set; }

        public string Goal { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string CreatedAtUtc { get; set; } = string.Empty;

        public string CompletedAtUtc { get; set; } = string.Empty;
    }
}
