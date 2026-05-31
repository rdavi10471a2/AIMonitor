using AIMonitor.Data;
using AIMonitor.MSBuild;

namespace AIMonitor.Data.Tests;

public sealed class SolutionIndexStoreTests
{
    [Fact]
    public void SaveSnapshot_persists_projects_documents_and_diagnostics()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), "AIMonitorTests", Guid.NewGuid().ToString("N"), "index.sqlite");
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
        MSBuildSolutionSnapshot snapshot = new(
            @"C:\Example\Example.sln",
            [
                new MSBuildProjectSnapshot(
                    "Example",
                    @"C:\Example\Example.csproj",
                    "C#",
                    [
                        new MSBuildDocumentSnapshot("Program.cs", @"C:\Example\Program.cs", [])
                    ],
                    ["DEBUG"])
            ],
            ["diagnostic"]);

        SolutionIndexRunSummary summary = store.SaveSnapshot(snapshot);
        IReadOnlyList<IndexedDocumentRow> documents = store.ListDocuments(summary.RunId);

        Assert.Equal(1, summary.ProjectCount);
        Assert.Equal(1, summary.DocumentCount);
        Assert.Equal(1, summary.DiagnosticCount);
        Assert.Single(documents);
        Assert.Equal("Program.cs", documents[0].Name);
    }
}
