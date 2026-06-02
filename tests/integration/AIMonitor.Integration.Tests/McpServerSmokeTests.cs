using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.MSBuild;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AIMonitor.Integration.Tests;

public sealed class McpServerSmokeTests
{
    static McpServerSmokeTests()
    {
        Environment.SetEnvironmentVariable("AIMONITOR_DISABLE_VALIDATION_DIALOG", "1");
    }

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
    public async Task Mcp_index_reference_tools_reject_source_map_selector_keys()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);

        string selectorKey = "Program.cs::Example::Program::method::GetValue()";
        CallToolResult references = await client.CallToolAsync(
            "find_indexed_references",
            new Dictionary<string, object?>
            {
                ["stableSymbolKey"] = selectorKey
            });

        Assert.False(references.IsError == true);
        Assert.True(ExtractJsonBool(ExtractToolText(references), "isError"));
        Assert.Contains("source-map selector key", ExtractToolText(references), StringComparison.OrdinalIgnoreCase);

        CallToolResult callers = await client.CallToolAsync(
            "find_indexed_callers",
            new Dictionary<string, object?>
            {
                ["stableSymbolKey"] = selectorKey
            });

        Assert.False(callers.IsError == true);
        Assert.True(ExtractJsonBool(ExtractToolText(callers), "isError"));
        Assert.Contains("indexed symbol key", ExtractToolText(callers), StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "MCP stdio bridge connects to the WinForms-owned MCP proxy hub; cover it with ToolSmokeTests live workflows.")]
    public async Task Mcp_bridge_forwards_stdio_to_server_and_records_request_response_telemetry()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateBridgeClientAsync(fixture);

        CallToolResult status = await client.CallToolAsync("get_monitor_status");
        Assert.False(status.IsError == true);
        Assert.Contains("projectCount", ExtractToolText(status), StringComparison.Ordinal);

        string logPath = Path.Combine(fixture.RuntimeRoot, "logs", "aimonitor.ndjson");
        Assert.True(File.Exists(logPath));
        string logText = await File.ReadAllTextAsync(logPath);
        Assert.Contains("AIMonitor.McpProxyHub", logText, StringComparison.Ordinal);
        Assert.Contains("adapter.mcp.request.started", logText, StringComparison.Ordinal);
        Assert.Contains("adapter.mcp.request.completed", logText, StringComparison.Ordinal);
        Assert.Contains("get_monitor_status", logText, StringComparison.Ordinal);
        Assert.Contains("AIMonitor.McpServer", logText, StringComparison.Ordinal);
        Assert.Contains("adapter.mcp.tool.called", logText, StringComparison.Ordinal);
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
        Assert.False(string.IsNullOrWhiteSpace(ExtractJsonString(stageJson, "stagedRecordId")));
        Assert.False(string.IsNullOrWhiteSpace(ExtractJsonString(stageJson, "stagedHash")));
        Assert.DoesNotContain("\"stagedRecord\":{", stageJson, StringComparison.Ordinal);
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

        CallToolResult sourceMap = await client.CallToolAsync(
            "get_source_map",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath
            });
        Assert.False(sourceMap.IsError == true);
        Assert.Contains("Program", ExtractToolText(sourceMap), StringComparison.Ordinal);

        CallToolResult records = await client.CallToolAsync(
            "list_session_staged_records",
            new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId
            });
        Assert.False(records.IsError == true);
    }

    [Fact]
    public async Task Mcp_roslyn_source_map_reports_actionable_razor_markup_boundary()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);
        string razorPath = Path.Combine(Path.GetDirectoryName(fixture.ProgramFilePath)!, "Pages", "Index.razor");
        Directory.CreateDirectory(Path.GetDirectoryName(razorPath)!);
        await File.WriteAllTextAsync(razorPath, "@page \"/\"\n<h1>Hello</h1>\n");

        CallToolResult sourceMap = await client.CallToolAsync(
            "get_source_map",
            new Dictionary<string, object?>
            {
                ["path"] = razorPath,
                ["scope"] = "file"
            });

        Assert.False(sourceMap.IsError == true);
        Assert.True(ExtractJsonBool(ExtractToolText(sourceMap), "isError"));
        string sourceMapError = ExtractToolText(sourceMap);
        Assert.Contains("cannot read or edit Razor markup directly", sourceMapError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("replace_text_in_file", sourceMapError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mcp_replace_text_preserves_line_endings_and_rejects_stale_hashes()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);
        string crlfContent = "namespace Example\r\n{\r\n    internal static class Program\r\n    {\r\n        public static string Value => \"old\";\r\n    }\r\n}\r\n";
        await File.WriteAllTextAsync(fixture.ProgramFilePath, crlfContent);

        CallToolResult refresh = await client.CallToolAsync(
            "refresh_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = fixture.ProgramFilePath
            });
        Assert.False(refresh.IsError == true);
        string workingFilePath = ExtractJsonString(ExtractToolText(refresh), "workingFilePath");

        CallToolResult staleReplace = await client.CallToolAsync(
            "replace_text_in_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["oldText"] = "old",
                ["newText"] = "stale",
                ["expectedMatches"] = 1,
                ["expectedFileHash"] = new string('0', 64)
            });
        Assert.True(staleReplace.IsError == true);

        CallToolResult replace = await client.CallToolAsync(
            "replace_text_in_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["oldText"] = "public static string Value => \"old\";",
                ["newText"] = "public static string Value => \"new\";\n        public static int Count => 1;",
                ["expectedMatches"] = 1
            });
        Assert.False(replace.IsError == true);
        string replaceJson = ExtractToolText(replace);
        Assert.Equal("CRLF", ExtractJsonString(replaceJson, "lineEnding"));
        Assert.Equal(1, ExtractJsonInt(replaceJson, "actualMatches"));

        string workingText = await File.ReadAllTextAsync(workingFilePath);
        Assert.Contains("public static int Count => 1;", workingText, StringComparison.Ordinal);
        Assert.Equal(0, CountBareLf(workingText));
        Assert.DoesNotContain("Count => 1", await File.ReadAllTextAsync(fixture.ProgramFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_replace_text_occurrence_index_does_not_require_unique_old_text()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);
        await File.WriteAllTextAsync(
            fixture.ProgramFilePath,
            "namespace Example { internal static class Program { public static string First => \"same\"; public static string Second => \"same\"; } }");

        CallToolResult refresh = await client.CallToolAsync(
            "refresh_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = fixture.ProgramFilePath
            });
        Assert.False(refresh.IsError == true);
        string workingFilePath = ExtractJsonString(ExtractToolText(refresh), "workingFilePath");

        CallToolResult replace = await client.CallToolAsync(
            "replace_text_in_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["oldText"] = "\"same\"",
                ["newText"] = "\"second\"",
                ["occurrenceIndex"] = 1
            });

        Assert.False(replace.IsError == true);
        string replaceJson = ExtractToolText(replace);
        Assert.Equal(2, ExtractJsonInt(replaceJson, "actualMatches"));

        string workingText = await File.ReadAllTextAsync(workingFilePath);
        Assert.Contains("First => \"same\"", workingText, StringComparison.Ordinal);
        Assert.Contains("Second => \"second\"", workingText, StringComparison.Ordinal);
        Assert.DoesNotContain("second", await File.ReadAllTextAsync(fixture.ProgramFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_submit_file_preserves_existing_line_endings()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);
        string crlfContent = "namespace Example\r\n{\r\n    internal static class Program\r\n    {\r\n    }\r\n}\r\n";
        await File.WriteAllTextAsync(fixture.ProgramFilePath, crlfContent);

        CallToolResult refresh = await client.CallToolAsync(
            "refresh_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = fixture.ProgramFilePath
            });
        Assert.False(refresh.IsError == true);
        string workingFilePath = ExtractJsonString(ExtractToolText(refresh), "workingFilePath");

        CallToolResult submit = await client.CallToolAsync(
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["content"] = "namespace Example\n{\n    internal static class Program\n    {\n        public static string Value => \"submitted\";\n    }\n}\n"
            });
        Assert.False(submit.IsError == true);

        string workingText = await File.ReadAllTextAsync(workingFilePath);
        Assert.Contains("submitted", workingText, StringComparison.Ordinal);
        Assert.Equal(0, CountBareLf(workingText));
        Assert.DoesNotContain("submitted", await File.ReadAllTextAsync(fixture.ProgramFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_find_text_span_and_replace_span_edit_working_copy_only()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);

        CallToolResult submit = await client.CallToolAsync(
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["content"] = "namespace Example\n{\n    internal static class Program\n    {\n        public static string Value => \"span-old\";\n    }\n}\n"
            });
        Assert.False(submit.IsError == true);
        string workingFilePath = ExtractJsonString(ExtractToolText(submit), "workingFilePath");

        CallToolResult span = await client.CallToolAsync(
            "find_text_span",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["findText"] = "span-old"
            });
        Assert.False(span.IsError == true);
        string spanJson = ExtractToolText(span);
        string oldTextHash = ExtractJsonString(spanJson, "textHash");

        CallToolResult replace = await client.CallToolAsync(
            "replace_span_in_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["startLine"] = ExtractJsonInt(spanJson, "startLine"),
                ["startColumn"] = ExtractJsonInt(spanJson, "startColumn"),
                ["endLine"] = ExtractJsonInt(spanJson, "endLine"),
                ["endColumn"] = ExtractJsonInt(spanJson, "endColumn"),
                ["newText"] = "span-new",
                ["expectedOldTextHash"] = oldTextHash,
                ["expectedOldText"] = "span-old"
            });
        Assert.False(replace.IsError == true);
        Assert.Contains("span-new", await File.ReadAllTextAsync(workingFilePath), StringComparison.Ordinal);
        Assert.DoesNotContain("span-new", await File.ReadAllTextAsync(fixture.ProgramFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_new_file_stage_and_reject_leaves_watched_source_absent()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);
        string newFilePath = Path.Combine(Path.GetDirectoryName(fixture.ProgramFilePath)!, "Generated", "McpRejectedThing.cs");

        CallToolResult create = await client.CallToolAsync(
            "new_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = newFilePath
            });
        Assert.False(create.IsError == true);
        string createJson = ExtractToolText(create);
        Assert.True(ExtractJsonBool(createJson, "isNewFile"));
        string workingFilePath = ExtractJsonString(createJson, "workingFilePath");
        Assert.True(File.Exists(workingFilePath));
        Assert.False(File.Exists(newFilePath));

        await File.WriteAllTextAsync(workingFilePath, "namespace Example.Generated { internal sealed class McpRejectedThing { } }");

        CallToolResult stage = await client.CallToolAsync(
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = newFilePath
            });
        Assert.False(stage.IsError == true);
        string stageJson = ExtractToolText(stage);
        string stagedRecordJson = await GetStagedRecordJsonAsync(client, ExtractJsonString(stageJson, "stagedRecordId"));
        Assert.True(ExtractJsonBool(stagedRecordJson, "isNewFile"));
        string reviewBaselineFilePath = ExtractJsonString(stagedRecordJson, "reviewBaselineFilePath");
        Assert.True(File.Exists(reviewBaselineFilePath));
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(reviewBaselineFilePath));

        CallToolResult decision = await client.CallToolAsync(
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = ExtractJsonString(stageJson, "stagedRecordId"),
                ["decision"] = "rejected"
            });
        Assert.False(decision.IsError == true);
        Assert.Equal("rejected", ExtractJsonString(ExtractToolText(decision), "classification"));
        Assert.False(File.Exists(newFilePath));
    }

    [Fact]
    public async Task Mcp_launch_staged_diff_blocks_when_premerge_validation_fails()
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
        string workingFilePath = ExtractJsonString(ExtractToolText(refresh), "workingFilePath");
        await File.WriteAllTextAsync(workingFilePath, "namespace Example { internal static class Program { public static string Value => ");

        CallToolResult stage = await client.CallToolAsync(
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath
            });
        Assert.False(stage.IsError == true);
        string stagedRecordId = ExtractJsonString(ExtractToolText(stage), "stagedRecordId");

        CallToolResult launch = await client.CallToolAsync(
            "launch_staged_diff",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId,
                ["diffToolPath"] = GetFakeDiffToolPath()
            });

        Assert.False(launch.IsError == true, ExtractToolText(launch));
        string launchJson = ExtractToolText(launch);
        Assert.Contains("\"launched\":false", launchJson, StringComparison.Ordinal);
        Assert.Contains("\"preMergeValidation\"", launchJson, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"failed\"", launchJson, StringComparison.Ordinal);
        Assert.Contains("Pre-merge validation failed", launchJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_new_file_member_pair_edit_stress_removes_removed_members_before_review()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);
        string newFilePath = Path.Combine(Path.GetDirectoryName(fixture.ProgramFilePath)!, "Generated", "McpMemberPairs.cs");

        CallToolResult create = await client.CallToolAsync(
            "new_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = newFilePath
            });
        Assert.False(create.IsError == true);
        string workingFilePath = ExtractJsonString(ExtractToolText(create), "workingFilePath");
        Assert.False(File.Exists(newFilePath));

        string scaffold = "namespace Example.Generated\n{\n    internal sealed class McpMemberPairs\n    {\n    }\n}\n";
        CallToolResult submit = await client.CallToolAsync(
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = newFilePath,
                ["content"] = scaffold
            });
        Assert.False(submit.IsError == true);

        for (int index = 1; index <= 3; index++)
        {
            CallToolResult keepProperty = await client.CallToolAsync(
                "add_property",
                new Dictionary<string, object?>
                {
                    ["path"] = newFilePath,
                    ["containingType"] = "McpMemberPairs",
                    ["declaration"] = $"public string KeepProperty{index} => \"keep-{index}\";"
                });
            Assert.False(keepProperty.IsError == true);

            CallToolResult removedProperty = await client.CallToolAsync(
                "add_property",
                new Dictionary<string, object?>
                {
                    ["path"] = newFilePath,
                    ["containingType"] = "McpMemberPairs",
                    ["declaration"] = $"public string RemovedProperty{index}_removed => \"remove-{index}\";"
                });
            Assert.False(removedProperty.IsError == true);

            CallToolResult keepMethod = await client.CallToolAsync(
                "add_method",
                new Dictionary<string, object?>
                {
                    ["path"] = newFilePath,
                    ["containingType"] = "McpMemberPairs",
                    ["declaration"] = $"public string KeepMethod{index}() => \"keep-method-{index}\";"
                });
            Assert.False(keepMethod.IsError == true);

            CallToolResult removedMethod = await client.CallToolAsync(
                "add_method",
                new Dictionary<string, object?>
                {
                    ["path"] = newFilePath,
                    ["containingType"] = "McpMemberPairs",
                    ["declaration"] = $"public string RemovedMethod{index}_removed() => \"remove-method-{index}\";"
                });
            Assert.False(removedMethod.IsError == true);
        }

        for (int index = 1; index <= 3; index++)
        {
            CallToolResult removeProperty = await client.CallToolAsync(
                "remove_symbol",
                new Dictionary<string, object?>
                {
                    ["path"] = newFilePath,
                    ["symbolSelectorJson"] = $$"""{"containingType":"McpMemberPairs","memberKind":"property","name":"RemovedProperty{{index}}_removed"}"""
                });
            Assert.False(removeProperty.IsError == true);

            CallToolResult removeMethod = await client.CallToolAsync(
                "remove_symbol",
                new Dictionary<string, object?>
                {
                    ["path"] = newFilePath,
                    ["symbolSelectorJson"] = $$"""{"containingType":"McpMemberPairs","memberKind":"method","name":"RemovedMethod{{index}}_removed"}"""
                });
            Assert.False(removeMethod.IsError == true);
        }

        string workingText = await File.ReadAllTextAsync(workingFilePath);
        Assert.Contains("KeepProperty1", workingText, StringComparison.Ordinal);
        Assert.Contains("KeepMethod3", workingText, StringComparison.Ordinal);
        Assert.DoesNotContain("_removed", workingText, StringComparison.Ordinal);
        Assert.False(File.Exists(newFilePath));

        CallToolResult stage = await client.CallToolAsync(
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = newFilePath,
                ["ledgerSummary"] = "mcp member pair stress"
            });
        Assert.False(stage.IsError == true);
        string stageJson = ExtractToolText(stage);
        string stagedRecordJson = await GetStagedRecordJsonAsync(client, ExtractJsonString(stageJson, "stagedRecordId"));
        string stagedFilePath = ExtractJsonString(stagedRecordJson, "stagedFilePath");
        string stagedText = await File.ReadAllTextAsync(stagedFilePath);
        Assert.Contains("KeepProperty2", stagedText, StringComparison.Ordinal);
        Assert.DoesNotContain("_removed", stagedText, StringComparison.Ordinal);

        CallToolResult decision = await client.CallToolAsync(
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = ExtractJsonString(stageJson, "stagedRecordId"),
                ["decision"] = "rejected"
            });
        Assert.False(decision.IsError == true);
        Assert.Equal("rejected", ExtractJsonString(ExtractToolText(decision), "classification"));
        Assert.False(File.Exists(newFilePath));
    }

    [Fact]
    public async Task Mcp_session_multi_file_accept_flow_tracks_both_staged_records()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);
        string helperFilePath = Path.Combine(Path.GetDirectoryName(fixture.ProgramFilePath)!, "Helper.cs");
        await File.WriteAllTextAsync(
            helperFilePath,
            "namespace Example { internal static class Helper { public static string Value() => \"old\"; } }");

        CallToolResult session = await client.CallToolAsync(
            "start_monitor_session",
            new Dictionary<string, object?>
            {
                ["purpose"] = "multi-file accepted flow"
            });
        Assert.False(session.IsError == true);
        string sessionId = ExtractJsonString(ExtractToolText(session), "sessionId");

        CallToolResult helperSubmit = await client.CallToolAsync(
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = helperFilePath,
                ["content"] = "namespace Example { internal static class Helper { public static string Value() => \"accepted-helper\"; } }",
                ["sessionId"] = sessionId
            });
        Assert.False(helperSubmit.IsError == true);

        CallToolResult programSubmit = await client.CallToolAsync(
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["content"] = "namespace Example { internal static class Program { public static string Value => Helper.Value(); } }",
                ["sessionId"] = sessionId
            });
        Assert.False(programSubmit.IsError == true);

        Assert.Contains("old", await File.ReadAllTextAsync(helperFilePath), StringComparison.Ordinal);
        Assert.DoesNotContain("Helper.Value", await File.ReadAllTextAsync(fixture.ProgramFilePath), StringComparison.Ordinal);

        CallToolResult helperStage = await client.CallToolAsync(
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = helperFilePath,
                ["ledgerSummary"] = "mcp session multi-file helper",
                ["sessionId"] = sessionId
            });
        Assert.False(helperStage.IsError == true);
        string helperStageJson = ExtractToolText(helperStage);
        string helperStagedRecordJson = await GetStagedRecordJsonAsync(client, ExtractJsonString(helperStageJson, "stagedRecordId"));
        string helperStagedFilePath = ExtractJsonString(helperStagedRecordJson, "stagedFilePath");
        string helperStagedHash = ExtractJsonString(helperStageJson, "stagedHash");
        string helperStagedRecordId = ExtractJsonString(helperStageJson, "stagedRecordId");

        CallToolResult programStage = await client.CallToolAsync(
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["ledgerSummary"] = "mcp session multi-file program",
                ["sessionId"] = sessionId
            });
        Assert.False(programStage.IsError == true);
        string programStageJson = ExtractToolText(programStage);
        string programStagedRecordJson = await GetStagedRecordJsonAsync(client, ExtractJsonString(programStageJson, "stagedRecordId"));
        string programStagedFilePath = ExtractJsonString(programStagedRecordJson, "stagedFilePath");
        string programStagedHash = ExtractJsonString(programStageJson, "stagedHash");
        string programStagedRecordId = ExtractJsonString(programStageJson, "stagedRecordId");

        CallToolResult sessionRecords = await client.CallToolAsync(
            "list_session_staged_records",
            new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId
            });
        Assert.False(sessionRecords.IsError == true);
        string sessionRecordsJson = ExtractToolText(sessionRecords);
        Assert.Contains(helperStagedRecordId, sessionRecordsJson, StringComparison.Ordinal);
        Assert.Contains(programStagedRecordId, sessionRecordsJson, StringComparison.Ordinal);

        CallToolResult helperLaunch = await client.CallToolAsync(
            "launch_staged_diff",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = helperStagedRecordId,
                ["diffToolPath"] = GetFakeDiffToolPath()
            });
        Assert.False(helperLaunch.IsError == true);
        Assert.Contains("launched", ExtractToolText(helperLaunch), StringComparison.Ordinal);

        File.Copy(helperStagedFilePath, helperFilePath, overwrite: true);
        CallToolResult helperDecision = await client.CallToolAsync(
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = helperStagedRecordId,
                ["decision"] = "accepted",
                ["expectedStagedHash"] = helperStagedHash
            });
        Assert.False(helperDecision.IsError == true, ExtractToolText(helperDecision));
        string helperDecisionJson = ExtractToolText(helperDecision);
        Assert.Equal("accepted", ExtractJsonString(helperDecisionJson, "classification"));
        Assert.Contains("\"indexRefresh\"", helperDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"rebuilt\"", helperDecisionJson, StringComparison.Ordinal);

        CallToolResult programLaunch = await client.CallToolAsync(
            "launch_staged_diff",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = programStagedRecordId,
                ["diffToolPath"] = GetFakeDiffToolPath()
            });
        Assert.False(programLaunch.IsError == true);
        Assert.Contains("launched", ExtractToolText(programLaunch), StringComparison.Ordinal);

        File.Copy(programStagedFilePath, fixture.ProgramFilePath, overwrite: true);
        CallToolResult programDecision = await client.CallToolAsync(
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = programStagedRecordId,
                ["decision"] = "accepted",
                ["expectedStagedHash"] = programStagedHash
            });
        Assert.False(programDecision.IsError == true, ExtractToolText(programDecision));
        string programDecisionJson = ExtractToolText(programDecision);
        Assert.Equal("accepted", ExtractJsonString(programDecisionJson, "classification"));
        Assert.Contains("\"indexRefresh\"", programDecisionJson, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"rebuilt\"", programDecisionJson, StringComparison.Ordinal);

        Assert.Contains("accepted-helper", await File.ReadAllTextAsync(helperFilePath), StringComparison.Ordinal);
        Assert.Contains("Helper.Value()", await File.ReadAllTextAsync(fixture.ProgramFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_claude_skill_sequence_uses_source_map_symbol_read_and_roslyn_edits()
    {
        McpFixture fixture = CreateFixture();
        await File.WriteAllTextAsync(
            fixture.ProgramFilePath,
            """
            namespace Example
            {
                internal static class Program
                {
                    private static readonly string RemovedField_removed = "remove";

                    public static string GetValue()
                    {
                        return "old";
                    }
                }
            }
            """);
        await using McpClient client = await CreateClientAsync(fixture);

        CallToolResult monitorStatus = await client.CallToolAsync("get_monitor_status");
        Assert.False(monitorStatus.IsError == true);
        CallToolResult workflowStatus = await client.CallToolAsync("get_workflow_status");
        Assert.False(workflowStatus.IsError == true);

        CallToolResult sourceMap = await client.CallToolAsync(
            "get_source_map",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["scope"] = "file",
                ["mode"] = "selector"
            });
        Assert.False(sourceMap.IsError == true);
        string sourceMapJson = ExtractToolText(sourceMap);
        Assert.Contains("GetValue", sourceMapJson, StringComparison.Ordinal);
        Assert.Contains("RemovedField_removed", sourceMapJson, StringComparison.Ordinal);

        const string getValueSelector = """{"containingType":"Program","memberKind":"method","name":"GetValue"}""";
        CallToolResult symbol = await client.CallToolAsync(
            "get_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["symbolSelectorJson"] = getValueSelector
            });
        Assert.False(symbol.IsError == true);
        Assert.Contains("old", ExtractToolText(symbol), StringComparison.Ordinal);

        CallToolResult replacement = await client.CallToolAsync(
            "submit_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["symbolSelectorJson"] = getValueSelector,
                ["code"] = "public static string GetValue() => \"new\";"
            });
        Assert.False(replacement.IsError == true);

        CallToolResult addProperty = await client.CallToolAsync(
            "add_property",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["containingType"] = "Program",
                ["declaration"] = "public static string AddedProperty => GetValue();"
            });
        Assert.False(addProperty.IsError == true);

        CallToolResult addMethod = await client.CallToolAsync(
            "add_method",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["containingType"] = "Program",
                ["declaration"] = "public static string AddedMethod() => AddedProperty;"
            });
        Assert.False(addMethod.IsError == true);

        CallToolResult removeField = await client.CallToolAsync(
            "remove_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["symbolSelectorJson"] = """{"containingType":"Program","memberKind":"field","name":"RemovedField_removed"}"""
            });
        Assert.False(removeField.IsError == true);

        string workingFilePath = ExtractJsonString(ExtractToolText(removeField), "workingFilePath");
        string workingText = await File.ReadAllTextAsync(workingFilePath);
        Assert.Contains("AddedMethod", workingText, StringComparison.Ordinal);
        Assert.Contains("=> \"new\";", workingText, StringComparison.Ordinal);
        Assert.DoesNotContain("RemovedField_removed", workingText, StringComparison.Ordinal);
        Assert.Contains("return \"old\";", await File.ReadAllTextAsync(fixture.ProgramFilePath), StringComparison.Ordinal);

        CallToolResult stage = await client.CallToolAsync(
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["ledgerSummary"] = "claude skill sequence smoke"
            });
        Assert.False(stage.IsError == true);
        string stageJson = ExtractToolText(stage);
        Assert.Contains("stagedRecordId", stageJson, StringComparison.Ordinal);

        CallToolResult decision = await client.CallToolAsync(
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = ExtractJsonString(stageJson, "stagedRecordId"),
                ["decision"] = "rejected"
            });
        Assert.False(decision.IsError == true);
        Assert.Equal("rejected", ExtractJsonString(ExtractToolText(decision), "classification"));
        Assert.Contains("return \"old\";", await File.ReadAllTextAsync(fixture.ProgramFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_compare_and_stage_snapshot_without_mutating_watched_source()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);

        CallToolResult submit = await client.CallToolAsync(
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["content"] = "namespace Example { internal static class Program { public static string Value => \"compare\"; } }"
            });
        Assert.False(submit.IsError == true);

        CallToolResult compare = await client.CallToolAsync(
            "compare_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = fixture.ProgramFilePath,
                ["ledgerSummary"] = "mcp compare smoke"
            });
        Assert.False(compare.IsError == true);
        string compareJson = ExtractToolText(compare);
        Assert.Equal("compare-ready", ExtractJsonString(compareJson, "classification"));
        Assert.True(File.Exists(ExtractJsonString(compareJson, "proposedSnapshotPath")));
        Assert.True(File.Exists(ExtractJsonString(compareJson, "ledgerPath")));

        CallToolResult stage = await client.CallToolAsync(
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath
            });
        Assert.False(stage.IsError == true);
        Assert.Contains("stagedRecordId", ExtractToolText(stage), StringComparison.Ordinal);
        Assert.DoesNotContain("compare", await File.ReadAllTextAsync(fixture.ProgramFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_get_ledger_rejects_sibling_paths_that_share_the_ledger_prefix()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);

        CallToolResult submit = await client.CallToolAsync(
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["content"] = "namespace Example { internal static class Program { public static string Value => \"ledger\"; } }"
            });
        Assert.False(submit.IsError == true);

        CallToolResult compare = await client.CallToolAsync(
            "compare_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = fixture.ProgramFilePath,
                ["ledgerSummary"] = "mcp ledger path smoke"
            });
        Assert.False(compare.IsError == true);
        string ledgerPath = ExtractJsonString(ExtractToolText(compare), "ledgerPath");
        string siblingDirectory = Path.GetDirectoryName(ledgerPath)! + "Secrets";
        Directory.CreateDirectory(siblingDirectory);
        string siblingPath = Path.Combine(siblingDirectory, "outside.md");
        await File.WriteAllTextAsync(siblingPath, "outside");

        CallToolResult read = await client.CallToolAsync(
            "get_ledger",
            new Dictionary<string, object?>
            {
                ["ledgerPath"] = siblingPath
            });

        Assert.True(read.IsError == true);
    }

    [Fact]
    public async Task Mcp_session_hash_check_detects_watched_source_changes()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);

        CallToolResult session = await client.CallToolAsync(
            "start_monitor_session",
            new Dictionary<string, object?>
            {
                ["purpose"] = "hash check smoke"
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

        CallToolResult unchanged = await client.CallToolAsync(
            "check_file_hash",
            new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId,
                ["sourceFilePath"] = fixture.ProgramFilePath
            });
        Assert.False(unchanged.IsError == true);
        Assert.True(ExtractJsonBool(ExtractToolText(unchanged), "knownInSession"));
        Assert.False(ExtractJsonBool(ExtractToolText(unchanged), "changedSinceFetch"));

        await File.WriteAllTextAsync(fixture.ProgramFilePath, "namespace Example { internal static class Program { public static string Value => \"external\"; } }");

        CallToolResult changed = await client.CallToolAsync(
            "check_file_hash",
            new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId,
                ["sourceFilePath"] = fixture.ProgramFilePath
            });
        Assert.False(changed.IsError == true);
        Assert.True(ExtractJsonBool(ExtractToolText(changed), "changedSinceFetch"));
    }

    [Fact]
    public async Task Mcp_record_decision_accept_requires_expected_staged_hash()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);

        CallToolResult submit = await client.CallToolAsync(
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["content"] = "namespace Example { internal static class Program { public static string Value => \"accepted\"; } }"
            });
        Assert.False(submit.IsError == true);

        CallToolResult stage = await client.CallToolAsync(
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath
            });
        Assert.False(stage.IsError == true);
        string stageJson = ExtractToolText(stage);
        string stagedRecordJson = await GetStagedRecordJsonAsync(client, ExtractJsonString(stageJson, "stagedRecordId"));
        File.Copy(ExtractJsonString(stagedRecordJson, "stagedFilePath"), fixture.ProgramFilePath, overwrite: true);

        CallToolResult decision = await client.CallToolAsync(
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = ExtractJsonString(stageJson, "stagedRecordId"),
                ["decision"] = "accepted"
            });
        Assert.True(decision.IsError == true);
    }

    [Fact]
    public async Task Mcp_write_tools_reject_refresh_required_sessions_after_accept()
    {
        McpFixture fixture = CreateFixture();
        await using McpClient client = await CreateClientAsync(fixture);

        CallToolResult submit = await client.CallToolAsync(
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["content"] = "namespace Example { internal static class Program { public static string Value => \"accepted\"; } }"
            });
        Assert.False(submit.IsError == true);

        CallToolResult stage = await client.CallToolAsync(
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath
            });
        Assert.False(stage.IsError == true);
        string stageJson = ExtractToolText(stage);
        string stagedRecordId = ExtractJsonString(stageJson, "stagedRecordId");
        string stagedHash = ExtractJsonString(stageJson, "stagedHash");
        string stagedRecordJson = await GetStagedRecordJsonAsync(client, stagedRecordId);
        string stagedFilePath = ExtractJsonString(stagedRecordJson, "stagedFilePath");

        CallToolResult launch = await client.CallToolAsync(
            "launch_staged_diff",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId,
                ["diffToolPath"] = GetFakeDiffToolPath()
            });
        Assert.False(launch.IsError == true, ExtractToolText(launch));
        Assert.Contains("\"launched\":true", ExtractToolText(launch), StringComparison.Ordinal);

        File.Copy(stagedFilePath, fixture.ProgramFilePath, overwrite: true);
        CallToolResult decision = await client.CallToolAsync(
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId,
                ["decision"] = "accepted",
                ["expectedStagedHash"] = stagedHash
            });
        Assert.False(decision.IsError == true, ExtractToolText(decision));
        Assert.Equal("accepted", ExtractJsonString(ExtractToolText(decision), "classification"));

        CallToolResult staleSubmit = await client.CallToolAsync(
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["content"] = "namespace Example { internal static class Program { public static string Value => \"stale\"; } }"
            });
        Assert.True(staleSubmit.IsError == true);

        CallToolResult staleSpan = await client.CallToolAsync(
            "replace_span_in_file",
            new Dictionary<string, object?>
            {
                ["path"] = fixture.ProgramFilePath,
                ["startLine"] = 1,
                ["startColumn"] = 1,
                ["endLine"] = 1,
                ["endColumn"] = 1,
                ["newText"] = "// stale"
            });
        Assert.True(staleSpan.IsError == true);
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

    private static async Task<McpClient> CreateBridgeClientAsync(McpFixture fixture)
    {
        string bridgeDll = Path.Combine(
            fixture.RepositoryRoot,
            "src",
            "AIMonitor.McpStdioBridge",
            "bin",
            GetBuildConfiguration(),
            "net10.0",
            "AIMonitor.McpStdioBridge.dll");

        StdioClientTransportOptions options = new()
        {
            Name = "ai-monitor-bridge",
            Command = "dotnet",
            Arguments = [bridgeDll, "--repo-root", fixture.RepositoryRoot, "--config", fixture.SettingsPath],
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

    private static async Task<string> GetStagedRecordJsonAsync(McpClient client, string stagedRecordId)
    {
        CallToolResult record = await client.CallToolAsync(
            "get_staged_record",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId
            });
        Assert.False(record.IsError == true, ExtractToolText(record));
        return ExtractToolText(record);
    }

    private static string ExtractJsonString(string json, string propertyName)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        return FindString(document.RootElement, propertyName)
            ?? throw new InvalidOperationException($"Could not find JSON string property '{propertyName}'.");
    }

    private static int ExtractJsonInt(string json, string propertyName)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        return FindNumber(document.RootElement, propertyName)
            ?? throw new InvalidOperationException($"Could not find JSON number property '{propertyName}'.");
    }

    private static bool ExtractJsonBool(string json, string propertyName)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        return FindBool(document.RootElement, propertyName)
            ?? throw new InvalidOperationException($"Could not find JSON bool property '{propertyName}'.");
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

    private static int? FindNumber(System.Text.Json.JsonElement element, string propertyName)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (System.Text.Json.JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName)
                    && property.Value.ValueKind == System.Text.Json.JsonValueKind.Number
                    && property.Value.TryGetInt32(out int value))
                {
                    return value;
                }

                int? nested = FindNumber(property.Value, propertyName);
                if (nested.HasValue)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (System.Text.Json.JsonElement item in element.EnumerateArray())
            {
                int? nested = FindNumber(item, propertyName);
                if (nested.HasValue)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool? FindBool(System.Text.Json.JsonElement element, string propertyName)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (System.Text.Json.JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals(propertyName)
                    && (property.Value.ValueKind == System.Text.Json.JsonValueKind.True
                        || property.Value.ValueKind == System.Text.Json.JsonValueKind.False))
                {
                    return property.Value.GetBoolean();
                }

                bool? nested = FindBool(property.Value, propertyName);
                if (nested.HasValue)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (System.Text.Json.JsonElement item in element.EnumerateArray())
            {
                bool? nested = FindBool(item, propertyName);
                if (nested.HasValue)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static int CountBareLf(string text)
    {
        string withoutCrLf = text.Replace("\r\n", string.Empty, StringComparison.Ordinal);
        return withoutCrLf.Count(character => character == '\n');
    }

    private static string GetFakeDiffToolPath()
    {
        if (OperatingSystem.IsWindows())
        {
            string windowsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe");
            if (File.Exists(windowsPath))
            {
                return windowsPath;
            }
        }

        foreach (string candidate in new[] { "/usr/bin/true", "/bin/true" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Unable to find a harmless executable for diff-launch tests.");
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
