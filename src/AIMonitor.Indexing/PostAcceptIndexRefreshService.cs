using System.Diagnostics;
using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.Logging;
using AIMonitor.MSBuild;
using AIMonitor.Workflow;

namespace AIMonitor.Indexing;

public sealed class PostAcceptIndexRefreshService
{
    public PostAcceptIndexRefreshResult RebuildAfterAcceptedDecision(
        MonitorSettings settings,
        IMonitorLogger logger,
        StagedEditRecord record,
        string source,
        PostAcceptIndexRefreshPlan? refreshPlan = null)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        string[] projectPaths = GetProjectRefreshPaths(record, refreshPlan);
        bool useProjectRefresh = projectPaths.Length > 0;
        logger.Write(
            MonitorLogLevel.Information,
            source,
            "index.refresh-after-accept.started",
            useProjectRefresh
                ? "Post-accept project index refresh started."
                : "Post-accept solution index rebuild started.",
            new Dictionary<string, string>
            {
                ["stagedRecordId"] = record.StagedRecordId,
                ["watchedFilePath"] = record.WatchedFilePath,
                ["watchedSolutionPath"] = settings.WatchedSolutionPath,
                ["databasePath"] = databasePath,
                ["refreshMode"] = useProjectRefresh ? "project" : "solution",
                ["projectPaths"] = string.Join(";", projectPaths)
            });

        try
        {
            SolutionIndexSummary summary = useProjectRefresh
                ? new SolutionIndexRebuildService().RefreshProjectsAsync(settings, projectPaths).GetAwaiter().GetResult()
                : new SolutionIndexRebuildService().RebuildAsync(settings).GetAwaiter().GetResult();
            stopwatch.Stop();
            PostAcceptIndexRefreshResult result = new()
            {
                Status = "rebuilt",
                RefreshMode = useProjectRefresh ? "project" : "solution",
                IsError = false,
                DatabasePath = databasePath,
                ProjectCount = summary.ProjectCount,
                DocumentCount = summary.DocumentCount,
                DiagnosticCount = summary.DiagnosticCount,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Message = useProjectRefresh
                    ? "Post-accept project index refresh completed."
                    : "Post-accept solution index rebuild completed."
            };
            new WorkflowEditService(settings).MarkIndexFresh(record.WatchedFilePath);
            logger.Write(
                MonitorLogLevel.Information,
                source,
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
                    ["isError"] = "false",
                    ["refreshMode"] = result.RefreshMode,
                    ["projectPaths"] = string.Join(";", projectPaths)
                });
            return result;
        }
        catch (Exception ex)
        {
            if (useProjectRefresh)
            {
                try
                {
                    SolutionIndexSummary fallbackSummary = new SolutionIndexRebuildService().RebuildAsync(settings).GetAwaiter().GetResult();
                    stopwatch.Stop();
                    PostAcceptIndexRefreshResult fallbackResult = new()
                    {
                        Status = "rebuilt",
                        RefreshMode = "solution-fallback",
                        IsError = false,
                        DatabasePath = databasePath,
                        ProjectCount = fallbackSummary.ProjectCount,
                        DocumentCount = fallbackSummary.DocumentCount,
                        DiagnosticCount = fallbackSummary.DiagnosticCount,
                        DurationMs = stopwatch.ElapsedMilliseconds,
                        Message = "Post-accept project index refresh failed; full solution index rebuild completed."
                    };
                    new WorkflowEditService(settings).MarkIndexFresh(record.WatchedFilePath);
                    logger.Write(
                        MonitorLogLevel.Warning,
                        source,
                        "index.refresh-after-accept.fallback-completed",
                        fallbackResult.Message,
                        new Dictionary<string, string>
                        {
                            ["stagedRecordId"] = record.StagedRecordId,
                            ["watchedFilePath"] = record.WatchedFilePath,
                            ["databasePath"] = databasePath,
                            ["projectCount"] = fallbackResult.ProjectCount.ToString(),
                            ["documentCount"] = fallbackResult.DocumentCount.ToString(),
                            ["diagnosticCount"] = fallbackResult.DiagnosticCount.ToString(),
                            ["durationMs"] = fallbackResult.DurationMs.ToString(),
                            ["isError"] = "false",
                            ["refreshMode"] = fallbackResult.RefreshMode,
                            ["projectPaths"] = string.Join(";", projectPaths),
                            ["projectRefreshError"] = ex.Message
                        });
                    return fallbackResult;
                }
                catch (Exception fallbackEx)
                {
                    ex = new InvalidOperationException(
                        "Project index refresh failed, and the full solution fallback also failed: " + fallbackEx.Message,
                        fallbackEx);
                }
            }

            stopwatch.Stop();
            PostAcceptIndexRefreshResult result = new()
            {
                Status = "failed",
                RefreshMode = useProjectRefresh ? "project" : "solution",
                IsError = true,
                DatabasePath = databasePath,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Message = ex.Message
            };
            logger.Write(
                MonitorLogLevel.Error,
                source,
                "index.refresh-after-accept.failed",
                "Post-accept solution index rebuild failed.",
                new Dictionary<string, string>
                {
                    ["stagedRecordId"] = record.StagedRecordId,
                    ["watchedFilePath"] = record.WatchedFilePath,
                    ["databasePath"] = databasePath,
                    ["durationMs"] = result.DurationMs.ToString(),
                    ["isError"] = "true",
                    ["refreshMode"] = result.RefreshMode,
                    ["projectPaths"] = string.Join(";", projectPaths),
                    ["error"] = ex.Message
                });
            return result;
        }
    }

    private static string[] GetProjectRefreshPaths(
        StagedEditRecord record,
        PostAcceptIndexRefreshPlan? refreshPlan)
    {
        if (!IsSafeProjectScopedRefresh(record))
        {
            return [];
        }

        return refreshPlan?.OwningProjectPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    private static bool IsSafeProjectScopedRefresh(StagedEditRecord record)
    {
        string extension = Path.GetExtension(record.WatchedFilePath);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(record.WatchedFilePath).Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(record.WatchedFilePath).Equals("Directory.Build.targets", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(record.WatchedFilePath).Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase);
    }
}
