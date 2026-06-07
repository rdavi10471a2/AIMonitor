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

    public async Task<SolutionIndexSummary> RefreshProjectsAsync(
        MonitorSettings settings,
        IReadOnlyList<string> projectPaths,
        CancellationToken cancellationToken = default)
    {
        string[] normalizedProjectPaths = projectPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedProjectPaths.Length == 0)
        {
            throw new ArgumentException("At least one project path is required.", nameof(projectPaths));
        }

        List<MSBuildProjectSnapshot> projects = [];
        List<string> diagnostics = [];
        foreach (string projectPath in normalizedProjectPaths)
        {
            MSBuildSolutionSnapshot snapshot = await workspaceLoader.OpenProjectAsync(projectPath, cancellationToken);
            diagnostics.AddRange(snapshot.Diagnostics);
            MSBuildProjectSnapshot? project = snapshot.Projects.FirstOrDefault(item =>
                item.ProjectPath.Equals(projectPath, StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                throw new InvalidOperationException("MSBuild did not return the requested project: " + projectPath);
            }

            projects.Add(project);
        }

        MSBuildSolutionSnapshot projectSnapshot = new(
            Path.GetFullPath(settings.WatchedSolutionPath),
            projects
                .GroupBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            diagnostics);
        return store.ReplaceProjects(projectSnapshot);
    }
}
