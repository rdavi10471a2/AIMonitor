using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.Indexing;
using AIMonitor.Logging;
using AIMonitor.Planning;
using AIMonitor.Runtime;
using AIMonitor.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIMonitor.McpServer;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        MonitorSettings settings = LoadSettings(args);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<IMonitorLogger>(_ => new MonitorLogPipeClientLogger(
            MonitorLogPipeNames.GetDefaultPipeName(settings),
            new JsonLinesMonitorLogger(MonitorLogPaths.GetDefaultLogPath(settings))));
        builder.Services.AddSingleton(SolutionIndexQueryService.Create(settings));
        builder.Services.AddSingleton(new WorkflowEditService(settings));
        builder.Services.AddSingleton(new PlanningService(settings));
        builder.Services.AddSingleton(new RoslynEditService(settings));
        builder.Services.AddSingleton(new WorkflowEditPaths(settings));
        builder.Services.AddSingleton<AIMonitorMcpRuntimeState>();
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<AIMonitorTools>();

        await builder.Build().RunAsync();
    }

    private static MonitorSettings LoadSettings(string[] args)
    {
        string repositoryRoot = GetOption(args, "--repo-root") ?? Directory.GetCurrentDirectory();
        string? settingsPath = GetOption(args, "--config");
        return MonitorSettingsLoader.Load(repositoryRoot, settingsPath);
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
}

[McpServerToolType]
public sealed class AIMonitorTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly MonitorSettings settings;
    private readonly SolutionIndexQueryService queryService;
    private readonly WorkflowEditService workflowService;
    private readonly PlanningService planningService;
    private readonly RoslynEditService roslynEditService;
    private readonly WorkflowEditPaths workflowPaths;
    private readonly AIMonitorMcpRuntimeState runtimeState;
    private readonly IHostApplicationLifetime applicationLifetime;
    private readonly IMonitorLogger logger;

    public AIMonitorTools(
        MonitorSettings settings,
        SolutionIndexQueryService queryService,
        WorkflowEditService workflowService,
        PlanningService planningService,
        RoslynEditService roslynEditService,
        WorkflowEditPaths workflowPaths,
        AIMonitorMcpRuntimeState runtimeState,
        IHostApplicationLifetime applicationLifetime,
        IMonitorLogger logger)
    {
        this.settings = settings;
        this.queryService = queryService;
        this.workflowService = workflowService;
        this.planningService = planningService;
        this.roslynEditService = roslynEditService;
        this.workflowPaths = workflowPaths;
        this.runtimeState = runtimeState;
        this.applicationLifetime = applicationLifetime;
        this.logger = logger;
    }

    [McpServerTool]
    [Description("Return paths and high-level status for the AIMonitor MCP server and watched solution.")]
    public AIMonitorMcpStatus GetMonitorStatus()
    {
        runtimeState.Touch();
        MonitorStatusResult indexStatus = queryService.GetMonitorStatus();
        return new AIMonitorMcpStatus(
            settings.RepositoryRoot,
            settings.RuntimeRoot,
            settings.WatchedSolutionPath,
            settings.WatchedProjectFolder,
            indexStatus.DatabasePath,
            indexStatus.DatabaseExists,
            indexStatus.ProjectCount,
            indexStatus.DocumentCount,
            indexStatus.SymbolCount,
            indexStatus.ReferenceCount,
            indexStatus.CallSiteCount,
            indexStatus.RelationshipCount,
            indexStatus.StaleFileCount,
            indexStatus.DiagnosticCount);
    }

    [McpServerTool]
    [Description("Probe whether the connected MCP client advertises and handles form elicitation. This tool does not mutate monitor or watched-project state.")]
    public async Task<AIMonitorElicitationProbeResult> TestElicitation(
        ModelContextProtocol.Server.McpServer server,
        CancellationToken cancellationToken)
    {
        runtimeState.Touch();
        bool advertisesElicitation = server.ClientCapabilities?.Elicitation is not null;
        bool advertisesFormElicitation = server.ClientCapabilities?.Elicitation?.Form is not null;
        string clientName = server.ClientInfo?.Name ?? string.Empty;
        string clientVersion = server.ClientInfo?.Version ?? string.Empty;

        if (!advertisesElicitation)
        {
            return new AIMonitorElicitationProbeResult(
                clientName,
                clientVersion,
                advertisesElicitation,
                advertisesFormElicitation,
                false,
                "capability-missing",
                false,
                string.Empty,
                "The connected MCP client did not advertise elicitation support.");
        }

        try
        {
            ElicitResult<AIMonitorElicitationProbeInput> response = await server.ElicitAsync<AIMonitorElicitationProbeInput>(
                "AIMonitor elicitation probe: confirm that the connected MCP client can render a small dynamic form.",
                null,
                cancellationToken);
            string contentJson = response.Content is null ? string.Empty : JsonSerializer.Serialize(response.Content, JsonOptions);
            return new AIMonitorElicitationProbeResult(
                clientName,
                clientVersion,
                advertisesElicitation,
                advertisesFormElicitation,
                true,
                response.Action,
                response.IsAccepted,
                contentJson,
                string.Empty);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is McpException)
        {
            return new AIMonitorElicitationProbeResult(
                clientName,
                clientVersion,
                advertisesElicitation,
                advertisesFormElicitation,
                true,
                "error",
                false,
                string.Empty,
                ex.Message);
        }
    }

    [McpServerTool]
    [Description("Return the monitor workflow status, including watched solution, runtime root, Working folder, and configured WinMerge candidates.")]
    public AIMonitorWorkflowStatus GetWorkflowStatus()
    {
        runtimeState.Touch();
        return new AIMonitorWorkflowStatus(
            settings.WatchedSolutionPath,
            settings.WatchedProjectFolder,
            settings.RuntimeRoot,
            workflowPaths.WorkingRoot,
            settings.WinMergeCandidatePaths.FirstOrDefault(File.Exists),
            settings.WinMergeCandidatePaths);
    }

    [McpServerTool]
    [Description("Return the AI-facing Current task context for the watched solution. Human notes are intentionally excluded; use the Plan Board UI for private operator notes.")]
    public CurrentTaskContext GetCurrentTaskContext()
    {
        runtimeState.Touch();
        return planningService.GetCurrentTaskContext();
    }

    [McpServerTool]
    [Description("Append one operator-provided iteration goal line to the Current task. Agents should ask the operator for the next iteration line before calling this tool.")]
    public PlanningIterationAppendResult AppendCurrentTaskIteration(
        [Description("One compact line describing the next iteration goal to append to the Current task.")] string iterationGoal)
    {
        runtimeState.Touch();
        return planningService.AppendIterationGoalToCurrentTask(iterationGoal);
    }

    [McpServerTool]
    [Description("Update an existing Planning iteration goal after explicit operator correction. Agents should not use this for ordinary chat; ask the operator to confirm the replacement line first.")]
    public PlanningIterationUpdateResult UpdateTaskIteration(
        [Description("The Planning iteration id returned by get_current_task_context or append_current_task_iteration.")] string iterationId,
        [Description("One compact replacement line for the iteration goal, confirmed by the operator.")] string iterationGoal)
    {
        runtimeState.Touch();
        return planningService.UpdateIterationGoal(iterationId, iterationGoal);
    }

    [McpServerTool]
    [Description("Return evaluated self-check guardrails for configured roots, working folders, diff tool availability, and watched-source safety boundaries.")]
    public AIMonitorSelfCheckResult GetSelfCheck()
    {
        runtimeState.Touch();
        AIMonitorGuardrailCheck[] guardrails = BuildSelfCheckGuardrails();
        string overallStatus = guardrails.Any(check => check.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)) ? "failed"
            : guardrails.Any(check => check.Status.Equals("warning", StringComparison.OrdinalIgnoreCase)) ? "warning"
            : guardrails.Any(check => check.Status.Equals("unavailable", StringComparison.OrdinalIgnoreCase)) ? "unavailable"
            : "passed";
        return new AIMonitorSelfCheckResult(
            settings.RepositoryRoot,
            settings.RuntimeRoot,
            settings.WatchedSolutionPath,
            settings.WatchedProjectFolder,
            workflowPaths.WorkingRoot,
            workflowPaths.HistoryRoot,
            workflowPaths.StagedRoot,
            File.Exists(settings.WatchedSolutionPath),
            Directory.Exists(settings.WatchedProjectFolder),
            settings.WinMergeCandidatePaths.FirstOrDefault(File.Exists),
            "agents edit monitor-owned Working candidates only; WinMerge review/save remains the watched-source mutation surface",
            overallStatus,
            guardrails);
    }

    [McpServerTool]
    [Description("Rebuild the monitor-owned SQLite index for the watched solution.")]
    public async Task<AIMonitorRefreshIndexResult> RefreshSolutionIndex()
    {
        runtimeState.Touch();
        Stopwatch stopwatch = Stopwatch.StartNew();
        SolutionIndexSummary summary = await new SolutionIndexRebuildService().RebuildAsync(settings);
        stopwatch.Stop();
        return new AIMonitorRefreshIndexResult(summary, queryService.GetMonitorStatus(), stopwatch.ElapsedMilliseconds);
    }

    [McpServerTool]
    [Description("Refresh one watched C# file in the monitor-owned SQLite solution index. AIMonitor currently rebuilds the semantic index and returns the requested file slice.")]
    public async Task<AIMonitorRefreshIndexFileResult> RefreshSolutionIndexFile(
        [Description("Watched C# file path, absolute or relative to the watched solution folder.")] string path)
    {
        runtimeState.Touch();
        AIMonitorRefreshIndexResult refresh = await RefreshSolutionIndex();
        IndexedFileDetailResult detail = queryService.GetFileDetail(path);
        return new AIMonitorRefreshIndexFileResult(
            refresh.Summary,
            refresh.Status,
            refresh.ElapsedMilliseconds,
            detail,
            detail.Files,
            detail.Symbols);
    }

    [McpServerTool]
    [Description("Refresh a watched source file into the monitor-owned Working folder, then refresh the same file in the monitor-owned SQLite solution index.")]
    public async Task<AIMonitorRefreshFileAndIndexResult> RefreshFileAndIndex(
        [Description("Watched source file path, absolute or relative to the watched solution folder.")] string sourceFilePath)
    {
        runtimeState.Touch();
        EditSessionStatus refresh = workflowService.Refresh(ResolveWatchedPath(sourceFilePath));
        AIMonitorRefreshIndexFileResult index = await RefreshSolutionIndexFile(sourceFilePath);
        return new AIMonitorRefreshFileAndIndexResult(refresh, index);
    }

    [McpServerTool]
    [Description("Return status for the monitor-owned watched solution index, including database path and indexed counts.")]
    public MonitorStatusResult GetSolutionIndexStatus()
    {
        runtimeState.Touch();
        return queryService.GetMonitorStatus();
    }

    [McpServerTool]
    [Description("Return the monitor-owned watched solution index as compact JSON with indexed files and symbols. Use maxFiles/maxSymbols to budget the payload.")]
    public SolutionIndexQueryResult GetSolutionIndex(
        [Description("Maximum files to return.")] int maxFiles = 5000,
        [Description("Maximum symbols to return.")] int maxSymbols = 50000)
    {
        runtimeState.Touch();
        return queryService.QueryIndex(maxFiles: maxFiles, maxSymbols: maxSymbols);
    }

    [McpServerTool]
    [Description("Return the monitor-owned watched solution index tree as compact JSON: projects, namespaces, and files.")]
    public AIMonitorSolutionIndexTree GetSolutionIndexTree()
    {
        runtimeState.Touch();
        IReadOnlyList<IndexedProjectRow> projects = queryService.ListProjects();
        IReadOnlyList<IndexedDocumentRow> documents = queryService.ListDocuments();
        IReadOnlyList<AIMonitorNamespaceTree> namespaces = queryService.ListSymbols()
            .GroupBy(symbol => symbol.Namespace)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new AIMonitorNamespaceTree(
                group.Key,
                group.Select(symbol => symbol.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                group.Count()))
            .ToArray();

        return new AIMonitorSolutionIndexTree(projects, documents, namespaces);
    }

    [McpServerTool]
    [Description("Query the monitor-owned watched solution index by scope. Scopes: solution, namespace, folder, file.")]
    public SolutionIndexQueryResult QuerySolutionIndex(
        [Description("Index scope: solution, namespace, folder, or file.")] string scope = "solution",
        [Description("Namespace text, folder path, or file path for scoped queries. Omit for solution scope.")] string? value = null,
        [Description("Maximum files to return.")] int maxFiles = 200,
        [Description("Maximum symbols to return.")] int maxSymbols = 500)
    {
        runtimeState.Touch();
        return queryService.QueryIndex(scope, value, maxFiles, maxSymbols);
    }

    [McpServerTool]
    [Description("Find indexed C# symbols by name text, optional kind, optional exact namespace, and optional containing type using the monitor-owned watched solution index. Qualified Type.Member text is treated as a containing-type member lookup.")]
    public IndexedSymbolSearchResult FindIndexedSymbols(
        [Description("Symbol name text to search for. Use Type.Member to avoid homonym fanout for members.")] string text,
        [Description("Optional exact symbol kind, such as class, method, property, field, constructor, enum, delegate, interface, struct, or record.")] string? kind = null,
        [Description("Optional exact namespace filter.")] string? namespaceName = null,
        [Description("Optional containing type filter, such as OrderRepository or My.Namespace.OrderRepository.")] string? containingType = null,
        [Description("Maximum symbols to return.")] int maxResults = 100)
    {
        runtimeState.Touch();
        return queryService.FindSymbols(text, kind, namespaceName, containingType, maxResults);
    }

    [McpServerTool]
    [Description("Return one indexed C# symbol by stable symbol key from the monitor-owned watched solution index.")]
    public object? GetIndexedSymbol(
        [Description("Stable symbol key returned by query_solution_index or find_indexed_symbols.")] string stableSymbolKey)
    {
        runtimeState.Touch();
        if (TryCreateIndexedStableSymbolKeyError(stableSymbolKey) is { } error)
        {
            return error;
        }

        return queryService.FindSymbols(string.Empty, maxResults: 50000)
            .Symbols
            .FirstOrDefault(symbol => symbol.Symbol.StableKey.Equals(stableSymbolKey, StringComparison.Ordinal));
    }

    [McpServerTool]
    [Description("Return persisted indexed reference sites for one stable C# symbol key. Lean shape omits repeated project path and file hash fields; rich shape returns complete stored rows.")]
    public object FindIndexedReferences(
        [Description("Stable symbol key returned by query_solution_index, find_indexed_symbols, or get_indexed_symbol.")] string stableSymbolKey,
        [Description("Maximum reference rows to return.")] int maxResults = 500,
        [Description("Response shape: lean or rich. Lean is optimized for MCP token cost; rich preserves every persisted reference row field.")] string responseShape = "lean")
    {
        runtimeState.Touch();
        if (TryCreateIndexedStableSymbolKeyError(stableSymbolKey) is { } error)
        {
            return error;
        }

        IndexedReferenceRow[] references = queryService.ListReferences(stableSymbolKey).Take(maxResults).ToArray();
        if (responseShape.Equals("rich", StringComparison.OrdinalIgnoreCase))
        {
            return references;
        }

        return references.Select(ToMcpReferenceRow).ToArray();
    }

    [McpServerTool]
    [Description("Return persisted indexed invocation/object-creation call sites for one stable C# method or constructor symbol key, including caller identity.")]
    public object FindIndexedCallers(
        [Description("Stable method or constructor symbol key returned by query_solution_index, find_indexed_symbols, or get_indexed_symbol.")] string stableSymbolKey,
        [Description("Maximum caller rows to return.")] int maxResults = 500)
    {
        runtimeState.Touch();
        if (TryCreateIndexedStableSymbolKeyError(stableSymbolKey) is { } error)
        {
            return error;
        }

        return queryService.ListCallSites(stableKey: stableSymbolKey)
            .Take(maxResults)
            .ToArray();
    }

    [McpServerTool]
    [Description("Return persisted indexed symbol relationship rows for one stable symbol key, including incoming and outgoing relationship direction.")]
    public object FindIndexedRelationships(
        [Description("Stable symbol key returned by query_solution_index, find_indexed_symbols, or get_indexed_symbol.")] string stableSymbolKey,
        [Description("Optional exact relationship kind filter.")] string? relationshipKind = null,
        [Description("Relationship direction: outgoing, incoming, or both.")] string direction = "both",
        [Description("Maximum relationship rows to return.")] int maxResults = 500)
    {
        runtimeState.Touch();
        if (TryCreateIndexedStableSymbolKeyError(stableSymbolKey) is { } error)
        {
            return error;
        }

        return queryService.ListRelationships(stableSymbolKey, direction, relationshipKind)
            .Take(maxResults)
            .ToArray();
    }

    [McpServerTool]
    [Description("Create a durable monitor session handle under monitor-owned runtime storage.")]
    public AIMonitorSessionState StartMonitorSession(
        [Description("Short purpose for this monitor session.")] string purpose = "monitor workflow")
    {
        runtimeState.Touch();
        AIMonitorSessionState session = new(
            $"session-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}"[..48],
            purpose,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            []);
        SaveSession(session);
        return session;
    }

    [McpServerTool]
    [Description("Set the planned task/iteration file list for a monitor session before watched-source edits begin.")]
    public AIMonitorSessionState SetMonitorSessionPlan(
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Current Planning task id from get_current_task_context.")] string taskId,
        [Description("Current Planning iteration id from get_current_task_context.")] string iterationId,
        [Description("Ordered planned files for this edit session. Each file needs a watched path, owning MSBuild project, role, and reason.")] IReadOnlyList<AIMonitorSessionPlannedFileInput> filesPlanned)
    {
        runtimeState.Touch();
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new ArgumentException("Task id is required.", nameof(taskId));
        }

        if (string.IsNullOrWhiteSpace(iterationId))
        {
            throw new ArgumentException("Iteration id is required.", nameof(iterationId));
        }

        if (filesPlanned is null || filesPlanned.Count == 0)
        {
            throw new ArgumentException("At least one planned file is required.", nameof(filesPlanned));
        }

        AIMonitorSessionState session = LoadSessionById(sessionId)
            ?? throw new InvalidOperationException($"Monitor session was not found: {sessionId}");
        string now = DateTimeOffset.UtcNow.ToString("O");
        AIMonitorSessionPlannedFile[] plannedFiles = filesPlanned
            .Select((file, index) => CreatePlannedFile(file, index + 1))
            .ToArray();
        AIMonitorSessionState updated = session with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Plan = new AIMonitorSessionPlan
            {
                TaskId = taskId.Trim(),
                IterationId = iterationId.Trim(),
                CreatedAtUtc = string.IsNullOrWhiteSpace(session.Plan?.CreatedAtUtc) ? now : session.Plan.CreatedAtUtc,
                UpdatedAtUtc = now,
                FilesPlanned = plannedFiles
            }
        };
        SaveSession(updated);
        return updated;
    }

    [McpServerTool]
    [Description("List durable monitor session handles known to this MCP server.")]
    public IReadOnlyList<AIMonitorSessionSummary> ListMonitorSessions()
    {
        runtimeState.Touch();
        return Directory.Exists(SessionRoot)
            ? Directory.EnumerateFiles(SessionRoot, "*.json")
                .Select(LoadSession)
                .Where(session => session is not null)
                .Select(session => new AIMonitorSessionSummary(session!.SessionId, session.Purpose, session.CreatedAtUtc, session.UpdatedAtUtc, session.Events.Count))
                .OrderByDescending(session => session.UpdatedAtUtc)
                .ToArray()
            : [];
    }

    [McpServerTool]
    [Description("Return a durable monitor session by explicit sessionId handle.")]
    public AIMonitorSessionState GetMonitorSession(
        [Description("Session handle returned by start_monitor_session.")] string sessionId)
    {
        runtimeState.Touch();
        return LoadSessionById(sessionId)
            ?? throw new InvalidOperationException($"Monitor session was not found: {sessionId}");
    }

    [McpServerTool]
    [Description("Append an event to a durable monitor session.")]
    public AIMonitorSessionState RecordMonitorSessionEvent(
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Short event type, such as user-message, tool-call, tool-result, final-answer, or error.")] string eventType,
        [Description("Human-readable event summary.")] string summary,
        [Description("Optional JSON payload for the event.")] string? payloadJson = null)
    {
        runtimeState.Touch();
        AIMonitorSessionState session = LoadSessionById(sessionId)
            ?? throw new InvalidOperationException($"Monitor session was not found: {sessionId}");
        List<AIMonitorSessionEvent> events = session.Events.ToList();
        events.Add(new AIMonitorSessionEvent(DateTimeOffset.UtcNow, eventType, summary, payloadJson));
        AIMonitorSessionState updated = session with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Events = events
        };
        SaveSession(updated);
        return updated;
    }

    [McpServerTool]
    [Description("List staged edit records owned by a durable monitor session.")]
    public IReadOnlyList<StagedEditRecord> ListSessionStagedRecords(
        [Description("Session handle returned by start_monitor_session.")] string sessionId)
    {
        runtimeState.Touch();
        _ = GetMonitorSession(sessionId);
        return workflowService.ListStagedRecords(sessionId);
    }

    [McpServerTool]
    [Description("Refresh a watched source file into the monitor-owned Working folder and clear candidate state for that file.")]
    public EditSessionStatus RefreshFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath)
    {
        runtimeState.Touch();
        return workflowService.Refresh(ResolveWatchedPath(sourceFilePath));
    }

    [McpServerTool]
    [Description("Create a new-file edit session with an empty monitor-owned Working candidate. Watched source is not created.")]
    public EditSessionStatus NewFile(
        [Description("Future watched source path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null)
    {
        runtimeState.Touch();
        EditSessionStatus status = workflowService.NewFile(ResolveWatchedPath(sourceFilePath));
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "new-file", status.WatchedFilePath, JsonSerializer.Serialize(status, JsonOptions));
        }

        return status;
    }

    [McpServerTool]
    [Description("Read a watched source file through the Monitor MCP server.")]
    public AIMonitorFileReadResult GetFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Optional session handle. When supplied, records that the file was fetched.")] string? sessionId = null)
    {
        runtimeState.Touch();
        string path = ResolveWatchedPath(sourceFilePath);
        string text = File.ReadAllText(path);
        AIMonitorFileHashInfo hashInfo = GetFileHashInfo(path);
        AIMonitorSessionFileAccess? access = null;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            access = RecordSessionFileAccess(sessionId, path, "read", hashInfo);
            RecordMonitorSessionEvent(sessionId, "file-fetch", path, JsonSerializer.Serialize(hashInfo, JsonOptions));
        }

        return new AIMonitorFileReadResult(path, workflowPaths.GetRelativeWatchedPath(path), hashInfo, access, text);
    }

    [McpServerTool]
    [Description("Check whether a watched source file has changed since it was last fetched in a durable monitor session.")]
    public AIMonitorFileHashCheckResult CheckFileHash(
        [Description("Session handle returned by start_monitor_session.")] string sessionId,
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath)
    {
        runtimeState.Touch();
        AIMonitorSessionState session = GetMonitorSession(sessionId);
        string path = ResolveWatchedPath(sourceFilePath);
        AIMonitorFileHashInfo current = GetFileHashInfo(path);
        AIMonitorSessionFileAccess? access = session.Files
            .Where(item => item.SourceFilePath.Equals(path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.LastAccessedAtUtc)
            .FirstOrDefault();
        AIMonitorFileHashInfo? previous = access?.Hash;

        return new AIMonitorFileHashCheckResult(
            path,
            previous is not null,
            previous?.Sha256.Equals(current.Sha256, StringComparison.OrdinalIgnoreCase) == false,
            current,
            previous,
            access);
    }

    [McpServerTool]
    [Description("Find source or related files under the watched project folder by filename or wildcard pattern.")]
    public IReadOnlyList<AIMonitorFileMatch> FindFile(
        [Description("Filename or wildcard pattern, such as Program.cs or *.razor.")] string fileNameOrPattern,
        [Description("Maximum number of matches to return.")] int maxResults = 25)
    {
        runtimeState.Touch();
        string pattern = string.IsNullOrWhiteSpace(fileNameOrPattern) ? "*" : fileNameOrPattern;
        return Directory.Exists(settings.WatchedProjectFolder)
            ? Directory.EnumerateFiles(settings.WatchedProjectFolder, pattern, SearchOption.AllDirectories)
                .Where(path => !IsUnderBuildOrHiddenDirectory(path))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(path => new AIMonitorFileMatch(Path.GetFileName(path), path, workflowPaths.GetRelativeWatchedPath(path)))
                .ToArray()
            : [];
    }

    [McpServerTool]
    [Description("Return a Roslyn-derived outline for a watched C# source file, including kind, name, span, signature, namespace, and containing type.")]
    public RoslynFileOutlineResult GetFileOutline(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        return roslynEditService.GetFileOutline(fullPath);
    }

    [McpServerTool]
    [Description("Return a Roslyn-derived source map for a C# file, folder, namespace, or watched project. Use selector mode before C# symbol edits.")]
    public object GetSourceMap(
        [Description("Optional source file/folder path, or namespace text when scope is namespace.")] string? path = null,
        [Description("Source map scope: auto, file, folder, namespace, or project.")] string scope = "auto",
        [Description("Source map density: auto, navigation, selector, detail, or full.")] string mode = "auto",
        [Description("Optional namespace text when scope is namespace.")] string? namespaceName = null,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null)
    {
        runtimeState.Touch();
        RoslynSourceMapResult result;
        try
        {
            result = roslynEditService.GetSourceMap(path, scope, mode, namespaceName);
        }
        catch (InvalidOperationException ex) when (IsRecoverableRoslynGuidanceError(ex))
        {
            return new AIMonitorToolErrorResult(true, ex.Message, "Use .cs/.razor.cs for Roslyn symbol tools or text/file workflow tools for markup.", path);
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "get-source-map", path ?? settings.WatchedProjectFolder, JsonSerializer.Serialize(result, JsonOptions));
        }

        return result;
    }

    [McpServerTool]
    [Description("Read one C# symbol body from the monitor-owned Working candidate using a Roslyn selector.")]
    public object GetSymbol(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Compatibility shortcut symbol name.")] string? symbolName = null,
        [Description("Structured selector JSON from get_source_map when available.")] string? symbolSelectorJson = null,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null)
    {
        runtimeState.Touch();
        string selector = !string.IsNullOrWhiteSpace(symbolSelectorJson)
            ? symbolSelectorJson
            : JsonSerializer.Serialize(new RoslynSymbolSelector(Name: symbolName), JsonOptions);
        RoslynSymbolReadResult result;
        try
        {
            result = roslynEditService.GetSymbol(ResolveWatchedPath(path), selector);
        }
        catch (InvalidOperationException ex) when (IsRecoverableRoslynGuidanceError(ex))
        {
            return new AIMonitorToolErrorResult(true, ex.Message, "Use .cs/.razor.cs for Roslyn symbol tools or text/file workflow tools for markup.", path);
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "get-symbol", result.WatchedFilePath, JsonSerializer.Serialize(result, JsonOptions));
        }

        return result;
    }

    [McpServerTool]
    [Description("Return edit workflow status for one watched source file.")]
    public EditSessionStatus GetEditStatus(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null)
    {
        runtimeState.Touch();
        EditSessionStatus status = workflowService.GetStatus(ResolveWatchedPath(sourceFilePath));
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "get-edit-status", status.WatchedFilePath, JsonSerializer.Serialize(status, JsonOptions));
        }

        return status;
    }

    [McpServerTool]
    [Description("Write a full-file candidate into the monitor-owned Working mirror. Does not create a staged record.")]
    public EditSessionStatus SubmitFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Complete replacement file content.")] string content,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EditSessionStatus status = workflowService.SubmitFile(fullPath, content, manifestJson);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "submit-file", fullPath, manifestJson);
        }

        return status;
    }

    [McpServerTool]
    [Description("Replace exact oldText in the monitor-owned Working mirror candidate.")]
    public ReplaceTextResult ReplaceTextInFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Exact old text to replace using ordinal matching.")] string oldText,
        [Description("Replacement text.")] string newText,
        [Description("Required number of matches in the current edit base. Leave -1 for unique replacement when occurrenceIndex is unset, or no total-match assertion when occurrenceIndex is set.")] int expectedMatches = -1,
        [Description("Optional 0-based occurrence index. Leave -1 for unique/global replacement; set 0 or greater to replace one occurrence without requiring unique oldText.")] int occurrenceIndex = -1,
        [Description("Optional SHA-256 hash of the current Working candidate.")] string? expectedFileHash = null,
        [Description("Optional SHA-256 hash of oldText.")] string? expectedOldTextHash = null,
        [Description("Optional durable session handle.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null)
    {
        runtimeState.Touch();
        if (!string.IsNullOrWhiteSpace(expectedOldTextHash)
            && !ComputeHash(oldText).Equals(expectedOldTextHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("oldText hash did not match expectedOldTextHash.");
        }

        int? expectedMatchCount = expectedMatches >= 0
            ? expectedMatches
            : occurrenceIndex >= 0 ? null : 1;

        ReplaceTextResult result = workflowService.ReplaceText(
            ResolveWatchedPath(path),
            oldText,
            newText,
            expectedMatchCount,
            expectedFileHash,
            occurrenceIndex >= 0 ? occurrenceIndex : null,
            manifestJson);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "replace-text-in-file", result.WatchedFilePath, JsonSerializer.Serialize(result, JsonOptions));
        }

        return result;
    }

    [McpServerTool]
    [Description("Find exact text in the current Working candidate and return 1-based line/column bounds.")]
    public TextSpanResult FindTextSpan(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Exact text to find using ordinal matching.")] string findText,
        [Description("0-based occurrence index when text appears multiple times.")] int occurrenceIndex = 0,
        [Description("Optional SHA-256 hash of the current Working candidate.")] string? expectedFileHash = null,
        [Description("Optional durable session handle.")] string? sessionId = null)
    {
        runtimeState.Touch();
        _ = sessionId;
        string fullPath = ResolveWatchedPath(path);
        EnsureSession(fullPath);
        return workflowService.FindTextSpan(fullPath, findText, occurrenceIndex, expectedFileHash);
    }

    [McpServerTool]
    [Description("Replace an exact 1-based line/column span in the monitor-owned Working mirror candidate.")]
    public EditSessionStatus ReplaceSpanInFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("1-based start line.")] int startLine,
        [Description("1-based start column.")] int startColumn,
        [Description("1-based exclusive end line.")] int endLine,
        [Description("1-based exclusive end column.")] int endColumn,
        [Description("Replacement text.")] string newText,
        [Description("Optional SHA-256 hash of the current Working candidate.")] string? expectedFileHash = null,
        [Description("Optional SHA-256 hash of the extracted old span text.")] string? expectedOldTextHash = null,
        [Description("Optional exact old span text.")] string? expectedOldText = null,
        [Description("Optional durable session handle.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        EnsureSession(fullPath);
        EditSessionStatus status = workflowService.ReplaceSpan(
            fullPath,
            startLine,
            startColumn,
            endLine,
            endColumn,
            newText,
            expectedFileHash,
            expectedOldTextHash,
            expectedOldText,
            manifestJson);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "replace-span-in-file", status.WatchedFilePath, null);
        }

        return status;
    }

    [McpServerTool]
    [Description("Stage the current Working mirror candidate for review. This creates one immutable staged record from the completed candidate.")]
    public AIMonitorStageCandidateResult StageCandidateForReview(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path,
        [Description("Optional compact ledger summary.")] string? ledgerSummary = null,
        [Description("Optional durable session handle.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null,
        [Description("Return the full staged record inline for debugging. Defaults to compact response.")] bool verbose = false)
    {
        runtimeState.Touch();
        StagedEditRecord record = workflowService.Stage(ResolveWatchedPath(path), ledgerSummary, sessionId);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            MarkSessionPlannedFileStaged(sessionId, record);
            RecordMonitorSessionEvent(sessionId, "stage-candidate-for-review", record.StagedRecordId, JsonSerializer.Serialize(record, JsonOptions));
        }

        StagedEditSummary summary = workflowService.CreateSummary(record);
        return new AIMonitorStageCandidateResult(
            summary.StagedRecordId,
            summary.StagedHash,
            summary.Status,
            summary.Classification,
            summary.RecordPath,
            summary,
            verbose ? record : null,
            "Candidate staged. Use get_staged_record for full details or launch_staged_diff for review.");
    }

    [McpServerTool]
    [Description("Replace one C# symbol in the monitor-owned Working candidate using a Roslyn selector.")]
    public RoslynEditResult SubmitSymbol(string path, string symbolSelectorJson, string code, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        RoslynEditResult result = roslynEditService.SubmitSymbol(ResolveWatchedPath(path), symbolSelectorJson, code, manifestJson);
        RecordRoslynSessionEvent(sessionId, "submit-symbol", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a using directive to the monitor-owned Working candidate.")]
    public RoslynEditResult AddUsing(string path, string @namespace, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        RoslynEditResult result = roslynEditService.AddUsing(ResolveWatchedPath(path), @namespace, manifestJson);
        RecordRoslynSessionEvent(sessionId, "add-using", result);
        return result;
    }

    [McpServerTool]
    [Description("Remove a using directive from the monitor-owned Working candidate.")]
    public RoslynEditResult RemoveUsing(string path, string @namespace, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        RoslynEditResult result = roslynEditService.RemoveUsing(ResolveWatchedPath(path), @namespace, manifestJson);
        RecordRoslynSessionEvent(sessionId, "remove-using", result);
        return result;
    }

    [McpServerTool]
    [Description("Add or remove the partial modifier on a C# type in the monitor-owned Working candidate.")]
    public RoslynEditResult SetTypePartial(string path, string containingType, bool isPartial, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        RoslynEditResult result = roslynEditService.SetTypePartial(ResolveWatchedPath(path), containingType, isPartial, manifestJson);
        RecordRoslynSessionEvent(sessionId, "set-type-partial", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# member or nested type to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddSymbol(string path, string containingType, string symbolType, string code, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        RoslynEditResult result = roslynEditService.AddSymbol(ResolveWatchedPath(path), containingType, symbolType, code, afterSymbol, manifestJson);
        RecordRoslynSessionEvent(sessionId, "add-symbol", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# field to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddField(string path, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        RoslynEditResult result = roslynEditService.AddField(ResolveWatchedPath(path), containingType, declaration, afterSymbol, manifestJson);
        RecordRoslynSessionEvent(sessionId, "add-field", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# property to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddProperty(string path, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        RoslynEditResult result = roslynEditService.AddProperty(ResolveWatchedPath(path), containingType, declaration, afterSymbol, manifestJson);
        RecordRoslynSessionEvent(sessionId, "add-property", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# method to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddMethod(string path, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        RoslynEditResult result = roslynEditService.AddMethod(ResolveWatchedPath(path), containingType, declaration, afterSymbol, manifestJson);
        RecordRoslynSessionEvent(sessionId, "add-method", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# constructor to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddConstructor(string path, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        RoslynEditResult result = roslynEditService.AddConstructor(ResolveWatchedPath(path), containingType, declaration, afterSymbol, manifestJson);
        RecordRoslynSessionEvent(sessionId, "add-constructor", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# nested type to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddNestedType(string path, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        RoslynEditResult result = roslynEditService.AddNestedType(ResolveWatchedPath(path), containingType, declaration, afterSymbol, manifestJson);
        RecordRoslynSessionEvent(sessionId, "add-nested-type", result);
        return result;
    }

    [McpServerTool]
    [Description("Remove one C# symbol from the monitor-owned Working candidate using a Roslyn selector.")]
    public RoslynEditResult RemoveSymbol(string path, string symbolSelectorJson, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        RoslynEditResult result = roslynEditService.RemoveSymbol(ResolveWatchedPath(path), symbolSelectorJson, manifestJson);
        RecordRoslynSessionEvent(sessionId, "remove-symbol", result);
        return result;
    }

    [McpServerTool]
    [Description("Classify a completed WinMerge review for a staged edit. Accepted decisions require the expected staged hash.")]
    public async Task<ReviewDecisionWithIndexRefreshResult> RecordDiffDecision(
        ModelContextProtocol.Server.McpServer server,
        [Description("Staged edit record id returned by stage_candidate_for_review.")] string stagedRecordId,
        [Description("Operator-reported outcome: accepted or rejected.")] string decision,
        [Description("Expected staged hash for accepted decisions.")] string? expectedStagedHash = null,
        [Description("Return the full staged record inline for debugging. Defaults to compact response.")] bool verbose = false,
        CancellationToken cancellationToken = default)
    {
        runtimeState.Touch();
        try
        {
            PostAcceptIndexRefreshPlan? indexRefreshPlan = CreateIndexRefreshPlanForStagedRecord(stagedRecordId);
            ReviewDecisionWithIndexRefreshResult result = new StagedDecisionWorkflow().Record(
                settings,
                logger,
                workflowService,
                stagedRecordId,
                decision,
                expectedStagedHash,
                "AIMonitor.McpServer",
                verbose,
                indexRefreshPlan);
            result.PostAcceptPlanning = await TryRunPostAcceptPlanningAsync(server, result, cancellationToken);
            result.SessionProgress = UpdateSessionProgressForDecision(result);
            if (result.PostAcceptPlanning?.Pending == true)
            {
                result.NextStep = result.NextStep + " Post-accept Planning is pending; call resolve_post_accept_planning_decision before mutating Planning.";
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.Write(
                MonitorLogLevel.Error,
                "AIMonitor.McpServer",
                "adapter.mcp.record-diff-decision.failed",
                ex.ToString(),
                new Dictionary<string, string>
                {
                    ["stagedRecordId"] = stagedRecordId,
                    ["decision"] = decision,
                    ["hasExpectedStagedHash"] = string.IsNullOrWhiteSpace(expectedStagedHash) ? "false" : "true"
                });
            throw;
        }
    }

    private PostAcceptIndexRefreshPlan? CreateIndexRefreshPlanForStagedRecord(string stagedRecordId)
    {
        StagedEditRecord record = workflowService.GetStagedRecord(stagedRecordId);
        AIMonitorSessionPlannedFile? plannedFile = FindPlannedFileForRecord(record);
        if (plannedFile is null || string.IsNullOrWhiteSpace(plannedFile.OwningProjectPath))
        {
            return null;
        }

        return new PostAcceptIndexRefreshPlan
        {
            OwningProjectPaths = [plannedFile.OwningProjectPath]
        };
    }

    private PreMergeValidationPlan? CreatePreMergeValidationPlanForStagedRecord(string stagedRecordId)
    {
        StagedEditRecord record = workflowService.GetStagedRecord(stagedRecordId);
        AIMonitorSessionPlannedFile? plannedFile = FindPlannedFileForRecord(record);
        if (plannedFile is null || string.IsNullOrWhiteSpace(plannedFile.OwningProjectPath))
        {
            return null;
        }

        return new PreMergeValidationPlan
        {
            OwningProjectPath = plannedFile.OwningProjectPath
        };
    }

    private AIMonitorSessionPlannedFile? FindPlannedFileForRecord(StagedEditRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.SessionId))
        {
            return null;
        }

        AIMonitorSessionState? session = LoadSessionById(record.SessionId);
        if (session?.Plan is null)
        {
            return null;
        }

        return FindPlannedFile(
            session.Plan.FilesPlanned,
            record.WatchedFilePath,
            record.RelativePath);
    }

    [McpServerTool]
    [Description("Complete a Planning iteration after explicit operator confirmation.")]
    public PlanningIterationCompletionResult CompleteTaskIteration(
        [Description("The Planning iteration id returned by get_current_task_context.")] string iterationId,
        [Description("One compact note explaining why the iteration is complete.")] string completionNote)
    {
        runtimeState.Touch();
        return planningService.CompleteIteration(iterationId, completionNote);
    }

    [McpServerTool]
    [Description("Resolve a pending post-accept Planning decision returned by record_diff_decision when elicitation was unavailable or canceled.")]
    public PostAcceptPlanningDecisionResult ResolvePostAcceptPlanningDecision(
        [Description("Pending decision id returned in record_diff_decision.postAcceptPlanning.pendingDecisionId.")] string pendingDecisionId,
        [Description("Planning action to apply: stop, keep-current-iteration-open, complete-current-iteration, append-next-iteration, replace-current-iteration, pause-task, close-task, or cancel-task.")] string action,
        [Description("One compact goal line for append-next-iteration or replace-current-iteration.")] string? nextIterationGoal = null,
        [Description("Required note for completing an iteration or moving the task to paused, closed, or canceled.")] string? statusNote = null)
    {
        runtimeState.Touch();
        PendingPostAcceptPlanningDecision pending = runtimeState.TakePendingPlanningDecision(pendingDecisionId);
        PostAcceptPlanningActionResult applied = planningService.ApplyPostAcceptPlanningAction(
            action,
            pending.CurrentIterationId,
            nextIterationGoal,
            statusNote);
        return new PostAcceptPlanningDecisionResult
        {
            Required = true,
            ElicitationAttempted = false,
            ElicitationAccepted = false,
            ElicitationAction = "resolved",
            Pending = false,
            PendingDecisionId = pendingDecisionId,
            ResolverTool = "resolve_post_accept_planning_decision",
            AppliedAction = applied,
            Message = applied.Message
        };
    }

    private async Task<PostAcceptPlanningDecisionResult?> TryRunPostAcceptPlanningAsync(
        ModelContextProtocol.Server.McpServer server,
        ReviewDecisionWithIndexRefreshResult decision,
        CancellationToken cancellationToken)
    {
        if (decision.Classification is not ("accepted" or "accepted-normalized"))
        {
            return null;
        }

        CurrentTaskContext context = planningService.GetCurrentTaskContext();
        if (!context.HasCurrentTask)
        {
            return new PostAcceptPlanningDecisionResult
            {
                Required = false,
                Message = "No Current task is selected; post-accept Planning discussion was not requested."
            };
        }

        if (server.ClientCapabilities?.Elicitation is null)
        {
            return CreatePendingPostAcceptPlanningDecision(
                decision.StagedRecordId,
                context,
                false,
                "capability-missing",
                "The connected MCP client did not advertise elicitation. Resolve post-accept Planning explicitly.");
        }

        try
        {
            ElicitResult<PostAcceptPlanningElicitationInput> elicitation = await server.ElicitAsync<PostAcceptPlanningElicitationInput>(
                CreatePostAcceptPlanningPrompt(decision, context),
                null,
                cancellationToken);
            if (!elicitation.IsAccepted || elicitation.Content is null)
            {
                return CreatePendingPostAcceptPlanningDecision(
                    decision.StagedRecordId,
                    context,
                    true,
                    elicitation.Action,
                    "Post-accept Planning elicitation was not accepted. Resolve post-accept Planning explicitly.");
            }

            string action = ToPostAcceptPlanningAction(elicitation.Content);
            PostAcceptPlanningActionResult applied = planningService.ApplyPostAcceptPlanningAction(
                action,
                context.CurrentIteration?.IterationId,
                elicitation.Content.NextIterationGoal,
                elicitation.Content.StatusNote);
            return new PostAcceptPlanningDecisionResult
            {
                Required = true,
                ElicitationAttempted = true,
                ElicitationAccepted = true,
                ElicitationAction = elicitation.Action,
                Pending = false,
                ResolverTool = "resolve_post_accept_planning_decision",
                AppliedAction = applied,
                Message = applied.Message
            };
        }
        catch (Exception ex)
        {
            return CreatePendingPostAcceptPlanningDecision(
                decision.StagedRecordId,
                context,
                true,
                "error",
                "Post-accept Planning elicitation failed: " + ex.Message + " Resolve post-accept Planning explicitly.");
        }
    }

    private PostAcceptPlanningDecisionResult CreatePendingPostAcceptPlanningDecision(
        string stagedRecordId,
        CurrentTaskContext context,
        bool elicitationAttempted,
        string elicitationAction,
        string message)
    {
        PendingPostAcceptPlanningDecision pending = runtimeState.AddPendingPlanningDecision(stagedRecordId, context);
        return new PostAcceptPlanningDecisionResult
        {
            Required = true,
            ElicitationAttempted = elicitationAttempted,
            ElicitationAccepted = false,
            ElicitationAction = elicitationAction,
            Pending = true,
            PendingDecisionId = pending.PendingDecisionId,
            ResolverTool = "resolve_post_accept_planning_decision",
            Message = message
        };
    }

    private static string CreatePostAcceptPlanningPrompt(
        ReviewDecisionWithIndexRefreshResult decision,
        CurrentTaskContext context)
    {
        string iteration = string.IsNullOrWhiteSpace(context.CurrentIterationGoal)
            ? "No current iteration is open."
            : "Current iteration: " + context.CurrentIterationGoal;
        return "AIMonitor post-accept Planning: " + decision.RelativePath + " was " + decision.Classification + ". "
            + iteration + " Choose whether the iteration is complete and the next Planning action.";
    }

    private static string ToPostAcceptPlanningAction(PostAcceptPlanningElicitationInput input)
    {
        if (input.CurrentIterationComplete)
        {
            return "complete-current-iteration";
        }

        if (input.NextAction == PostAcceptPlanningNextAction.KeepCurrentIterationOpen)
        {
            return "keep-current-iteration-open";
        }

        if (input.NextAction == PostAcceptPlanningNextAction.CompleteCurrentIteration)
        {
            return "complete-current-iteration";
        }

        if (input.NextAction == PostAcceptPlanningNextAction.AppendNextIteration)
        {
            return "append-next-iteration";
        }

        if (input.NextAction == PostAcceptPlanningNextAction.ReplaceCurrentIteration)
        {
            return "replace-current-iteration";
        }

        if (input.NextAction == PostAcceptPlanningNextAction.PauseTask)
        {
            return "pause-task";
        }

        if (input.NextAction == PostAcceptPlanningNextAction.CloseTask)
        {
            return "close-task";
        }

        if (input.NextAction == PostAcceptPlanningNextAction.CancelTask)
        {
            return "cancel-task";
        }

        return "stop";
    }

    [McpServerTool]
    [Description("Run pre-merge validation, then launch WinMerge for a staged edit record and return review paths.")]
    public AIMonitorStagedDiffLaunchResult LaunchStagedDiff(
        [Description("Staged edit record id returned by stage_candidate_for_review.")] string stagedRecordId,
        [Description("Explicit diff tool executable path.")] string? diffToolPath = null,
        [Description("Force launch after an explicit human validation override.")] bool forceValidation = false,
        [Description("Return the full staged record inline for debugging. Defaults to compact response.")] bool verbose = false)
    {
        runtimeState.Touch();
        PreMergeValidationPlan? validationPlan = CreatePreMergeValidationPlanForStagedRecord(stagedRecordId);
        StagedDiffLaunchWorkflowResult result = new StagedDiffLaunchWorkflow().Launch(
            settings,
            logger,
            workflowService,
            stagedRecordId,
            "AIMonitor.McpServer",
            diffToolPath,
            forceValidation,
            verbose,
            validationPlan);
        return new AIMonitorStagedDiffLaunchResult(
            result.StagedRecordSummary,
            result.StagedRecord,
            result.PreMergeValidation,
            result.DiffLaunch,
            result.NextStep);
    }

    [McpServerTool]
    [Description("Return the full persisted staged edit record by id. Use after compact stage/launch/decision replies when debug detail is needed.")]
    public StagedEditRecord GetStagedRecord(
        [Description("Staged edit record id returned by stage_candidate_for_review.")] string stagedRecordId)
    {
        runtimeState.Touch();
        return workflowService.GetStagedRecord(stagedRecordId);
    }

    [McpServerTool]
    [Description("Create a proposed compare snapshot for a monitor Working file and return review paths.")]
    public CompareSnapshotResult CompareFile(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string sourceFilePath,
        [Description("Optional compact ledger summary to append to the monitor-owned ledger.")] string? ledgerSummary = null,
        [Description("Refresh from source first if the Working copy is missing.")] bool refreshIfMissing = true,
        [Description("Optional durable session handle for ownership/telemetry.")] string? sessionId = null)
    {
        runtimeState.Touch();
        string path = ResolveWatchedPath(sourceFilePath);
        if (refreshIfMissing && !workflowService.GetStatus(path).WorkingFileExists)
        {
            workflowService.Refresh(path);
        }

        CompareSnapshotResult result = workflowService.Compare(path, ledgerSummary);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "compare-file", result.WorkingFilePath, JsonSerializer.Serialize(result, JsonOptions));
        }

        return result;
    }

    [McpServerTool]
    [Description("List monitor run/history entries recorded under monitor-owned workflow history.")]
    public IReadOnlyList<Dictionary<string, object?>> ListMonitorRuns(
        [Description("Maximum entries to return.")] int maxEntries = 100)
    {
        runtimeState.Touch();
        string path = Path.Combine(workflowPaths.HistoryRoot, "_runs.json");
        if (!File.Exists(path))
        {
            return [];
        }

        IReadOnlyList<Dictionary<string, object?>> entries = JsonSerializer.Deserialize<IReadOnlyList<Dictionary<string, object?>>>(File.ReadAllText(path), JsonOptions) ?? [];
        return entries.TakeLast(maxEntries).ToArray();
    }

    [McpServerTool]
    [Description("Return recorded entries for one monitor run id.")]
    public IReadOnlyList<Dictionary<string, object?>> GetMonitorRun(
        [Description("Run id from list_monitor_runs.")] string runId)
    {
        runtimeState.Touch();
        return ListMonitorRuns(500)
            .Where(entry => entry.TryGetValue("runId", out object? value) && string.Equals(value?.ToString(), runId, StringComparison.Ordinal))
            .ToArray();
    }

    [McpServerTool]
    [Description("List monitor-owned per-file ledgers.")]
    public IReadOnlyList<AIMonitorLedgerInfo> ListLedgers(
        [Description("Maximum ledgers to return.")] int maxEntries = 100)
    {
        runtimeState.Touch();
        string root = Path.Combine(workflowPaths.HistoryRoot, "Ledgers");
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.md")
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Take(maxEntries)
                .Select(info => new AIMonitorLedgerInfo(info.FullName, info.Length, info.LastWriteTimeUtc))
                .ToArray()
            : [];
    }

    [McpServerTool]
    [Description("Read one monitor-owned per-file ledger by source file or ledger path.")]
    public AIMonitorLedgerReadResult GetLedger(
        [Description("Optional source file path, absolute or relative to the watched solution folder.")] string? sourceFilePath = null,
        [Description("Optional absolute ledger path under the ledger root.")] string? ledgerPath = null)
    {
        runtimeState.Touch();
        string root = Path.GetFullPath(Path.Combine(workflowPaths.HistoryRoot, "Ledgers"));
        string path = !string.IsNullOrWhiteSpace(ledgerPath)
            ? Path.GetFullPath(ledgerPath)
            : Path.Combine(root, $"{Sanitize(workflowPaths.GetRelativeWatchedPath(ResolveWatchedPath(sourceFilePath ?? throw new InvalidOperationException("sourceFilePath or ledgerPath is required."))).Replace(Path.DirectorySeparatorChar, '_'))}.md");
        string relativeLedgerPath = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relativeLedgerPath)
            || relativeLedgerPath.Equals("..", StringComparison.Ordinal)
            || relativeLedgerPath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativeLedgerPath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ledger path must be under monitor-owned ledger storage.");
        }

        return new AIMonitorLedgerReadResult(path, File.Exists(path), File.Exists(path) ? File.ReadAllText(path) : string.Empty);
    }

    [McpServerTool]
    [Description("Archive/prune monitor-owned history. AIMonitor keeps history by default; this compatibility tool reports the current retention posture without deleting files.")]
    public AIMonitorCompatibilityResult PruneMonitorHistory(
        [Description("Retention window in days.")] int retentionDays = 7)
    {
        runtimeState.Touch();
        return new AIMonitorCompatibilityResult(
            "not-pruned",
            "AIMonitor currently keeps workflow history until an explicit UI/operator cleanup flow is implemented.",
            new Dictionary<string, string?> { ["retentionDays"] = retentionDays.ToString() });
    }

    [McpServerTool]
    [Description("Return the Markdown tool manifest for the AIMonitor MCP Server tool surface.")]
    public string GetToolManifest()
    {
        runtimeState.Touch();
        return ComposeToolManifest();
    }

    [McpServerTool]
    [Description("Return the normal staging guide for AIMonitor watched-project edits.")]
    public string GetStagingGuide()
    {
        runtimeState.Touch();
        return ComposeStagingGuide();
    }

    [McpServerTool]
    [Description("Return the smoke-test coverage todo/catalog for AIMonitor.")]
    public string GetSmokeTestCatalog()
    {
        runtimeState.Touch();
        string path = Path.Combine(settings.RepositoryRoot, "docs", "findings", "SmokeCoverageTodo.md");
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "AIMonitor smoke coverage catalog is missing.";
    }

    [McpServerTool]
    [Description("List watched project folders. AIMonitor currently has one configured watched project folder.")]
    public IReadOnlyList<AIMonitorWatchedProjectInfo> ListWatchedProjects()
    {
        runtimeState.Touch();
        return
        [
            new AIMonitorWatchedProjectInfo(
                Path.GetFileName(settings.WatchedProjectFolder),
                settings.WatchedProjectFolder,
                File.Exists(settings.WatchedSolutionPath) ? [settings.WatchedSolutionPath] : [])
        ];
    }

    [McpServerTool]
    [Description("Request graceful shutdown of this AIMonitor MCP server process.")]
    public AIMonitorServerShutdownResult ShutdownServer(
        [Description("Optional operator/client reason for the shutdown request.")] string? reason = null)
    {
        runtimeState.RequestShutdown(reason);
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            applicationLifetime.StopApplication();
        });
        return new AIMonitorServerShutdownResult(Environment.ProcessId, DateTimeOffset.UtcNow, string.IsNullOrWhiteSpace(reason) ? "shutdown_server requested" : reason);
    }

    private string ComposeToolManifest()
    {
        StringBuilder builder = new();
        builder.AppendLine("# AIMonitor MCP Tool Manifest");
        builder.AppendLine();
        builder.AppendLine("This manifest is generated from the currently loaded AIMonitor MCP tool methods.");
        builder.AppendLine();
        foreach (MethodInfo method in typeof(AIMonitorTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .OrderBy(method => ToToolName(method.Name), StringComparer.Ordinal))
        {
            string toolName = ToToolName(method.Name);
            string description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "No description.";
            builder.AppendLine($"## `{toolName}`");
            builder.AppendLine();
            builder.AppendLine(description);
            builder.AppendLine();
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length > 0)
            {
                builder.AppendLine("Parameters:");
                foreach (ParameterInfo parameter in parameters)
                {
                    string parameterDescription = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
                    string nullable = IsNullableParameter(parameter) ? "optional" : "required";
                    builder.AppendLine($"- `{parameter.Name}` ({parameter.ParameterType.Name}, {nullable}): {parameterDescription}");
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine("## Safety Notes");
        builder.AppendLine();
        builder.AppendLine("- Watched source is not edited directly by agents.");
        builder.AppendLine("- Existing files enter through `refresh_file`; future files enter through `new_file`.");
        builder.AppendLine("- Candidate edits happen in monitor-owned Working files.");
        builder.AppendLine("- Review uses `stage_candidate_for_review`, `launch_staged_diff`, WinMerge review/save, and `record_diff_decision`.");
        builder.AppendLine("- `launch_staged_diff` runs pre-merge validation; failed validation requires host/operator approval before WinMerge opens.");
        builder.AppendLine("- Accepted decisions trigger index refresh metadata; refresh before editing the same watched file again.");
        return builder.ToString();
    }

    private string ComposeStagingGuide()
    {
        StringBuilder builder = new();
        builder.AppendLine("# AIMonitor Staging Guide");
        builder.AppendLine();
        builder.AppendLine("Use this sequence for watched-project edits through MCP.");
        builder.AppendLine();
        builder.AppendLine("1. Check `get_self_check`, `get_workflow_status`, and `get_monitor_status` when starting a session.");
        builder.AppendLine("2. For existing files, call `refresh_file`. For future watched files, call `new_file`.");
        builder.AppendLine("3. Edit only the monitor-owned Working candidate with `submit_file`, text/span tools, or Roslyn typed edit tools.");
        builder.AppendLine("4. For C# symbol edits, prefer `get_source_map` in selector mode and `get_symbol` before mutation.");
        builder.AppendLine("5. Stage with `stage_candidate_for_review`.");
        builder.AppendLine("6. Launch review with `launch_staged_diff`; this runs pre-merge validation before WinMerge.");
        builder.AppendLine("7. If validation fails and no host dialog appears, stop and ask the operator before using `forceValidation`.");
        builder.AppendLine("8. The operator reviews/saves in WinMerge. WinMerge is the watched-source mutation surface.");
        builder.AppendLine("9. Record the result with `record_diff_decision`.");
        builder.AppendLine("10. After `accepted` or `accepted-normalized`, inspect `indexRefresh` and call `refresh_file` before editing that watched file again.");
        builder.AppendLine();
        builder.AppendLine("Failure paths:");
        builder.AppendLine();
        builder.AppendLine("- `blocked`, `dirty-unexpected`, `superseded`, missing Working files, and stale hashes require recovery before follow-up edits.");
        builder.AppendLine("- Warnings do not block by themselves; build/compile errors and explicit validation failures do.");
        builder.AppendLine("- Do not manually copy candidates into watched source outside WinMerge/decision classification.");
        return builder.ToString();
    }

    private AIMonitorGuardrailCheck[] BuildSelfCheckGuardrails()
    {
        string repositoryRoot = Path.GetFullPath(settings.RepositoryRoot);
        string runtimeRoot = Path.GetFullPath(settings.RuntimeRoot);
        string watchedProjectFolder = Path.GetFullPath(settings.WatchedProjectFolder);
        string workingRoot = Path.GetFullPath(workflowPaths.WorkingRoot);
        string historyRoot = Path.GetFullPath(workflowPaths.HistoryRoot);
        string stagedRoot = Path.GetFullPath(workflowPaths.StagedRoot);
        List<AIMonitorGuardrailCheck> checks =
        [
            CheckPathExists("repository-root-exists", repositoryRoot, Directory.Exists(repositoryRoot), "Repository root exists.", "Repository root is missing."),
            CheckPathExists("watched-solution-exists", settings.WatchedSolutionPath, File.Exists(settings.WatchedSolutionPath), "Watched solution/project exists.", "Watched solution/project is missing."),
            CheckPathExists("watched-project-folder-exists", watchedProjectFolder, Directory.Exists(watchedProjectFolder), "Watched project folder exists.", "Watched project folder is missing."),
            CheckPathExists("runtime-root-exists", runtimeRoot, Directory.Exists(runtimeRoot), "Runtime root exists.", "Runtime root is missing."),
            CheckPathUnderRoot("working-under-runtime", workingRoot, runtimeRoot, "Working root is under runtime root.", "Working root is outside runtime root."),
            CheckPathUnderRoot("history-under-runtime", historyRoot, runtimeRoot, "History root is under runtime root.", "History root is outside runtime root."),
            CheckPathUnderRoot("staged-under-runtime", stagedRoot, runtimeRoot, "Staged root is under runtime root.", "Staged root is outside runtime root."),
            CheckPathOutsideRoot("runtime-outside-watched-source", runtimeRoot, watchedProjectFolder, "Runtime state is outside watched source.", "Runtime state is inside watched source."),
        ];

        string? diffTool = settings.WinMergeCandidatePaths.FirstOrDefault(File.Exists);
        checks.Add(diffTool is null
            ? new AIMonitorGuardrailCheck("diff-tool-available", "warning", "No configured WinMerge candidate exists on disk.", string.Join(";", settings.WinMergeCandidatePaths))
            : new AIMonitorGuardrailCheck("diff-tool-available", "passed", "Configured WinMerge candidate exists.", diffTool));

        return checks.ToArray();
    }

    private static AIMonitorGuardrailCheck CheckPathExists(string name, string path, bool passed, string passedMessage, string failedMessage)
    {
        return new AIMonitorGuardrailCheck(name, passed ? "passed" : "failed", passed ? passedMessage : failedMessage, path);
    }

    private static AIMonitorGuardrailCheck CheckPathUnderRoot(string name, string path, string root, string passedMessage, string failedMessage)
    {
        bool passed = IsPathUnderRoot(path, root);
        return new AIMonitorGuardrailCheck(name, passed ? "passed" : "failed", passed ? passedMessage : failedMessage, path);
    }

    private static AIMonitorGuardrailCheck CheckPathOutsideRoot(string name, string path, string root, string passedMessage, string failedMessage)
    {
        bool passed = !IsPathUnderRoot(path, root);
        return new AIMonitorGuardrailCheck(name, passed ? "passed" : "failed", passed ? passedMessage : failedMessage, path);
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNullableParameter(ParameterInfo parameter)
    {
        return parameter.HasDefaultValue
            || Nullable.GetUnderlyingType(parameter.ParameterType) is not null
            || !parameter.ParameterType.IsValueType;
    }

    private static string ToToolName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        StringBuilder builder = new(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private string SessionRoot => Path.Combine(MonitorWorkspacePaths.GetWatchedSolutionWorkspaceRoot(settings), "workflow", "sessions");

    private string ResolveWatchedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path is required.", nameof(path));
        }

        string fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(settings.WatchedProjectFolder, path));
        workflowPaths.GetRelativeWatchedPath(fullPath);
        return fullPath;
    }

    private EditSessionStatus EnsureSession(string watchedFilePath)
    {
        return workflowService.EnsureEditableSession(watchedFilePath);
    }

    private void SaveSession(AIMonitorSessionState session)
    {
        Directory.CreateDirectory(SessionRoot);
        File.WriteAllText(GetSessionPath(session.SessionId), JsonSerializer.Serialize(session, JsonOptions));
    }

    private AIMonitorSessionPlannedFile CreatePlannedFile(AIMonitorSessionPlannedFileInput input, int sequence)
    {
        if (string.IsNullOrWhiteSpace(input.Path))
        {
            throw new ArgumentException("Every planned file needs a path.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.OwningProjectPath))
        {
            throw new ArgumentException("Every planned file needs an owning MSBuild project path.", nameof(input));
        }

        string fullPath = ResolveWatchedPath(input.Path);
        return new AIMonitorSessionPlannedFile
        {
            Sequence = sequence,
            Path = input.Path.Trim(),
            FullPath = fullPath,
            RelativePath = workflowPaths.GetRelativeWatchedPath(fullPath),
            OwningProjectPath = ResolveProjectPath(input.OwningProjectPath),
            Role = string.IsNullOrWhiteSpace(input.Role) ? "edit" : input.Role.Trim(),
            Reason = input.Reason.Trim(),
            Status = "planned"
        };
    }

    private string ResolveProjectPath(string projectPath)
    {
        return Path.GetFullPath(Path.IsPathRooted(projectPath)
            ? projectPath
            : Path.Combine(settings.WatchedProjectFolder, projectPath));
    }

    private void MarkSessionPlannedFileStaged(string sessionId, StagedEditRecord record)
    {
        AIMonitorSessionState? session = LoadSessionById(sessionId);
        if (session?.Plan is null)
        {
            return;
        }

        List<AIMonitorSessionPlannedFile> files = ClonePlannedFiles(session.Plan.FilesPlanned);
        AIMonitorSessionPlannedFile? file = FindPlannedFile(files, record.WatchedFilePath, record.RelativePath);
        if (file is null)
        {
            return;
        }

        file.Status = "staged";
        file.StagedRecordId = record.StagedRecordId;
        SaveSession(UpdateSessionPlan(session, files));
    }

    private ReviewDecisionSessionProgress? UpdateSessionProgressForDecision(ReviewDecisionWithIndexRefreshResult decision)
    {
        StagedEditRecord record = decision.StagedRecord ?? workflowService.GetStagedRecord(decision.StagedRecordId);
        if (string.IsNullOrWhiteSpace(record.SessionId))
        {
            return null;
        }

        AIMonitorSessionState? session = LoadSessionById(record.SessionId);
        if (session is null)
        {
            return new ReviewDecisionSessionProgress
            {
                SessionId = record.SessionId,
                HasPlan = false,
                Message = "The staged record has a session id, but the monitor session was not found."
            };
        }

        if (session.Plan is null)
        {
            return new ReviewDecisionSessionProgress
            {
                SessionId = session.SessionId,
                HasPlan = false,
                Message = "The monitor session has no planned file set."
            };
        }

        List<AIMonitorSessionPlannedFile> files = ClonePlannedFiles(session.Plan.FilesPlanned);
        AIMonitorSessionPlannedFile? current = FindPlannedFile(files, decision.WatchedFilePath, decision.RelativePath);
        bool matched = current is not null;
        if (current is not null)
        {
            current.Status = string.IsNullOrWhiteSpace(decision.Classification) ? decision.Decision : decision.Classification;
            current.StagedRecordId = decision.StagedRecordId;
            current.Decision = decision.Decision;
            current.Classification = decision.Classification;
            current.DecidedAtUtc = record.DecisionAtUtc;
            SaveSession(UpdateSessionPlan(session, files));
        }

        int decidedCount = files.Count(file => IsPlannedFileDecided(file));
        int plannedCount = files.Count;
        return new ReviewDecisionSessionProgress
        {
            SessionId = session.SessionId,
            HasPlan = true,
            TaskId = session.Plan.TaskId,
            IterationId = session.Plan.IterationId,
            PlannedFileCount = plannedCount,
            DecidedFileCount = decidedCount,
            CurrentFileSequence = current?.Sequence ?? 0,
            CurrentFileRelativePath = current?.RelativePath ?? decision.RelativePath,
            CurrentFileStatus = current?.Status ?? "not-in-plan",
            CurrentFileMatchedPlan = matched,
            IsSessionComplete = plannedCount > 0 && decidedCount == plannedCount,
            Message = CreateSessionProgressMessage(matched, current, decidedCount, plannedCount)
        };
    }

    private AIMonitorSessionState UpdateSessionPlan(AIMonitorSessionState session, IReadOnlyList<AIMonitorSessionPlannedFile> files)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        return session with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Plan = new AIMonitorSessionPlan
            {
                TaskId = session.Plan?.TaskId ?? string.Empty,
                IterationId = session.Plan?.IterationId ?? string.Empty,
                CreatedAtUtc = session.Plan?.CreatedAtUtc ?? now,
                UpdatedAtUtc = now,
                FilesPlanned = files
            }
        };
    }

    private static List<AIMonitorSessionPlannedFile> ClonePlannedFiles(IReadOnlyList<AIMonitorSessionPlannedFile> files)
    {
        return files
            .Select(file => new AIMonitorSessionPlannedFile
            {
                Sequence = file.Sequence,
                Path = file.Path,
                FullPath = file.FullPath,
                RelativePath = file.RelativePath,
                OwningProjectPath = file.OwningProjectPath,
                Role = file.Role,
                Reason = file.Reason,
                Status = file.Status,
                StagedRecordId = file.StagedRecordId,
                Decision = file.Decision,
                Classification = file.Classification,
                DecidedAtUtc = file.DecidedAtUtc
            })
            .ToList();
    }

    private static AIMonitorSessionPlannedFile? FindPlannedFile(
        IReadOnlyList<AIMonitorSessionPlannedFile> files,
        string watchedFilePath,
        string relativePath)
    {
        return files.FirstOrDefault(file =>
                file.FullPath.Equals(watchedFilePath, StringComparison.OrdinalIgnoreCase))
            ?? files.FirstOrDefault(file =>
                file.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
            ?? files.FirstOrDefault(file =>
                file.Path.Equals(watchedFilePath, StringComparison.OrdinalIgnoreCase)
                || file.Path.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPlannedFileDecided(AIMonitorSessionPlannedFile file)
    {
        return file.Status.Equals("accepted", StringComparison.OrdinalIgnoreCase)
            || file.Status.Equals("accepted-normalized", StringComparison.OrdinalIgnoreCase)
            || file.Status.Equals("rejected", StringComparison.OrdinalIgnoreCase)
            || file.Status.Equals("dirty-unexpected", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(file.Decision);
    }

    private static string CreateSessionProgressMessage(
        bool matched,
        AIMonitorSessionPlannedFile? current,
        int decidedCount,
        int plannedCount)
    {
        if (!matched)
        {
            return "Decision recorded, but the decided file was not listed in the monitor session plan.";
        }

        string currentText = current is null ? "current file" : $"file {current.Sequence} of {plannedCount}";
        return decidedCount == plannedCount
            ? $"Decision recorded for {currentText}. All planned session files are decided."
            : $"Decision recorded for {currentText}. Session has {plannedCount - decidedCount} planned file(s) still undecided.";
    }

    private AIMonitorSessionFileAccess RecordSessionFileAccess(
        string sessionId,
        string sourceFilePath,
        string accessKind,
        AIMonitorFileHashInfo hash)
    {
        AIMonitorSessionState session = LoadSessionById(sessionId)
            ?? throw new InvalidOperationException($"Monitor session was not found: {sessionId}");
        List<AIMonitorSessionFileAccess> files = session.Files.ToList();
        AIMonitorSessionFileAccess? previous = files
            .FirstOrDefault(item => item.SourceFilePath.Equals(sourceFilePath, StringComparison.OrdinalIgnoreCase));
        AIMonitorSessionFileAccess updated = new(
            sessionId,
            sourceFilePath,
            workflowPaths.GetRelativeWatchedPath(sourceFilePath),
            accessKind,
            hash,
            (previous?.FetchCount ?? 0) + 1,
            previous?.FirstAccessedAtUtc ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        if (previous is not null)
        {
            files.Remove(previous);
        }

        files.Add(updated);
        SaveSession(session with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Files = files
        });
        return updated;
    }

    private AIMonitorSessionState? LoadSessionById(string sessionId)
    {
        string path = GetSessionPath(sessionId);
        return File.Exists(path) ? LoadSession(path) : null;
    }

    private AIMonitorSessionState? LoadSession(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<AIMonitorSessionState>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string GetSessionPath(string sessionId)
    {
        return Path.Combine(SessionRoot, $"{Sanitize(sessionId)}.json");
    }

    private void RecordRoslynSessionEvent(string? sessionId, string eventType, RoslynEditResult result)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, eventType, result.WatchedFilePath, JsonSerializer.Serialize(result, JsonOptions));
        }
    }

    private static AIMonitorFileHashInfo GetFileHashInfo(string path)
    {
        FileInfo info = new(path);
        return new AIMonitorFileHashInfo(
            ComputeFileHash(path),
            info.Length,
            info.LastWriteTimeUtc);
    }

    private static AIMonitorToolErrorResult? TryCreateIndexedStableSymbolKeyError(string stableSymbolKey)
    {
        if (string.IsNullOrWhiteSpace(stableSymbolKey))
        {
            return new AIMonitorToolErrorResult(
                true,
                "A stable indexed symbol key is required. Use query_solution_index, find_indexed_symbols, or get_indexed_symbol to obtain a symbol:<hash> key.",
                "symbol:<hash>",
                stableSymbolKey);
        }

        if (stableSymbolKey.StartsWith("symbol:", StringComparison.Ordinal))
        {
            return null;
        }

        if (stableSymbolKey.Contains("::", StringComparison.Ordinal))
        {
            return new AIMonitorToolErrorResult(
                true,
                "This looks like a Roslyn source-map selector key, not an indexed symbol key. find_indexed_references and find_indexed_callers require the symbol:<hash> key returned by query_solution_index, find_indexed_symbols, or get_indexed_symbol.",
                "symbol:<hash>",
                stableSymbolKey);
        }

        return new AIMonitorToolErrorResult(
            true,
            "Indexed reference tools require a stable indexed symbol key in symbol:<hash> form. Use query_solution_index, find_indexed_symbols, or get_indexed_symbol first.",
            "symbol:<hash>",
            stableSymbolKey);
    }

    private static AIMonitorIndexedReferenceResult ToMcpReferenceRow(IndexedReferenceRow reference)
    {
        return new AIMonitorIndexedReferenceResult(
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

    private static bool IsRecoverableRoslynGuidanceError(InvalidOperationException ex)
    {
        return ex.Message.Contains("Razor markup", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("supports C# source files only", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateExpectedHash(string path, string? expectedFileHash)
    {
        if (!string.IsNullOrWhiteSpace(expectedFileHash)
            && !ComputeFileHash(path).Equals(expectedFileHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Working candidate hash did not match expectedFileHash.");
        }
    }

    private static bool IsUnderBuildOrHiddenDirectory(string path)
    {
        string[] parts = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => part.StartsWith(".", StringComparison.Ordinal)
            || part.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || part.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    private static string ComputeFileHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeHash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = new(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "item" : clean;
    }
}

public sealed record AIMonitorMcpStatus(
    string RepositoryRoot,
    string RuntimeRoot,
    string WatchedSolutionPath,
    string WatchedProjectFolder,
    string DatabasePath,
    bool DatabaseExists,
    int ProjectCount,
    int DocumentCount,
    int SymbolCount,
    int ReferenceCount,
    int CallSiteCount,
    int RelationshipCount,
    int StaleFileCount,
    int DiagnosticCount);

public sealed record AIMonitorWorkflowStatus(
    string WatchedSolutionPath,
    string WatchedProjectFolder,
    string RuntimeRoot,
    string WorkingRoot,
    string? ResolvedDiffToolPath,
    IReadOnlyList<string> WinMergeCandidatePaths);

public sealed record AIMonitorToolErrorResult(
    bool IsError,
    string Message,
    string Expected,
    string? Received);

public sealed record AIMonitorIndexedReferenceResult(
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

public sealed record AIMonitorSolutionIndexTree(
    IReadOnlyList<IndexedProjectRow> Projects,
    IReadOnlyList<IndexedDocumentRow> Files,
    IReadOnlyList<AIMonitorNamespaceTree> Namespaces);

public sealed record AIMonitorNamespaceTree(
    string Namespace,
    IReadOnlyList<string> Files,
    int SymbolCount);

public sealed record AIMonitorStageCandidateResult(
    string StagedRecordId,
    string StagedHash,
    string Status,
    string Classification,
    string StagedRecordPath,
    StagedEditSummary StagedRecordSummary,
    StagedEditRecord? StagedRecord,
    string NextStep);

public sealed record AIMonitorStagedDiffLaunchResult(
    StagedEditSummary StagedRecordSummary,
    StagedEditRecord? StagedRecord,
    PreMergeValidationResult PreMergeValidation,
    DiffLaunchResult DiffLaunch,
    string NextStep);

public sealed record AIMonitorSelfCheckResult(
    string RepositoryRoot,
    string RuntimeRoot,
    string WatchedSolutionPath,
    string WatchedProjectFolder,
    string WorkingRoot,
    string HistoryRoot,
    string StagedRoot,
    bool WatchedSolutionExists,
    bool WatchedProjectFolderExists,
    string? ResolvedDiffToolPath,
    string SafetySummary,
    string OverallStatus,
    IReadOnlyList<AIMonitorGuardrailCheck> Guardrails);

public sealed record AIMonitorGuardrailCheck(
    string Name,
    string Status,
    string Message,
    string? Path);

public sealed record AIMonitorRefreshIndexFileResult(
    SolutionIndexSummary Summary,
    MonitorStatusResult Status,
    long ElapsedMilliseconds,
    IndexedFileDetailResult Detail,
    IReadOnlyList<IndexedDocumentRow> Files,
    IReadOnlyList<IndexedSymbolRow> Symbols);

public sealed record AIMonitorRefreshIndexResult(
    SolutionIndexSummary Summary,
    MonitorStatusResult Status,
    long ElapsedMilliseconds);

public sealed record AIMonitorRefreshFileAndIndexResult(
    EditSessionStatus Refresh,
    AIMonitorRefreshIndexFileResult Index);

public sealed record AIMonitorSessionState(
    string SessionId,
    string Purpose,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<AIMonitorSessionEvent> Events)
{
    public IReadOnlyList<AIMonitorSessionFileAccess> Files { get; init; } = [];

    public AIMonitorSessionPlan? Plan { get; init; }
}

public sealed record AIMonitorSessionSummary(
    string SessionId,
    string Purpose,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int EventCount);

public sealed record AIMonitorSessionEvent(
    DateTimeOffset TimestampUtc,
    string EventType,
    string Summary,
    string? PayloadJson);

public sealed record AIMonitorSessionFileAccess(
    string SessionId,
    string SourceFilePath,
    string RelativePath,
    string AccessKind,
    AIMonitorFileHashInfo Hash,
    int FetchCount,
    DateTimeOffset FirstAccessedAtUtc,
    DateTimeOffset LastAccessedAtUtc);

public sealed record AIMonitorFileHashInfo(
    string Sha256,
    long Length,
    DateTime LastWriteTimeUtc);

public sealed record AIMonitorFileReadResult(
    string SourceFilePath,
    string RelativePath,
    AIMonitorFileHashInfo Hash,
    AIMonitorSessionFileAccess? SessionAccess,
    string Content);

public sealed record AIMonitorFileHashCheckResult(
    string SourceFilePath,
    bool KnownInSession,
    bool ChangedSinceFetch,
    AIMonitorFileHashInfo Current,
    AIMonitorFileHashInfo? Previous,
    AIMonitorSessionFileAccess? PreviousAccess);

public sealed record AIMonitorFileMatch(
    string Name,
    string Path,
    string RelativePath);

public sealed record AIMonitorCompatibilityResult(
    string Status,
    string Message,
    IReadOnlyDictionary<string, string?> Arguments);

public sealed record AIMonitorLedgerInfo(
    string Path,
    long Length,
    DateTime LastWriteTimeUtc);

public sealed record AIMonitorLedgerReadResult(
    string Path,
    bool Exists,
    string Content);

public sealed record AIMonitorWatchedProjectInfo(
    string Name,
    string Path,
    IReadOnlyList<string> SolutionFiles);

public sealed record AIMonitorServerShutdownResult(
    int ProcessId,
    DateTimeOffset RequestedAtUtc,
    string Reason);

public sealed record AIMonitorElicitationProbeResult(
    string ClientName,
    string ClientVersion,
    bool AdvertisesElicitation,
    bool AdvertisesFormElicitation,
    bool RequestAttempted,
    string Action,
    bool IsAccepted,
    string ContentJson,
    string Error);

public sealed class AIMonitorElicitationProbeInput
{
    [Description("Whether the client-rendered elicitation form reached the operator.")]
    public bool ContinueProbe { get; set; }

    [Description("A small operator note returned from the MCP client.")]
    public string Note { get; set; } = string.Empty;

    [Description("The next action to take after this proof request.")]
    public AIMonitorElicitationProbeNextAction NextAction { get; set; }
}

public enum AIMonitorElicitationProbeNextAction
{
    Stop,
    ContinuePlanning,
    ContinueImplementation
}

public sealed class AIMonitorMcpRuntimeState
{
    private readonly IMonitorLogger logger;
    private readonly ConcurrentDictionary<string, PendingPostAcceptPlanningDecision> pendingPlanningDecisions = new(StringComparer.Ordinal);
    private long lastActivityTicks = DateTimeOffset.UtcNow.UtcTicks;
    private int shutdownRequested;

    public AIMonitorMcpRuntimeState(IMonitorLogger logger)
    {
        this.logger = logger;
    }

    public DateTimeOffset LastActivityUtc => new(Interlocked.Read(ref lastActivityTicks), TimeSpan.Zero);

    public bool ShutdownRequested => Volatile.Read(ref shutdownRequested) == 1;

    public PendingPostAcceptPlanningDecision AddPendingPlanningDecision(string stagedRecordId, CurrentTaskContext context)
    {
        PendingPostAcceptPlanningDecision pending = new PendingPostAcceptPlanningDecision
        {
            PendingDecisionId = "planning-decision-" + Guid.NewGuid().ToString("N"),
            StagedRecordId = stagedRecordId,
            TaskId = context.TaskId,
            TaskTitle = context.Title,
            CurrentIterationId = context.CurrentIteration?.IterationId ?? string.Empty,
            CurrentIterationGoal = context.CurrentIterationGoal,
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        pendingPlanningDecisions[pending.PendingDecisionId] = pending;
        return pending;
    }

    public PendingPostAcceptPlanningDecision TakePendingPlanningDecision(string pendingDecisionId)
    {
        if (string.IsNullOrWhiteSpace(pendingDecisionId))
        {
            throw new ArgumentException("Pending decision id is required.", nameof(pendingDecisionId));
        }

        if (pendingPlanningDecisions.TryRemove(pendingDecisionId, out PendingPostAcceptPlanningDecision? pending))
        {
            return pending;
        }

        throw new InvalidOperationException("Pending post-accept Planning decision was not found: " + pendingDecisionId);
    }

    public void Touch([CallerMemberName] string toolName = "")
    {
        Interlocked.Exchange(ref lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);
        logger.Write(
            MonitorLogLevel.Information,
            "AIMonitor.McpServer",
            "adapter.mcp.tool.called",
            "MCP tool call observed.",
            new Dictionary<string, string>
            {
                ["requestId"] = Guid.NewGuid().ToString("N"),
                ["adapterProtocol"] = "mcp",
                ["toolName"] = ToSnakeCase(toolName),
                ["memberName"] = toolName,
                ["isError"] = "false"
            });
    }

    public void RequestShutdown(string? reason)
    {
        _ = reason;
        Volatile.Write(ref shutdownRequested, 1);
        Touch();
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        StringBuilder builder = new(value.Length + 8);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
