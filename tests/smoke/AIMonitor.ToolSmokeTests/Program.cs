using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.Logging;
using AIMonitor.MSBuild;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AIMonitor.ToolSmokeTests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--fixture-index-matrix", StringComparer.OrdinalIgnoreCase))
        {
            return await RunFixtureIndexMatrixAsync();
        }

        if (args.Contains("--webviewer-file-by-file", StringComparer.OrdinalIgnoreCase))
        {
            return await RunWebViewerFileByFileAsync();
        }

        if (args.Contains("--mcp-live-workflow", StringComparer.OrdinalIgnoreCase))
        {
            return await RunMcpLiveWorkflowAsync();
        }

        if (args.Contains("--visible-test-suite", StringComparer.OrdinalIgnoreCase))
        {
            return RunVisibleTestSuite();
        }

        Console.WriteLine("AIMonitor tool smoke tests");
        Console.WriteLine();
        Console.WriteLine("Available modes:");
        Console.WriteLine("  --fixture-index-matrix    Build a generated fixture and compare AIMonitor index rows with an independent Roslyn pass.");
        Console.WriteLine("  --webviewer-file-by-file  Compare selected SchemaStudioWebViewer files against index and grep sanity counts.");
        Console.WriteLine("  --mcp-live-workflow       Call the local MCP server against config/appsettings.json so WinForms can show adapter telemetry.");
        Console.WriteLine("  --visible-test-suite      Run dotnet test and emit test run/case telemetry to the WinForms monitor log.");
        return 2;
    }

    private static int RunVisibleTestSuite()
    {
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        MonitorSettings settings = MonitorSettingsLoader.Load(repositoryRoot);
        IMonitorLogger logger = CreateMonitorLogger(settings);
        string runId = $"tests-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}"[..48];
        string resultsRoot = Path.Combine(repositoryRoot, "runtime", "test-results", runId);
        Directory.CreateDirectory(resultsRoot);

        logger.Write(
            MonitorLogLevel.Information,
            "AIMonitor.ToolSmokeTests",
            "test.run.started",
            "Visible test suite started.",
            new Dictionary<string, string>
            {
                ["runId"] = runId,
                ["command"] = "dotnet test .\\AIMonitor.slnx --no-build",
                ["resultsRoot"] = resultsRoot
            });

        ProcessResult result = RunProcess(
            "dotnet",
            [
                "test",
                ".\\AIMonitor.slnx",
                "--no-build",
                "--logger",
                "trx",
                "--results-directory",
                resultsRoot
            ],
            repositoryRoot,
            TimeSpan.FromMinutes(5));

        int caseCount = EmitTrxCaseTelemetry(logger, runId, resultsRoot);
        logger.Write(
            result.ExitCode == 0 ? MonitorLogLevel.Information : MonitorLogLevel.Error,
            "AIMonitor.ToolSmokeTests",
            "test.run.completed",
            result.ExitCode == 0 ? "Visible test suite completed." : "Visible test suite failed.",
            new Dictionary<string, string>
            {
                ["runId"] = runId,
                ["exitCode"] = result.ExitCode.ToString(),
                ["timedOut"] = result.TimedOut.ToString().ToLowerInvariant(),
                ["caseTelemetryCount"] = caseCount.ToString(),
                ["resultsRoot"] = resultsRoot,
                ["stdoutPreview"] = Preview(result.StandardOutput),
                ["stderrPreview"] = Preview(result.StandardError)
            });

        Console.WriteLine($"Visible test telemetry run: {runId}");
        Console.WriteLine($"TRX results: {resultsRoot}");
        Console.WriteLine($"Case telemetry emitted: {caseCount}");
        return result.ExitCode;
    }

    private static IMonitorLogger CreateMonitorLogger(MonitorSettings settings)
    {
        return new MonitorLogPipeClientLogger(
            MonitorLogPipeNames.GetDefaultPipeName(settings),
            new JsonLinesMonitorLogger(MonitorLogPaths.GetDefaultLogPath(settings)),
            TimeSpan.FromSeconds(1));
    }

    private static int EmitTrxCaseTelemetry(IMonitorLogger logger, string runId, string resultsRoot)
    {
        int count = 0;
        foreach (string trxPath in Directory.EnumerateFiles(resultsRoot, "*.trx", SearchOption.AllDirectories))
        {
            XDocument document = XDocument.Load(trxPath);
            XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
            foreach (XElement result in document.Descendants(ns + "UnitTestResult"))
            {
                string testName = result.Attribute("testName")?.Value ?? string.Empty;
                string outcome = result.Attribute("outcome")?.Value ?? string.Empty;
                string duration = result.Attribute("duration")?.Value ?? string.Empty;
                string testId = result.Attribute("testId")?.Value ?? string.Empty;
                logger.Write(
                    outcome.Equals("Passed", StringComparison.OrdinalIgnoreCase) ? MonitorLogLevel.Information : MonitorLogLevel.Error,
                    "AIMonitor.ToolSmokeTests",
                    "test.case.completed",
                    $"{testName} {outcome}.",
                    new Dictionary<string, string>
                    {
                        ["runId"] = runId,
                        ["testName"] = testName,
                        ["testId"] = testId,
                        ["outcome"] = outcome,
                        ["duration"] = duration,
                        ["trxPath"] = trxPath
                    });
                count++;
            }
        }

        return count;
    }

    private static string Preview(string value)
    {
        string singleLine = string.Join(" ", value.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= 600 ? singleLine : singleLine[..600] + "...";
    }

    private static ProcessResult RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(timeout);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }

        return new ProcessResult(
            exited ? process.ExitCode : -1,
            !exited,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
    }

    private sealed record ProcessResult(
        int ExitCode,
        bool TimedOut,
        string StandardOutput,
        string StandardError);

    private static async Task<int> RunMcpLiveWorkflowAsync()
    {
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = Path.Combine(repositoryRoot, "config", "appsettings.json");
        string serverDll = Path.Combine(repositoryRoot, "src", "AIMonitor.McpServer", "bin", "Debug", "net10.0", "AIMonitor.McpServer.dll");
        if (!File.Exists(serverDll))
        {
            Console.Error.WriteLine($"MCP server build output not found: {serverDll}");
            return 1;
        }

        StdioClientTransportOptions options = new()
        {
            Name = "ai-monitor-live-smoke",
            Command = "dotnet",
            Arguments = [serverDll, "--repo-root", repositoryRoot, "--config", settingsPath],
            WorkingDirectory = repositoryRoot
        };

        await using McpClient client = await McpClient.CreateAsync(new StdioClientTransport(options));
        await CallAndPrintAsync(client, "get_monitor_status");
        await CallAndPrintAsync(client, "get_workflow_status");
        await CallAndPrintAsync(
            client,
            "find_file",
            new Dictionary<string, object?>
            {
                ["fileNameOrPattern"] = "AppConfig.cs",
                ["maxResults"] = 5
            });
        await CallAndPrintAsync(
            client,
            "get_solution_index_status");
        return 0;
    }

    private static async Task CallAndPrintAsync(
        McpClient client,
        string toolName,
        Dictionary<string, object?>? arguments = null)
    {
        CallToolResult result = await client.CallToolAsync(toolName, arguments);
        Console.WriteLine($"{toolName}: error={result.IsError == true}");
    }

    private static async Task<int> RunFixtureIndexMatrixAsync()
    {
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string runRoot = Path.Combine(repositoryRoot, "runtime", "smoke", "tool", DateTime.Now.ToString("yyyyMMdd_HHmmss"), "fixture-index-matrix");
        string observedRoot = Path.Combine(runRoot, "FixtureProject");
        Directory.CreateDirectory(observedRoot);

        await File.WriteAllTextAsync(Path.Combine(observedRoot, "FixtureProject.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(observedRoot, "FixtureProject.slnx"), """
            <Solution>
              <Project Path="FixtureProject.csproj" />
            </Solution>
            """);
        await File.WriteAllTextAsync(Path.Combine(observedRoot, "Fixture.A.cs"), FixtureSourceA);
        await File.WriteAllTextAsync(Path.Combine(observedRoot, "Fixture.B.cs"), FixtureSourceB);
        await File.WriteAllTextAsync(Path.Combine(observedRoot, "Fixture.Generated.g.cs"), FixtureGeneratedSource);

        string solutionPath = Path.Combine(observedRoot, "FixtureProject.slnx");
        IndexSnapshot index = await BuildIndexAsync(repositoryRoot, solutionPath, Path.Combine(runRoot, "runtime"));
        MatrixCheck[] checks = BuildMatrixChecks();
        IReadOnlyDictionary<string, RoslynCounts> roslynCounts = BuildRoslynCounts(observedRoot, checks);
        List<MatrixResult> results = [];
        foreach (MatrixCheck check in checks)
        {
            IndexedSymbolRow? target = FindIndexedSymbol(index.Symbols, check);
            IReadOnlyList<IndexedReferenceRow> references = target is null
                ? []
                : index.References.Where(reference => reference.TargetStableKey == target.StableKey).ToArray();
            results.Add(new MatrixResult(check, target, references, roslynCounts.GetValueOrDefault(check.Name)));
        }

        bool passed = results.All(result => result.Passed);
        string summary = BuildFixtureSummary(index.Summary, results, passed);
        string summaryPath = Path.Combine(runRoot, "summary.md");
        await File.WriteAllTextAsync(summaryPath, summary);
        Console.WriteLine(summary);
        Console.WriteLine($"Summary: {summaryPath}");
        return passed ? 0 : 1;
    }

    private static async Task<int> RunWebViewerFileByFileAsync()
    {
        string solutionPath = @"C:\SchemaStudioWebViewer\SchemaStudioWebViewer.sln";
        if (!File.Exists(solutionPath))
        {
            Console.WriteLine($"SchemaStudioWebViewer solution not found, skipping: {solutionPath}");
            return 0;
        }

        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string observedRoot = Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory;
        string runRoot = Path.Combine(repositoryRoot, "runtime", "smoke", "tool", DateTime.Now.ToString("yyyyMMdd_HHmmss"), "webviewer-file-by-file");
        Directory.CreateDirectory(runRoot);

        string[] targetFiles =
        [
            Path.Combine("Components", "Pages", "DomainObjectModeler", "DomainObjectModeler.Selection.cs"),
            Path.Combine("SchemaStudio.Data", "Repositories", "DatabaseRepository.cs"),
            Path.Combine("SchemaStudio.Data", "Repositories", "DatabaseRelationshipRepository.cs")
        ];

        IndexSnapshot index = await BuildIndexAsync(repositoryRoot, solutionPath, Path.Combine(runRoot, "runtime"));
        string[] grepCorpus = Directory.EnumerateFiles(observedRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(observedRoot, "*.razor", SearchOption.AllDirectories))
            .Where(path => !PathHasSegment(path, "bin"))
            .Where(path => !PathHasSegment(path, "obj"))
            .Where(path => !PathHasSegment(path, ".git"))
            .Where(path => !PathHasSegment(path, ".vs"))
            .Where(path => !PathHasSegment(path, "SourceBackups"))
            .ToArray();

        StringBuilder summary = new();
        summary.AppendLine("# WebViewer File-by-File Smoke");
        summary.AppendLine();
        summary.AppendLine($"Solution: `{solutionPath}`");
        summary.AppendLine($"Indexed projects: `{index.Summary.ProjectCount}`");
        summary.AppendLine($"Indexed documents: `{index.Summary.DocumentCount}`");
        summary.AppendLine($"Indexed symbols: `{index.Symbols.Count}`");
        summary.AppendLine($"Indexed references: `{index.References.Count}`");
        summary.AppendLine();
        summary.AppendLine("| File | Indexed symbols | Indexed refs in file | Grep anchors |");
        summary.AppendLine("|---|---:|---:|---:|");

        bool passed = true;
        foreach (string relativePath in targetFiles)
        {
            string fullPath = Path.Combine(observedRoot, relativePath);
            IReadOnlyList<IndexedSymbolRow> fileSymbols = index.Symbols.Where(symbol => PathMatchesRelativePath(symbol.FilePath, relativePath)).ToArray();
            IReadOnlyList<IndexedReferenceRow> fileReferences = index.References.Where(reference => PathMatchesRelativePath(reference.FilePath, relativePath)).ToArray();
            int grepAnchors = File.Exists(fullPath)
                ? CountGrepOccurrences(grepCorpus, Path.GetFileNameWithoutExtension(relativePath).Split('.').First())
                : 0;
            passed &= File.Exists(fullPath) && (fileSymbols.Count > 0 || fileReferences.Count > 0);
            summary.AppendLine($"| `{relativePath}` | `{fileSymbols.Count}` | `{fileReferences.Count}` | `{grepAnchors}` |");
        }

        string summaryPath = Path.Combine(runRoot, "summary.md");
        await File.WriteAllTextAsync(summaryPath, summary.ToString());
        Console.WriteLine(summary.ToString());
        Console.WriteLine($"Summary: {summaryPath}");
        return passed ? 0 : 1;
    }

    private static async Task<IndexSnapshot> BuildIndexAsync(string repositoryRoot, string solutionPath, string runtimeRoot)
    {
        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, solutionPath, runtimeRoot);
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
        SolutionIndexBuilder builder = new(new MSBuildWorkspaceLoader(), store);
        SolutionIndexSummary summary = await builder.RebuildAsync(settings);
        return new IndexSnapshot(summary, store.ListSymbols(), store.ListReferences());
    }

    private static IReadOnlyDictionary<string, RoslynCounts> BuildRoslynCounts(string observedRoot, IReadOnlyList<MatrixCheck> checks)
    {
        SyntaxTree[] trees = Directory.EnumerateFiles(observedRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !PathHasSegment(path, "bin"))
            .Where(path => !PathHasSegment(path, "obj"))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "AIMonitorFixtureMatrix",
            trees,
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Dictionary<string, RoslynCounts> result = [];
        foreach (MatrixCheck check in checks)
        {
            ISymbol? target = FindRoslynTarget(compilation, check);
            if (target is null)
            {
                result[check.Name] = new RoslynCounts(false, 0);
                continue;
            }

            int references = 0;
            foreach (SyntaxTree tree in trees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
                SyntaxNode root = tree.GetRoot();
                foreach (SyntaxNode node in root.DescendantNodes().Where(IsReferenceCandidate))
                {
                    ISymbol? symbol = NormalizeSymbol(GetBestSymbol(model.GetSymbolInfo(node)));
                    if (symbol is not null && SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, target.OriginalDefinition))
                    {
                        references++;
                    }
                }
            }

            result[check.Name] = new RoslynCounts(true, references);
        }

        return result;
    }

    private static ISymbol? FindRoslynTarget(Compilation compilation, MatrixCheck check)
    {
        return compilation.SyntaxTrees
            .Select(tree => compilation.GetSemanticModel(tree, ignoreAccessibility: true))
            .SelectMany(model => model.SyntaxTree.GetRoot().DescendantNodes().Select(node => model.GetDeclaredSymbol(node)).Where(symbol => symbol is not null).Select(symbol => symbol!))
            .FirstOrDefault(symbol => symbol.Name.Equals(check.SymbolName, StringComparison.Ordinal)
                && symbol.Kind.ToString().Equals(check.RoslynKind, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsReferenceCandidate(SyntaxNode node)
    {
        return node is IdentifierNameSyntax;
    }

    private static ISymbol? GetBestSymbol(SymbolInfo info)
    {
        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
    }

    private static ISymbol? NormalizeSymbol(ISymbol? symbol)
    {
        return symbol is IMethodSymbol { ReducedFrom: not null } method
            ? method.ReducedFrom
            : symbol;
    }

    private static IndexedSymbolRow? FindIndexedSymbol(IReadOnlyList<IndexedSymbolRow> symbols, MatrixCheck check)
    {
        return symbols.FirstOrDefault(symbol =>
            symbol.Name.Equals(check.SymbolName, StringComparison.Ordinal)
            && symbol.Kind.Equals(check.IndexKind, StringComparison.OrdinalIgnoreCase)
            && symbol.Signature.Contains(check.SignatureContains, StringComparison.Ordinal));
    }

    private static MatrixCheck[] BuildMatrixChecks()
    {
        return
        [
            new("instance method", "Target", "Method", "Method", "Target", 2),
            new("static method", "StaticTarget", "Method", "Method", "StaticTarget", 2),
            new("property", "Value", "Property", "Property", "Value", 3),
            new("field", "Counter", "Field", "Field", "Counter", 3),
            new("event", "Changed", "Event", "Event", "Changed", 2),
            new("base type", "FixtureBase", "NamedType", "NamedType", "FixtureBase", 1),
            new("extension method", "Doubled", "Method", "Method", "Doubled", 1)
        ];
    }

    private static string BuildFixtureSummary(SolutionIndexSummary summary, IReadOnlyList<MatrixResult> results, bool passed)
    {
        string rows = string.Join(Environment.NewLine, results.Select(result =>
            $"- `{result.Check.Name}` target `{result.IndexTarget?.StableKey}` Roslyn target resolved `{result.Roslyn?.TargetResolved}` Roslyn refs `{result.Roslyn?.ReferenceCount}` AIMonitor refs `{result.IndexReferences.Count}/{result.Check.ExpectedReferences}` passed `{result.Passed}`"));
        return $"""
            # Fixture Index Matrix Smoke

            Passed: `{passed}`

            - Indexed projects: `{summary.ProjectCount}`
            - Indexed documents: `{summary.DocumentCount}`
            - Matrix checks: `{results.Count}`
            - Passing checks: `{results.Count(result => result.Passed)}`

            ## Matrix

            {rows}
            """;
    }

    private static IReadOnlyList<MetadataReference> GetMetadataReferences()
    {
        string? trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        return string.IsNullOrWhiteSpace(trustedAssemblies)
            ? []
            : trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Where(File.Exists)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
    }

    private static int CountGrepOccurrences(IEnumerable<string> files, string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return 0;
        }

        Regex pattern = new($@"\b{Regex.Escape(identifier)}\b", RegexOptions.Compiled);
        int total = 0;
        foreach (string file in files)
        {
            total += pattern.Matches(File.ReadAllText(file)).Count;
        }

        return total;
    }

    private static bool PathMatchesRelativePath(string fullPath, string relativePath)
    {
        string normalizedFullPath = fullPath.Replace('\\', '/');
        string normalizedRelativePath = relativePath.Replace('\\', '/');
        return normalizedFullPath.Equals(normalizedRelativePath, StringComparison.OrdinalIgnoreCase)
            || normalizedFullPath.EndsWith("/" + normalizedRelativePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathHasSegment(string filePath, string segment)
    {
        return filePath
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals(segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveRepositoryRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AIMonitor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to resolve AIMonitor repository root.");
    }

    private const string FixtureSourceA =
        """
        using System;

        namespace AIMonitor.ToolSmokeFixture;

        internal abstract class FixtureBase
        {
        }

        internal delegate int FixtureDelegate(int value);

        internal sealed class FixtureTarget : FixtureBase
        {
            public int Counter;
            public int Value { get; set; }
            public event EventHandler? Changed;

            public FixtureTarget()
            {
            }

            public FixtureTarget(int seed)
            {
                Counter = seed;
            }

            public int Target(int value) => value + 1;
            public static int StaticTarget(int value) => value + 2;
            public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
        }

        internal static class FixtureExtensions
        {
            public static int Doubled(this int value) => value * 2;
        }
        """;

    private const string FixtureSourceB =
        """
        namespace AIMonitor.ToolSmokeFixture;

        internal sealed class FixtureCaller
        {
            private readonly FixtureTarget target = new FixtureTarget(1);
            private readonly FixtureDelegate gate = FixtureTarget.StaticTarget;

            public int CallInstance() => target.Target(1) + target.Target(2);
            public int CallStatic() => FixtureTarget.StaticTarget(3);
            public int ReadWriteMembers()
            {
                target.Counter = target.Value;
                target.Value = target.Counter;
                return target.Value;
            }

            public void WireEvent()
            {
                target.Changed += (_, _) => { };
            }

            public int InvokeDelegate() => gate(4);
            public int CallExtension() => 5.Doubled();
        }
        """;

    private const string FixtureGeneratedSource =
        """
        // <auto-generated/>
        namespace AIMonitor.ToolSmokeFixture;

        internal sealed class GeneratedShape
        {
            public int GeneratedMethod() => 1;
        }
        """;

    private sealed record IndexSnapshot(
        SolutionIndexSummary Summary,
        IReadOnlyList<IndexedSymbolRow> Symbols,
        IReadOnlyList<IndexedReferenceRow> References);

    private sealed record MatrixCheck(
        string Name,
        string SymbolName,
        string RoslynKind,
        string IndexKind,
        string SignatureContains,
        int ExpectedReferences);

    private sealed record RoslynCounts(bool TargetResolved, int ReferenceCount);

    private sealed record MatrixResult(
        MatrixCheck Check,
        IndexedSymbolRow? IndexTarget,
        IReadOnlyList<IndexedReferenceRow> IndexReferences,
        RoslynCounts? Roslyn)
    {
        public bool Passed =>
            IndexTarget is not null
            && Roslyn?.TargetResolved == true
            && Roslyn.ReferenceCount == Check.ExpectedReferences
            && IndexReferences.Count == Check.ExpectedReferences
            && IndexReferences.Count == Roslyn.ReferenceCount;
    }
}
