using AIMonitor.Core;
using AIMonitor.Workflow;

namespace AIMonitor.Workflow.Tests;

public sealed class WorkflowEditServiceSafetyTests
{
    [Fact]
    public void Accepted_decision_requires_recorded_premerge_validation()
    {
        WorkflowFixture fixture = CreateFixture();
        WorkflowEditService service = new(fixture.Settings);
        StagedEditRecord record = StageChangedCandidate(service, fixture);

        service.RecordDiffLaunch(record.StagedRecordId, launched: true, "test launch");
        File.Copy(record.StagedFilePath, fixture.ProgramFilePath, overwrite: true);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            service.RecordDecision(record.StagedRecordId, "accepted", record.StagedHash));
        Assert.Contains("pre-merge validation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accepted_decision_rejects_failed_validation_without_force_approval()
    {
        WorkflowFixture fixture = CreateFixture();
        WorkflowEditService service = new(fixture.Settings);
        StagedEditRecord record = StageChangedCandidate(service, fixture);

        service.RecordPreMergeValidation(
            record.StagedRecordId,
            new PreMergeValidationResult { Status = "failed", IsError = true, DiagnosticCount = 1 },
            forceApproved: false);
        service.RecordDiffLaunch(record.StagedRecordId, launched: true, "test launch");
        File.Copy(record.StagedFilePath, fixture.ProgramFilePath, overwrite: true);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            service.RecordDecision(record.StagedRecordId, "accepted", record.StagedHash));
        Assert.Contains("failed pre-merge validation", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accepted_decision_rejects_dirty_unexpected_watched_source()
    {
        WorkflowFixture fixture = CreateFixture();
        WorkflowEditService service = new(fixture.Settings);
        StagedEditRecord record = StageChangedCandidate(service, fixture);

        service.RecordPreMergeValidation(
            record.StagedRecordId,
            new PreMergeValidationResult { Status = "passed", IsError = false },
            forceApproved: false);
        service.RecordDiffLaunch(record.StagedRecordId, launched: true, "test launch");
        File.WriteAllText(fixture.ProgramFilePath, "namespace Example { internal static class Program { public static string Value => \"external\"; } }");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            service.RecordDecision(record.StagedRecordId, "accepted", record.StagedHash));
        Assert.Contains("Cannot accept", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReplaceText_honors_occurrence_index_without_adapter_file_writes()
    {
        WorkflowFixture fixture = CreateFixture();
        WorkflowEditService service = new(fixture.Settings);
        EditSessionStatus refresh = service.Refresh(fixture.ProgramFilePath);
        File.WriteAllText(refresh.WorkingFilePath, "one fish one fish");

        ReplaceTextResult result = service.ReplaceText(
            fixture.ProgramFilePath,
            "one",
            "two",
            expectedMatches: 2,
            occurrenceIndex: 1);

        Assert.True(result.Changed);
        Assert.Equal("one fish two fish", File.ReadAllText(refresh.WorkingFilePath));
    }

    [Fact]
    public void ReplaceSpan_uses_crlf_aware_line_columns()
    {
        WorkflowFixture fixture = CreateFixture();
        WorkflowEditService service = new(fixture.Settings);
        EditSessionStatus refresh = service.Refresh(fixture.ProgramFilePath);
        File.WriteAllText(refresh.WorkingFilePath, "first\r\nsecond\r\nthird\r\n");

        TextSpanResult span = service.FindTextSpan(fixture.ProgramFilePath, "second");
        EditSessionStatus status = service.ReplaceSpan(
            fixture.ProgramFilePath,
            span.StartLine,
            span.StartColumn,
            span.EndLine,
            span.EndColumn,
            "changed",
            expectedOldTextHash: span.TextHash,
            expectedOldText: "second");

        Assert.Equal("pending", status.Classification);
        Assert.Equal("first\r\nchanged\r\nthird\r\n", File.ReadAllText(refresh.WorkingFilePath));
    }

    private static StagedEditRecord StageChangedCandidate(WorkflowEditService service, WorkflowFixture fixture)
    {
        EditSessionStatus refresh = service.Refresh(fixture.ProgramFilePath);
        File.WriteAllText(refresh.WorkingFilePath, "namespace Example { internal static class Program { public static string Value => \"candidate\"; } }");
        return service.Stage(fixture.ProgramFilePath);
    }

    private static WorkflowFixture CreateFixture()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "AIMonitorWorkflowSafetyTests", Guid.NewGuid().ToString("N"));
        string repositoryRoot = Path.Combine(tempRoot, "Repo");
        string runtimeRoot = Path.Combine(tempRoot, "Runtime");
        string watchedRoot = Path.Combine(tempRoot, "Watched");
        string projectPath = Path.Combine(watchedRoot, "Example.csproj");
        string programFilePath = Path.Combine(watchedRoot, "Program.cs");

        Directory.CreateDirectory(watchedRoot);
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(programFilePath, "namespace Example { internal static class Program { } }");

        return new WorkflowFixture(
            MonitorSettings.Create(repositoryRoot, projectPath, runtimeRoot),
            programFilePath);
    }

    private sealed record WorkflowFixture(MonitorSettings Settings, string ProgramFilePath);
}
