using AIMonitor.Core;

namespace AIMonitor.Planning.Tests
{
    public sealed class PlanningPathsTests
    {
        [Fact]
        public void GetDefaultBoardDatabasePath_lives_under_watched_solution_planning_workspace()
        {
            MonitorSettings settings = MonitorSettings.Create(
                "C:\\Monitor",
                "C:\\Watched\\Watched.sln",
                "C:\\Monitor\\runtime");

            string databasePath = PlanningPaths.GetDefaultBoardDatabasePath(settings);
            string workspaceRoot = MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings);

            Assert.Equal(
                Path.Combine(workspaceRoot, "planning", "board.sqlite"),
                databasePath);
        }
    }
}
