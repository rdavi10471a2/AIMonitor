using AIMonitor.Core;
using AIMonitor.Indexing;
using AIMonitor.Logging;
using AIMonitor.Workflow;

namespace AIMonitor.Indexing.Tests;

public sealed class StagedDecisionWorkflowTests
{
    [Fact]
    public async Task RebuildAsync_clears_index_stale_flags_after_successful_manual_rebuild()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AIMonitorIndexingTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string sourcePath = Path.Combine(watchedRoot, "Program.cs");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(
            projectPath,
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
        File.WriteAllText(sourcePath, "namespace Example { internal static class Program { } }");

        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, projectPath, runtimeRoot);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus refresh = workflowService.Refresh(sourcePath);
        File.WriteAllText(refresh.WorkingFilePath, "namespace Example { internal static class Program { public static string Value => \"candidate\"; } }");
        StagedEditRecord record = workflowService.Stage(sourcePath);
        workflowService.RecordPreMergeValidation(
            record.StagedRecordId,
            new PreMergeValidationResult { Status = "passed", IsError = false },
            forceApproved: false);
        workflowService.RecordDiffLaunch(record.StagedRecordId, launched: true, "test launch");
        File.Copy(record.StagedFilePath, sourcePath, overwrite: true);

        workflowService.RecordDecision(record.StagedRecordId, "accepted", record.StagedHash);

        Assert.True(workflowService.GetStatus(sourcePath).IndexStale);

        await new SolutionIndexRebuildService().RebuildAsync(settings);

        Assert.False(workflowService.GetStatus(sourcePath).IndexStale);
    }

    [Fact]
    public void Record_reports_stale_index_when_post_accept_rebuild_fails()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AIMonitorIndexingTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string missingProjectPath = Path.Combine(watchedRoot, "Missing.csproj");
        string sourcePath = Path.Combine(watchedRoot, "Program.cs");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(sourcePath, "namespace Example { internal static class Program { } }");

        MonitorSettings settings = MonitorSettings.Create(repositoryRoot, missingProjectPath, runtimeRoot);
        WorkflowEditService workflowService = new(settings);
        EditSessionStatus refresh = workflowService.Refresh(sourcePath);
        File.WriteAllText(refresh.WorkingFilePath, "namespace Example { internal static class Program { public static string Value => \"candidate\"; } }");
        StagedEditRecord record = workflowService.Stage(sourcePath);
        workflowService.RecordPreMergeValidation(
            record.StagedRecordId,
            new PreMergeValidationResult { Status = "passed", IsError = false },
            forceApproved: false);
        workflowService.RecordDiffLaunch(record.StagedRecordId, launched: true, "test launch");
        File.Copy(record.StagedFilePath, sourcePath, overwrite: true);

        ReviewDecisionWithIndexRefreshResult result = new StagedDecisionWorkflow().Record(
            settings,
            NullMonitorLogger.Instance,
            workflowService,
            record.StagedRecordId,
            "accepted",
            record.StagedHash,
            "AIMonitor.Indexing.Tests");

        Assert.Equal("accepted", result.Classification);
        Assert.NotNull(result.IndexRefresh);
        Assert.True(result.IndexRefresh.IsError);
        Assert.Contains("index rows are stale", result.NextStep, StringComparison.OrdinalIgnoreCase);
        Assert.True(workflowService.GetStatus(sourcePath).IndexStale);
    }

    private sealed class NullMonitorLogger : IMonitorLogger
    {
        public static readonly NullMonitorLogger Instance = new();

        public void Write(
            MonitorLogLevel level,
            string source,
            string eventName,
            string message,
            IReadOnlyDictionary<string, string>? properties = null)
        {
        }
    }
}
