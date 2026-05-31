using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.MSBuild;

namespace AIMonitor.Data.Tests;

public sealed class SolutionIndexBuilderTests
{
    [Fact]
    public async Task RebuildAsync_loads_configured_solution_and_writes_sqlite_index()
    {
        string root = Path.Combine(Path.GetTempPath(), "AIMonitorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string solutionPath = Path.Combine(root, "Fixture.slnx");
        string projectPath = Path.Combine(root, "Fixture", "Fixture.csproj");
        string sourcePath = Path.Combine(root, "Fixture", "Program.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);

        await File.WriteAllTextAsync(solutionPath, $$"""
            <Solution>
              <Project Path="Fixture/Fixture.csproj" />
            </Solution>
            """);

        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(sourcePath, """
            namespace Fixture;

            public sealed class Program
            {
                public static void Main()
                {
                }
            }
            """);

        MonitorSettings settings = MonitorSettings.Create(root, solutionPath);
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
        SolutionIndexBuilder builder = new(new MSBuildWorkspaceLoader(), store);

        SolutionIndexRunSummary summary = await builder.RebuildAsync(settings);
        IReadOnlyList<IndexedDocumentRow> documents = store.ListDocuments(summary.RunId);

        Assert.True(File.Exists(databasePath));
        Assert.StartsWith(
            MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            databasePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, summary.ProjectCount);
        Assert.Contains(documents, document => document.Name == "Program.cs");
    }
}
