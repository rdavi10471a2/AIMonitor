using AIMonitor.Core;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        return await CreateSnapshotAsync(solutionPath, solution, workspace.Diagnostics, cancellationToken);
    }

    public async Task<MSBuildSolutionSnapshot> OpenProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        EnsureMSBuildRegistered();

        using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        Microsoft.CodeAnalysis.Project project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
        return await CreateSnapshotAsync(projectPath, project.Solution, workspace.Diagnostics, cancellationToken);
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

    private static async Task<MSBuildSolutionSnapshot> CreateSnapshotAsync(
        string inputPath,
        Solution solution,
        IEnumerable<WorkspaceDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        List<MSBuildProjectSnapshot> projects = [];
        foreach (Microsoft.CodeAnalysis.Project project in solution.Projects.OrderBy(project => project.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Compilation? compilation = await project.GetCompilationAsync(cancellationToken);
            ProjectSymbolIndex symbolIndex = compilation is null
                ? ProjectSymbolIndex.Empty
                : await ProjectSymbolIndex.BuildAsync(project, compilation, cancellationToken);

            MSBuildEvaluatedProject evaluatedProject = MSBuildEvaluatedProject.Empty;
            if (!string.IsNullOrWhiteSpace(project.FilePath) && File.Exists(project.FilePath))
            {
                evaluatedProject = MSBuildEvaluatedProject.Load(project.FilePath);
            }

            string stableProjectKey = StableIdentifier.FromParts(
                "project",
                project.FilePath ?? string.Empty,
                project.Language,
                evaluatedProject.TargetFramework,
                evaluatedProject.TargetFrameworks);

            MSBuildDocumentSnapshot[] documents = project.Documents
                .Where(document => document.SourceCodeKind == SourceCodeKind.Regular)
                .OrderBy(document => document.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(document => new MSBuildDocumentSnapshot(
                    StableIdentifier.FromParts("document", stableProjectKey, document.FilePath ?? string.Empty),
                    document.Name,
                    document.FilePath ?? string.Empty,
                    document.Folders.ToArray()))
                .ToArray();

            projects.Add(new MSBuildProjectSnapshot(
                stableProjectKey,
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
                symbolIndex.Symbols,
                symbolIndex.References,
                project.ProjectReferences
                    .Select(reference => reference.ProjectId.Id.ToString())
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                evaluatedProject.ProjectReferences,
                evaluatedProject.PackageReferences,
                evaluatedProject.FrameworkReferences,
                evaluatedProject.GlobalUsings,
                project.ParseOptions?.PreprocessorSymbolNames.Order(StringComparer.Ordinal).ToArray() ?? []));
        }

        MSBuildProjectSnapshot[] orderedProjects = projects
            .OrderBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string[] diagnosticMessages = diagnostics
            .Select(diagnostic => diagnostic.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();

        return new MSBuildSolutionSnapshot(
            Path.GetFullPath(inputPath),
            orderedProjects,
            diagnosticMessages);
    }
}

public sealed record MSBuildSolutionSnapshot(
    string InputPath,
    IReadOnlyList<MSBuildProjectSnapshot> Projects,
    IReadOnlyList<string> Diagnostics);

public sealed record MSBuildProjectSnapshot(
    string StableProjectKey,
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
    IReadOnlyList<MSBuildSymbolSnapshot> Symbols,
    IReadOnlyList<MSBuildReferenceSnapshot> References,
    IReadOnlyList<string> RoslynProjectReferenceIds,
    IReadOnlyList<MSBuildProjectReferenceSnapshot> ProjectReferences,
    IReadOnlyList<MSBuildPackageReferenceSnapshot> PackageReferences,
    IReadOnlyList<MSBuildFrameworkReferenceSnapshot> FrameworkReferences,
    IReadOnlyList<MSBuildGlobalUsingSnapshot> GlobalUsings,
    IReadOnlyList<string> PreprocessorSymbols);

public sealed record MSBuildDocumentSnapshot(
    string StableDocumentKey,
    string Name,
    string FilePath,
    IReadOnlyList<string> Folders);

public sealed record MSBuildSymbolSnapshot(
    string StableKey,
    string Name,
    string Kind,
    string Namespace,
    string ContainingType,
    string FilePath,
    int StartLine,
    int EndLine,
    string Signature);

public sealed record MSBuildReferenceSnapshot(
    string TargetStableKey,
    string FilePath,
    int Line,
    int Column,
    string ReferenceKind,
    string Snippet);

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

internal sealed class ProjectSymbolIndex
{
    private ProjectSymbolIndex(
        IReadOnlyList<MSBuildSymbolSnapshot> symbols,
        IReadOnlyList<MSBuildReferenceSnapshot> references)
    {
        Symbols = symbols;
        References = references;
    }

    public static ProjectSymbolIndex Empty { get; } = new([], []);

    public IReadOnlyList<MSBuildSymbolSnapshot> Symbols { get; }

    public IReadOnlyList<MSBuildReferenceSnapshot> References { get; }

    public static async Task<ProjectSymbolIndex> BuildAsync(
        Microsoft.CodeAnalysis.Project project,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        Dictionary<ISymbol, MSBuildSymbolSnapshot> declared = new(SymbolEqualityComparer.Default);
        foreach (Document document in project.Documents.Where(document => document.SourceCodeKind == SourceCodeKind.Regular))
        {
            SyntaxTree? tree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (tree is null)
            {
                continue;
            }

            SemanticModel model = compilation.GetSemanticModel(tree);
            SyntaxNode root = await tree.GetRootAsync(cancellationToken);
            foreach (SyntaxNode node in root.DescendantNodes())
            {
                ISymbol? symbol = GetDeclaredSymbol(model, node, cancellationToken);
                if (symbol is null || declared.ContainsKey(symbol))
                {
                    continue;
                }

                declared[symbol] = CreateSymbolSnapshot(symbol, node, document.FilePath ?? string.Empty, tree);
            }
        }

        List<MSBuildReferenceSnapshot> references = [];
        foreach (Document document in project.Documents.Where(document => document.SourceCodeKind == SourceCodeKind.Regular))
        {
            SyntaxTree? tree = await document.GetSyntaxTreeAsync(cancellationToken);
            if (tree is null)
            {
                continue;
            }

            SemanticModel model = compilation.GetSemanticModel(tree);
            SyntaxNode root = await tree.GetRootAsync(cancellationToken);
            foreach (SyntaxNode node in root.DescendantNodes().Where(IsReferenceCandidate))
            {
                ISymbol? target = GetReferencedSymbol(model, node, cancellationToken);
                if (target is null || !declared.TryGetValue(target, out MSBuildSymbolSnapshot? targetSnapshot))
                {
                    continue;
                }

                FileLinePositionSpan span = tree.GetLineSpan(node.Span, cancellationToken);
                references.Add(new MSBuildReferenceSnapshot(
                    targetSnapshot.StableKey,
                    document.FilePath ?? string.Empty,
                    span.StartLinePosition.Line + 1,
                    span.StartLinePosition.Character + 1,
                    node.Kind().ToString(),
                    node.ToString()));
            }
        }

        return new ProjectSymbolIndex(
            declared.Values.OrderBy(symbol => symbol.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(symbol => symbol.StartLine)
                .ThenBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            references.OrderBy(reference => reference.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(reference => reference.Line)
                .ThenBy(reference => reference.Column)
                .ToArray());
    }

    private static ISymbol? GetDeclaredSymbol(SemanticModel model, SyntaxNode node, CancellationToken cancellationToken)
    {
        return node switch
        {
            BaseTypeDeclarationSyntax declaration => model.GetDeclaredSymbol(declaration, cancellationToken),
            BaseMethodDeclarationSyntax declaration => model.GetDeclaredSymbol(declaration, cancellationToken),
            PropertyDeclarationSyntax declaration => model.GetDeclaredSymbol(declaration, cancellationToken),
            EventDeclarationSyntax declaration => model.GetDeclaredSymbol(declaration, cancellationToken),
            EventFieldDeclarationSyntax declaration => declaration.Declaration.Variables.Count == 1
                ? model.GetDeclaredSymbol(declaration.Declaration.Variables[0], cancellationToken)
                : null,
            FieldDeclarationSyntax declaration => declaration.Declaration.Variables.Count == 1
                ? model.GetDeclaredSymbol(declaration.Declaration.Variables[0], cancellationToken)
                : null,
            _ => null
        };
    }

    private static bool IsReferenceCandidate(SyntaxNode node)
    {
        return node is IdentifierNameSyntax
            or GenericNameSyntax
            or MemberAccessExpressionSyntax
            or InvocationExpressionSyntax
            or ObjectCreationExpressionSyntax;
    }

    private static ISymbol? GetReferencedSymbol(SemanticModel model, SyntaxNode node, CancellationToken cancellationToken)
    {
        SymbolInfo symbolInfo = model.GetSymbolInfo(node, cancellationToken);
        ISymbol? symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            return constructor.ContainingType;
        }

        return symbol;
    }

    private static MSBuildSymbolSnapshot CreateSymbolSnapshot(
        ISymbol symbol,
        SyntaxNode node,
        string filePath,
        SyntaxTree tree)
    {
        FileLinePositionSpan span = tree.GetLineSpan(node.Span);
        string containingType = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty;
        string namespaceName = symbol.ContainingNamespace?.IsGlobalNamespace == false
            ? symbol.ContainingNamespace.ToDisplayString()
            : string.Empty;
        string signature = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        string stableKey = StableIdentifier.FromParts("symbol", filePath, symbol.Kind.ToString(), signature, (span.StartLinePosition.Line + 1).ToString());

        return new MSBuildSymbolSnapshot(
            stableKey,
            symbol.Name,
            symbol.Kind.ToString(),
            namespaceName,
            containingType,
            filePath,
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1,
            signature);
    }
}
