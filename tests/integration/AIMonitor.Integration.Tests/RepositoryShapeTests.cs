using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AIMonitor.Integration.Tests;

public sealed class RepositoryShapeTests
{
    private static readonly Dictionary<string, int> AllowedUsingDeclarationCounts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["src/AIMonitor.App/Controls/AdapterSurfaceControl.cs"] = 3,
        ["src/AIMonitor.App/Controls/SharedLogControl.cs"] = 2,
        ["src/AIMonitor.App/Controls/SolutionIndexControl.cs"] = 1,
        ["src/AIMonitor.App/McpProxyHubService.cs"] = 3,
        ["src/AIMonitor.Cli/Program.cs"] = 3,
        ["src/AIMonitor.Core/MonitorSettingsLoader.cs"] = 6,
        ["src/AIMonitor.Data/SolutionIndexDatabase.cs"] = 6,
        ["src/AIMonitor.Data/SolutionIndexQueryService.cs"] = 2,
        ["src/AIMonitor.Data/SolutionIndexStore.cs"] = 28,
        ["src/AIMonitor.Logging/JsonLinesMonitorLogger.cs"] = 2,
        ["src/AIMonitor.Logging/MonitorLogPipeClientLogger.cs"] = 2,
        ["src/AIMonitor.Logging/MonitorLogPipeServer.cs"] = 2,
        ["src/AIMonitor.Logging/MonitorLogService.cs"] = 2,
        ["src/AIMonitor.McpServer/Program.cs"] = 1,
        ["src/AIMonitor.McpStdioBridge/Program.cs"] = 5,
        ["src/AIMonitor.MSBuild/MSBuildWorkspaceLoader.cs"] = 5,
        ["src/AIMonitor.Workflow/FileHash.cs"] = 1,
        ["src/AIMonitor.Workflow/PreMergeValidationService.cs"] = 1,
        ["src/AIMonitor.Workflow/RoslynEditService.cs"] = 1,
        ["src/AIMonitor.Workflow/WorkflowEditService.cs"] = 17,
        ["tests/integration/AIMonitor.Integration.Tests/CliIndexQueryTests.cs"] = 66,
        ["tests/integration/AIMonitor.Integration.Tests/McpServerSmokeTests.cs"] = 31,
        ["tests/smoke/AIMonitor.SmokeTests/Program.cs"] = 1,
        ["tests/smoke/AIMonitor.ToolSmokeTests/Program.cs"] = 16,
        ["tests/unit/AIMonitor.Data.Tests/SolutionIndexQueryServiceTests.cs"] = 1,
        ["tests/unit/AIMonitor.Data.Tests/SolutionIndexStoreTests.cs"] = 2,
        ["tests/unit/AIMonitor.Logging.Tests/JsonLinesMonitorLoggerTests.cs"] = 1,
        ["tests/unit/AIMonitor.Logging.Tests/MonitorLogPipeTests.cs"] = 1,
        ["tests/unit/AIMonitor.MSBuild.Tests/MSBuildWorkspaceLoaderTests.cs"] = 1
    };

    [Fact]
    public void Source_tests_samples_and_docs_are_top_level_siblings()
    {
        string repoRoot = FindRepositoryRoot();

        Assert.True(Directory.Exists(Path.Combine(repoRoot, "src")));
        Assert.True(Directory.Exists(Path.Combine(repoRoot, "tests")));
        Assert.True(Directory.Exists(Path.Combine(repoRoot, "samples")));
        Assert.True(Directory.Exists(Path.Combine(repoRoot, "docs")));
    }

    [Fact]
    public void CSharp_files_do_not_use_top_level_statements_or_unbraced_control_flow()
    {
        string repoRoot = FindRepositoryRoot();
        List<string> violations = [];

        foreach (string filePath in EnumerateRepositoryCSharpFiles(repoRoot))
        {
            string source = File.ReadAllText(filePath);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: filePath);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
            string relativePath = NormalizePath(Path.GetRelativePath(repoRoot, filePath));

            foreach (GlobalStatementSyntax statement in root.Members.OfType<GlobalStatementSyntax>())
            {
                FileLinePositionSpan span = tree.GetLineSpan(statement.Span);
                violations.Add($"{relativePath}:{span.StartLinePosition.Line + 1} uses a top-level statement.");
            }

            int usingDeclarationCount = 0;
            foreach (LocalDeclarationStatementSyntax declaration in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            {
                if (declaration.UsingKeyword != default)
                {
                    usingDeclarationCount++;
                }
            }

            AllowedUsingDeclarationCounts.TryGetValue(relativePath, out int allowedUsingDeclarationCount);
            if (usingDeclarationCount > allowedUsingDeclarationCount)
            {
                violations.Add($"{relativePath} has {usingDeclarationCount} using/await using declarations; allowed baseline is {allowedUsingDeclarationCount}. Use explicit using blocks in new source.");
            }

            foreach (StatementSyntax statement in root.DescendantNodes().OfType<StatementSyntax>())
            {
                if (HasUnbracedControlFlowBody(statement))
                {
                    FileLinePositionSpan span = tree.GetLineSpan(statement.Span);
                    violations.Add($"{relativePath}:{span.StartLinePosition.Line + 1} uses control flow without braces.");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static bool HasUnbracedControlFlowBody(StatementSyntax statement)
    {
        return statement switch
        {
            IfStatementSyntax item => item.Statement is not BlockSyntax
                || item.Else?.Statement is IfStatementSyntax { Statement: not BlockSyntax }
                || item.Else?.Statement is not null and not BlockSyntax and not IfStatementSyntax,
            ForStatementSyntax item => item.Statement is not BlockSyntax,
            ForEachStatementSyntax item => item.Statement is not BlockSyntax,
            ForEachVariableStatementSyntax item => item.Statement is not BlockSyntax,
            WhileStatementSyntax item => item.Statement is not BlockSyntax,
            DoStatementSyntax item => item.Statement is not BlockSyntax,
            LockStatementSyntax item => item.Statement is not BlockSyntax,
            UsingStatementSyntax item => item.Statement is not BlockSyntax,
            FixedStatementSyntax item => item.Statement is not BlockSyntax,
            _ => false
        };
    }

    private static IEnumerable<string> EnumerateRepositoryCSharpFiles(string repoRoot)
    {
        string[] roots =
        [
            Path.Combine(repoRoot, "src"),
            Path.Combine(repoRoot, "tests"),
            Path.Combine(repoRoot, "samples")
        ];

        foreach (string root in roots.Where(Directory.Exists))
        {
            foreach (string filePath in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (!IsIgnoredGeneratedPath(repoRoot, filePath) && IsRepositoryStyleGuardPath(repoRoot, filePath))
                {
                    yield return filePath;
                }
            }
        }
    }

    private static bool IsIgnoredGeneratedPath(string repoRoot, string filePath)
    {
        string relativePath = Path.GetRelativePath(repoRoot, filePath);
        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("runtime", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRepositoryStyleGuardPath(string repoRoot, string filePath)
    {
        string relativePath = Path.GetRelativePath(repoRoot, filePath);
        if (!relativePath.StartsWith("samples" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Length >= 3
            && segments[0].Equals("samples", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("watched-solutions", StringComparison.OrdinalIgnoreCase)
            && (
                segments[2].Equals("WorkflowHarnessSample", StringComparison.OrdinalIgnoreCase)
                || segments[2].Equals("CodexWindows", StringComparison.OrdinalIgnoreCase)
                || segments[2].Equals("CodexBlazor", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AIMonitor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find AIMonitor repository root.");
    }
}
