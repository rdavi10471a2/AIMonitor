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
    public void Accepted_decision_requires_successful_diff_review_launch()
    {
        WorkflowFixture fixture = CreateFixture();
        WorkflowEditService service = new(fixture.Settings);
        StagedEditRecord record = StageChangedCandidate(service, fixture);

        service.RecordPreMergeValidation(
            record.StagedRecordId,
            new PreMergeValidationResult { Status = "passed", IsError = false },
            forceApproved: false);
        File.Copy(record.StagedFilePath, fixture.ProgramFilePath, overwrite: true);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            service.RecordDecision(record.StagedRecordId, "accepted", record.StagedHash));
        Assert.Contains("successful diff review launch", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Accepted_decision_succeeds_after_force_approved_failed_validation()
    {
        WorkflowFixture fixture = CreateFixture();
        WorkflowEditService service = new(fixture.Settings);
        StagedEditRecord record = StageChangedCandidate(service, fixture);

        service.RecordPreMergeValidation(
            record.StagedRecordId,
            new PreMergeValidationResult { Status = "failed", IsError = true, DiagnosticCount = 1 },
            forceApproved: true);
        service.RecordDiffLaunch(record.StagedRecordId, launched: true, "test launch");
        File.Copy(record.StagedFilePath, fixture.ProgramFilePath, overwrite: true);

        StagedEditRecord accepted = service.RecordDecision(record.StagedRecordId, "accepted", record.StagedHash);

        Assert.Equal("accepted", accepted.Classification);
        Assert.True(service.GetStatus(fixture.ProgramFilePath).RequiresRefresh);
    }

    [Fact]
    public void RecordDecision_rejects_terminal_record_reuse()
    {
        WorkflowFixture fixture = CreateFixture();
        WorkflowEditService service = new(fixture.Settings);
        StagedEditRecord record = StageChangedCandidate(service, fixture);

        service.RecordDecision(record.StagedRecordId, "rejected");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            service.RecordDecision(record.StagedRecordId, "rejected"));
        Assert.Contains("already has a final decision", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_blocks_after_accept_until_refresh()
    {
        WorkflowFixture fixture = CreateFixture();
        WorkflowEditService service = new(fixture.Settings);
        StagedEditRecord record = StageChangedCandidate(service, fixture);

        service.RecordPreMergeValidation(
            record.StagedRecordId,
            new PreMergeValidationResult { Status = "passed", IsError = false },
            forceApproved: false);
        service.RecordDiffLaunch(record.StagedRecordId, launched: true, "test launch");
        File.Copy(record.StagedFilePath, fixture.ProgramFilePath, overwrite: true);
        service.RecordDecision(record.StagedRecordId, "accepted", record.StagedHash);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            service.Stage(fixture.ProgramFilePath));
        Assert.Contains("refresh_file", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refresh_preserves_index_stale_after_accept_until_rebuild_marks_fresh()
    {
        WorkflowFixture fixture = CreateFixture();
        WorkflowEditService service = new(fixture.Settings);
        StagedEditRecord record = StageChangedCandidate(service, fixture);

        service.RecordPreMergeValidation(
            record.StagedRecordId,
            new PreMergeValidationResult { Status = "passed", IsError = false },
            forceApproved: false);
        service.RecordDiffLaunch(record.StagedRecordId, launched: true, "test launch");
        File.Copy(record.StagedFilePath, fixture.ProgramFilePath, overwrite: true);
        service.RecordDecision(record.StagedRecordId, "accepted", record.StagedHash);

        Assert.True(service.GetStatus(fixture.ProgramFilePath).IndexStale);

        EditSessionStatus refreshed = service.Refresh(fixture.ProgramFilePath);

        Assert.True(refreshed.IndexStale);
        Assert.Equal("index-stale", refreshed.Classification);

        service.MarkIndexFresh(fixture.ProgramFilePath);

        Assert.False(service.GetStatus(fixture.ProgramFilePath).IndexStale);
    }

    [Fact]
    public void Roslyn_typed_edit_blocks_after_accept_until_refresh()
    {
        WorkflowFixture fixture = CreateFixture();
        WorkflowEditService service = new(fixture.Settings);
        StagedEditRecord record = StageChangedCandidate(service, fixture);

        service.RecordPreMergeValidation(
            record.StagedRecordId,
            new PreMergeValidationResult { Status = "passed", IsError = false },
            forceApproved: false);
        service.RecordDiffLaunch(record.StagedRecordId, launched: true, "test launch");
        File.Copy(record.StagedFilePath, fixture.ProgramFilePath, overwrite: true);
        service.RecordDecision(record.StagedRecordId, "accepted", record.StagedHash);

        RoslynEditService roslyn = new(fixture.Settings);
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            roslyn.AddMethod(
                fixture.ProgramFilePath,
                "Program",
                "public static string Extra() => \"blocked\";"));
        Assert.Contains("refresh_file", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Roslyn_typed_edit_succeeds_after_refresh_and_index_rebuild()
    {
        WorkflowFixture fixture = CreateFixture();
        WorkflowEditService service = new(fixture.Settings);
        StagedEditRecord record = StageChangedCandidate(service, fixture);

        service.RecordPreMergeValidation(
            record.StagedRecordId,
            new PreMergeValidationResult { Status = "passed", IsError = false },
            forceApproved: false);
        service.RecordDiffLaunch(record.StagedRecordId, launched: true, "test launch");
        File.Copy(record.StagedFilePath, fixture.ProgramFilePath, overwrite: true);
        service.RecordDecision(record.StagedRecordId, "accepted", record.StagedHash);
        service.MarkIndexFresh(fixture.ProgramFilePath);
        service.Refresh(fixture.ProgramFilePath);

        RoslynEditService roslyn = new(fixture.Settings);
        RoslynEditResult result = roslyn.AddMethod(
            fixture.ProgramFilePath,
            "Program",
            "public static string Extra() => \"allowed\";");

        Assert.Equal("updated", result.Status);
        Assert.Contains("Extra", File.ReadAllText(result.WorkingFilePath), StringComparison.Ordinal);
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
