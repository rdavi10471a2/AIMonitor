using AIMonitor.Data;
using AIMonitor.MSBuild;
using Microsoft.Data.Sqlite;

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
                        new MSBuildDocumentSnapshot("Program.cs", @"C:\Example\Program.cs", [])
                    ],
                    [],
                    [new MSBuildProjectReferenceSnapshot(@"..\Lib\Lib.csproj", @"C:\Lib\Lib.csproj")],
                    [new MSBuildPackageReferenceSnapshot("Microsoft.Data.Sqlite", "10.0.8")],
                    [new MSBuildFrameworkReferenceSnapshot("Microsoft.WindowsDesktop.App.WindowsForms")],
                    [new MSBuildGlobalUsingSnapshot("System", "", "")],
                    ["DEBUG"])
            ],
            ["diagnostic"]);

        SolutionIndexSummary summary = store.SaveSnapshot(snapshot);
        IReadOnlyList<IndexedDocumentRow> documents = store.ListDocuments();
        IReadOnlyList<IndexedProjectRow> projects = store.ListProjects();
        IReadOnlyList<IndexedPackageReferenceRow> packages = store.ListPackageReferences();

        Assert.Equal(1, summary.ProjectCount);
        Assert.Equal(1, summary.DocumentCount);
        Assert.Equal(1, summary.DiagnosticCount);
        Assert.Single(documents);
        Assert.Equal("Program.cs", documents[0].Name);
        Assert.Single(projects);
        Assert.Equal("net10.0", projects[0].TargetFramework);
        Assert.Single(packages);
        Assert.Equal("Microsoft.Data.Sqlite", packages[0].Include);
        Assert.False(TableExists(databasePath, "index_runs"));
        Assert.True(TableExists(databasePath, "solution_state"));
    }

    private static bool TableExists(string databasePath, string tableName)
    {
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            select count(*)
            from sqlite_master
            where type = 'table' and name = $tableName;
            """;
        command.Parameters.AddWithValue("$tableName", tableName);

        object? result = command.ExecuteScalar();
        return Convert.ToInt32(result) > 0;
    }
}
