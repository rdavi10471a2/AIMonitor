using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.MSBuild;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AIMonitor.Integration.Tests;

public sealed class McpServerSmokeTests
{
    [Fact]
    public async Task Mcp_server_lists_monitor_tools_and_serves_index_queries()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);

        IList<McpClientTool> tools = await client.ListToolsAsync();
        string[] toolNames = tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray();

        string[] expectedToolNames =
        [
            "get_monitor_status",
            "get_workflow_status",
            "get_self_check",
            "refresh_solution_index",
            "refresh_solution_index_file",
            "refresh_file_and_index",
            "get_solution_index_status",
            "get_solution_index",
            "get_solution_index_tree",
            "query_solution_index",
            "find_indexed_symbols",
            "get_indexed_symbol",
            "find_indexed_references",
            "find_indexed_callers",
            "find_indexed_relationships",
            "start_monitor_session",
            "list_monitor_sessions",
            "get_monitor_session",
            "record_monitor_session_event",
            "list_session_staged_records",
            "refresh_file",
            "new_file",
            "get_file",
            "check_file_hash",
            "find_file",
            "get_file_outline",
            "get_source_map",
            "get_symbol",
            "submit_file",
            "replace_text_in_file",
            "find_text_span",
            "replace_span_in_file",
            "stage_candidate_for_review",
            "submit_symbol",
            "add_using",
            "remove_using",
            "set_type_partial",
            "add_symbol",
            "add_field",
            "add_property",
            "add_method",
            "add_constructor",
            "add_nested_type",
            "remove_symbol",
            "launch_staged_diff",
            "record_diff_decision",
            "compare_file",
            "list_monitor_runs",
            "get_monitor_run",
            "list_ledgers",
            "get_ledger",
            "prune_monitor_history",
            "get_tool_manifest",
            "get_staging_guide",
            "get_smoke_test_catalog",
            "list_watched_projects",
            "shutdown_server"
        ];
        foreach (string expectedToolName in expectedToolNames)
        {
            Assert.Contains(expectedToolName, toolNames);
        }

        CallToolResult status = await client.CallToolAsync("get_monitor_status");
        Assert.False(status.IsError == true);
        Assert.Contains("projectCount", Serialize(status), StringComparison.Ordinal);

        CallToolResult symbols = await client.CallToolAsync(
            "find_indexed_symbols",
            new Dictionary<string, object?>
            {
                ["text"] = "Program"
            });
        Assert.False(symbols.IsError == true);
        string symbolsJson = ExtractToolText(symbols);
        Assert.Contains(fixture.ProgramSymbolStableKey, symbolsJson, StringComparison.Ordinal);
        Assert.Contains("Program.cs", symbolsJson, StringComparison.Ordinal);

        string logPath = Path.Combine(fixture.RuntimeRoot, "logs", "aimonitor.ndjson");
        Assert.True(File.Exists(logPath));
        string logText = await File.ReadAllTextAsync(logPath);
        Assert.Contains("adapter.mcp.tool.called", logText, StringComparison.Ordinal);
        Assert.Contains("get_monitor_status", logText, StringComparison.Ordinal);
        Assert.Contains("find_indexed_symbols", logText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_refresh_and_stage_use_monitor_working_copy()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);

        CallToolResult refresh = await client.CallToolAsync(
            "refresh_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = fixture.ProgramFilePath
            });
        Assert.False(refresh.IsError == true);
        string refreshJson = ExtractToolText(refresh);
        string workingFilePath = ExtractJsonString(refreshJson, "workingFilePath");
        Assert.True(File.Exists(workingFilePath));
        Assert.Equal("namespace Example { internal static class Program { } }", await File.ReadAllTextAsync(fixture.ProgramFilePath));

        await File.WriteAllTextAsync(workingFilePath, "namespace Example { internal static class Program { public static string Value => \"mcp\"; } }");

        CallToolResult stage = await client.CallToolAsync(
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["ledgerSummary"] = "mcp smoke candidate"
        });
        Assert.False(stage.IsError == true);
        string stageJson = ExtractToolText(stage);
        Assert.Contains("stagedRecordId", stageJson, StringComparison.Ordinal);
        Assert.DoesNotContain("mcp", await File.ReadAllTextAsync(fixture.ProgramFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_candidate_tools_edit_working_copy_only()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);

        CallToolResult session = await client.CallToolAsync(
            "start_monitor_session",
            new Dictionary<string, object?>
            {
                ["purpose"] = "mcp candidate smoke"
            });
        Assert.False(session.IsError == true);
        string sessionId = ExtractJsonString(ExtractToolText(session), "sessionId");

        CallToolResult read = await client.CallToolAsync(
            "get_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = fixture.ProgramFilePath,
                ["sessionId"] = sessionId
            });
        Assert.False(read.IsError == true);
        Assert.Contains("namespace Example", ExtractToolText(read), StringComparison.Ordinal);

        CallToolResult submit = await client.CallToolAsync(
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["content"] = "namespace Example { internal static class Program { public static string Value => \"submitted\"; } }",
                ["sessionId"] = sessionId
            });
        Assert.False(submit.IsError == true);
        string workingFilePath = ExtractJsonString(ExtractToolText(submit), "workingFilePath");
        Assert.Contains("submitted", await File.ReadAllTextAsync(workingFilePath), StringComparison.Ordinal);
        Assert.DoesNotContain("submitted", await File.ReadAllTextAsync(fixture.ProgramFilePath), StringComparison.Ordinal);

        CallToolResult replaceText = await client.CallToolAsync(
            "replace_text_in_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["oldText"] = "submitted",
                ["newText"] = "replaced",
                ["expectedMatches"] = 1,
                ["sessionId"] = sessionId
            });
        Assert.False(replaceText.IsError == true);
        Assert.Contains("replaced", await File.ReadAllTextAsync(workingFilePath), StringComparison.Ordinal);

        CallToolResult span = await client.CallToolAsync(
            "find_text_span",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["findText"] = "replaced"
            });
        Assert.False(span.IsError == true);
        Assert.Contains("textHash", ExtractToolText(span), StringComparison.Ordinal);

        CallToolResult compatibility = await client.CallToolAsync(
            "get_source_map",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath
            });
        Assert.False(compatibility.IsError == true);
        Assert.Contains("not-implemented", ExtractToolText(compatibility), StringComparison.Ordinal);

        CallToolResult records = await client.CallToolAsync(
            "list_session_staged_records",
            new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId
            });
        Assert.False(records.IsError == true);
    }

    private static async Task<McpClient> CreateClientAsync(McpFixture fixture)
    {
        string serverDll = Path.Combine(
            fixture.RepositoryRoot,
            "src",
            "AIMonitor.McpServer",
            "bin",
            GetBuildConfiguration(),
            "net10.0",
            "AIMonitor.McpServer.dll");

        StdioClientTransportOptions options = new()
        {
            Name = "ai-monitor",
            Command = "dotnet",
            Arguments = [serverDll, "--repo-root", fixture.RepositoryRoot, "--config", fixture.SettingsPath],
            WorkingDirectory = fixture.RepositoryRoot
        };

        return await McpClient.CreateAsync(new StdioClientTransport(options));
    }

    private static McpFixture CreateFixture()
    {
        string repositoryRoot = FindRepositoryRoot();
        string tempRoot = Path.Combine(Path.GetTempPath(), "AIMonitorMcpTests", Guid.NewGuid().ToString("N"));
        string settingsPath = Path.Combine(tempRoot, "config", "appsettings.json");
        string runtimeRoot = Path.Combine(tempRoot, "runtime");
        string watchedSolutionPath = Path.Combine(tempRoot, "Watched", "Example.csproj");
        string programFilePath = Path.Combine(tempRoot, "Watched", "Program.cs");
        string programSymbolStableKey = "symbol:program";

        Directory.CreateDirectory(Path.GetDirectoryName(watchedSolutionPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(
            watchedSolutionPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(programFilePath, "namespace Example { internal static class Program { } }");

        MonitorSettingsLoader.SaveLocal(repositoryRoot, watchedSolutionPath, runtimeRoot, settingsPath);
        MonitorSettings settings = MonitorSettingsLoader.Load(repositoryRoot, settingsPath);
        SolutionIndexStore store = new(new SolutionIndexDatabase(MonitorDataPaths.GetDefaultIndexDatabasePath(settings)));
        store.SaveSnapshot(new MSBuildSolutionSnapshot(
            watchedSolutionPath,
            [
                new MSBuildProjectSnapshot(
                    "project:example",
                    "Example",
                    watchedSolutionPath,
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
                    [new MSBuildDocumentSnapshot("document:program", "Program.cs", programFilePath, [])],
                    [
                        new MSBuildSymbolSnapshot(
                            programSymbolStableKey,
                            "Program",
                            "NamedType",
                            "Example",
                            "",
                            programFilePath,
                            1,
                            1,
                            "Example.Program")
                    ],
                    [
                        new MSBuildReferenceSnapshot(
                            programSymbolStableKey,
                            programFilePath,
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
                    ["DEBUG"])
            ],
            []));

        return new McpFixture(repositoryRoot, settingsPath, watchedSolutionPath, programFilePath, runtimeRoot, programSymbolStableKey);
    }

    private static string Serialize(object value)
    {
        return System.Text.Json.JsonSerializer.Serialize(value);
    }

    private static string ExtractToolText(CallToolResult result)
    {
        string wrapperJson = Serialize(result);
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(wrapperJson);
        foreach (System.Text.Json.JsonElement content in document.RootElement.GetProperty("content").EnumerateArray())
        {
            if (content.TryGetProperty("text", out System.Text.Json.JsonElement text)
                && text.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return text.GetString() ?? string.Empty;
            }
        }

        throw new InvalidOperationException("MCP tool result did not include text content.");
    }

    private static string ExtractJsonString(string json, string propertyName)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        return FindString(document.RootElement, propertyName)
            ?? throw new InvalidOperationException($"Could not find JSON string property '{propertyName}'.");
    }

    private static string? FindString(System.Text.Json.JsonElement element, string propertyName)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (System.Text.Json.JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName)
                    && property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                string? nested = FindString(property.Value, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (System.Text.Json.JsonElement item in element.EnumerateArray())
            {
                string? nested = FindString(item, propertyName);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string GetBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AIMonitor.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find AIMonitor.slnx.");
    }

    private sealed record McpFixture(
        string RepositoryRoot,
        string SettingsPath,
        string WatchedSolutionPath,
        string ProgramFilePath,
        string RuntimeRoot,
        string ProgramSymbolStableKey);
}
