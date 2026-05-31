using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace AIMonitor.MSBuild;

public sealed class MSBuildWorkspaceLoader
{
    private static readonly object RegistrationGate = new();
    private static bool registrationAttempted;

    public async Task<MSBuildSolutionSnapshot> OpenSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        EnsureMSBuildRegistered();

        using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        Solution solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        return CreateSnapshot(solutionPath, solution, workspace.Diagnostics);
    }

    public async Task<MSBuildSolutionSnapshot> OpenProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        EnsureMSBuildRegistered();

        using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        Project project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
        return CreateSnapshot(projectPath, project.Solution, workspace.Diagnostics);
    }

    private static void EnsureMSBuildRegistered()
    {
        lock (RegistrationGate)
        {
            if (registrationAttempted)
            {
                return;
            }

            registrationAttempted = true;
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }
        }
    }

    private static MSBuildSolutionSnapshot CreateSnapshot(
        string inputPath,
        Solution solution,
        IEnumerable<WorkspaceDiagnostic> diagnostics)
    {
        MSBuildProjectSnapshot[] projects = solution.Projects
            .OrderBy(project => project.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(project => new MSBuildProjectSnapshot(
                project.Name,
                project.FilePath ?? string.Empty,
                project.Language,
                project.Documents.Count(document => document.SourceCodeKind == SourceCodeKind.Regular),
                project.ParseOptions?.PreprocessorSymbolNames.Order(StringComparer.Ordinal).ToArray() ?? []))
            .ToArray();

        string[] diagnosticMessages = diagnostics
            .Select(diagnostic => diagnostic.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();

        return new MSBuildSolutionSnapshot(
            Path.GetFullPath(inputPath),
            projects,
            diagnosticMessages);
    }
}

public sealed record MSBuildSolutionSnapshot(
    string InputPath,
    IReadOnlyList<MSBuildProjectSnapshot> Projects,
    IReadOnlyList<string> Diagnostics);

public sealed record MSBuildProjectSnapshot(
    string Name,
    string ProjectPath,
    string Language,
    int RegularDocumentCount,
    IReadOnlyList<string> PreprocessorSymbols);
