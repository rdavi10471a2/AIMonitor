using System.Text.Json;
using System.Text.Json.Serialization;
using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.MSBuild;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AIMonitor.LanguageCorpusSmokeTests;

internal static class Program
{
    private const string BindStart = "/*<bind>*/";
    private const string BindEnd = "/*</bind>*/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static async Task<int> Main(string[] args)
    {
        bool assertMode = args.Contains("--assert", StringComparer.OrdinalIgnoreCase);
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string runRoot = Path.Combine(
            repositoryRoot,
            "runtime",
            "smoke",
            "language-corpus",
            DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(runRoot);

        CorpusCase[] cases = LoadCorpusCases(repositoryRoot);
        string observedRoot = Path.Combine(runRoot, "CorpusProject");
        string projectPath = Path.Combine(observedRoot, "CorpusProject.csproj");
        string solutionPath = Path.Combine(observedRoot, "CorpusProject.slnx");
        Directory.CreateDirectory(observedRoot);

        Dictionary<CorpusCase, CorpusMarkup> corpus = [];
        foreach (CorpusCase item in cases)
        {
            CorpusMarkup markup = StripBindMarkers(item);
            corpus[item] = markup;
            WriteCorpusSource(observedRoot, item.RelativePath, markup.Source);
            foreach (CorpusSourceFile additionalSource in item.AdditionalSources)
            {
                WriteCorpusSource(observedRoot, additionalSource.RelativePath, additionalSource.Source);
            }
        }

        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(solutionPath, """
            <Solution>
              <Project Path="CorpusProject.csproj" />
            </Solution>
            """);

        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, solutionPath, Path.Combine(runRoot, "MonitorRuntime"));
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
        SolutionIndexBuilder builder = new(new MSBuildWorkspaceLoader(), store);
        SolutionIndexSummary summary = await builder.RebuildAsync(settings);

        IReadOnlyList<IndexedSymbolRow> symbols = store.ListSymbols();
        IReadOnlyList<IndexedReferenceRow> references = store.ListReferences();
        IReadOnlyDictionary<string, RoslynAnswer> roslynAnswers = BuildRoslynAnswers(observedRoot, corpus);

        List<CorpusResult> results = [];
        foreach (CorpusCase item in cases)
        {
            CorpusMarkup markup = corpus[item];
            RoslynAnswer? answer = roslynAnswers.GetValueOrDefault(item.Name);
            IndexedSymbolRow? target = FindIndexedTarget(symbols, item);
            IReadOnlyList<IndexedReferenceRow> targetReferences = target is null
                ? []
                : references.Where(reference => reference.TargetStableKey == target.StableKey).ToArray();
            FileLinePositionSpan bindSpan = CSharpSyntaxTree.ParseText(markup.Source, path: item.RelativePath)
                .GetLineSpan(new TextSpan(markup.BindStart, markup.BindLength));
            int bindLine = bindSpan.StartLinePosition.Line + 1;
            int bindColumn = bindSpan.StartLinePosition.Character + 1;
            bool monitorReferenceAtBind = targetReferences.Any(reference =>
                NormalizePath(reference.FilePath).EndsWith(NormalizePath(item.RelativePath), StringComparison.OrdinalIgnoreCase)
                && reference.Line == bindLine
                && reference.Column == bindColumn);
            if (!item.ExpectedMonitorTarget)
            {
                monitorReferenceAtBind = true;
            }

            results.Add(new CorpusResult(item, answer, target, targetReferences, monitorReferenceAtBind));
        }

        bool passed = results.Where(result => !result.Case.Informational).All(result => result.Passed);
        string summaryText = BuildSummary(summary, symbols.Count, references.Count, results, passed, assertMode);
        string summaryPath = Path.Combine(runRoot, "summary.md");
        await File.WriteAllTextAsync(summaryPath, summaryText);
        Console.WriteLine(summaryText);
        Console.WriteLine($"Summary: {summaryPath}");

        return assertMode && !passed ? 1 : 0;
    }

    private static void WriteCorpusSource(string observedRoot, string relativePath, string source)
    {
        string targetPath = Path.Combine(observedRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText(targetPath, source);
    }

    private static CorpusCase[] LoadCorpusCases(string repositoryRoot)
    {
        string corpusRoot = Path.Combine(repositoryRoot, "tests", "smoke", "AIMonitor.LanguageCorpusSmokeTests", "Corpus");
        List<CorpusCase> cases = [];
        foreach (string expectedPath in Directory.EnumerateFiles(corpusRoot, "expected.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string caseRoot = Path.GetDirectoryName(expectedPath)!;
            CorpusExpected expected = JsonSerializer.Deserialize<CorpusExpected>(File.ReadAllText(expectedPath), JsonOptions)
                ?? throw new InvalidOperationException($"Unable to read corpus expected file: {expectedPath}");
            string sourcePath = Path.Combine(caseRoot, expected.SourceFile);
            cases.Add(new CorpusCase(
                expected.Name,
                expected.RelativePath,
                expected.ExpectedTargetRelativePath ?? expected.RelativePath,
                File.ReadAllText(sourcePath),
                expected.ExpectedKind,
                expected.ExpectedName,
                expected.ExpectedRoslynDisplay,
                expected.ExpectedMonitorTarget ?? true,
                expected.Informational ?? false,
                expected.ExpectedReferenceCount,
                expected.ExpectedCallerCount,
                expected.ExpectedRelationshipKinds ?? [],
                LoadAdditionalSources(caseRoot, expected.AdditionalSources)));
        }

        return cases.ToArray();
    }

    private static IReadOnlyList<CorpusSourceFile> LoadAdditionalSources(
        string caseRoot,
        IReadOnlyList<CorpusExpectedSource>? additionalSources)
    {
        if (additionalSources is null || additionalSources.Count == 0)
        {
            return [];
        }

        return additionalSources
            .Select(source => new CorpusSourceFile(
                source.RelativePath,
                File.ReadAllText(Path.Combine(caseRoot, source.SourceFile))))
            .ToArray();
    }

    private static CorpusMarkup StripBindMarkers(CorpusCase item)
    {
        int startMarker = item.Source.IndexOf(BindStart, StringComparison.Ordinal);
        int endMarker = item.Source.IndexOf(BindEnd, StringComparison.Ordinal);
        if (startMarker < 0 || endMarker < 0 || endMarker < startMarker)
        {
            throw new InvalidOperationException($"Corpus case '{item.Name}' is missing bind markers.");
        }

        string withoutStart = item.Source.Remove(startMarker, BindStart.Length);
        int adjustedEnd = endMarker - BindStart.Length;
        string source = withoutStart.Remove(adjustedEnd, BindEnd.Length);
        return new CorpusMarkup(source, startMarker, adjustedEnd - startMarker);
    }

    private static IReadOnlyDictionary<string, RoslynAnswer> BuildRoslynAnswers(
        string observedRoot,
        IReadOnlyDictionary<CorpusCase, CorpusMarkup> corpus)
    {
        SyntaxTree[] trees = corpus
            .SelectMany(item => item.Key.AllSources(item.Value.Source))
            .GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => CSharpSyntaxTree.ParseText(
                group.First().Source,
                path: Path.Combine(observedRoot, group.Key)))
            .ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "AIMonitorLanguageCorpus",
            trees,
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Dictionary<string, RoslynAnswer> answers = [];
        foreach ((CorpusCase item, CorpusMarkup markup) in corpus)
        {
            SyntaxTree tree = trees.Single(candidate =>
                Path.GetFileName(candidate.FilePath).Equals(Path.GetFileName(item.RelativePath), StringComparison.OrdinalIgnoreCase));
            SemanticModel model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            SyntaxNode root = tree.GetRoot();
            SyntaxNode node = root.FindNode(new TextSpan(markup.BindStart, markup.BindLength), getInnermostNodeForTie: true);
            ISymbol? symbol = ResolveBoundSymbol(model, node);
            string display = symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? string.Empty;
            answers[item.Name] = new RoslynAnswer(symbol is not null, display);
        }

        return answers;
    }

    private static ISymbol? ResolveBoundSymbol(SemanticModel model, SyntaxNode node)
    {
        if (node.FirstAncestorOrSelf<AttributeSyntax>() is { } attribute
            && attribute.Name.Span.Contains(node.Span))
        {
            ISymbol? attributeSymbol = GetBestSymbol(model.GetSymbolInfo(attribute));
            return attributeSymbol is IMethodSymbol method ? method.ContainingType : attributeSymbol;
        }

        SyntaxNode target = node;
        if (node is BinaryExpressionSyntax binary)
        {
            target = binary;
        }
        else if (node.Parent is BinaryExpressionSyntax parentBinary
            && parentBinary.OperatorToken.Span.Contains(node.Span))
        {
            target = parentBinary;
        }
        else if (node.Parent is ElementAccessExpressionSyntax elementAccess)
        {
            target = elementAccess;
        }
        else if (node.FirstAncestorOrSelf<ObjectCreationExpressionSyntax>() is { } creation
            && creation.Type.Span.Contains(node.Span))
        {
            target = creation;
        }
        else if (node.FirstAncestorOrSelf<InvocationExpressionSyntax>() is { } invocation
            && invocation.Expression.Span.Contains(node.Span))
        {
            target = invocation;
        }

        ISymbol? conversionSymbol = target is ExpressionSyntax expression
            ? model.GetConversion(expression).MethodSymbol
            : null;
        if (conversionSymbol is IMethodSymbol { MethodKind: MethodKind.Conversion })
        {
            return conversionSymbol;
        }

        return GetBestSymbol(model.GetSymbolInfo(target));
    }

    private static ISymbol? GetBestSymbol(SymbolInfo info)
    {
        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
    }

    private static IndexedSymbolRow? FindIndexedTarget(IReadOnlyList<IndexedSymbolRow> symbols, CorpusCase item)
    {
        return symbols.FirstOrDefault(symbol =>
            NormalizePath(symbol.FilePath).EndsWith(NormalizePath(item.ExpectedTargetRelativePath), StringComparison.OrdinalIgnoreCase)
            && KindMatches(symbol.Kind, item.ExpectedKind)
            && (symbol.Name.Equals(item.ExpectedName, StringComparison.Ordinal)
                || symbol.Signature.Contains(item.ExpectedName, StringComparison.Ordinal)));
    }

    private static bool KindMatches(string actualKind, string expectedKind)
    {
        return expectedKind switch
        {
            "class" or "record" or "interface" => actualKind.Equals("NamedType", StringComparison.OrdinalIgnoreCase),
            "constructor" or "method" or "operator" or "conversion" or "local_function" => actualKind.Equals("Method", StringComparison.OrdinalIgnoreCase),
            "field" or "enum_member" => actualKind.Equals("Field", StringComparison.OrdinalIgnoreCase),
            "property" or "indexer" => actualKind.Equals("Property", StringComparison.OrdinalIgnoreCase),
            "event" => actualKind.Equals("Event", StringComparison.OrdinalIgnoreCase),
            _ => actualKind.Equals(expectedKind, StringComparison.OrdinalIgnoreCase)
        };
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

    private static string BuildSummary(
        SolutionIndexSummary summary,
        int symbolCount,
        int referenceCount,
        IReadOnlyList<CorpusResult> results,
        bool passed,
        bool assertMode)
    {
        string rows = string.Join(Environment.NewLine, results.Select(result =>
            $"- `{result.Case.Name}` mode `{(result.Case.Informational ? "informational" : "asserted")}` Roslyn `{result.Roslyn?.TargetDisplay}` AIMonitor target `{result.IndexedTarget?.StableKey}` refs `{result.References.Count}/{result.Case.ExpectedReferenceCount}` bind row `{result.MonitorReferenceAtBind}` passed `{result.Passed}`"));

        return $"""
            # AIMonitor Language Corpus Smoke

            Assert mode: `{assertMode}`
            Passed asserted cases: `{passed}`

            - Indexed projects: `{summary.ProjectCount}`
            - Indexed documents: `{summary.DocumentCount}`
            - Indexed symbols: `{symbolCount}`
            - Indexed references: `{referenceCount}`
            - Cases: `{results.Count}`
            - Asserted cases: `{results.Count(result => !result.Case.Informational)}`
            - Passing asserted cases: `{results.Count(result => !result.Case.Informational && result.Passed)}`

            ## Matrix

            {rows}
            """;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
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

    private sealed record CorpusCase(
        string Name,
        string RelativePath,
        string ExpectedTargetRelativePath,
        string Source,
        string ExpectedKind,
        string ExpectedName,
        string ExpectedRoslynDisplay,
        bool ExpectedMonitorTarget,
        bool Informational,
        int ExpectedReferenceCount,
        int? ExpectedCallerCount,
        IReadOnlyList<string> ExpectedRelationshipKinds,
        IReadOnlyList<CorpusSourceFile> AdditionalSources)
    {
        public IEnumerable<CorpusSourceFile> AllSources(string primarySource)
        {
            yield return new CorpusSourceFile(RelativePath, primarySource);
            foreach (CorpusSourceFile source in AdditionalSources)
            {
                yield return source;
            }
        }
    }

    private sealed record CorpusSourceFile(string RelativePath, string Source);

    private sealed record CorpusExpected(
        string Name,
        string SourceFile,
        string RelativePath,
        string? ExpectedTargetRelativePath,
        string ExpectedKind,
        string ExpectedName,
        string ExpectedRoslynDisplay,
        bool? ExpectedMonitorTarget,
        bool? Informational,
        int ExpectedReferenceCount,
        int? ExpectedCallerCount,
        IReadOnlyList<string>? ExpectedRelationshipKinds = null,
        IReadOnlyList<CorpusExpectedSource>? AdditionalSources = null);

    private sealed record CorpusExpectedSource(string SourceFile, string RelativePath);

    private sealed record CorpusMarkup(string Source, int BindStart, int BindLength);

    private sealed record RoslynAnswer(bool TargetResolved, string TargetDisplay);

    private sealed record CorpusResult(
        CorpusCase Case,
        RoslynAnswer? Roslyn,
        IndexedSymbolRow? IndexedTarget,
        IReadOnlyList<IndexedReferenceRow> References,
        bool MonitorReferenceAtBind)
    {
        public bool Passed =>
            Roslyn?.TargetResolved == true
            && Roslyn.TargetDisplay.Equals(Case.ExpectedRoslynDisplay, StringComparison.Ordinal)
            && (Case.ExpectedMonitorTarget ? IndexedTarget is not null : IndexedTarget is null)
            && References.Count == Case.ExpectedReferenceCount
            && MonitorReferenceAtBind;
    }
}
