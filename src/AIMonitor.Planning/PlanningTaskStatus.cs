namespace AIMonitor.Planning
{
    public static class PlanningTaskStatus
    {
        public const string Backlog = "Backlog";
        public const string Ready = "Ready";
        public const string Current = "Current";
        public const string Paused = "Paused";
        public const string Closed = "Closed";
        public const string Canceled = "Canceled";
        public const string Done = "Done";

        public static bool IsKnown(string status)
        {
            return status.Equals(Backlog, StringComparison.Ordinal)
                || status.Equals(Ready, StringComparison.Ordinal)
                || status.Equals(Current, StringComparison.Ordinal)
                || status.Equals(Paused, StringComparison.Ordinal)
                || status.Equals(Closed, StringComparison.Ordinal)
                || status.Equals(Canceled, StringComparison.Ordinal)
                || status.Equals(Done, StringComparison.Ordinal);
        }

        public static bool IsTerminal(string status)
        {
            return status.Equals(Closed, StringComparison.Ordinal)
                || status.Equals(Canceled, StringComparison.Ordinal)
                || status.Equals(Done, StringComparison.Ordinal);
        }
    }
}
