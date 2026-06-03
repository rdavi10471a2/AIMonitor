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
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AIMonitor.ToolSmokeTests;

internal static class Program
{
    private static string? smokeSettingsPathOverride;

    private static async Task<int> Main(string[] args)
    {
        smokeSettingsPathOverride = args.Contains("--sample-workflow-harness", StringComparer.OrdinalIgnoreCase)
            ? Path.Combine("config", "appsettings.workflow-harness-sample.json")
            : GetOption(args, "--config");

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

        if (args.Contains("--mcp-live-edit-workflow", StringComparer.OrdinalIgnoreCase))
        {
            return await RunMcpLiveEditWorkflowAsync();
        }

        if (args.Contains("--mcp-live-all-edit-tools", StringComparer.OrdinalIgnoreCase))
        {
            return await RunMcpLiveAllEditToolsAsync();
        }

        if (args.Contains("--mcp-live-multi-file-session", StringComparer.OrdinalIgnoreCase))
        {
            return await RunMcpLiveMultiFileSessionAsync();
        }

        if (args.Contains("--mcp-live-human-winmerge", StringComparer.OrdinalIgnoreCase))
        {
            return await RunMcpLiveHumanWinMergeAsync();
        }

        if (args.Contains("--mcp-live-human-existing-winmerge", StringComparer.OrdinalIgnoreCase))
        {
            return await RunMcpLiveHumanExistingWinMergeAsync();
        }

        if (args.Contains("--mcp-live-human-member-pairs-winmerge", StringComparer.OrdinalIgnoreCase))
        {
            return await RunMcpLiveHumanMemberPairsWinMergeAsync();
        }

        if (args.Contains("--mcp-live-human-member-pairs-remove-winmerge", StringComparer.OrdinalIgnoreCase))
        {
            return await RunMcpLiveHumanMemberPairsRemoveWinMergeAsync(args);
        }

        if (args.Contains("--cleanup-workflow-harness-member-pairs", StringComparer.OrdinalIgnoreCase))
        {
            return CleanupWorkflowHarnessMemberPairsFile(args);
        }

        if (args.Contains("--mcp-live-premerge-failure", StringComparer.OrdinalIgnoreCase))
        {
            return await RunMcpLivePremergeFailureAsync();
        }

        if (args.Contains("--mcp-live-restore-premerge-fixture-winmerge", StringComparer.OrdinalIgnoreCase))
        {
            return await RunMcpLiveRestorePremergeFixtureWinMergeAsync();
        }

        if (args.Contains("--mcp-live-syntax-rejection", StringComparer.OrdinalIgnoreCase))
        {
            return await RunMcpLiveSyntaxRejectionAsync();
        }

        if (args.Contains("--mcp-live-record-decision", StringComparer.OrdinalIgnoreCase))
        {
            return await RunMcpLiveRecordDecisionAsync(args);
        }

        if (args.Contains("--visible-test-suite", StringComparer.OrdinalIgnoreCase))
        {
            return await RunVisibleTestSuiteAsync();
        }

        Console.WriteLine("AIMonitor tool smoke tests");
        Console.WriteLine();
        Console.WriteLine("Available modes:");
        Console.WriteLine("  --fixture-index-matrix    Build a generated fixture and compare AIMonitor index rows with an independent Roslyn pass.");
        Console.WriteLine("  --webviewer-file-by-file  Compare selected SchemaStudioWebViewer files against index and grep sanity counts.");
        Console.WriteLine("  --mcp-live-workflow       Call the local MCP server through the WinForms-owned stdio bridge so the UI shows adapter telemetry.");
        Console.WriteLine("  --mcp-live-edit-workflow  Run file-level MCP edit calls through the stdio bridge against the monitor-owned Working copy, then reject.");
        Console.WriteLine("  --mcp-live-all-edit-tools Run non-human file and Roslyn edit tools through the stdio bridge against a monitor-owned new-file candidate, then reject.");
        Console.WriteLine("  --mcp-live-multi-file-session Run a two-file staged session through the stdio bridge, then reject both files.");
        Console.WriteLine("  --mcp-live-human-winmerge Launch real WinMerge through the live MCP bridge and stop for human save/reject.");
        Console.WriteLine("  --mcp-live-human-existing-winmerge Launch real WinMerge for an existing-file edit through the live MCP bridge.");
        Console.WriteLine("  --mcp-live-human-member-pairs-winmerge Create an AppConfig class with paired member categories, stage, and launch WinMerge.");
        Console.WriteLine("  --mcp-live-human-member-pairs-remove-winmerge --relative-path <path> --class-name <name> Remove _removed members, stage, and launch WinMerge.");
        Console.WriteLine("  --cleanup-workflow-harness-member-pairs --relative-path <path> Delete a generated WorkflowHarnessSample member-pairs test file.");
        Console.WriteLine("  --mcp-live-premerge-failure Stage a syntax-valid compile-failing candidate through MCP and verify validation blocks WinMerge.");
        Console.WriteLine("  --mcp-live-restore-premerge-fixture-winmerge Restore the AppConfig pre-merge failure fixture through WinMerge.");
        Console.WriteLine("  --mcp-live-syntax-rejection Verify malformed C# is rejected at edit time before staging.");
        Console.WriteLine("  --mcp-live-record-decision --staged-record-id <id> --decision accepted|rejected [--expected-staged-hash <hash>] Record the human WinMerge decision.");
        Console.WriteLine("  --visible-test-suite      Run live MCP calls, then dotnet test, and emit test result telemetry to the WinForms monitor log.");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --sample-workflow-harness Use the committed samples/watched-solutions/WorkflowHarnessSample config.");
        Console.WriteLine("  --config <path>           Use an explicit AIMonitor config path for bridge-backed smoke modes.");
        return 2;
    }

    private static async Task<int> RunVisibleTestSuiteAsync()
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

        int liveExitCode = await RunMcpLiveWorkflowAsync();
        if (liveExitCode == 0)
        {
            liveExitCode = await RunMcpLiveAllEditToolsAsync();
        }

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
        int exitCode = liveExitCode == 0 ? result.ExitCode : liveExitCode;
        logger.Write(
            exitCode == 0 ? MonitorLogLevel.Information : MonitorLogLevel.Error,
            "AIMonitor.ToolSmokeTests",
            "test.run.completed",
            exitCode == 0 ? "Visible test suite completed." : "Visible test suite failed.",
            new Dictionary<string, string>
            {
                ["runId"] = runId,
                ["exitCode"] = exitCode.ToString(),
                ["liveMcpExitCode"] = liveExitCode.ToString(),
                ["dotnetTestExitCode"] = result.ExitCode.ToString(),
                ["timedOut"] = result.TimedOut.ToString().ToLowerInvariant(),
                ["caseTelemetryCount"] = caseCount.ToString(),
                ["resultsRoot"] = resultsRoot,
                ["stdoutPreview"] = Preview(result.StandardOutput),
                ["stderrPreview"] = Preview(result.StandardError)
            });

        Console.WriteLine($"Visible test telemetry run: {runId}");
        Console.WriteLine($"Live MCP workflow exit code: {liveExitCode}");
        Console.WriteLine($"TRX results: {resultsRoot}");
        Console.WriteLine($"Case telemetry emitted: {caseCount}");
        return exitCode;
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
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);

        await using McpClient client = await CreateBridgeClientAsync(repositoryRoot, settingsPath);
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

    private static async Task<int> RunMcpLiveEditWorkflowAsync()
    {
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);
        const string relativePath = "AppConfig/AppConfig.cs";
        const string oldText = "        public bool InitiaWorkflowTestPassed3 { get; set; } = true;";
        const string newText = oldText + "\r\n\r\n        public bool BridgeFileLevelEditSmoke { get; set; } = true;";

        await using McpClient client = await CreateBridgeClientAsync(repositoryRoot, settingsPath);
        await CallAndPrintAsync(
            client,
            "refresh_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = relativePath
            });
        await CallAndPrintAsync(
            client,
            "replace_text_in_file",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["oldText"] = oldText,
                ["newText"] = newText,
                ["expectedMatches"] = 1
            });
        CallToolResult stage = await CallAndPrintAsync(
            client,
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["ledgerSummary"] = "bridge live file-level edit smoke"
            });

        string stagedRecordId = ExtractJsonString(ExtractToolText(stage), "stagedRecordId");
        await CallAndPrintAsync(
            client,
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId,
                ["decision"] = "rejected"
            });
        return 0;
    }

    private static async Task<int> RunMcpLiveAllEditToolsAsync()
    {
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);
        const string existingRelativePath = "AppConfig/AppConfig.cs";
        const string smokeRelativePath = "AppConfig/AIMonitorBridgeAllEditToolsSmoke.cs";
        const string containingType = "AIMonitorBridgeAllEditToolsSmoke";
        string configurationNamespace = ResolveConfigurationNamespace(repositoryRoot, settingsPath);

        string initialContent = $$"""
            using System;

            namespace {{configurationNamespace}}
            {
                public partial class AIMonitorBridgeAllEditToolsSmoke
                {
                    private int existingField;

                    public string ExistingProperty { get; set; } = "initial";

                    public AIMonitorBridgeAllEditToolsSmoke()
                    {
                    }

                    public string ExistingMethod()
                    {
                        return ExistingProperty;
                    }

                    public string RemovedProperty_removed { get; set; } = "remove";

                    public string RemovedMethod_removed()
                    {
                        return RemovedProperty_removed;
                    }

                    public class ExistingNested
                    {
                    }
                }
            }
            """;

        await using McpClient client = await CreateBridgeClientAsync(repositoryRoot, settingsPath);
        CallToolResult session = await CallAndPrintAsync(
            client,
            "start_monitor_session",
            new Dictionary<string, object?>
            {
                ["title"] = "bridge all edit tools smoke"
            });
        string sessionId = ExtractJsonString(ExtractToolText(session), "sessionId");

        await CallAndPrintAsync(
            client,
            "get_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = existingRelativePath,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "check_file_hash",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = existingRelativePath,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "get_source_map",
            new Dictionary<string, object?>
            {
                ["path"] = existingRelativePath,
                ["scope"] = "file",
                ["mode"] = "selector",
                ["sessionId"] = sessionId
            });

        await CallAndPrintAsync(
            client,
            "new_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = smokeRelativePath,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["content"] = initialContent,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "get_edit_status",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = smokeRelativePath,
                ["sessionId"] = sessionId
            });
        CallToolResult span = await CallAndPrintAsync(
            client,
            "find_text_span",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["findText"] = "return ExistingProperty;",
                ["sessionId"] = sessionId
            });
        string spanJson = ExtractToolText(span);
        await CallAndPrintAsync(
            client,
            "replace_span_in_file",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["startLine"] = ExtractJsonInt(spanJson, "startLine"),
                ["startColumn"] = ExtractJsonInt(spanJson, "startColumn"),
                ["endLine"] = ExtractJsonInt(spanJson, "endLine"),
                ["endColumn"] = ExtractJsonInt(spanJson, "endColumn"),
                ["newText"] = "return $\"changed:{ExistingProperty}\";",
                ["expectedOldText"] = "return ExistingProperty;",
                ["sessionId"] = sessionId
            });

        string propertySelector = JsonSerializer.Serialize(new
        {
            containingType,
            memberKind = "property",
            name = "ExistingProperty"
        });
        await CallAndPrintAsync(
            client,
            "get_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["symbolSelectorJson"] = propertySelector,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "submit_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["symbolSelectorJson"] = propertySelector,
                ["code"] = "public string ExistingProperty { get; set; } = \"updated\";",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_using",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["namespace"] = "System.Linq",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "remove_using",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["namespace"] = "System.Linq",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "set_type_partial",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["containingType"] = containingType,
                ["isPartial"] = false,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "set_type_partial",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["containingType"] = containingType,
                ["isPartial"] = true,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["containingType"] = containingType,
                ["symbolType"] = "field",
                ["code"] = "private readonly string addedSymbolField = \"added\";",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_field",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["containingType"] = containingType,
                ["declaration"] = "private int AddedField;",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_property",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["containingType"] = containingType,
                ["declaration"] = "public bool AddedProperty { get; set; }",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_method",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["containingType"] = containingType,
                ["declaration"] = "public string AddedMethod() => ExistingProperty;",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_constructor",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["containingType"] = containingType,
                ["declaration"] = "public AIMonitorBridgeAllEditToolsSmoke(string existingProperty) { ExistingProperty = existingProperty; }",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_nested_type",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["containingType"] = containingType,
                ["declaration"] = "public class AddedNested { }",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "remove_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["symbolSelectorJson"] = JsonSerializer.Serialize(new
                {
                    containingType,
                    memberKind = "property",
                    name = "RemovedProperty_removed"
                }),
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "remove_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["symbolSelectorJson"] = JsonSerializer.Serialize(new
                {
                    containingType,
                    memberKind = "method",
                    name = "RemovedMethod_removed"
                }),
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "compare_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = smokeRelativePath,
                ["ledgerSummary"] = "bridge all edit tools smoke compare",
                ["sessionId"] = sessionId
            });
        CallToolResult stage = await CallAndPrintAsync(
            client,
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = smokeRelativePath,
                ["ledgerSummary"] = "bridge all edit tools smoke stage",
                ["sessionId"] = sessionId
            });
        string stagedRecordId = ExtractJsonString(ExtractToolText(stage), "stagedRecordId");
        await CallAndPrintAsync(
            client,
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId,
                ["decision"] = "rejected"
            });
        return 0;
    }

    private static async Task<int> RunMcpLiveMultiFileSessionAsync()
    {
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);
        string configurationNamespace = ResolveConfigurationNamespace(repositoryRoot, settingsPath);
        string firstRelativePath = "AppConfig/AIMonitorBridgeMultiFileOne.cs";
        string secondRelativePath = "AppConfig/AIMonitorBridgeMultiFileTwo.cs";

        await using McpClient client = await CreateBridgeClientAsync(repositoryRoot, settingsPath);
        CallToolResult session = await CallAndPrintAsync(
            client,
            "start_monitor_session",
            new Dictionary<string, object?>
            {
                ["title"] = "bridge multi-file session smoke"
            });
        string sessionId = ExtractJsonString(ExtractToolText(session), "sessionId");

        await CreateStageAndRejectNewFileAsync(
            client,
            sessionId,
            firstRelativePath,
            $$"""
            namespace {{configurationNamespace}}
            {
                public static class AIMonitorBridgeMultiFileOne
                {
                    public static string Value => "one";
                }
            }
            """);
        await CreateStageAndRejectNewFileAsync(
            client,
            sessionId,
            secondRelativePath,
            $$"""
            namespace {{configurationNamespace}}
            {
                public static class AIMonitorBridgeMultiFileTwo
                {
                    public static string Value => AIMonitorBridgeMultiFileOne.Value + ":two";
                }
            }
            """);

        await CallAndPrintAsync(
            client,
            "list_session_staged_records",
            new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId
            });
        return 0;
    }

    private static async Task<int> RunMcpLiveHumanWinMergeAsync()
    {
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);
        string configurationNamespace = ResolveConfigurationNamespace(repositoryRoot, settingsPath);
        string marker = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff");
        string relativePath = $"AppConfig/AIMonitorHumanWinMergeSmoke_{marker}.cs";
        string content = $$"""
            namespace {{configurationNamespace}}
            {
                public static class AIMonitorHumanWinMergeSmoke_{{marker}}
                {
                    public static string Marker => "{{marker}}";
                }
            }
            """;

        await using McpClient client = await CreateBridgeClientAsync(repositoryRoot, settingsPath);
        CallToolResult session = await CallAndPrintAsync(
            client,
            "start_monitor_session",
            new Dictionary<string, object?>
            {
                ["title"] = "human WinMerge smoke"
            });
        string sessionId = ExtractJsonString(ExtractToolText(session), "sessionId");

        await CallAndPrintAsync(
            client,
            "new_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = relativePath,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["content"] = content,
                ["sessionId"] = sessionId
            });
        CallToolResult stage = await CallAndPrintAsync(
            client,
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["ledgerSummary"] = "human WinMerge smoke",
                ["sessionId"] = sessionId
            });
        string stageJson = ExtractToolText(stage);
        string stagedRecordId = ExtractJsonString(stageJson, "stagedRecordId");
        string stagedHash = ExtractJsonString(stageJson, "stagedHash");

        CallToolResult launch = await CallAndPrintAsync(
            client,
            "launch_staged_diff",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId
            });
        string launchText = ExtractToolText(launch);

        Console.WriteLine();
        Console.WriteLine("Human WinMerge smoke launched.");
        Console.WriteLine($"Session ID: {sessionId}");
        Console.WriteLine($"Staged record ID: {stagedRecordId}");
        Console.WriteLine($"Expected staged hash: {stagedHash}");
        Console.WriteLine("After reviewing/saving in WinMerge, record the result with:");
        Console.WriteLine($"dotnet .\\tests\\smoke\\AIMonitor.ToolSmokeTests\\bin\\Debug\\net10.0\\AIMonitor.ToolSmokeTests.dll --mcp-live-record-decision --staged-record-id {stagedRecordId} --decision accepted --expected-staged-hash {stagedHash}");
        Console.WriteLine($"dotnet .\\tests\\smoke\\AIMonitor.ToolSmokeTests\\bin\\Debug\\net10.0\\AIMonitor.ToolSmokeTests.dll --mcp-live-record-decision --staged-record-id {stagedRecordId} --decision rejected");
        Console.WriteLine();
        Console.WriteLine(launchText);
        return launch.IsError == true ? 1 : 0;
    }

    private static async Task<int> RunMcpLiveHumanExistingWinMergeAsync()
    {
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);
        const string relativePath = "AppConfig/AppConfig.cs";
        string marker = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff");
        const string oldText = "        public bool InitiaWorkflowTestPassed3 { get; set; } = true;";
        string newText = oldText + $"\r\n\r\n        public bool AIMonitorExistingWinMergeSmoke_{marker} {{ get; set; }} = true;";

        await using McpClient client = await CreateBridgeClientAsync(repositoryRoot, settingsPath);
        CallToolResult session = await CallAndPrintAsync(
            client,
            "start_monitor_session",
            new Dictionary<string, object?>
            {
                ["title"] = "existing-file human WinMerge smoke"
            });
        string sessionId = ExtractJsonString(ExtractToolText(session), "sessionId");

        await CallAndPrintAsync(
            client,
            "refresh_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = relativePath,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "replace_text_in_file",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["oldText"] = oldText,
                ["newText"] = newText,
                ["expectedMatches"] = 1,
                ["sessionId"] = sessionId
            });
        CallToolResult stage = await CallAndPrintAsync(
            client,
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["ledgerSummary"] = "existing-file human WinMerge smoke",
                ["sessionId"] = sessionId
            });
        string stageJson = ExtractToolText(stage);
        string stagedRecordId = ExtractJsonString(stageJson, "stagedRecordId");
        string stagedHash = ExtractJsonString(stageJson, "stagedHash");

        CallToolResult launch = await CallAndPrintAsync(
            client,
            "launch_staged_diff",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId
            });
        string launchText = ExtractToolText(launch);

        Console.WriteLine();
        Console.WriteLine("Existing-file human WinMerge smoke launched.");
        Console.WriteLine($"Session ID: {sessionId}");
        Console.WriteLine($"Staged record ID: {stagedRecordId}");
        Console.WriteLine($"Expected staged hash: {stagedHash}");
        Console.WriteLine("After reviewing/saving in WinMerge, record the result with:");
        Console.WriteLine($"dotnet .\\tests\\smoke\\AIMonitor.ToolSmokeTests\\bin\\Debug\\net10.0\\AIMonitor.ToolSmokeTests.dll --mcp-live-record-decision --staged-record-id {stagedRecordId} --decision accepted --expected-staged-hash {stagedHash}");
        Console.WriteLine($"dotnet .\\tests\\smoke\\AIMonitor.ToolSmokeTests\\bin\\Debug\\net10.0\\AIMonitor.ToolSmokeTests.dll --mcp-live-record-decision --staged-record-id {stagedRecordId} --decision rejected");
        Console.WriteLine();
        Console.WriteLine(launchText);
        return launch.IsError == true ? 1 : 0;
    }

    private static async Task<int> RunMcpLiveHumanMemberPairsWinMergeAsync()
    {
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);
        string configurationNamespace = ResolveConfigurationNamespace(repositoryRoot, settingsPath);
        string marker = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff");
        string className = $"AIMonitorMemberPairsManual_{marker}";
        string relativePath = $"AppConfig/{className}.cs";
        string content = $$"""
            using System;

            namespace {{configurationNamespace}}
            {
                public partial class {{className}}
                {
                }
            }
            """;

        await using McpClient client = await CreateBridgeClientAsync(repositoryRoot, settingsPath);
        CallToolResult session = await CallAndPrintAsync(
            client,
            "start_monitor_session",
            new Dictionary<string, object?>
            {
                ["title"] = "human member-pairs create smoke"
            });
        string sessionId = ExtractJsonString(ExtractToolText(session), "sessionId");

        await CallAndPrintAsync(
            client,
            "new_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = relativePath,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["content"] = content,
                ["sessionId"] = sessionId,
                ["manifestJson"] = """{"intent":"manual-member-pairs-create"}"""
            });

        await AddMemberPairAsync(client, sessionId, relativePath, className);
        await StageLaunchAndPrintDecisionAsync(
            client,
            sessionId,
            relativePath,
            "human member-pairs create smoke",
            $"dotnet .\\tests\\smoke\\AIMonitor.ToolSmokeTests\\bin\\Debug\\net10.0\\AIMonitor.ToolSmokeTests.dll --mcp-live-human-member-pairs-remove-winmerge --relative-path {relativePath} --class-name {className}");
        return 0;
    }

    private static async Task<int> RunMcpLiveHumanMemberPairsRemoveWinMergeAsync(string[] args)
    {
        string relativePath = GetRequiredOption(args, "--relative-path");
        string className = GetRequiredOption(args, "--class-name");
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);

        await using McpClient client = await CreateBridgeClientAsync(repositoryRoot, settingsPath);
        CallToolResult session = await CallAndPrintAsync(
            client,
            "start_monitor_session",
            new Dictionary<string, object?>
            {
                ["title"] = "human member-pairs remove smoke"
            });
        string sessionId = ExtractJsonString(ExtractToolText(session), "sessionId");

        await CallAndPrintAsync(
            client,
            "refresh_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = relativePath,
                ["sessionId"] = sessionId
            });

        await RemoveMemberPairAsync(client, sessionId, relativePath, className);
        await StageLaunchAndPrintDecisionAsync(
            client,
            sessionId,
            relativePath,
            "human member-pairs remove smoke",
            null);
        return 0;
    }

    private static int CleanupWorkflowHarnessMemberPairsFile(string[] args)
    {
        if (!args.Contains("--sample-workflow-harness", StringComparer.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Cleanup is only allowed with --sample-workflow-harness.");
            return 1;
        }

        string relativePath = GetRequiredOption(args, "--relative-path").Replace('/', Path.DirectorySeparatorChar);
        string fileName = Path.GetFileName(relativePath);
        if (!relativePath.StartsWith($"AppConfig{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || !fileName.StartsWith("AIMonitorMemberPairsManual_", StringComparison.Ordinal)
            || !fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part == ".."))
        {
            Console.Error.WriteLine("Cleanup refused because the path is not a generated WorkflowHarnessSample member-pairs file.");
            return 1;
        }

        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);
        MonitorSettings settings = MonitorSettingsLoader.Load(repositoryRoot, settingsPath);
        string fullPath = Path.GetFullPath(Path.Combine(settings.WatchedProjectFolder, relativePath));
        string watchedRoot = Path.GetFullPath(settings.WatchedProjectFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!fullPath.StartsWith(watchedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Cleanup refused because the resolved path is outside the watched sample folder.");
            return 1;
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            Console.WriteLine($"Deleted generated harness file: {fullPath}");
        }
        else
        {
            Console.WriteLine($"Generated harness file already absent: {fullPath}");
        }

        string backupPath = fullPath + ".bak";
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
            Console.WriteLine($"Deleted generated harness backup: {backupPath}");
        }

        return 0;
    }

    private static async Task AddMemberPairAsync(McpClient client, string sessionId, string relativePath, string className)
    {
        await CallAndPrintAsync(
            client,
            "add_field",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["containingType"] = className,
                ["declaration"] = "private readonly string NormalField = \"normal\";",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_field",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["containingType"] = className,
                ["declaration"] = "private readonly string RemovedField_removed = \"remove\";",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_property",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["containingType"] = className,
                ["declaration"] = "public string NormalProperty { get; set; } = \"normal\";",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_property",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["containingType"] = className,
                ["declaration"] = "public string RemovedProperty_removed { get; set; } = \"remove\";",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_method",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["containingType"] = className,
                ["declaration"] = "public string NormalMethod() => NormalProperty;",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_method",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["containingType"] = className,
                ["declaration"] = "public string RemovedMethod_removed() => RemovedProperty_removed;",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_constructor",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["containingType"] = className,
                ["declaration"] = $"public {className}(string normalValue) {{ NormalProperty = normalValue; }}",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_constructor",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["containingType"] = className,
                ["declaration"] = $"public {className}(int removedConstructor_removed) {{ RemovedProperty_removed = removedConstructor_removed.ToString(); }}",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["containingType"] = className,
                ["symbolType"] = "event",
                ["code"] = "public event EventHandler? NormalEvent;",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["containingType"] = className,
                ["symbolType"] = "event",
                ["code"] = "public event EventHandler? RemovedEvent_removed;",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_nested_type",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["containingType"] = className,
                ["declaration"] = "public sealed class NormalNested { }",
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "add_nested_type",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["containingType"] = className,
                ["declaration"] = "public sealed class RemovedNested_removed { }",
                ["sessionId"] = sessionId
            });
    }

    private static async Task RemoveMemberPairAsync(McpClient client, string sessionId, string relativePath, string className)
    {
        await RemoveSymbolAsync(client, sessionId, relativePath, className, "field", "RemovedField_removed");
        await RemoveSymbolAsync(client, sessionId, relativePath, className, "property", "RemovedProperty_removed");
        await RemoveSymbolAsync(client, sessionId, relativePath, className, "method", "RemovedMethod_removed");
        await RemoveSymbolAsync(client, sessionId, relativePath, className, "event", "RemovedEvent_removed");
        await RemoveSymbolAsync(client, sessionId, relativePath, className, "class", "RemovedNested_removed");
        await CallAndPrintAsync(
            client,
            "remove_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["symbolSelectorJson"] = JsonSerializer.Serialize(new
                {
                    containingType = className,
                    memberKind = "constructor",
                    name = className,
                    parameterTypes = new[] { "int" }
                }),
                ["sessionId"] = sessionId
            });
    }

    private static async Task RemoveSymbolAsync(
        McpClient client,
        string sessionId,
        string relativePath,
        string containingType,
        string memberKind,
        string name)
    {
        await CallAndPrintAsync(
            client,
            "remove_symbol",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["symbolSelectorJson"] = JsonSerializer.Serialize(new
                {
                    containingType,
                    memberKind,
                    name
                }),
                ["sessionId"] = sessionId
            });
    }

    private static async Task StageLaunchAndPrintDecisionAsync(
        McpClient client,
        string sessionId,
        string relativePath,
        string ledgerSummary,
        string? nextCommand)
    {
        CallToolResult stage = await CallAndPrintAsync(
            client,
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["ledgerSummary"] = ledgerSummary,
                ["sessionId"] = sessionId
            });
        string stageJson = ExtractToolText(stage);
        string stagedRecordId = ExtractJsonString(stageJson, "stagedRecordId");
        string stagedHash = ExtractJsonString(stageJson, "stagedHash");

        CallToolResult launch = await CallAndPrintAsync(
            client,
            "launch_staged_diff",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId
            });

        Console.WriteLine();
        Console.WriteLine($"{ledgerSummary} launched.");
        Console.WriteLine($"Session ID: {sessionId}");
        Console.WriteLine($"Relative path: {relativePath}");
        Console.WriteLine($"Staged record ID: {stagedRecordId}");
        Console.WriteLine($"Expected staged hash: {stagedHash}");
        Console.WriteLine("After reviewing/saving in WinMerge, record the result with:");
        Console.WriteLine($"dotnet .\\tests\\smoke\\AIMonitor.ToolSmokeTests\\bin\\Debug\\net10.0\\AIMonitor.ToolSmokeTests.dll --mcp-live-record-decision --staged-record-id {stagedRecordId} --decision accepted --expected-staged-hash {stagedHash}");
        Console.WriteLine($"dotnet .\\tests\\smoke\\AIMonitor.ToolSmokeTests\\bin\\Debug\\net10.0\\AIMonitor.ToolSmokeTests.dll --mcp-live-record-decision --staged-record-id {stagedRecordId} --decision rejected");
        if (!string.IsNullOrWhiteSpace(nextCommand))
        {
            Console.WriteLine("After accepting, run the next step with:");
            Console.WriteLine(nextCommand);
        }

        Console.WriteLine();
        Console.WriteLine(ExtractToolText(launch));
    }

    private static async Task<int> RunMcpLivePremergeFailureAsync()
    {
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);
        const string relativePath = "AppConfig/AppConfig.cs";
        const string oldText = "        public bool InitiaWorkflowTestPassed3 { get; set; } = true;";
        const string newText = "        public bool InitiaWorkflowTestPassed3 { get; set; } = MissingPremergeSymbol.Value;";

        await using McpClient client = await CreateBridgeClientAsync(repositoryRoot, settingsPath);
        CallToolResult session = await CallAndPrintAsync(
            client,
            "start_monitor_session",
            new Dictionary<string, object?>
            {
                ["title"] = "premerge validation failure smoke"
            });
        string sessionId = ExtractJsonString(ExtractToolText(session), "sessionId");

        await CallAndPrintAsync(
            client,
            "refresh_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = relativePath,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "replace_text_in_file",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["oldText"] = oldText,
                ["newText"] = newText,
                ["expectedMatches"] = 1,
                ["sessionId"] = sessionId
            });
        CallToolResult stage = await CallAndPrintAsync(
            client,
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["ledgerSummary"] = "premerge validation failure smoke",
                ["sessionId"] = sessionId
            });
        string stageJson = ExtractToolText(stage);
        string stagedRecordId = ExtractJsonString(stageJson, "stagedRecordId");
        string stagedHash = ExtractJsonString(stageJson, "stagedHash");

        CallToolResult launch = await CallAndPrintAsync(
            client,
            "launch_staged_diff",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId
            });
        string launchText = ExtractToolText(launch);
        if (!launchText.Contains("\"status\":\"failed\"", StringComparison.OrdinalIgnoreCase)
            || !launchText.Contains("\"isError\":true", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Expected pre-merge validation failure before any launch decision.");
            Console.Error.WriteLine(launchText);
            return 1;
        }

        string launchStatus = launchText.Contains("\"launched\":true", StringComparison.OrdinalIgnoreCase)
            ? "override-launched"
            : "blocked";
        Console.WriteLine();
        Console.WriteLine("Pre-merge validation failure smoke passed.");
        Console.WriteLine($"Launch status: {launchStatus}");
        Console.WriteLine($"Session ID: {sessionId}");
        Console.WriteLine($"Staged record ID: {stagedRecordId}");
        Console.WriteLine($"Expected staged hash: {stagedHash}");
        Console.WriteLine("After reviewing/saving in WinMerge, record the result with:");
        Console.WriteLine($"dotnet .\\tests\\smoke\\AIMonitor.ToolSmokeTests\\bin\\Debug\\net10.0\\AIMonitor.ToolSmokeTests.dll --mcp-live-record-decision --staged-record-id {stagedRecordId} --decision accepted --expected-staged-hash {stagedHash}");
        Console.WriteLine($"dotnet .\\tests\\smoke\\AIMonitor.ToolSmokeTests\\bin\\Debug\\net10.0\\AIMonitor.ToolSmokeTests.dll --mcp-live-record-decision --staged-record-id {stagedRecordId} --decision rejected");
        Console.WriteLine(launchText);
        return launch.IsError == true ? 1 : 0;
    }

    private static async Task<int> RunMcpLiveRestorePremergeFixtureWinMergeAsync()
    {
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);
        const string relativePath = "AppConfig/AppConfig.cs";
        const string oldText = "        public bool InitiaWorkflowTestPassed3 { get; set; } = MissingPremergeSymbol.Value;";
        const string newText = "        public bool InitiaWorkflowTestPassed3 { get; set; } = true;";

        await using McpClient client = await CreateBridgeClientAsync(repositoryRoot, settingsPath);
        CallToolResult session = await CallAndPrintAsync(
            client,
            "start_monitor_session",
            new Dictionary<string, object?>
            {
                ["title"] = "restore premerge validation fixture"
            });
        string sessionId = ExtractJsonString(ExtractToolText(session), "sessionId");

        await CallAndPrintAsync(
            client,
            "refresh_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = relativePath,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "replace_text_in_file",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["oldText"] = oldText,
                ["newText"] = newText,
                ["expectedMatches"] = 1,
                ["sessionId"] = sessionId
            });
        await StageLaunchAndPrintDecisionAsync(
            client,
            sessionId,
            relativePath,
            "restore premerge validation fixture",
            "Pre-merge fixture restore smoke");
        return 0;
    }

    private static async Task<int> RunMcpLiveSyntaxRejectionAsync()
    {
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);
        const string relativePath = "AppConfig/AppConfig.cs";
        const string oldText = "        public bool InitiaWorkflowTestPassed3 { get; set; } = true;";
        const string newText = "        public bool InitiaWorkflowTestPassed3 { get; set; } = ;";

        await using McpClient client = await CreateBridgeClientAsync(repositoryRoot, settingsPath);
        CallToolResult session = await CallAndPrintAsync(
            client,
            "start_monitor_session",
            new Dictionary<string, object?>
            {
                ["title"] = "syntax rejection smoke"
            });
        string sessionId = ExtractJsonString(ExtractToolText(session), "sessionId");

        await CallAndPrintAsync(
            client,
            "refresh_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = relativePath,
                ["sessionId"] = sessionId
            });
        CallToolResult replace = await CallAndPrintAsync(
            client,
            "replace_text_in_file",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["oldText"] = oldText,
                ["newText"] = newText,
                ["expectedMatches"] = 1,
                ["sessionId"] = sessionId
            });
        string replaceText = ExtractToolText(replace);
        if (replace.IsError != true)
        {
            Console.Error.WriteLine("Expected malformed C# to be rejected before staging.");
            Console.Error.WriteLine(replaceText);
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Syntax rejection smoke passed.");
        Console.WriteLine("Agent recovery contract: rewrite the Working candidate into valid C# and retry the edit; do not force this path to WinMerge.");
        Console.WriteLine($"Session ID: {sessionId}");
        Console.WriteLine(replaceText);
        return 0;
    }

    private static async Task<int> RunMcpLiveRecordDecisionAsync(string[] args)
    {
        string stagedRecordId = GetRequiredOption(args, "--staged-record-id");
        string decision = GetRequiredOption(args, "--decision");
        string? expectedStagedHash = GetOption(args, "--expected-staged-hash");
        string repositoryRoot = ResolveRepositoryRoot(AppContext.BaseDirectory);
        string settingsPath = ResolveSmokeSettingsPath(repositoryRoot);

        await using McpClient client = await CreateBridgeClientAsync(repositoryRoot, settingsPath);
        await CallAndPrintAsync(
            client,
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId,
                ["decision"] = decision,
                ["expectedStagedHash"] = expectedStagedHash
            });
        return 0;
    }

    private static async Task CreateStageAndRejectNewFileAsync(
        McpClient client,
        string sessionId,
        string relativePath,
        string content)
    {
        await CallAndPrintAsync(
            client,
            "new_file",
            new Dictionary<string, object?>
            {
                ["sourceFilePath"] = relativePath,
                ["sessionId"] = sessionId
            });
        await CallAndPrintAsync(
            client,
            "submit_file",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["content"] = content,
                ["sessionId"] = sessionId
            });
        CallToolResult stage = await CallAndPrintAsync(
            client,
            "stage_candidate_for_review",
            new Dictionary<string, object?>
            {
                ["path"] = relativePath,
                ["ledgerSummary"] = "bridge multi-file session smoke",
                ["sessionId"] = sessionId
            });
        string stagedRecordId = ExtractJsonString(ExtractToolText(stage), "stagedRecordId");
        await CallAndPrintAsync(
            client,
            "record_diff_decision",
            new Dictionary<string, object?>
            {
                ["stagedRecordId"] = stagedRecordId,
                ["decision"] = "rejected"
            });
    }

    private static async Task<McpClient> CreateBridgeClientAsync(string repositoryRoot, string settingsPath)
    {
        string bridgeDll = Path.Combine(repositoryRoot, "src", "AIMonitor.McpStdioBridge", "bin", "Debug", "net10.0", "AIMonitor.McpStdioBridge.dll");
        if (!File.Exists(bridgeDll))
        {
            throw new FileNotFoundException("MCP stdio bridge build output not found.", bridgeDll);
        }

        StdioClientTransportOptions options = new()
        {
            Name = "ai-monitor-live-smoke",
            Command = "dotnet",
            Arguments = [bridgeDll, "--repo-root", repositoryRoot, "--config", settingsPath],
            WorkingDirectory = repositoryRoot
        };

        return await McpClient.CreateAsync(new StdioClientTransport(options));
    }

    private static string ResolveSmokeSettingsPath(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(smokeSettingsPathOverride))
        {
            return Path.Combine(repositoryRoot, "config", "appsettings.json");
        }

        return Path.GetFullPath(Path.IsPathFullyQualified(smokeSettingsPathOverride)
            ? smokeSettingsPathOverride
            : Path.Combine(repositoryRoot, smokeSettingsPathOverride));
    }

    private static string ResolveConfigurationNamespace(string repositoryRoot, string settingsPath)
    {
        MonitorSettings settings = MonitorSettingsLoader.Load(repositoryRoot, settingsPath);
        string solutionName = Path.GetFileNameWithoutExtension(settings.WatchedSolutionPath);
        string safeName = Regex.Replace(solutionName, "[^A-Za-z0-9_]", "_");
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "WatchedSample";
        }

        if (char.IsDigit(safeName[0]))
        {
            safeName = "_" + safeName;
        }

        return $"{safeName}.Configuration";
    }

    private static async Task<CallToolResult> CallAndPrintAsync(
        McpClient client,
        string toolName,
        Dictionary<string, object?>? arguments = null)
    {
        CallToolResult result = await client.CallToolAsync(toolName, arguments);
        Console.WriteLine($"{toolName}: error={result.IsError == true}");
        return result;
    }

    private static string ExtractToolText(CallToolResult result)
    {
        string wrapperJson = JsonSerializer.Serialize(result);
        using JsonDocument document = JsonDocument.Parse(wrapperJson);
        foreach (JsonElement content in document.RootElement.GetProperty("content").EnumerateArray())
        {
            if (content.TryGetProperty("text", out JsonElement text)
                && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString() ?? string.Empty;
            }
        }

        return wrapperJson;
    }

    private static string ExtractJsonString(string json, string propertyName)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Expected JSON string property '{propertyName}'.");
        }

        return value.GetString() ?? string.Empty;
    }

    private static string GetRequiredOption(string[] args, string name)
    {
        return GetOption(args, name) ?? throw new InvalidOperationException($"Missing required option {name}.");
    }

    private static string? GetOption(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static int ExtractJsonInt(string json, string propertyName)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidOperationException($"Expected JSON number property '{propertyName}'.");
        }

        return value.GetInt32();
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
