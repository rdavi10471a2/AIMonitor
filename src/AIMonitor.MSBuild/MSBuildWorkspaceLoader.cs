using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using MSBuildProject = Microsoft.Build.Evaluation.Project;

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
        Microsoft.CodeAnalysis.Project project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
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
            .Select(project =>
            {
                MSBuildEvaluatedProject evaluatedProject = MSBuildEvaluatedProject.Empty;
                if (!string.IsNullOrWhiteSpace(project.FilePath) && File.Exists(project.FilePath))
                {
                    evaluatedProject = MSBuildEvaluatedProject.Load(project.FilePath);
                }

                MSBuildDocumentSnapshot[] documents = project.Documents
                    .Where(document => document.SourceCodeKind == SourceCodeKind.Regular)
                    .OrderBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase)
                    .Select(document => new MSBuildDocumentSnapshot(
                        document.Name,
                        document.FilePath ?? string.Empty,
                        document.Folders.ToArray()))
                    .ToArray();

                return new MSBuildProjectSnapshot(
                    project.Name,
                    project.FilePath ?? string.Empty,
                    project.Language,
                    evaluatedProject.TargetFramework,
                    evaluatedProject.TargetFrameworks,
                    evaluatedProject.OutputType,
                    evaluatedProject.Sdk,
                    evaluatedProject.AssemblyName,
                    evaluatedProject.RootNamespace,
                    evaluatedProject.Nullable,
                    evaluatedProject.ImplicitUsings,
                    evaluatedProject.LangVersion,
                    documents,
                    project.ProjectReferences
                        .Select(reference => reference.ProjectId.Id.ToString())
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    evaluatedProject.ProjectReferences,
                    evaluatedProject.PackageReferences,
                    evaluatedProject.FrameworkReferences,
                    evaluatedProject.GlobalUsings,
                    project.ParseOptions?.PreprocessorSymbolNames.Order(StringComparer.Ordinal).ToArray() ?? []);
            })
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
    string TargetFramework,
    string TargetFrameworks,
    string OutputType,
    string Sdk,
    string AssemblyName,
    string RootNamespace,
    string Nullable,
    string ImplicitUsings,
    string LangVersion,
    IReadOnlyList<MSBuildDocumentSnapshot> Documents,
    IReadOnlyList<string> RoslynProjectReferenceIds,
    IReadOnlyList<MSBuildProjectReferenceSnapshot> ProjectReferences,
    IReadOnlyList<MSBuildPackageReferenceSnapshot> PackageReferences,
    IReadOnlyList<MSBuildFrameworkReferenceSnapshot> FrameworkReferences,
    IReadOnlyList<MSBuildGlobalUsingSnapshot> GlobalUsings,
    IReadOnlyList<string> PreprocessorSymbols);

public sealed record MSBuildDocumentSnapshot(
    string Name,
    string FilePath,
    IReadOnlyList<string> Folders);

public sealed record MSBuildProjectReferenceSnapshot(
    string Include,
    string FullPath);

public sealed record MSBuildPackageReferenceSnapshot(
    string Include,
    string Version);

public sealed record MSBuildFrameworkReferenceSnapshot(
    string Include);

public sealed record MSBuildGlobalUsingSnapshot(
    string Include,
    string Static,
    string Alias);

internal sealed record MSBuildEvaluatedProject(
    string TargetFramework,
    string TargetFrameworks,
    string OutputType,
    string Sdk,
    string AssemblyName,
    string RootNamespace,
    string Nullable,
    string ImplicitUsings,
    string LangVersion,
    IReadOnlyList<MSBuildProjectReferenceSnapshot> ProjectReferences,
    IReadOnlyList<MSBuildPackageReferenceSnapshot> PackageReferences,
    IReadOnlyList<MSBuildFrameworkReferenceSnapshot> FrameworkReferences,
    IReadOnlyList<MSBuildGlobalUsingSnapshot> GlobalUsings)
{
    public static MSBuildEvaluatedProject Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        [],
        [],
        [],
        []);

    public static MSBuildEvaluatedProject Load(string projectPath)
    {
        ProjectCollection collection = new();
        try
        {
            MSBuildProject project = collection.LoadProject(projectPath);
            return new MSBuildEvaluatedProject(
                GetProperty(project, "TargetFramework"),
                GetProperty(project, "TargetFrameworks"),
                GetProperty(project, "OutputType"),
                project.Xml.Sdk ?? string.Empty,
                GetProperty(project, "AssemblyName"),
                GetProperty(project, "RootNamespace"),
                GetProperty(project, "Nullable"),
                GetProperty(project, "ImplicitUsings"),
                GetProperty(project, "LangVersion"),
                GetProjectReferences(project),
                GetPackageReferences(project),
                GetFrameworkReferences(project),
                GetGlobalUsings(project));
        }
        finally
        {
            collection.UnloadAllProjects();
        }
    }

    private static string GetProperty(MSBuildProject project, string propertyName)
    {
        return project.GetPropertyValue(propertyName) ?? string.Empty;
    }

    private static IReadOnlyList<MSBuildProjectReferenceSnapshot> GetProjectReferences(MSBuildProject project)
    {
        return project.GetItems("ProjectReference")
            .Select(item => new MSBuildProjectReferenceSnapshot(
                item.EvaluatedInclude,
                item.GetMetadataValue("FullPath")))
            .OrderBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<MSBuildPackageReferenceSnapshot> GetPackageReferences(MSBuildProject project)
    {
        return project.GetItems("PackageReference")
            .Select(item => new MSBuildPackageReferenceSnapshot(
                item.EvaluatedInclude,
                item.GetMetadataValue("Version")))
            .OrderBy(item => item.Include, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<MSBuildFrameworkReferenceSnapshot> GetFrameworkReferences(MSBuildProject project)
    {
        return project.GetItems("FrameworkReference")
            .Select(item => new MSBuildFrameworkReferenceSnapshot(item.EvaluatedInclude))
            .OrderBy(item => item.Include, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<MSBuildGlobalUsingSnapshot> GetGlobalUsings(MSBuildProject project)
    {
        return project.GetItems("Using")
            .Select(item => new MSBuildGlobalUsingSnapshot(
                item.EvaluatedInclude,
                item.GetMetadataValue("Static"),
                item.GetMetadataValue("Alias")))
            .OrderBy(item => item.Include, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
