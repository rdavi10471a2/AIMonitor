using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.MSBuild;
using AIMonitor.Workflow;

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
        SolutionIndexSummary summary = await builder.RebuildAsync(settings, cancellationToken);
        new WorkflowEditService(settings).MarkAllIndexesFresh();
        return summary;
    }

    public async Task<SolutionIndexSummary> RefreshProjectsAsync(
        MonitorSettings settings,
        IReadOnlyList<string> projectPaths,
        CancellationToken cancellationToken = default)
    {
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
        SolutionIndexBuilder builder = new(new MSBuildWorkspaceLoader(), store);
        return await builder.RefreshProjectsAsync(settings, projectPaths, cancellationToken);
    }
}
