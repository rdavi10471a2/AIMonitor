using AIMonitor.Core;

namespace AIMonitor.Planning
{
    public static class PlanningPaths
    {
        public static string GetDefaultBoardDatabasePath(MonitorSettings settings)
        {
            return Path.Combine(
                GetPlanningRoot(settings),
                "board.sqlite");
        }

        public static string GetDefaultTaskMemoryRoot(MonitorSettings settings)
        {
            return Path.Combine(
                GetPlanningRoot(settings),
                "task-memory");
        }

        private static string GetPlanningRoot(MonitorSettings settings)
        {
            return Path.Combine(
                MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
                "planning");
        }
    }
}
