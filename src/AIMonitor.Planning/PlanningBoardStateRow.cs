namespace AIMonitor.Planning
{
    public sealed class PlanningBoardStateRow
    {
        public string WatchedSolutionPath { get; set; } = string.Empty;

        public string WatchedProjectFolder { get; set; } = string.Empty;

        public string ActiveTaskId { get; set; } = string.Empty;

        public string CreatedAtUtc { get; set; } = string.Empty;

        public string UpdatedAtUtc { get; set; } = string.Empty;
    }
}
