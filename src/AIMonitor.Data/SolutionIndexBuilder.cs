using AIMonitor.Core;
using AIMonitor.MSBuild;

namespace AIMonitor.Data;

public sealed class SolutionIndexBuilder
{
    private readonly MSBuildWorkspaceLoader workspaceLoader;
    private readonly SolutionIndexStore store;

    public SolutionIndexBuilder(MSBuildWorkspaceLoader workspaceLoader, SolutionIndexStore store)
    {
        this.workspaceLoader = workspaceLoader;
        this.store = store;
    }

    public async Task<SolutionIndexSummary> RebuildAsync(
        MonitorSettings settings,
        CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(settings.WatchedSolutionPath);
        MSBuildSolutionSnapshot snapshot = extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            ? await workspaceLoader.OpenProjectAsync(settings.WatchedSolutionPath, cancellationToken)
            : await workspaceLoader.OpenSolutionAsync(settings.WatchedSolutionPath, cancellationToken);

        return store.SaveSnapshot(snapshot);
    }
}
