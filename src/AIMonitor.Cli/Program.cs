using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.Logging;
using AIMonitor.MSBuild;
using AIMonitor.Runtime;
using AIMonitor.Workflow;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIMonitor.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("AIMonitor CLI scaffold");
            Console.WriteLine("Commands:");
            Console.WriteLine("  hub start [--repo-root <path>]");
            Console.WriteLine("  status [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  index rebuild [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  index summary [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  index projects [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  index documents [--project <path>] [--file <path>] [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  index symbols [--file <path>] [--name <name>] [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  index references [--symbol <stable-key>] [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  index references-in-file --file <path> [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  index packages [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  edit refresh --file <path> [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  edit new --file <future-path> [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  edit replace-text --file <path> --old-text <text>|--old-text-file <path> --new-text <text>|--new-text-file <path> [--expected-matches <n>] [--expected-working-hash <hash>] [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  edit status --file <path> [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  edit stage --file <path> [--ledger-summary <text>] [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  edit launch-diff --staged-record-id <id> [--diff-tool <path>] [--force-validation] [--repo-root <path>] [--config <path>]");
            Console.WriteLine("  edit record-decision --staged-record-id <id> --decision accepted|rejected [--repo-root <path>] [--config <path>]");
            return 0;
        }

        if (IsHubStart(args))
        {
            return StartHub(args);
        }

        if (string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase))
        {
            return Query(args, service => service.GetMonitorStatus());
        }

        if (IsIndexRebuild(args))
        {
            return await RebuildIndexAsync(args);
        }

        if (IsIndexQuery(args))
        {
            return QueryIndex(args);
        }

        if (IsEditCommand(args))
        {
            return Edit(args);
        }

        Console.Error.WriteLine($"Unknown command: {args[0]}");
        return 2;
    }

    private static bool IsIndexRebuild(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "index", StringComparison.OrdinalIgnoreCase)
            && string.Equals(args[1], "rebuild", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHubStart(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "hub", StringComparison.OrdinalIgnoreCase)
            && string.Equals(args[1], "start", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIndexQuery(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "index", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEditCommand(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "edit", StringComparison.OrdinalIgnoreCase);
    }

    private static int QueryIndex(string[] args)
    {
        return args[1].ToLowerInvariant() switch
        {
            "summary" => Query(args, service => service.GetSummary()),
            "projects" => Query(args, service => service.ListProjects()),
            "documents" => Query(args, service => service.ListDocuments(
                GetOption(args, "--project"),
                GetOption(args, "--file"))),
            "symbols" => Query(args, service => service.ListSymbols(
                GetOption(args, "--file"),
                GetOption(args, "--name"))),
            "references" => Query(args, service => service.ListReferences(GetOption(args, "--symbol"))),
            "references-in-file" => Query(args, service => service.ListReferencesInFile(RequireOption(args, "--file"))),
            "packages" => Query(args, service => service.ListPackageReferences()),
            _ => UnknownIndexCommand(args[1])
        };
    }

    private static int Query<T>(string[] args, Func<SolutionIndexQueryService, T> query)
    {
        try
        {
            MonitorSettings settings = LoadSettings(args);
            IMonitorLogger logger = CreateLogger(settings);
            string commandName = GetCommandName(args);
            string commandLine = string.Join(" ", args.Select(QuoteArgument));
            string requestId = Guid.NewGuid().ToString("N");
            Stopwatch stopwatch = Stopwatch.StartNew();
            logger.Write(
                MonitorLogLevel.Information,
                "AIMonitor.Cli",
                "adapter.query.started",
                "CLI adapter query started.",
                new Dictionary<string, string>
                {
                    ["requestId"] = requestId,
                    ["adapterProtocol"] = "cli-mcp-like",
                    ["command"] = commandName,
                    ["toolName"] = commandName,
                    ["commandLine"] = commandLine,
                    ["paramsPreview"] = CreateParamsPreview(args)
                });

            SolutionIndexQueryService service = CreateQueryService(settings);
            T result = query(service);
            string responseJson = JsonSerializer.Serialize(result, JsonOptions);
            Console.WriteLine(responseJson);
            stopwatch.Stop();

            logger.Write(
                MonitorLogLevel.Information,
                "AIMonitor.Cli",
                "adapter.query.completed",
                "CLI adapter query completed.",
                new Dictionary<string, string>
                {
                    ["requestId"] = requestId,
                    ["adapterProtocol"] = "cli-mcp-like",
                    ["command"] = commandName,
                    ["toolName"] = commandName,
                    ["commandLine"] = commandLine,
                    ["durationMs"] = stopwatch.ElapsedMilliseconds.ToString(),
                    ["isError"] = "false",
                    ["contentType"] = "application/json",
                    ["contentShape"] = GetResultKind(responseJson),
                    ["contentCount"] = GetResultCount(responseJson),
                    ["contentTextPreview"] = CreateResponsePreview(responseJson),
                    ["contentText"] = responseJson
                });
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Edit(string[] args)
    {
        return ExecuteJsonCommand(args, () =>
        {
            MonitorSettings settings = LoadSettings(args);
            IMonitorLogger logger = CreateLogger(settings);
            WorkflowEditService service = new(settings);
            string subcommand = args[1].ToLowerInvariant();
            return subcommand switch
            {
                "refresh" => service.Refresh(RequireOption(args, "--file")),
                "new" => service.NewFile(RequireOption(args, "--file")),
                "replace-text" => service.ReplaceText(
                    RequireOption(args, "--file"),
                    RequireTextOption(args, "--old-text", "--old-text-file"),
                    RequireTextOption(args, "--new-text", "--new-text-file"),
                    GetIntOption(args, "--expected-matches"),
                    GetOption(args, "--expected-working-hash")),
                "status" => service.GetStatus(RequireOption(args, "--file")),
                "stage" => service.Stage(RequireOption(args, "--file"), GetOption(args, "--ledger-summary")),
                "launch-diff" => LaunchDiff(args, settings, logger, service),
                "record-decision" => RecordDecision(args, settings, logger, service),
                "accept" => service.Accept(
                    RequireOption(args, "--file"),
                    RequireOption(args, "--expected-hash")),
                "reject" => service.Reject(RequireOption(args, "--file")),
                _ => throw new InvalidOperationException($"Unknown edit command: {args[1]}")
            };
        });
    }

    private static object LaunchDiff(string[] args, MonitorSettings settings, IMonitorLogger logger, WorkflowEditService service)
    {
        StagedEditRecord record = service.GetStagedRecord(RequireOption(args, "--staged-record-id"));
        PreMergeValidationResult validation = ValidateStagedRecord(settings, record);
        bool forceValidation = HasOption(args, "--force-validation");
        string validationPrompt = "";
        if (validation.IsError && !forceValidation && CanShowValidationDialog())
        {
            forceValidation = PromptForValidationOverride(validation);
            validationPrompt = forceValidation ? "approved" : "cancelled";
        }

        logger.Write(
            validation.IsError ? MonitorLogLevel.Warning : MonitorLogLevel.Information,
            "AIMonitor.Cli",
            "premerge.validation.completed",
            validation.Message,
            new Dictionary<string, string>
            {
                ["stagedRecordId"] = record.StagedRecordId,
                ["watchedFilePath"] = record.WatchedFilePath,
                ["relativePath"] = record.RelativePath,
                ["validationStatus"] = validation.Status,
                ["diagnosticCount"] = validation.DiagnosticCount.ToString(),
                ["validationWorkspacePath"] = validation.ValidationWorkspacePath,
                ["forceValidation"] = forceValidation.ToString().ToLowerInvariant(),
                ["validationPrompt"] = validationPrompt,
                ["isError"] = validation.IsError.ToString().ToLowerInvariant()
            });

        if (validation.IsError && !forceValidation)
        {
            StagedEditRecord blockedRecord = service.RecordDiffLaunch(
                record.StagedRecordId,
                launched: false,
                "Pre-merge validation failed. WinMerge launch is blocked unless --force-validation is used after human approval.");
            return new
            {
                stagedRecord = blockedRecord,
                preMergeValidation = validation,
                diffLaunch = new
                {
                    launched = false,
                    status = "blocked-premerge-validation",
                    message = "Pre-merge validation failed. Human approval is required before force-launching WinMerge."
                },
                nextStep = CanShowValidationDialog()
                    ? "Human cancelled validation override. Fix and restage before launching WinMerge."
                    : "Validation failed and no interactive dialog is available. Ask the user whether to override; rerun edit launch-diff with --force-validation only if they explicitly approve."
            };
        }

        DiffLaunchResult result = new WinMergeDiffToolLauncher().Launch(new DiffLaunchRequest
        {
            OriginalFilePath = GetDiffOriginalFilePath(record),
            ProposedFilePath = record.StagedFilePath,
            ExplicitToolPath = GetOption(args, "--diff-tool")
        });
        StagedEditRecord updatedRecord = service.RecordDiffLaunch(record.StagedRecordId, result.Launched, result.Message);
        return new
        {
            stagedRecord = updatedRecord,
            preMergeValidation = validation,
            diffLaunch = result,
            nextStep = "After WinMerge review, save the staged candidate into the watched source for accept, or leave watched source unchanged for reject. Then run edit record-decision."
        };
    }

    private static string GetDiffOriginalFilePath(StagedEditRecord record)
    {
        if (record.IsNewFile)
        {
            string? watchedDirectory = Path.GetDirectoryName(record.WatchedFilePath);
            if (!string.IsNullOrWhiteSpace(watchedDirectory))
            {
                Directory.CreateDirectory(watchedDirectory);
            }

            return record.WatchedFilePath;
        }

        return string.IsNullOrWhiteSpace(record.ReviewBaselineFilePath)
            ? record.WatchedFilePath
            : record.ReviewBaselineFilePath;
    }

    private static object RecordDecision(string[] args, MonitorSettings settings, IMonitorLogger logger, WorkflowEditService service)
    {
        StagedEditRecord record = service.RecordDecision(
            RequireOption(args, "--staged-record-id"),
            RequireOption(args, "--decision"));
        PostAcceptIndexRefreshResult? indexRefresh = null;
        if (record.Classification is "accepted" or "accepted-normalized")
        {
            indexRefresh = RebuildIndexAfterAcceptedDecision(settings, logger, record);
        }

        return new
        {
            stagedRecordId = record.StagedRecordId,
            watchedFilePath = record.WatchedFilePath,
            relativePath = record.RelativePath,
            decision = record.Decision,
            classification = record.Classification,
            status = record.Status,
            message = record.Message,
            stagedRecord = record,
            indexRefresh,
            nextStep = record.Classification is "accepted" or "accepted-normalized"
                ? "Index was rebuilt after accept. Run edit refresh before further edits to this watched file."
                : "Decision recorded. Do not rely on changed index rows unless an accepted decision rebuilt the index."
        };
    }

    private static int ExecuteJsonCommand<T>(string[] args, Func<T> command)
    {
        try
        {
            MonitorSettings settings = LoadSettings(args);
            IMonitorLogger logger = CreateLogger(settings);
            string commandName = GetCommandName(args);
            string commandLine = string.Join(" ", args.Select(QuoteArgument));
            string requestId = Guid.NewGuid().ToString("N");
            Stopwatch stopwatch = Stopwatch.StartNew();
            logger.Write(
                MonitorLogLevel.Information,
                "AIMonitor.Cli",
                "adapter.query.started",
                "CLI adapter command started.",
                new Dictionary<string, string>
                {
                    ["requestId"] = requestId,
                    ["adapterProtocol"] = "cli-mcp-like",
                    ["command"] = commandName,
                    ["toolName"] = commandName,
                    ["commandLine"] = commandLine,
                    ["paramsPreview"] = CreateParamsPreview(args)
                });

            T result = command();
            string responseJson = JsonSerializer.Serialize(result, JsonOptions);
            Console.WriteLine(responseJson);
            stopwatch.Stop();
            logger.Write(
                MonitorLogLevel.Information,
                "AIMonitor.Cli",
                "adapter.query.completed",
                "CLI adapter command completed.",
                new Dictionary<string, string>
                {
                    ["requestId"] = requestId,
                    ["adapterProtocol"] = "cli-mcp-like",
                    ["command"] = commandName,
                    ["toolName"] = commandName,
                    ["commandLine"] = commandLine,
                    ["durationMs"] = stopwatch.ElapsedMilliseconds.ToString(),
                    ["isError"] = "false",
                    ["contentType"] = "application/json",
                    ["contentShape"] = GetResultKind(responseJson),
                    ["contentCount"] = GetResultCount(responseJson),
                    ["contentTextPreview"] = CreateResponsePreview(responseJson),
                    ["contentText"] = responseJson
                });
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RebuildIndexAsync(string[] args)
    {
        try
        {
            MonitorSettings settings = LoadSettings(args);
            string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
            string logPath = MonitorLogPaths.GetDefaultLogPath(settings);
            IMonitorLogger logger = CreateLogger(settings);

            logger.Write(
                MonitorLogLevel.Information,
                "AIMonitor.Cli",
                "index.rebuild.started",
                "Solution index rebuild started.",
                new Dictionary<string, string>
                {
                    ["watchedSolutionPath"] = settings.WatchedSolutionPath,
                    ["databasePath"] = databasePath
                });

            SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
            SolutionIndexBuilder builder = new(new MSBuildWorkspaceLoader(), store);
            SolutionIndexSummary summary = await builder.RebuildAsync(settings);

            logger.Write(
                MonitorLogLevel.Information,
                "AIMonitor.Cli",
                "index.rebuild.completed",
                "Solution index rebuild completed.",
                new Dictionary<string, string>
                {
                    ["projectCount"] = summary.ProjectCount.ToString(),
                    ["documentCount"] = summary.DocumentCount.ToString(),
                    ["diagnosticCount"] = summary.DiagnosticCount.ToString()
                });

            Console.WriteLine($"Indexed solution: {settings.WatchedSolutionPath}");
            Console.WriteLine($"Database: {databasePath}");
            Console.WriteLine($"Log: {logPath}");
            Console.WriteLine($"Projects: {summary.ProjectCount}");
            Console.WriteLine($"Documents: {summary.DocumentCount}");
            Console.WriteLine($"Diagnostics: {summary.DiagnosticCount}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static PostAcceptIndexRefreshResult RebuildIndexAfterAcceptedDecision(
        MonitorSettings settings,
        IMonitorLogger logger,
        StagedEditRecord record)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        logger.Write(
            MonitorLogLevel.Information,
            "AIMonitor.Cli",
            "index.refresh-after-accept.started",
            "Post-accept solution index rebuild started.",
            new Dictionary<string, string>
            {
                ["stagedRecordId"] = record.StagedRecordId,
                ["watchedFilePath"] = record.WatchedFilePath,
                ["watchedSolutionPath"] = settings.WatchedSolutionPath,
                ["databasePath"] = databasePath
            });

        try
        {
            SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
            SolutionIndexBuilder builder = new(new MSBuildWorkspaceLoader(), store);
            SolutionIndexSummary summary = builder.RebuildAsync(settings).GetAwaiter().GetResult();
            stopwatch.Stop();
            PostAcceptIndexRefreshResult result = new()
            {
                Status = "rebuilt",
                IsError = false,
                DatabasePath = databasePath,
                ProjectCount = summary.ProjectCount,
                DocumentCount = summary.DocumentCount,
                DiagnosticCount = summary.DiagnosticCount,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Message = "Post-accept solution index rebuild completed."
            };
            logger.Write(
                MonitorLogLevel.Information,
                "AIMonitor.Cli",
                "index.refresh-after-accept.completed",
                result.Message,
                new Dictionary<string, string>
                {
                    ["stagedRecordId"] = record.StagedRecordId,
                    ["watchedFilePath"] = record.WatchedFilePath,
                    ["databasePath"] = databasePath,
                    ["projectCount"] = result.ProjectCount.ToString(),
                    ["documentCount"] = result.DocumentCount.ToString(),
                    ["diagnosticCount"] = result.DiagnosticCount.ToString(),
                    ["durationMs"] = result.DurationMs.ToString(),
                    ["isError"] = "false"
                });
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            PostAcceptIndexRefreshResult result = new()
            {
                Status = "failed",
                IsError = true,
                DatabasePath = databasePath,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Message = ex.Message
            };
            logger.Write(
                MonitorLogLevel.Error,
                "AIMonitor.Cli",
                "index.refresh-after-accept.failed",
                "Post-accept solution index rebuild failed.",
                new Dictionary<string, string>
                {
                    ["stagedRecordId"] = record.StagedRecordId,
                    ["watchedFilePath"] = record.WatchedFilePath,
                    ["databasePath"] = databasePath,
                    ["durationMs"] = result.DurationMs.ToString(),
                    ["isError"] = "true",
                    ["error"] = ex.Message
                });
            return result;
        }
    }

    private static PreMergeValidationResult ValidateStagedRecord(MonitorSettings settings, StagedEditRecord record)
    {
        if (!File.Exists(record.StagedFilePath))
        {
            return new PreMergeValidationResult
            {
                Status = "missing-staged-file",
                IsError = true,
                Message = "Pre-merge validation failed because the staged candidate file is missing."
            };
        }

        if (!File.Exists(settings.WatchedSolutionPath))
        {
            return new PreMergeValidationResult
            {
                Status = "missing-solution",
                IsError = true,
                Message = "Pre-merge validation failed because the watched solution file is missing."
            };
        }

        string validationRoot = Path.Combine(
            MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings),
            "validation",
            $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}"[..42]);
        string sourceRoot = settings.WatchedProjectFolder;
        string validationSolutionPath = Path.Combine(validationRoot, Path.GetRelativePath(sourceRoot, settings.WatchedSolutionPath));
        try
        {
            CopyDirectoryForValidation(sourceRoot, validationRoot);
            string validationCandidatePath = Path.Combine(validationRoot, record.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(validationCandidatePath) ?? validationRoot);
            File.Copy(record.StagedFilePath, validationCandidatePath, overwrite: true);

            ProcessResult build = RunProcess(
                "dotnet",
                ["build", validationSolutionPath, "--nologo", "-v:minimal"],
                validationRoot,
                TimeSpan.FromMinutes(3));
            string output = string.Join(Environment.NewLine, [build.StandardOutput, build.StandardError]);
            string[] errorDiagnostics = ExtractBuildErrors(output);
            bool failed = build.TimedOut || build.ExitCode != 0;
            if (failed && errorDiagnostics.Length == 0)
            {
                errorDiagnostics = [$"dotnet build exited with code {build.ExitCode} but did not emit parseable error diagnostics."];
            }

            return new PreMergeValidationResult
            {
                Status = build.TimedOut ? "timeout" : failed ? "failed" : "passed",
                IsError = failed,
                DiagnosticCount = errorDiagnostics.Length,
                Diagnostics = errorDiagnostics,
                ValidationWorkspacePath = validationRoot,
                Message = build.TimedOut
                    ? "Pre-merge full solution build timed out."
                    : failed
                        ? "Pre-merge full solution build failed."
                        : "Pre-merge full solution build passed."
            };
        }
        catch (Exception ex)
        {
            return new PreMergeValidationResult
            {
                Status = "failed",
                IsError = true,
                DiagnosticCount = 1,
                Diagnostics = [ex.Message],
                ValidationWorkspacePath = validationRoot,
                Message = "Pre-merge full solution build failed."
            };
        }
    }

    private static void CopyDirectoryForValidation(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (string directoryPath in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string directoryName = Path.GetFileName(directoryPath);
            if (IsSkippedValidationDirectory(directoryName))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(sourceRoot, directoryPath);
            if (relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(IsSkippedValidationDirectory))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(destinationRoot, relativePath));
        }

        foreach (string filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceRoot, filePath);
            if (relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(IsSkippedValidationDirectory))
            {
                continue;
            }

            string destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationRoot);
            File.Copy(filePath, destinationPath, overwrite: true);
        }
    }

    private static bool IsSkippedValidationDirectory(string directoryName)
    {
        return directoryName.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
            || directoryName.Equals("packages", StringComparison.OrdinalIgnoreCase);
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

    private static string[] ExtractBuildErrors(string output)
    {
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains(": error ", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
    }

    private static bool PromptForValidationOverride(PreMergeValidationResult validation)
    {
        string diagnostics = validation.Diagnostics.Length == 0
            ? "No error diagnostics were captured."
            : string.Join(Environment.NewLine, validation.Diagnostics.Take(8));
        string dialogContent = diagnostics + Environment.NewLine + Environment.NewLine
            + "Open WinMerge anyway?";
        if (TryPromptForValidationOverrideWithTaskDialog(dialogContent, out bool launchApproved))
        {
            return launchApproved;
        }

        string message = "Pre-merge validation failed." + Environment.NewLine + Environment.NewLine
            + dialogContent;
        int result = MessageBoxW(
            IntPtr.Zero,
            message,
            "AIMonitor Pre-Merge Validation",
            MessageBoxTypeOkCancel | MessageBoxIconWarning | MessageBoxDefaultButton2 | MessageBoxSetForeground);
        return result == MessageBoxResultOk;
    }

    private static bool TryPromptForValidationOverrideWithTaskDialog(string message, out bool launchApproved)
    {
        launchApproved = false;
        IntPtr buttonsPointer = IntPtr.Zero;

        try
        {
            TaskDialogButton[] buttons =
            [
                new()
                {
                    ButtonId = TaskDialogButtonLaunch,
                    ButtonText = "Yes Launch"
                },
                new()
                {
                    ButtonId = TaskDialogButtonCancel,
                    ButtonText = "Cancel"
                }
            ];

            int buttonSize = Marshal.SizeOf<TaskDialogButton>();
            buttonsPointer = Marshal.AllocHGlobal(buttonSize * buttons.Length);
            for (int index = 0; index < buttons.Length; index++)
            {
                Marshal.StructureToPtr(buttons[index], buttonsPointer + (index * buttonSize), fDeleteOld: false);
            }

            TaskDialogConfig config = new()
            {
                Size = (uint)Marshal.SizeOf<TaskDialogConfig>(),
                Flags = TaskDialogAllowDialogCancellation | TaskDialogPositionRelativeToWindow,
                WindowTitle = "AIMonitor Pre-Merge Validation",
                MainIcon = TaskDialogWarningIcon,
                MainInstruction = "Pre-merge validation failed.",
                Content = message,
                ButtonCount = (uint)buttons.Length,
                Buttons = buttonsPointer,
                DefaultButton = TaskDialogButtonCancel
            };

            int hr = TaskDialogIndirect(ref config, out int selectedButton, out _, out _);
            if (hr != 0)
            {
                return false;
            }

            launchApproved = selectedButton == TaskDialogButtonLaunch;
            return true;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        finally
        {
            if (buttonsPointer != IntPtr.Zero)
            {
                int buttonSize = Marshal.SizeOf<TaskDialogButton>();
                for (int index = 0; index < 2; index++)
                {
                    Marshal.DestroyStructure<TaskDialogButton>(buttonsPointer + (index * buttonSize));
                }

                Marshal.FreeHGlobal(buttonsPointer);
            }
        }
    }

    private static bool CanShowValidationDialog()
    {
        return OperatingSystem.IsWindows() && Environment.UserInteractive;
    }

    private static int StartHub(string[] args)
    {
        try
        {
            string repositoryRoot = GetOption(args, "--repo-root") ?? Directory.GetCurrentDirectory();
            string appProject = Path.Combine(repositoryRoot, "src", "AIMonitor.App", "AIMonitor.App.csproj");
            if (!File.Exists(appProject))
            {
                Console.Error.WriteLine($"AIMonitor.App project was not found: {appProject}");
                return 1;
            }

            using System.Diagnostics.Process process = new();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                WorkingDirectory = repositoryRoot,
                UseShellExecute = true
            };
            process.StartInfo.ArgumentList.Add("run");
            process.StartInfo.ArgumentList.Add("--project");
            process.StartInfo.ArgumentList.Add(appProject);
            process.Start();
            Console.WriteLine($"Started AIMonitor hub from {appProject}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static SolutionIndexQueryService CreateQueryService(string[] args)
    {
        return SolutionIndexQueryService.Create(LoadSettings(args));
    }

    private static SolutionIndexQueryService CreateQueryService(MonitorSettings settings)
    {
        return SolutionIndexQueryService.Create(settings);
    }

    private static MonitorSettings LoadSettings(string[] args)
    {
        string repositoryRoot = GetOption(args, "--repo-root") ?? Directory.GetCurrentDirectory();
        string? settingsPath = GetOption(args, "--config");
        return MonitorSettingsLoader.Load(repositoryRoot, settingsPath);
    }

    private static IMonitorLogger CreateLogger(MonitorSettings settings)
    {
        return new MonitorLogPipeClientLogger(
            MonitorLogPipeNames.GetDefaultPipeName(settings),
            new JsonLinesMonitorLogger(MonitorLogPaths.GetDefaultLogPath(settings)));
    }

    private static string? GetOption(string[] args, string optionName)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static bool HasOption(string[] args, string optionName)
    {
        return args.Any(argument => string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase));
    }

    private static string RequireOption(string[] args, string optionName)
    {
        return GetOption(args, optionName)
            ?? throw new InvalidOperationException($"{optionName} is required.");
    }

    private static int? GetIntOption(string[] args, string optionName)
    {
        string? value = GetOption(args, optionName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, out int parsed))
        {
            throw new InvalidOperationException($"{optionName} must be an integer.");
        }

        return parsed;
    }

    private static string RequireTextOption(string[] args, string inlineOptionName, string fileOptionName)
    {
        string? inlineValue = GetOption(args, inlineOptionName);
        string? filePath = GetOption(args, fileOptionName);
        if (HasOption(args, inlineOptionName) && !string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException($"Use either {inlineOptionName} or {fileOptionName}, not both.");
        }

        if (HasOption(args, inlineOptionName))
        {
            return inlineValue ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            return File.ReadAllText(filePath);
        }

        throw new InvalidOperationException($"{inlineOptionName} or {fileOptionName} is required.");
    }

    private static string QuoteArgument(string argument)
    {
        return argument.Contains(' ', StringComparison.Ordinal)
            ? "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : argument;
    }

    private static string GetCommandName(string[] args)
    {
        if (args.Length >= 2 && string.Equals(args[0], "index", StringComparison.OrdinalIgnoreCase))
        {
            return $"index {args[1]}";
        }

        if (args.Length >= 2 && string.Equals(args[0], "edit", StringComparison.OrdinalIgnoreCase))
        {
            return $"edit {args[1]}";
        }

        return args[0];
    }

    private static string GetResultKind(string responseJson)
    {
        using JsonDocument document = JsonDocument.Parse(responseJson);
        return document.RootElement.ValueKind.ToString();
    }

    private static string GetResultCount(string responseJson)
    {
        using JsonDocument document = JsonDocument.Parse(responseJson);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.GetArrayLength().ToString()
            : "1";
    }

    private static string CreateResponsePreview(string responseJson)
    {
        string singleLine = string.Join(" ", responseJson.Split(
            [' ', '\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= 600
            ? singleLine
            : singleLine[..600] + "...";
    }

    private static string CreateParamsPreview(string[] args)
    {
        Dictionary<string, string> values = [];
        for (int index = 0; index < args.Length; index++)
        {
            string value = args[index];
            if (!value.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            values[value.TrimStart('-')] = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[index + 1]
                : "true";
        }

        return JsonSerializer.Serialize(values, JsonOptions);
    }

    private static int UnknownIndexCommand(string command)
    {
        Console.Error.WriteLine($"Unknown index command: {command}");
        return 2;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int TaskDialogIndirect(
        ref TaskDialogConfig taskConfig,
        out int button,
        out int radioButton,
        [MarshalAs(UnmanagedType.Bool)] out bool verificationFlagChecked);

    private const uint MessageBoxTypeOkCancel = 0x00000001;
    private const uint MessageBoxIconWarning = 0x00000030;
    private const uint MessageBoxDefaultButton2 = 0x00000100;
    private const uint MessageBoxSetForeground = 0x00010000;
    private const int MessageBoxResultOk = 1;
    private const int TaskDialogButtonLaunch = 1001;
    private const int TaskDialogButtonCancel = 2;
    private const uint TaskDialogAllowDialogCancellation = 0x00000008;
    private const uint TaskDialogPositionRelativeToWindow = 0x00001000;
    private static readonly IntPtr TaskDialogWarningIcon = new(ushort.MaxValue);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TaskDialogButton
    {
        public int ButtonId;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string ButtonText;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TaskDialogConfig
    {
        public uint Size;
        public IntPtr ParentWindowHandle;
        public IntPtr InstanceHandle;
        public uint Flags;
        public uint CommonButtons;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string WindowTitle;

        public IntPtr MainIcon;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string MainInstruction;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Content;

        public uint ButtonCount;
        public IntPtr Buttons;
        public int DefaultButton;
        public uint RadioButtonCount;
        public IntPtr RadioButtons;
        public int DefaultRadioButton;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? VerificationText;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ExpandedInformation;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ExpandedControlText;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? CollapsedControlText;

        public IntPtr FooterIcon;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Footer;

        public IntPtr Callback;
        public IntPtr CallbackData;
        public uint Width;
    }

    private sealed class PreMergeValidationResult
    {
        public string Status { get; set; } = string.Empty;

        public bool IsError { get; set; }

        public int DiagnosticCount { get; set; }

        public string[] Diagnostics { get; set; } = [];

        public string ValidationWorkspacePath { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }

    private sealed record ProcessResult(
        int ExitCode,
        bool TimedOut,
        string StandardOutput,
        string StandardError);

    private sealed class PostAcceptIndexRefreshResult
    {
        public string Status { get; set; } = string.Empty;

        public bool IsError { get; set; }

        public string DatabasePath { get; set; } = string.Empty;

        public int ProjectCount { get; set; }

        public int DocumentCount { get; set; }

        public int DiagnosticCount { get; set; }

        public long DurationMs { get; set; }

        public string Message { get; set; } = string.Empty;
    }
}
