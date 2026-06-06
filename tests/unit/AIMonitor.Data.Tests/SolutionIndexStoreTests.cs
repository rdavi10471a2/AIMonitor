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
                    "project:test",
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
                        new MSBuildDocumentSnapshot("document:test", "Program.cs", @"C:\Example\Program.cs", [], "abc123")
                    ],
                    [
                        new MSBuildSymbolSnapshot(
                            @"C:/Example/Program.cs::NamedType::Example.Program::3",
                            "Program",
                            "NamedType",
                            "Example",
                            "",
                            @"C:\Example\Program.cs",
                            3,
                            8,
                            "Example.Program"),
                        new MSBuildSymbolSnapshot(
                            "symbol:get-value",
                            "GetValue",
                            "Method",
                            "Example",
                            "Program",
                            @"C:\Example\Program.cs",
                            5,
                            7,
                            "Example.Program.GetValue()",
                            "Public",
                            false,
                            false,
                            false,
                            true,
                            false,
                            "Ordinary"),
                        new MSBuildSymbolSnapshot(
                            "symbol:target-method",
                            "TargetMethod",
                            "Method",
                            "Example",
                            "Program",
                            @"C:\Example\Program.cs",
                            10,
                            12,
                            "Example.Program.TargetMethod()")
                    ],
                    [
                        new MSBuildReferenceSnapshot(
                            @"C:/Example/Program.cs::NamedType::Example.Program::3",
                            @"C:\Example\Program.cs",
                            5,
                            13,
                            "IdentifierName",
                            "Program"),
                        new MSBuildReferenceSnapshot(
                            "symbol:target-method",
                            @"C:\Example\Program.cs",
                            6,
                            20,
                            "InvocationExpression",
                            "TargetMethod()"),
                        new MSBuildReferenceSnapshot(
                            @"C:/Example/Program.cs::NamedType::Example.Program::3",
                            @"C:\Example\Program.cs",
                            3,
                            1,
                            "partial_declaration",
                            "Program")
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
        IReadOnlyList<IndexedSymbolRow> symbols = store.ListSymbols();
        IReadOnlyList<IndexedReferenceRow> references = store.ListReferences(symbols[0].StableKey);
        IReadOnlyList<IndexedCallSiteRow> callSites = store.ListCallSites("symbol:target-method");
        IReadOnlyList<IndexedRelationshipRow> relationships = store.ListRelationships(symbols[0].StableKey);
        IReadOnlyList<IndexedPackageReferenceRow> packages = store.ListPackageReferences();

        Assert.Equal(1, summary.ProjectCount);
        Assert.Equal(1, summary.DocumentCount);
        Assert.Equal(1, summary.DiagnosticCount);
        Assert.Single(documents);
        Assert.Equal("Program.cs", documents[0].Name);
        Assert.Equal("abc123", documents[0].ContentHash);
        Assert.Single(projects);
        Assert.Equal("project:test", projects[0].StableKey);
        Assert.Equal("net10.0", projects[0].TargetFramework);
        Assert.Equal(3, symbols.Count);
        IndexedSymbolRow getValue = Assert.Single(symbols, symbol => symbol.StableKey == "symbol:get-value");
        Assert.Equal("Public", getValue.Accessibility);
        Assert.True(getValue.IsVirtual);
        Assert.False(getValue.IsSealed);
        Assert.Equal("Ordinary", getValue.MethodKind);
        Assert.Contains(references, reference => reference.ReferenceKind == "IdentifierName");
        Assert.Contains(references, reference => reference.ReferenceKind == "partial_declaration");
        IndexedCallSiteRow callSite = Assert.Single(callSites);
        Assert.Equal("symbol:get-value", callSite.CallerStableKey);
        Assert.Equal("GetValue", callSite.CallerName);
        Assert.Equal("symbol:target-method", callSite.TargetStableKey);
        IndexedRelationshipRow relationship = Assert.Single(relationships);
        Assert.Equal("partial_declaration", relationship.RelationshipKind);
        Assert.Equal(symbols[0].StableKey, relationship.SourceStableKey);
        Assert.Equal(symbols[0].StableKey, relationship.TargetStableKey);
        Assert.Single(packages);
        Assert.Equal("Microsoft.Data.Sqlite", packages[0].Include);
        Assert.False(TableExists(databasePath, "index_runs"));
        Assert.True(TableExists(databasePath, "solution_state"));
    }

    [Fact]
    public void SaveSnapshot_replaces_previous_snapshot_rows()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), "AIMonitorTests", Guid.NewGuid().ToString("N"), "index.sqlite");
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));

        store.SaveSnapshot(CreateSnapshot(
            @"C:\Example\Example.sln",
            "project:old",
            "Old",
            @"C:\Example\Old.csproj",
            "Old.cs",
            @"C:\Example\Old.cs",
            "symbol:old",
            "OldType",
            "Old.Package",
            "old diagnostic"));

        SolutionIndexSummary summary = store.SaveSnapshot(CreateSnapshot(
            @"C:\Example\Example.sln",
            "project:new",
            "New",
            @"C:\Example\New.csproj",
            "New.cs",
            @"C:\Example\New.cs",
            "symbol:new",
            "NewType",
            "New.Package",
            "new diagnostic"));

        Assert.Equal(1, summary.ProjectCount);
        Assert.Equal(1, summary.DocumentCount);
        Assert.Equal(1, summary.DiagnosticCount);
        Assert.DoesNotContain(store.ListProjects(), project => project.StableKey == "project:old");
        Assert.DoesNotContain(store.ListDocuments(), document => document.Name == "Old.cs");
        Assert.DoesNotContain(store.ListSymbols(), symbol => symbol.StableKey == "symbol:old");
        Assert.DoesNotContain(store.ListReferences(), reference => reference.TargetStableKey == "symbol:old");
        Assert.DoesNotContain(store.ListPackageReferences(), package => package.Include == "Old.Package");
        Assert.Contains(store.ListProjects(), project => project.StableKey == "project:new");
        Assert.Contains(store.ListDocuments(), document => document.Name == "New.cs");
        Assert.Contains(store.ListSymbols(), symbol => symbol.StableKey == "symbol:new");
        Assert.Contains(store.ListReferences(), reference => reference.TargetStableKey == "symbol:new");
        Assert.Contains(store.ListPackageReferences(), package => package.Include == "New.Package");
    }

    [Fact]
    public void SaveSnapshot_zero_project_snapshot_does_not_clear_existing_index()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), "AIMonitorTests", Guid.NewGuid().ToString("N"), "index.sqlite");
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));

        store.SaveSnapshot(CreateSnapshot(
            @"C:\Example\Example.sln",
            "project:old",
            "Old",
            @"C:\Example\Old.csproj",
            "Old.cs",
            @"C:\Example\Old.cs",
            "symbol:old",
            "OldType",
            "Old.Package",
            "old diagnostic"));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            store.SaveSnapshot(new MSBuildSolutionSnapshot(
                @"C:\Example\Example.sln",
                [],
                ["degraded load"])));

        Assert.Contains("zero-project snapshot", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(store.ListProjects());
        Assert.Contains(store.ListDocuments(), document => document.Name == "Old.cs");
        Assert.Contains(store.ListSymbols(), symbol => symbol.StableKey == "symbol:old");
        Assert.Equal(1, store.GetSummary().ProjectCount);
    }

    private static MSBuildSolutionSnapshot CreateSnapshot(
        string inputPath,
        string projectKey,
        string projectName,
        string projectPath,
        string documentName,
        string documentPath,
        string symbolKey,
        string symbolName,
        string packageName,
        string diagnostic)
    {
        return new MSBuildSolutionSnapshot(
            inputPath,
            [
                new MSBuildProjectSnapshot(
                    projectKey,
                    projectName,
                    projectPath,
                    "C#",
                    "net10.0",
                    "",
                    "Library",
                    "Microsoft.NET.Sdk",
                    projectName,
                    projectName,
                    "enable",
                    "enable",
                    "latest",
                    [
                        new MSBuildDocumentSnapshot($"document:{documentName}", documentName, documentPath, [], documentName)
                    ],
                    [
                        new MSBuildSymbolSnapshot(
                            symbolKey,
                            symbolName,
                            "NamedType",
                            projectName,
                            "",
                            documentPath,
                            1,
                            1,
                            $"{projectName}.{symbolName}")
                    ],
                    [
                        new MSBuildReferenceSnapshot(
                            symbolKey,
                            documentPath,
                            1,
                            1,
                            "IdentifierName",
                            symbolName)
                    ],
                    [],
                    [],
                    [new MSBuildPackageReferenceSnapshot(packageName, "1.0.0")],
                    [],
                    [],
                    []),
            ],
            [diagnostic]);
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
