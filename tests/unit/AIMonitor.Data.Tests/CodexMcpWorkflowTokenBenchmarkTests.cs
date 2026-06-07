using AIMonitor.Core;
using AIMonitor.MSBuild;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIMonitor.Data.Tests;

// Codex-owned token benchmark: measure the context Codex ingests when it follows the AIMonitor MCP-first
// navigation workflow instead of falling back to local grep/read exploration.
public sealed class CodexMcpWorkflowTokenBenchmarkTests
{
    private const string DefaultBenchSolution = @"C:\VSCodeProjects\SchemaStudioBench\SchemaStudioWebViewer.sln";
    private const string DefaultOutputDir = @"C:\VSCodeProjects\AIMonitor\runtime\codex-token-benchmark";

    [Fact]
    [Trait("Suite", "CodexTokenBenchmark")]
    public async Task FullCoverage_codex_mcp_first_vs_local_grep_read()
    {
        string solutionPath = Environment.GetEnvironmentVariable("CODEX_BENCH_SOLUTION") ?? DefaultBenchSolution;
        if (!File.Exists(solutionPath))
        {
            return;
        }

        string sourceRoot = Path.GetDirectoryName(solutionPath)!;
        string outputDir = Environment.GetEnvironmentVariable("CODEX_BENCH_OUT") ?? DefaultOutputDir;
        Directory.CreateDirectory(outputDir);

        string tempRoot = Path.Combine(Path.GetTempPath(), "AIMonitorCodexTokenBench", Guid.NewGuid().ToString("N"));
        MonitorSettings settings = MonitorSettings.Create(tempRoot, solutionPath, Path.Combine(tempRoot, "runtime"));
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
        await new SolutionIndexBuilder(new MSBuildWorkspaceLoader(), store).RebuildAsync(settings);

        SolutionIndexQueryService query = SolutionIndexQueryService.Create(settings);
        JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
        IReadOnlyList<IndexedSymbolQueryItem> symbols = query.FindSymbols(string.Empty, maxResults: 50000).Symbols;
        IReadOnlyList<IndexedReferenceRow> allReferences = query.ListReferences(null);
        ILookup<string, IndexedReferenceRow> referencesByTarget =
            allReferences.ToLookup(reference => reference.TargetStableKey, StringComparer.Ordinal);
        List<SourceFile> sourceFiles = LoadSourceFiles(sourceRoot);

        Dictionary<string, int> symbolLookupBytes = new(StringComparer.Ordinal);
        Dictionary<string, GrepCost> grepCache = new(StringComparer.Ordinal);

        StringBuilder csv = new();
        csv.AppendLine("name,kind,refCount,codexMcpBytes,codexMcpTokens,localGrepContextBytes,localGrepContextTokens,localReadBytes,localReadTokens,localHitFiles,readRatio,contextRatio,isMember,qualifiedResolved");

        long codexMcpTotalBytes = 0;
        long localContextTotalBytes = 0;
        long localReadTotalBytes = 0;
        int measured = 0;
        int mcpBeatsRead = 0;
        int mcpBeatsContext = 0;
        int memberCount = 0;
        int qualifiedMisses = 0;
        int errors = 0;
        List<double> readRatios = new();
        List<(string Name, long LocalReadBytes, long McpBytes)> biggestWins = new();

        Stopwatch stopwatch = Stopwatch.StartNew();
        foreach (IndexedSymbolQueryItem item in symbols)
        {
            IndexedSymbolRow symbol = item.Symbol;
            if (string.IsNullOrWhiteSpace(symbol.Name))
            {
                continue;
            }

            try
            {
                string containingSimple = GetContainingTypeSimpleName(symbol.ContainingType);
                bool isMember = containingSimple.Length > 0;
                string codexQueryText = isMember ? containingSimple + "." + symbol.Name : symbol.Name;
                int qualifiedBytes = MeasureFindSymbols(query, json, symbolLookupBytes, codexQueryText);
                bool qualifiedResolved = query.FindSymbols(codexQueryText, maxResults: 1).TotalSymbolCount > 0;
                int lookupBytes = qualifiedBytes;
                if (!qualifiedResolved)
                {
                    lookupBytes += MeasureFindSymbols(query, json, symbolLookupBytes, symbol.Name);
                    qualifiedMisses++;
                }

                if (isMember)
                {
                    memberCount++;
                }

                IndexedReferenceRow[] references = referencesByTarget[symbol.StableKey].Take(500).ToArray();
                LeanReferenceRow[] leanReferences = references.Select(ToLeanReference).ToArray();
                int referenceBytes = JsonSerializer.Serialize(leanReferences, json).Length;
                int definitionBytes = JsonSerializer.Serialize(symbol, json).Length;
                long codexMcpBytes = lookupBytes + referenceBytes + definitionBytes;

                if (!grepCache.TryGetValue(symbol.Name, out GrepCost grep))
                {
                    grep = MeasureGrep(symbol.Name, sourceFiles);
                    grepCache[symbol.Name] = grep;
                }

                long localContextBytes = grep.ContextBytes;
                long localReadBytes = grep.PlainBytes + grep.FilesTotalBytes;
                double readRatio = codexMcpBytes > 0 ? (double)localReadBytes / codexMcpBytes : 0;
                double contextRatio = codexMcpBytes > 0 ? (double)localContextBytes / codexMcpBytes : 0;

                codexMcpTotalBytes += codexMcpBytes;
                localContextTotalBytes += localContextBytes;
                localReadTotalBytes += localReadBytes;
                measured++;
                if (localReadBytes > codexMcpBytes)
                {
                    mcpBeatsRead++;
                }

                if (localContextBytes > codexMcpBytes)
                {
                    mcpBeatsContext++;
                }

                readRatios.Add(readRatio);
                biggestWins.Add((symbol.Name, localReadBytes, codexMcpBytes));

                csv.Append('"').Append(symbol.Name.Replace("\"", "\"\"")).Append('"').Append(',')
                    .Append(symbol.Kind).Append(',')
                    .Append(references.Length.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(codexMcpBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append((codexMcpBytes / 4.0).ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                    .Append(localContextBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append((localContextBytes / 4.0).ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                    .Append(localReadBytes.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append((localReadBytes / 4.0).ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                    .Append(grep.FileCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(readRatio.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                    .Append(contextRatio.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                    .Append(isMember ? "1" : "0").Append(',')
                    .Append(qualifiedResolved ? "1" : "0")
                    .AppendLine();
            }
            catch (Exception ex)
            {
                errors++;
                if (errors <= 20)
                {
                    csv.Append('"').Append(symbol.Name.Replace("\"", "\"\"")).Append("\",ERROR,,,,,,,,,,,,")
                        .Append('"').Append(ex.Message.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ")).Append('"')
                        .AppendLine();
                }
            }
        }

        stopwatch.Stop();
        readRatios.Sort();
        biggestWins.Sort((left, right) => right.LocalReadBytes.CompareTo(left.LocalReadBytes));
        double medianReadRatio = readRatios.Count > 0 ? readRatios[readRatios.Count / 2] : 0;
        double aggregateReadRatio = codexMcpTotalBytes > 0 ? (double)localReadTotalBytes / codexMcpTotalBytes : 0;
        double aggregateContextRatio = codexMcpTotalBytes > 0 ? (double)localContextTotalBytes / codexMcpTotalBytes : 0;

        StringBuilder summary = new();
        summary.AppendLine("# Codex MCP-first vs local grep/read token benchmark");
        summary.AppendLine($"target solution: {solutionPath}");
        summary.AppendLine($"elapsed: {stopwatch.Elapsed:hh\\:mm\\:ss}");
        summary.AppendLine($"symbols measured: {measured}   (errors: {errors})");
        summary.AppendLine("metric: bytes Codex ingests for symbol navigation and impact discovery; tokens = bytes / 4");
        summary.AppendLine();
        summary.AppendLine("## Aggregate");
        summary.AppendLine($"Codex MCP-first total tokens:     {codexMcpTotalBytes / 4:N0}");
        summary.AppendLine($"Codex local grep-context tokens:  {localContextTotalBytes / 4:N0}   (candidate view, not a complete answer)");
        summary.AppendLine($"Codex local grep-read tokens:     {localReadTotalBytes / 4:N0}   (grep hits plus reading every hit file)");
        summary.AppendLine($"AGGREGATE RATIO local grep-read / MCP-first = {aggregateReadRatio:F2}x");
        summary.AppendLine($"AGGREGATE RATIO local context   / MCP-first = {aggregateContextRatio:F2}x");
        summary.AppendLine();
        summary.AppendLine("## Per-symbol");
        summary.AppendLine($"median local-read ratio: {medianReadRatio:F2}x");
        summary.AppendLine($"MCP-first cheaper than local grep-read: {mcpBeatsRead}/{measured} ({Percent(mcpBeatsRead, measured):F1}%)");
        summary.AppendLine($"MCP-first cheaper than local context:   {mcpBeatsContext}/{measured} ({Percent(mcpBeatsContext, measured):F1}%)");
        summary.AppendLine();
        summary.AppendLine("## Qualified member lookup");
        summary.AppendLine($"members measured: {memberCount}");
        summary.AppendLine($"members still forcing bare-name fallback: {qualifiedMisses}");
        summary.AppendLine();
        summary.AppendLine("## Top 15 by local grep-read cost");
        foreach ((string Name, long LocalReadBytes, long McpBytes) entry in biggestWins.Take(15))
        {
            double ratio = entry.McpBytes > 0 ? (double)entry.LocalReadBytes / entry.McpBytes : 0;
            summary.AppendLine($"  {entry.Name,-45} local {entry.LocalReadBytes / 4,9:N0} tok  vs MCP {entry.McpBytes / 4,7:N0} tok  = {ratio:F1}x");
        }

        string csvPath = Path.Combine(outputDir, "codex-baseline.csv");
        string summaryPath = Path.Combine(outputDir, "codex-baseline-summary.txt");
        File.WriteAllText(csvPath, csv.ToString());
        File.WriteAllText(summaryPath, summary.ToString());
        Console.WriteLine(summary.ToString());
        Console.WriteLine($"[codex-token-bench] wrote {csvPath} and {summaryPath}");

        Assert.True(measured > 0, "No symbols measured.");
    }

    private static int MeasureFindSymbols(
        SolutionIndexQueryService query,
        JsonSerializerOptions json,
        Dictionary<string, int> cache,
        string text)
    {
        if (cache.TryGetValue(text, out int cached))
        {
            return cached;
        }

        IndexedSymbolSearchResult result = query.FindSymbols(text, maxResults: 100);
        int bytes = JsonSerializer.Serialize(result, json).Length;
        cache[text] = bytes;
        return bytes;
    }

    private static string GetContainingTypeSimpleName(string containingType)
    {
        if (string.IsNullOrEmpty(containingType))
        {
            return string.Empty;
        }

        int dot = containingType.LastIndexOf('.');
        if (dot >= 0)
        {
            return containingType[(dot + 1)..];
        }

        return containingType;
    }

    private sealed record LeanReferenceRow(
        string TargetStableKey,
        string FilePath,
        int Line,
        int Column,
        string ReferenceKind,
        string Snippet,
        string TargetName,
        string TargetKind,
        string CallerStableKey,
        string CallerName,
        string CallerKind);

    private static LeanReferenceRow ToLeanReference(IndexedReferenceRow reference)
    {
        return new LeanReferenceRow(
            reference.TargetStableKey,
            reference.FilePath,
            reference.Line,
            reference.Column,
            reference.ReferenceKind,
            reference.Snippet,
            reference.TargetName,
            reference.TargetKind,
            reference.CallerStableKey,
            reference.CallerName,
            reference.CallerKind);
    }

    private sealed record SourceFile(string AbsPath, string[] Lines, long Size);

    private readonly record struct GrepCost(long PlainBytes, long ContextBytes, int FileCount, long FilesTotalBytes);

    private static List<SourceFile> LoadSourceFiles(string root)
    {
        List<SourceFile> files = new();
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string normalized = path.Replace('/', '\\');
            if (normalized.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("\\.vs\\", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("\\.git\\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".bak" || extension == ".log")
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch
            {
                continue;
            }

            if (IsBinary(bytes))
            {
                continue;
            }

            string text = Encoding.UTF8.GetString(bytes);
            files.Add(new SourceFile(path, text.Split('\n'), bytes.Length));
        }

        return files;
    }

    private static bool IsBinary(byte[] bytes)
    {
        int sniffLength = Math.Min(bytes.Length, 8000);
        for (int index = 0; index < sniffLength; index++)
        {
            if (bytes[index] == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static GrepCost MeasureGrep(string name, List<SourceFile> files)
    {
        Regex word = new(@"\b" + Regex.Escape(name) + @"\b", RegexOptions.CultureInvariant);
        long plainBytes = 0;
        long contextBytes = 0;
        long filesTotalBytes = 0;
        int fileCount = 0;

        foreach (SourceFile file in files)
        {
            List<int>? matches = null;
            for (int index = 0; index < file.Lines.Length; index++)
            {
                string line = file.Lines[index];
                if (!line.Contains(name, StringComparison.Ordinal) || !word.IsMatch(line))
                {
                    continue;
                }

                matches ??= new List<int>();
                matches.Add(index);
            }

            if (matches == null)
            {
                continue;
            }

            fileCount++;
            filesTotalBytes += file.Size;

            foreach (int index in matches)
            {
                string line = file.Lines[index].TrimEnd('\r');
                plainBytes += Encoding.UTF8.GetByteCount(file.AbsPath) + Encoding.UTF8.GetByteCount(line)
                    + EncodedIntLength(index + 1) + 3;
            }

            SortedSet<int> included = new();
            foreach (int match in matches)
            {
                int from = Math.Max(0, match - 3);
                int to = Math.Min(file.Lines.Length - 1, match + 3);
                for (int contextIndex = from; contextIndex <= to; contextIndex++)
                {
                    included.Add(contextIndex);
                }
            }

            int previous = int.MinValue;
            foreach (int index in included)
            {
                if (previous != int.MinValue && index != previous + 1)
                {
                    contextBytes += 3;
                }

                string line = file.Lines[index].TrimEnd('\r');
                contextBytes += Encoding.UTF8.GetByteCount(file.AbsPath) + Encoding.UTF8.GetByteCount(line)
                    + EncodedIntLength(index + 1) + 3;
                previous = index;
            }
        }

        return new GrepCost(plainBytes, contextBytes, fileCount, filesTotalBytes);
    }

    private static int EncodedIntLength(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture).Length;
    }

    private static double Percent(int numerator, int denominator)
    {
        if (denominator == 0)
        {
            return 0;
        }

        return 100.0 * numerator / denominator;
    }
}
