using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.Indexing;
using AIMonitor.Logging;
using AIMonitor.MSBuild;
using AIMonitor.Runtime;
using AIMonitor.Workflow;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
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
    private readonly RoslynEditService roslynEditService;
    private readonly WorkflowEditPaths workflowPaths;
    private readonly AIMonitorMcpRuntimeState runtimeState;
    private readonly IHostApplicationLifetime applicationLifetime;
    private readonly IMonitorLogger logger;

    public AIMonitorTools(
        MonitorSettings settings,
        SolutionIndexQueryService queryService,
        WorkflowEditService workflowService,
        RoslynEditService roslynEditService,
        WorkflowEditPaths workflowPaths,
        AIMonitorMcpRuntimeState runtimeState,
        IHostApplicationLifetime applicationLifetime,
        IMonitorLogger logger)
    {
        this.settings = settings;
        this.queryService = queryService;
        this.workflowService = workflowService;
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
            indexStatus.DiagnosticCount);
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
    [Description("Return a self-check snapshot for configured roots, working folders, diff tool availability, and safety guardrails.")]
    public AIMonitorSelfCheckResult GetSelfCheck()
    {
        runtimeState.Touch();
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
            "agents edit monitor-owned Working candidates only; WinMerge review/save remains the watched-source mutation surface");
    }

    [McpServerTool]
    [Description("Rebuild the monitor-owned SQLite index for the watched solution.")]
    public async Task<SolutionIndexSummary> RefreshSolutionIndex()
    {
        runtimeState.Touch();
        SolutionIndexStore store = new(new SolutionIndexDatabase(queryService.DatabasePath));
        SolutionIndexBuilder builder = new(new MSBuildWorkspaceLoader(), store);
        return await builder.RebuildAsync(settings);
    }

    [McpServerTool]
    [Description("Refresh one watched C# file in the monitor-owned SQLite solution index. AIMonitor currently rebuilds the semantic index and returns the requested file slice.")]
    public async Task<AIMonitorRefreshIndexFileResult> RefreshSolutionIndexFile(
        [Description("Watched C# file path, absolute or relative to the watched solution folder.")] string path)
    {
        runtimeState.Touch();
        SolutionIndexSummary summary = await RefreshSolutionIndex();
        return new AIMonitorRefreshIndexFileResult(
            summary,
            queryService.ListDocuments(filePath: ResolveWatchedPath(path)).ToArray(),
            queryService.ListSymbols(filePath: ResolveWatchedPath(path)).ToArray());
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
    public AIMonitorSolutionIndexResult GetSolutionIndex(
        [Description("Maximum files to return.")] int maxFiles = 5000,
        [Description("Maximum symbols to return.")] int maxSymbols = 50000)
    {
        runtimeState.Touch();
        return new AIMonitorSolutionIndexResult(
            queryService.GetMonitorStatus(),
            queryService.ListDocuments().Take(maxFiles).ToArray(),
            queryService.ListSymbols().Take(maxSymbols).ToArray());
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
    public AIMonitorSolutionIndexResult QuerySolutionIndex(
        [Description("Index scope: solution, namespace, folder, or file.")] string scope = "solution",
        [Description("Namespace text, folder path, or file path for scoped queries. Omit for solution scope.")] string? value = null,
        [Description("Maximum files to return.")] int maxFiles = 200,
        [Description("Maximum symbols to return.")] int maxSymbols = 500)
    {
        runtimeState.Touch();
        IEnumerable<IndexedDocumentRow> documents = queryService.ListDocuments();
        IEnumerable<IndexedSymbolRow> symbols = queryService.ListSymbols();
        string normalizedScope = scope.ToLowerInvariant();
        if (normalizedScope == "file" && !string.IsNullOrWhiteSpace(value))
        {
            documents = documents.Where(row => PathMatches(row.FilePath, value));
            symbols = symbols.Where(row => PathMatches(row.FilePath, value));
        }
        else if (normalizedScope == "folder" && !string.IsNullOrWhiteSpace(value))
        {
            documents = documents.Where(row => row.FilePath.Contains(value, StringComparison.OrdinalIgnoreCase));
            symbols = symbols.Where(row => row.FilePath.Contains(value, StringComparison.OrdinalIgnoreCase));
        }
        else if (normalizedScope == "namespace" && !string.IsNullOrWhiteSpace(value))
        {
            symbols = symbols.Where(row => row.Namespace.Equals(value, StringComparison.Ordinal));
            HashSet<string> files = symbols.Select(row => row.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            documents = documents.Where(row => files.Contains(row.FilePath));
        }
        else if (normalizedScope != "solution")
        {
            throw new ArgumentException("Scope must be solution, namespace, folder, or file.", nameof(scope));
        }

        return new AIMonitorSolutionIndexResult(
            queryService.GetMonitorStatus(),
            documents.Take(maxFiles).ToArray(),
            symbols.Take(maxSymbols).ToArray());
    }

    [McpServerTool]
    [Description("Find indexed C# symbols by name text, optional kind, and optional exact namespace using the monitor-owned watched solution index.")]
    public IReadOnlyList<IndexedSymbolRow> FindIndexedSymbols(
        [Description("Symbol name text to search for.")] string text,
        [Description("Optional exact symbol kind, such as class, method, property, field, constructor, enum, delegate, interface, struct, or record.")] string? kind = null,
        [Description("Optional exact namespace filter.")] string? namespaceName = null,
        [Description("Maximum symbols to return.")] int maxResults = 100)
    {
        runtimeState.Touch();
        IEnumerable<IndexedSymbolRow> symbols = queryService.ListSymbols()
            .Where(symbol => symbol.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(kind))
        {
            symbols = symbols.Where(symbol => symbol.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(namespaceName))
        {
            symbols = symbols.Where(symbol => symbol.Namespace.Equals(namespaceName, StringComparison.Ordinal));
        }

        return symbols.Take(maxResults).ToArray();
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

        return queryService.ListSymbols().FirstOrDefault(symbol => symbol.StableKey.Equals(stableSymbolKey, StringComparison.Ordinal));
    }

    [McpServerTool]
    [Description("Return persisted indexed reference sites for one stable C# symbol key.")]
    public object FindIndexedReferences(
        [Description("Stable symbol key returned by query_solution_index, find_indexed_symbols, or get_indexed_symbol.")] string stableSymbolKey,
        [Description("Maximum reference rows to return.")] int maxResults = 500)
    {
        runtimeState.Touch();
        if (TryCreateIndexedStableSymbolKeyError(stableSymbolKey) is { } error)
        {
            return error;
        }

        return queryService.ListReferences(stableSymbolKey).Take(maxResults).ToArray();
    }

    [McpServerTool]
    [Description("Return persisted indexed invocation call sites for one stable C# method or constructor symbol key.")]
    public object FindIndexedCallers(
        [Description("Stable method or constructor symbol key returned by query_solution_index, find_indexed_symbols, or get_indexed_symbol.")] string stableSymbolKey,
        [Description("Maximum caller rows to return.")] int maxResults = 500)
    {
        runtimeState.Touch();
        if (TryCreateIndexedStableSymbolKeyError(stableSymbolKey) is { } error)
        {
            return error;
        }

        return queryService.ListReferences(stableKey: stableSymbolKey)
            .Where(reference => reference.ReferenceKind.Contains("Invocation", StringComparison.OrdinalIgnoreCase)
                || reference.ReferenceKind.Contains("ObjectCreation", StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .ToArray();
    }

    [McpServerTool]
    [Description("Return indexed symbol relationship rows for one stable symbol key. AIMonitor V2 currently returns an empty compatibility set until relationship rows are added to the shared index schema.")]
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

        _ = stableSymbolKey;
        _ = relationshipKind;
        _ = direction;
        _ = maxResults;
        return Array.Empty<AIMonitorIndexedRelationship>();
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
    [Description("List staged edit records visible to a durable monitor session. AIMonitor currently reports all runtime staged records because staged records do not yet persist a session id.")]
    public IReadOnlyList<StagedEditRecord> ListSessionStagedRecords(
        [Description("Session handle returned by start_monitor_session.")] string sessionId)
    {
        runtimeState.Touch();
        _ = GetMonitorSession(sessionId);
        return ListStagedRecords();
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
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "file-fetch", path, JsonSerializer.Serialize(hashInfo, JsonOptions));
        }

        return new AIMonitorFileReadResult(path, workflowPaths.GetRelativeWatchedPath(path), hashInfo, text);
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
        AIMonitorSessionEvent? fetch = session.Events
            .Where(item => item.EventType.Equals("file-fetch", StringComparison.OrdinalIgnoreCase)
                && item.Summary.Equals(path, StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();
        AIMonitorFileHashInfo? previous = null;
        if (!string.IsNullOrWhiteSpace(fetch?.PayloadJson))
        {
            previous = JsonSerializer.Deserialize<AIMonitorFileHashInfo>(fetch.PayloadJson, JsonOptions);
        }

        return new AIMonitorFileHashCheckResult(
            path,
            previous is not null,
            previous?.Sha256.Equals(current.Sha256, StringComparison.OrdinalIgnoreCase) == false,
            current,
            previous);
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
    [Description("Return a text outline for a watched source file. C# semantic source maps are not yet part of AIMonitor; this returns line-oriented type/member candidates.")]
    public AIMonitorFileOutlineResult GetFileOutline(
        [Description("Source file path, absolute or relative to the watched solution folder.")] string path)
    {
        runtimeState.Touch();
        string fullPath = ResolveWatchedPath(path);
        string[] lines = File.ReadAllLines(fullPath);
        IReadOnlyList<AIMonitorOutlineItem> items = lines
            .Select((line, index) => new { line, lineNumber = index + 1 })
            .Where(item => LooksLikeCSharpDeclaration(item.line))
            .Select(item => new AIMonitorOutlineItem(item.lineNumber, item.line.Trim()))
            .ToArray();
        return new AIMonitorFileOutlineResult(fullPath, workflowPaths.GetRelativeWatchedPath(fullPath), items);
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
        EditSessionStatus status = workflowService.SubmitFile(fullPath, content);
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
        [Description("Required number of matches in the current edit base. Defaults to 1.")] int expectedMatches = 1,
        [Description("Optional 0-based occurrence index. Leave -1 for unique/global replacement; set 0 or greater to replace one occurrence.")] int occurrenceIndex = -1,
        [Description("Optional SHA-256 hash of the current Working candidate.")] string? expectedFileHash = null,
        [Description("Optional SHA-256 hash of oldText.")] string? expectedOldTextHash = null,
        [Description("Optional durable session handle.")] string? sessionId = null,
        [Description("Optional JSON manifest expressing model intent.")] string? manifestJson = null)
    {
        runtimeState.Touch();
        _ = manifestJson;
        if (!string.IsNullOrWhiteSpace(expectedOldTextHash)
            && !ComputeHash(oldText).Equals(expectedOldTextHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("oldText hash did not match expectedOldTextHash.");
        }

        ReplaceTextResult result = workflowService.ReplaceText(
            ResolveWatchedPath(path),
            oldText,
            newText,
            expectedMatches,
            expectedFileHash,
            occurrenceIndex >= 0 ? occurrenceIndex : null);
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
        return workflowService.FindTextSpan(ResolveWatchedPath(path), findText, occurrenceIndex, expectedFileHash);
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
        _ = manifestJson;
        EditSessionStatus status = workflowService.ReplaceSpan(
            ResolveWatchedPath(path),
            startLine,
            startColumn,
            endLine,
            endColumn,
            newText,
            expectedFileHash,
            expectedOldTextHash,
            expectedOldText);
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
        _ = manifestJson;
        StagedEditRecord record = workflowService.Stage(ResolveWatchedPath(path), ledgerSummary);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            RecordMonitorSessionEvent(sessionId, "stage-candidate-for-review", record.StagedRecordId, JsonSerializer.Serialize(record, JsonOptions));
        }

        return new AIMonitorStageCandidateResult(
            workflowService.CreateSummary(record),
            verbose ? record : null,
            "Candidate staged. Use get_staged_record for full details or launch_staged_diff for review.");
    }

    [McpServerTool]
    [Description("Replace one C# symbol in the monitor-owned Working candidate using a Roslyn selector.")]
    public RoslynEditResult SubmitSymbol(string path, string symbolSelectorJson, string code, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        _ = manifestJson;
        RoslynEditResult result = roslynEditService.SubmitSymbol(ResolveWatchedPath(path), symbolSelectorJson, code);
        RecordRoslynSessionEvent(sessionId, "submit-symbol", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a using directive to the monitor-owned Working candidate.")]
    public RoslynEditResult AddUsing(string path, string @namespace, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        _ = manifestJson;
        RoslynEditResult result = roslynEditService.AddUsing(ResolveWatchedPath(path), @namespace);
        RecordRoslynSessionEvent(sessionId, "add-using", result);
        return result;
    }

    [McpServerTool]
    [Description("Remove a using directive from the monitor-owned Working candidate.")]
    public RoslynEditResult RemoveUsing(string path, string @namespace, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        _ = manifestJson;
        RoslynEditResult result = roslynEditService.RemoveUsing(ResolveWatchedPath(path), @namespace);
        RecordRoslynSessionEvent(sessionId, "remove-using", result);
        return result;
    }

    [McpServerTool]
    [Description("Add or remove the partial modifier on a C# type in the monitor-owned Working candidate.")]
    public RoslynEditResult SetTypePartial(string path, string containingType, bool isPartial, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        _ = manifestJson;
        RoslynEditResult result = roslynEditService.SetTypePartial(ResolveWatchedPath(path), containingType, isPartial);
        RecordRoslynSessionEvent(sessionId, "set-type-partial", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# member or nested type to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddSymbol(string path, string containingType, string symbolType, string code, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        _ = manifestJson;
        RoslynEditResult result = roslynEditService.AddSymbol(ResolveWatchedPath(path), containingType, symbolType, code, afterSymbol);
        RecordRoslynSessionEvent(sessionId, "add-symbol", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# field to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddField(string path, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        _ = manifestJson;
        RoslynEditResult result = roslynEditService.AddField(ResolveWatchedPath(path), containingType, declaration, afterSymbol);
        RecordRoslynSessionEvent(sessionId, "add-field", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# property to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddProperty(string path, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        _ = manifestJson;
        RoslynEditResult result = roslynEditService.AddProperty(ResolveWatchedPath(path), containingType, declaration, afterSymbol);
        RecordRoslynSessionEvent(sessionId, "add-property", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# method to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddMethod(string path, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        _ = manifestJson;
        RoslynEditResult result = roslynEditService.AddMethod(ResolveWatchedPath(path), containingType, declaration, afterSymbol);
        RecordRoslynSessionEvent(sessionId, "add-method", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# constructor to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddConstructor(string path, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        _ = manifestJson;
        RoslynEditResult result = roslynEditService.AddConstructor(ResolveWatchedPath(path), containingType, declaration, afterSymbol);
        RecordRoslynSessionEvent(sessionId, "add-constructor", result);
        return result;
    }

    [McpServerTool]
    [Description("Add a C# nested type to a containing type in the monitor-owned Working candidate.")]
    public RoslynEditResult AddNestedType(string path, string containingType, string declaration, string? afterSymbol = null, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        _ = manifestJson;
        RoslynEditResult result = roslynEditService.AddNestedType(ResolveWatchedPath(path), containingType, declaration, afterSymbol);
        RecordRoslynSessionEvent(sessionId, "add-nested-type", result);
        return result;
    }

    [McpServerTool]
    [Description("Remove one C# symbol from the monitor-owned Working candidate using a Roslyn selector.")]
    public RoslynEditResult RemoveSymbol(string path, string symbolSelectorJson, string? sessionId = null, string? manifestJson = null)
    {
        runtimeState.Touch();
        _ = manifestJson;
        RoslynEditResult result = roslynEditService.RemoveSymbol(ResolveWatchedPath(path), symbolSelectorJson);
        RecordRoslynSessionEvent(sessionId, "remove-symbol", result);
        return result;
    }

    [McpServerTool]
    [Description("Classify a completed WinMerge review for a staged edit. Accepted decisions require the expected staged hash.")]
    public ReviewDecisionWithIndexRefreshResult RecordDiffDecision(
        [Description("Staged edit record id returned by stage_candidate_for_review.")] string stagedRecordId,
        [Description("Operator-reported outcome: accepted or rejected.")] string decision,
        [Description("Expected staged hash for accepted decisions.")] string? expectedStagedHash = null,
        [Description("Return the full staged record inline for debugging. Defaults to compact response.")] bool verbose = false)
    {
        runtimeState.Touch();
        StagedEditRecord record = workflowService.RecordDecision(stagedRecordId, decision, expectedStagedHash);
        PostAcceptIndexRefreshResult? indexRefresh = null;
        if (record.Classification is "accepted" or "accepted-normalized")
        {
            indexRefresh = new PostAcceptIndexRefreshService().RebuildAfterAcceptedDecision(
                settings,
                logger,
                record,
                "AIMonitor.McpServer");
        }

        return new ReviewDecisionWithIndexRefreshResult
        {
            StagedRecordId = record.StagedRecordId,
            WatchedFilePath = record.WatchedFilePath,
            RelativePath = record.RelativePath,
            Decision = record.Decision,
            Classification = record.Classification,
            Status = record.Status,
            Message = record.Message,
            StagedRecordSummary = workflowService.CreateSummary(record),
            StagedRecordPath = workflowService.CreateSummary(record).RecordPath,
            StagedRecord = verbose ? record : null,
            IndexRefresh = indexRefresh,
            NextStep = record.Classification is "accepted" or "accepted-normalized"
                ? "Index was rebuilt after accept. Run edit refresh before further edits to this watched file."
                : "Decision recorded. Do not rely on changed index rows unless an accepted decision rebuilt the index."
        };
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
        StagedDiffLaunchWorkflowResult result = new StagedDiffLaunchWorkflow().Launch(
            settings,
            logger,
            workflowService,
            stagedRecordId,
            "AIMonitor.McpServer",
            diffToolPath,
            forceValidation,
            verbose);
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
            "AIMonitor V2 currently keeps workflow history until an explicit UI/operator cleanup flow is implemented.",
            new Dictionary<string, string?> { ["retentionDays"] = retentionDays.ToString() });
    }

    [McpServerTool]
    [Description("Return the Markdown tool manifest for the AIMonitor MCP Server tool surface.")]
    public string GetToolManifest()
    {
        runtimeState.Touch();
        string path = Path.Combine(settings.RepositoryRoot, "docs", "feature-maps", "SharedAdapterSurface.md");
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "AIMonitor shared adapter surface documentation is missing.";
    }

    [McpServerTool]
    [Description("Return the normal staging guide for AIMonitor watched-project edits.")]
    public string GetStagingGuide()
    {
        runtimeState.Touch();
        string path = Path.Combine(settings.RepositoryRoot, "docs", "workflows", "SafeEditWorkflow.md");
        return File.Exists(path)
            ? File.ReadAllText(path)
            : "AIMonitor safe edit workflow documentation is missing.";
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

    private static bool PathMatches(string candidate, string value)
    {
        return candidate.Equals(value, StringComparison.OrdinalIgnoreCase)
            || candidate.EndsWith(value, StringComparison.OrdinalIgnoreCase);
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
        EditSessionStatus status = workflowService.GetStatus(watchedFilePath);
        if (!status.HasSession)
        {
            return workflowService.Refresh(watchedFilePath);
        }

        if (status.RequiresRefresh)
        {
            throw new InvalidOperationException($"Previous decision was accepted for {status.RelativePath}. Run refresh_file for this watched file before editing or staging it again. If the watched solution changed, start a new monitor session and refresh the file from the new watched solution.");
        }

        return status;
    }

    private void SaveSession(AIMonitorSessionState session)
    {
        Directory.CreateDirectory(SessionRoot);
        File.WriteAllText(GetSessionPath(session.SessionId), JsonSerializer.Serialize(session, JsonOptions));
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

    private IReadOnlyList<StagedEditRecord> ListStagedRecords()
    {
        return Directory.Exists(workflowPaths.StagedRecordsRoot)
            ? Directory.EnumerateFiles(workflowPaths.StagedRecordsRoot, "*.json")
                .Select(path => JsonSerializer.Deserialize<StagedEditRecord>(File.ReadAllText(path), JsonOptions))
                .Where(record => record is not null)
                .Select(record => record!)
                .OrderByDescending(record => record.CreatedAtUtc, StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    private AIMonitorCompatibilityResult SemanticEditNotImplemented(string toolName, string path, string? sessionId, string? manifestJson)
    {
        return new AIMonitorCompatibilityResult(
            "not-implemented",
            $"{toolName} is part of the MonitorBaseClaude semantic edit surface. AIMonitor has not ported that Roslyn edit service yet; use submit_file, replace_text_in_file, or replace_span_in_file against the monitor-owned Working candidate.",
            new Dictionary<string, string?>
            {
                ["path"] = path,
                ["sessionId"] = sessionId,
                ["manifestJson"] = manifestJson
            });
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

    private static bool LooksLikeCSharpDeclaration(string line)
    {
        string trimmed = line.Trim();
        return trimmed.Contains(" class ", StringComparison.Ordinal)
            || trimmed.Contains(" interface ", StringComparison.Ordinal)
            || trimmed.Contains(" struct ", StringComparison.Ordinal)
            || trimmed.Contains(" record ", StringComparison.Ordinal)
            || trimmed.Contains(" enum ", StringComparison.Ordinal)
            || trimmed.Contains(" void ", StringComparison.Ordinal)
            || trimmed.Contains(" string ", StringComparison.Ordinal)
            || trimmed.Contains(" int ", StringComparison.Ordinal)
            || trimmed.Contains(" bool ", StringComparison.Ordinal);
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
    int DiagnosticCount);

public sealed record AIMonitorWorkflowStatus(
    string WatchedSolutionPath,
    string WatchedProjectFolder,
    string RuntimeRoot,
    string WorkingRoot,
    string? ResolvedDiffToolPath,
    IReadOnlyList<string> WinMergeCandidatePaths);

public sealed record AIMonitorSolutionIndexResult(
    MonitorStatusResult Status,
    IReadOnlyList<IndexedDocumentRow> Files,
    IReadOnlyList<IndexedSymbolRow> Symbols);

public sealed record AIMonitorToolErrorResult(
    bool IsError,
    string Message,
    string Expected,
    string? Received);

public sealed record AIMonitorSolutionIndexTree(
    IReadOnlyList<IndexedProjectRow> Projects,
    IReadOnlyList<IndexedDocumentRow> Files,
    IReadOnlyList<AIMonitorNamespaceTree> Namespaces);

public sealed record AIMonitorNamespaceTree(
    string Namespace,
    IReadOnlyList<string> Files,
    int SymbolCount);

public sealed record AIMonitorStageCandidateResult(
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
    string SafetySummary);

public sealed record AIMonitorRefreshIndexFileResult(
    SolutionIndexSummary Summary,
    IReadOnlyList<IndexedDocumentRow> Files,
    IReadOnlyList<IndexedSymbolRow> Symbols);

public sealed record AIMonitorRefreshFileAndIndexResult(
    EditSessionStatus Refresh,
    AIMonitorRefreshIndexFileResult Index);

public sealed record AIMonitorIndexedRelationship(
    string SourceStableKey,
    string TargetStableKey,
    string RelationshipKind,
    string Direction);

public sealed record AIMonitorSessionState(
    string SessionId,
    string Purpose,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<AIMonitorSessionEvent> Events);

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

public sealed record AIMonitorFileHashInfo(
    string Sha256,
    long Length,
    DateTime LastWriteTimeUtc);

public sealed record AIMonitorFileReadResult(
    string SourceFilePath,
    string RelativePath,
    AIMonitorFileHashInfo Hash,
    string Content);

public sealed record AIMonitorFileHashCheckResult(
    string SourceFilePath,
    bool KnownInSession,
    bool ChangedSinceFetch,
    AIMonitorFileHashInfo Current,
    AIMonitorFileHashInfo? Previous);

public sealed record AIMonitorFileMatch(
    string Name,
    string Path,
    string RelativePath);

public sealed record AIMonitorFileOutlineResult(
    string SourceFilePath,
    string RelativePath,
    IReadOnlyList<AIMonitorOutlineItem> Items);

public sealed record AIMonitorOutlineItem(
    int Line,
    string Text);

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

public sealed class AIMonitorMcpRuntimeState
{
    private readonly IMonitorLogger logger;
    private long lastActivityTicks = DateTimeOffset.UtcNow.UtcTicks;
    private int shutdownRequested;

    public AIMonitorMcpRuntimeState(IMonitorLogger logger)
    {
        this.logger = logger;
    }

    public DateTimeOffset LastActivityUtc => new(Interlocked.Read(ref lastActivityTicks), TimeSpan.Zero);

    public bool ShutdownRequested => Volatile.Read(ref shutdownRequested) == 1;

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
