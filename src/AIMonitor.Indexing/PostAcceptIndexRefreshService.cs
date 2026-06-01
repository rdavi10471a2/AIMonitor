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
        string source)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        logger.Write(
            MonitorLogLevel.Information,
            source,
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
                    ["error"] = ex.Message
                });
            return result;
        }
    }
}
