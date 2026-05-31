using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.MSBuild;

namespace AIMonitor.Data.Tests;

public sealed class SolutionIndexQueryServiceTests
{
    [Fact]
    public void GetMonitorStatus_does_not_create_database_when_index_is_missing()
    {
        string tempRoot = CreateTempRoot();
        MonitorSettings settings = MonitorSettings.Create(
            tempRoot,
            Path.Combine(tempRoot, "Watched", "Missing.sln"),
            Path.Combine(tempRoot, "runtime"));
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        SolutionIndexQueryService service = SolutionIndexQueryService.Create(settings);

        MonitorStatusResult status = service.GetMonitorStatus();

        Assert.False(status.DatabaseExists);
        Assert.False(File.Exists(databasePath));
        Assert.Equal(settings.WatchedSolutionPath, status.WatchedSolutionPath);
        Assert.Equal(databasePath, status.DatabasePath);
        Assert.Equal(0, status.ProjectCount);
    }

    [Fact]
    public void Query_methods_filter_documents_symbols_and_file_references()
    {
        string tempRoot = CreateTempRoot();
        string filePath = Path.Combine(tempRoot, "Watched", "Program.cs");
        MonitorSettings settings = MonitorSettings.Create(
            tempRoot,
            Path.Combine(tempRoot, "Watched", "Example.sln"),
            Path.Combine(tempRoot, "runtime"));
        SolutionIndexStore store = new(new SolutionIndexDatabase(MonitorDataPaths.GetDefaultIndexDatabasePath(settings)));
        store.SaveSnapshot(CreateSnapshot(settings.WatchedSolutionPath, filePath));
        SolutionIndexQueryService service = SolutionIndexQueryService.Create(settings);

        IReadOnlyList<IndexedDocumentRow> documents = service.ListDocuments(filePath: filePath);
        IReadOnlyList<IndexedSymbolRow> symbols = service.ListSymbols(filePath, "Program");
        IReadOnlyList<IndexedReferenceRow> references = service.ListReferencesInFile(filePath);

        Assert.Single(documents);
        Assert.Equal(string.Empty, documents[0].ContentHash);
        Assert.Single(symbols);
        Assert.Single(references);
        Assert.Equal("symbol:program", references[0].TargetStableKey);
    }

    private static MSBuildSolutionSnapshot CreateSnapshot(string solutionPath, string filePath)
    {
        return new MSBuildSolutionSnapshot(
            solutionPath,
            [
                new MSBuildProjectSnapshot(
                    "project:example",
                    "Example",
                    Path.Combine(Path.GetDirectoryName(solutionPath)!, "Example.csproj"),
                    "C#",
                    "net10.0",
                    "",
                    "Exe",
                    "Microsoft.NET.Sdk",
                    "Example",
                    "Example",
                    "enable",
                    "enable",
                    "latest",
                    [
                        new MSBuildDocumentSnapshot("document:program", "Program.cs", filePath, [])
                    ],
                    [
                        new MSBuildSymbolSnapshot(
                            "symbol:program",
                            "Program",
                            "NamedType",
                            "Example",
                            "",
                            filePath,
                            1,
                            1,
                            "Example.Program")
                    ],
                    [
                        new MSBuildReferenceSnapshot(
                            "symbol:program",
                            filePath,
                            1,
                            45,
                            "IdentifierName",
                            "Program")
                    ],
                    [],
                    [],
                    [],
                    [],
                    [],
                    [])
            ],
            []);
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "AIMonitorDataTests", Guid.NewGuid().ToString("N"));
    }
}
