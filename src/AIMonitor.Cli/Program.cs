using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.Logging;
using AIMonitor.MSBuild;

namespace AIMonitor.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("AIMonitor CLI scaffold");
            Console.WriteLine("Commands: index rebuild [--repo-root <path>] [--config <path>]");
            Console.WriteLine("Commands planned: status, load-solution, stage, launch-diff, record-decision.");
            return 0;
        }

        if (IsIndexRebuild(args))
        {
            return await RebuildIndexAsync(args);
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

    private static async Task<int> RebuildIndexAsync(string[] args)
    {
        try
        {
            string repositoryRoot = GetOption(args, "--repo-root") ?? Directory.GetCurrentDirectory();
            string? settingsPath = GetOption(args, "--config");
            MonitorSettings settings = MonitorSettingsLoader.Load(repositoryRoot, settingsPath);
            string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
            JsonLinesMonitorLogger logger = new(MonitorLogPaths.GetDefaultLogPath(settings));

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
            Console.WriteLine($"Log: {logger.LogPath}");
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
