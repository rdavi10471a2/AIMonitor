namespace AIMonitor.Core
{
    public sealed record PlanningSettings(
        bool Enabled,
        string DatabasePath,
        string TaskMemoryRoot)
    {
        public static PlanningSettings Disabled()
        {
            return new PlanningSettings(false, string.Empty, string.Empty);
        }
    }
}
