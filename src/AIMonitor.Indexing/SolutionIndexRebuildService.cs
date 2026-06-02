using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.MSBuild;

namespace AIMonitor.Indexing;

public sealed class SolutionIndexRebuildService
{
    public async Task<SolutionIndexSummary> RebuildAsync(
        MonitorSettings settings,
        CancellationToken cancellationToken = default)
    {
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
        SolutionIndexBuilder builder = new(new MSBuildWorkspaceLoader(), store);
        return await builder.RebuildAsync(settings, cancellationToken);
    }
}
