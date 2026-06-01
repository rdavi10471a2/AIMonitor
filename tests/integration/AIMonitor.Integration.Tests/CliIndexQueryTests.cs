using System.Diagnostics;
using System.Text.Json;
using AIMonitor.Core;
using AIMonitor.Data;
using AIMonitor.MSBuild;

namespace AIMonitor.Integration.Tests;

public sealed class CliIndexQueryTests
{
    [Fact]
    public async Task Status_reports_seeded_index_counts()
    {
        CliFixture fixture = CreateFixture();

        CliResult result = await RunCliAsync(
            "status",
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.StdOut);
        JsonElement root = document.RootElement;
        Assert.True(root.GetProperty("databaseExists").GetBoolean());
        Assert.Equal(fixture.WatchedSolutionPath, root.GetProperty("watchedSolutionPath").GetString());
        Assert.Equal(1, root.GetProperty("projectCount").GetInt32());
        Assert.Equal(1, root.GetProperty("documentCount").GetInt32());
    }

    [Fact]
    public async Task Symbols_filters_by_file_and_name()
    {
        CliFixture fixture = CreateFixture();

        CliResult result = await RunCliAsync(
            "index",
            "symbols",
            "--file",
            fixture.ProgramFilePath,
            "--name",
            "Program",
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.StdOut);
        JsonElement row = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("Program", row.GetProperty("name").GetString());
        Assert.Equal(fixture.ProgramFilePath, row.GetProperty("filePath").GetString());
    }

    [Fact]
    public async Task References_in_file_returns_seeded_reference()
    {
        CliFixture fixture = CreateFixture();

        CliResult result = await RunCliAsync(
            "index",
            "references-in-file",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, result.ExitCode);
        using JsonDocument document = JsonDocument.Parse(result.StdOut);
        JsonElement row = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(fixture.ProgramSymbolStableKey, row.GetProperty("targetStableKey").GetString());
        Assert.Equal("IdentifierName", row.GetProperty("referenceKind").GetString());
    }

    [Fact]
    public async Task Edit_refresh_stage_and_record_decision_round_trip_through_working_candidate()
    {
        CliFixture fixture = CreateFixture();

        CliResult refresh = await RunCliAsync(
            "edit",
            "refresh",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, refresh.ExitCode);
        using JsonDocument refreshDocument = JsonDocument.Parse(refresh.StdOut);
        string workingFilePath = refreshDocument.RootElement.GetProperty("workingFilePath").GetString()
            ?? throw new InvalidOperationException("Missing working file path.");
        Assert.True(File.Exists(workingFilePath));

        await File.WriteAllTextAsync(workingFilePath, "namespace Example { internal static class Program { public static string Value => \"changed\"; } }");

        CliResult status = await RunCliAsync(
            "edit",
            "status",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, status.ExitCode);
        using JsonDocument statusDocument = JsonDocument.Parse(status.StdOut);
        JsonElement statusRoot = statusDocument.RootElement;
        Assert.Equal("pending", statusRoot.GetProperty("classification").GetString());
        string stagedHash = statusRoot.GetProperty("stagedHash").GetString()
            ?? throw new InvalidOperationException("Missing staged hash.");

        CliResult stage = await RunCliAsync(
            "edit",
            "stage",
            "--file",
            fixture.ProgramFilePath,
            "--ledger-summary",
            "integration test candidate",
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, stage.ExitCode);
        using JsonDocument stageDocument = JsonDocument.Parse(stage.StdOut);
        string stagedRecordId = stageDocument.RootElement.GetProperty("stagedRecordId").GetString()
            ?? throw new InvalidOperationException("Missing staged record id.");
        string stagedFilePath = stageDocument.RootElement.GetProperty("stagedFilePath").GetString()
            ?? throw new InvalidOperationException("Missing staged file path.");
        Assert.Equal(stagedHash, stageDocument.RootElement.GetProperty("stagedHash").GetString());

        await LaunchDiffAsync(fixture, stagedRecordId);
        File.Copy(stagedFilePath, fixture.ProgramFilePath, overwrite: true);

        CliResult decision = await RunCliAsync(
            "edit",
            "record-decision",
            "--staged-record-id",
            stagedRecordId,
            "--decision",
            "accepted",
            "--expected-staged-hash",
            stagedHash,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, decision.ExitCode);
        using JsonDocument decisionDocument = JsonDocument.Parse(decision.StdOut);
        Assert.Equal("accepted", decisionDocument.RootElement.GetProperty("classification").GetString());
        Assert.Contains("changed", await File.ReadAllTextAsync(fixture.ProgramFilePath), StringComparison.Ordinal);

        CliResult postAcceptStatus = await RunCliAsync(
            "edit",
            "status",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, postAcceptStatus.ExitCode);
        using JsonDocument postAcceptStatusDocument = JsonDocument.Parse(postAcceptStatus.StdOut);
        Assert.True(postAcceptStatusDocument.RootElement.GetProperty("requiresRefresh").GetBoolean());
        Assert.Equal("refresh-required", postAcceptStatusDocument.RootElement.GetProperty("classification").GetString());

        CliResult staleReplace = await RunCliAsync(
            "edit",
            "replace-text",
            "--file",
            fixture.ProgramFilePath,
            "--old-text",
            "changed",
            "--new-text",
            "changed again",
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(1, staleReplace.ExitCode);
        Assert.Contains("Run edit refresh", staleReplace.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_record_decision_accept_requires_expected_staged_hash()
    {
        CliFixture fixture = CreateFixture();

        CliResult refresh = await RunCliAsync(
            "edit",
            "refresh",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, refresh.ExitCode);
        using JsonDocument refreshDocument = JsonDocument.Parse(refresh.StdOut);
        string workingFilePath = refreshDocument.RootElement.GetProperty("workingFilePath").GetString()
            ?? throw new InvalidOperationException("Missing working file path.");
        await File.WriteAllTextAsync(workingFilePath, "namespace Example { internal static class Program { public static string Value => \"changed\"; } }");

        CliResult stage = await RunCliAsync(
            "edit",
            "stage",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, stage.ExitCode);
        using JsonDocument stageDocument = JsonDocument.Parse(stage.StdOut);
        string stagedRecordId = stageDocument.RootElement.GetProperty("stagedRecordId").GetString()
            ?? throw new InvalidOperationException("Missing staged record id.");
        string stagedFilePath = stageDocument.RootElement.GetProperty("stagedFilePath").GetString()
            ?? throw new InvalidOperationException("Missing staged file path.");
        File.Copy(stagedFilePath, fixture.ProgramFilePath, overwrite: true);

        CliResult decision = await RunCliAsync(
            "edit",
            "record-decision",
            "--staged-record-id",
            stagedRecordId,
            "--decision",
            "accepted",
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(1, decision.ExitCode);
        Assert.Contains("--expected-staged-hash is required", decision.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_replace_text_updates_working_candidate_and_preserves_crlf_line_endings()
    {
        CliFixture fixture = CreateFixture();
        string crlfContent = "namespace Example\r\n{\r\n    internal static class Program\r\n    {\r\n        public static string Value => \"old\";\r\n    }\r\n}\r\n";
        await File.WriteAllTextAsync(fixture.ProgramFilePath, crlfContent);

        CliResult refresh = await RunCliAsync(
            "edit",
            "refresh",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, refresh.ExitCode);
        using JsonDocument refreshDocument = JsonDocument.Parse(refresh.StdOut);
        string workingFilePath = refreshDocument.RootElement.GetProperty("workingFilePath").GetString()
            ?? throw new InvalidOperationException("Missing working file path.");
        string oldTextPath = Path.Combine(Path.GetTempPath(), "AIMonitorCliTests", Guid.NewGuid().ToString("N"), "old.txt");
        string newTextPath = Path.Combine(Path.GetDirectoryName(oldTextPath)!, "new.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(oldTextPath)!);
        await File.WriteAllTextAsync(oldTextPath, "public static string Value => \"old\";");
        await File.WriteAllTextAsync(newTextPath, "public static string Value => \"new\";\npublic static int Count => 1;");

        CliResult replace = await RunCliAsync(
            "edit",
            "replace-text",
            "--file",
            fixture.ProgramFilePath,
            "--old-text-file",
            oldTextPath,
            "--new-text-file",
            newTextPath,
            "--expected-matches",
            "1",
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, replace.ExitCode);
        using JsonDocument replaceDocument = JsonDocument.Parse(replace.StdOut);
        Assert.Equal(1, replaceDocument.RootElement.GetProperty("actualMatches").GetInt32());
        Assert.Equal("CRLF", replaceDocument.RootElement.GetProperty("lineEnding").GetString());

        string updatedWorkingText = await File.ReadAllTextAsync(workingFilePath);
        Assert.Contains("public static int Count => 1;", updatedWorkingText, StringComparison.Ordinal);
        Assert.Equal(0, CountBareLf(updatedWorkingText));

        CliResult status = await RunCliAsync(
            "edit",
            "status",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, status.ExitCode);
        using JsonDocument statusDocument = JsonDocument.Parse(status.StdOut);
        Assert.Equal("pending", statusDocument.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public async Task Edit_record_decision_rejected_requires_watched_source_to_remain_original()
    {
        CliFixture fixture = CreateFixture();

        CliResult refresh = await RunCliAsync(
            "edit",
            "refresh",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, refresh.ExitCode);
        using JsonDocument refreshDocument = JsonDocument.Parse(refresh.StdOut);
        string workingFilePath = refreshDocument.RootElement.GetProperty("workingFilePath").GetString()
            ?? throw new InvalidOperationException("Missing working file path.");
        string originalContent = await File.ReadAllTextAsync(fixture.ProgramFilePath);
        await File.WriteAllTextAsync(workingFilePath, "namespace Example { internal static class Program { public static string Value => \"rejected\"; } }");

        CliResult stage = await RunCliAsync(
            "edit",
            "stage",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, stage.ExitCode);
        using JsonDocument stageDocument = JsonDocument.Parse(stage.StdOut);
        string stagedRecordId = stageDocument.RootElement.GetProperty("stagedRecordId").GetString()
            ?? throw new InvalidOperationException("Missing staged record id.");
        string stagedFilePath = stageDocument.RootElement.GetProperty("stagedFilePath").GetString()
            ?? throw new InvalidOperationException("Missing staged file path.");
        File.Delete(stagedFilePath);

        CliResult decision = await RunCliAsync(
            "edit",
            "record-decision",
            "--staged-record-id",
            stagedRecordId,
            "--decision",
            "rejected",
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, decision.ExitCode);
        using JsonDocument decisionDocument = JsonDocument.Parse(decision.StdOut);
        Assert.Equal("rejected", decisionDocument.RootElement.GetProperty("classification").GetString());
        Assert.Equal(originalContent, await File.ReadAllTextAsync(fixture.ProgramFilePath));
    }

    [Fact]
    public async Task Edit_launch_diff_validation_reports_errors_only()
    {
        CliFixture fixture = CreateFixture();

        CliResult refresh = await RunCliAsync(
            "edit",
            "refresh",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, refresh.ExitCode);
        using JsonDocument refreshDocument = JsonDocument.Parse(refresh.StdOut);
        string workingFilePath = refreshDocument.RootElement.GetProperty("workingFilePath").GetString()
            ?? throw new InvalidOperationException("Missing working file path.");
        await File.WriteAllTextAsync(
            workingFilePath,
            "#warning intentional warning should not be shown\r\nnamespace Example { internal static class Program { public static string Value => \"broken\" } }\r\n");

        CliResult stage = await RunCliAsync(
            "edit",
            "stage",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, stage.ExitCode);
        using JsonDocument stageDocument = JsonDocument.Parse(stage.StdOut);
        string stagedRecordId = stageDocument.RootElement.GetProperty("stagedRecordId").GetString()
            ?? throw new InvalidOperationException("Missing staged record id.");

        CliResult launch = await RunCliAsync(
            "edit",
            "launch-diff",
            "--staged-record-id",
            stagedRecordId,
            "--force-validation",
            "--diff-tool",
            Path.Combine(fixture.RepositoryRoot, "missing-winmerge.exe"),
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, launch.ExitCode);
        using JsonDocument launchDocument = JsonDocument.Parse(launch.StdOut);
        JsonElement validation = launchDocument.RootElement.GetProperty("preMergeValidation");
        Assert.Equal("failed", validation.GetProperty("status").GetString());
        Assert.True(validation.GetProperty("isError").GetBoolean());
        Assert.True(validation.GetProperty("diagnosticCount").GetInt32() > 0);
        string diagnostics = string.Join(
            Environment.NewLine,
            validation.GetProperty("diagnostics").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(": error ", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(": warning ", diagnostics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("intentional warning", diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Edit_launch_diff_validation_does_not_block_on_warnings_only()
    {
        CliFixture fixture = CreateFixture();

        CliResult refresh = await RunCliAsync(
            "edit",
            "refresh",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, refresh.ExitCode);
        using JsonDocument refreshDocument = JsonDocument.Parse(refresh.StdOut);
        string workingFilePath = refreshDocument.RootElement.GetProperty("workingFilePath").GetString()
            ?? throw new InvalidOperationException("Missing working file path.");
        await File.WriteAllTextAsync(
            workingFilePath,
            "#warning intentional warning should not block\r\nnamespace Example { internal static class Program { public static string Value => \"warning-only\"; } }\r\n");

        CliResult stage = await RunCliAsync(
            "edit",
            "stage",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, stage.ExitCode);
        using JsonDocument stageDocument = JsonDocument.Parse(stage.StdOut);
        string stagedRecordId = stageDocument.RootElement.GetProperty("stagedRecordId").GetString()
            ?? throw new InvalidOperationException("Missing staged record id.");

        CliResult launch = await RunCliAsync(
            "edit",
            "launch-diff",
            "--staged-record-id",
            stagedRecordId,
            "--diff-tool",
            Path.Combine(fixture.RepositoryRoot, "missing-winmerge.exe"),
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, launch.ExitCode);
        using JsonDocument launchDocument = JsonDocument.Parse(launch.StdOut);
        JsonElement validation = launchDocument.RootElement.GetProperty("preMergeValidation");
        Assert.Equal("passed", validation.GetProperty("status").GetString());
        Assert.False(validation.GetProperty("isError").GetBoolean());
        Assert.Equal(0, validation.GetProperty("diagnosticCount").GetInt32());
        Assert.Empty(validation.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task Edit_launch_diff_validation_excludes_runtime_when_runtime_is_under_watched_root()
    {
        CliFixture fixture = CreateFixture(runtimeUnderWatchedRoot: true);
        string runtimeMarkerPath = Path.Combine(Path.GetDirectoryName(fixture.WatchedSolutionPath)!, "runtime", "marker.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(runtimeMarkerPath)!);
        await File.WriteAllTextAsync(runtimeMarkerPath, "runtime state must not be copied into validation");

        CliResult refresh = await RunCliAsync(
            "edit",
            "refresh",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, refresh.ExitCode);
        using JsonDocument refreshDocument = JsonDocument.Parse(refresh.StdOut);
        string workingFilePath = refreshDocument.RootElement.GetProperty("workingFilePath").GetString()
            ?? throw new InvalidOperationException("Missing working file path.");
        await File.WriteAllTextAsync(workingFilePath, "namespace Example { internal static class Program { public static string Value => \"validation\"; } }");

        CliResult stage = await RunCliAsync(
            "edit",
            "stage",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, stage.ExitCode);
        using JsonDocument stageDocument = JsonDocument.Parse(stage.StdOut);
        string stagedRecordId = stageDocument.RootElement.GetProperty("stagedRecordId").GetString()
            ?? throw new InvalidOperationException("Missing staged record id.");

        CliResult launch = await RunCliAsync(
            "edit",
            "launch-diff",
            "--staged-record-id",
            stagedRecordId,
            "--diff-tool",
            Path.Combine(fixture.RepositoryRoot, "missing-winmerge.exe"),
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, launch.ExitCode);
        using JsonDocument launchDocument = JsonDocument.Parse(launch.StdOut);
        string validationWorkspacePath = launchDocument.RootElement
            .GetProperty("preMergeValidation")
            .GetProperty("validationWorkspacePath")
            .GetString()
            ?? throw new InvalidOperationException("Missing validation workspace path.");
        Assert.False(File.Exists(Path.Combine(validationWorkspacePath, "runtime", "marker.txt")));
    }

    [Fact]
    public async Task Edit_launch_diff_blocks_when_staged_candidate_changes_after_stage()
    {
        CliFixture fixture = CreateFixture();

        CliResult refresh = await RunCliAsync(
            "edit",
            "refresh",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, refresh.ExitCode);
        using JsonDocument refreshDocument = JsonDocument.Parse(refresh.StdOut);
        string workingFilePath = refreshDocument.RootElement.GetProperty("workingFilePath").GetString()
            ?? throw new InvalidOperationException("Missing working file path.");
        await File.WriteAllTextAsync(workingFilePath, "namespace Example { internal static class Program { public static string Value => \"candidate\"; } }");

        CliResult stage = await RunCliAsync(
            "edit",
            "stage",
            "--file",
            fixture.ProgramFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, stage.ExitCode);
        using JsonDocument stageDocument = JsonDocument.Parse(stage.StdOut);
        string stagedRecordId = stageDocument.RootElement.GetProperty("stagedRecordId").GetString()
            ?? throw new InvalidOperationException("Missing staged record id.");
        string stagedFilePath = stageDocument.RootElement.GetProperty("stagedFilePath").GetString()
            ?? throw new InvalidOperationException("Missing staged file path.");
        await File.WriteAllTextAsync(stagedFilePath, "namespace Example { internal static class Program { public static string Value => \"tampered\"; } }");

        CliResult launch = await RunCliAsync(
            "edit",
            "launch-diff",
            "--staged-record-id",
            stagedRecordId,
            "--diff-tool",
            GetFakeDiffToolPath(),
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, launch.ExitCode);
        using JsonDocument launchDocument = JsonDocument.Parse(launch.StdOut);
        JsonElement validation = launchDocument.RootElement.GetProperty("preMergeValidation");
        Assert.Equal("staged-hash-mismatch", validation.GetProperty("status").GetString());
        Assert.True(validation.GetProperty("isError").GetBoolean());
        Assert.False(launchDocument.RootElement.GetProperty("diffLaunch").GetProperty("launched").GetBoolean());
        Assert.Equal("blocked-premerge-validation", launchDocument.RootElement.GetProperty("diffLaunch").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Edit_new_file_accepts_when_watched_file_matches_staged_candidate()
    {
        CliFixture fixture = CreateFixture();
        string newFilePath = Path.Combine(Path.GetDirectoryName(fixture.ProgramFilePath)!, "Generated", "NewThing.cs");

        CliResult create = await RunCliAsync(
            "edit",
            "new",
            "--file",
            newFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, create.ExitCode);
        using JsonDocument createDocument = JsonDocument.Parse(create.StdOut);
        string workingFilePath = createDocument.RootElement.GetProperty("workingFilePath").GetString()
            ?? throw new InvalidOperationException("Missing working file path.");
        Assert.True(createDocument.RootElement.GetProperty("isNewFile").GetBoolean());

        await File.WriteAllTextAsync(workingFilePath, "namespace Example.Generated { internal sealed class NewThing { } }");

        CliResult stage = await RunCliAsync(
            "edit",
            "stage",
            "--file",
            newFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, stage.ExitCode);
        using JsonDocument stageDocument = JsonDocument.Parse(stage.StdOut);
        string stagedRecordId = stageDocument.RootElement.GetProperty("stagedRecordId").GetString()
            ?? throw new InvalidOperationException("Missing staged record id.");
        string stagedFilePath = stageDocument.RootElement.GetProperty("stagedFilePath").GetString()
            ?? throw new InvalidOperationException("Missing staged file path.");
        string stagedHash = stageDocument.RootElement.GetProperty("stagedHash").GetString()
            ?? throw new InvalidOperationException("Missing staged hash.");
        string reviewBaselineFilePath = stageDocument.RootElement.GetProperty("reviewBaselineFilePath").GetString()
            ?? throw new InvalidOperationException("Missing review baseline path.");
        Assert.True(stageDocument.RootElement.GetProperty("isNewFile").GetBoolean());
        Assert.True(File.Exists(reviewBaselineFilePath));
        Assert.False(File.Exists(newFilePath));

        await LaunchDiffAsync(fixture, stagedRecordId);
        Assert.True(File.Exists(newFilePath));
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(newFilePath));
        Directory.CreateDirectory(Path.GetDirectoryName(newFilePath)!);
        File.Copy(stagedFilePath, newFilePath, overwrite: true);
        Assert.True(File.Exists(newFilePath));

        CliResult decision = await RunCliAsync(
            "edit",
            "record-decision",
            "--staged-record-id",
            stagedRecordId,
            "--decision",
            "accepted",
            "--expected-staged-hash",
            stagedHash,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, decision.ExitCode);
        using JsonDocument decisionDocument = JsonDocument.Parse(decision.StdOut);
        Assert.Equal("accepted", decisionDocument.RootElement.GetProperty("classification").GetString());
        Assert.True(File.Exists(newFilePath));
    }

    [Fact]
    public async Task Edit_new_file_launch_creates_blank_watched_file_and_reject_cleans_it_up()
    {
        CliFixture fixture = CreateFixture();
        string newFilePath = Path.Combine(Path.GetDirectoryName(fixture.ProgramFilePath)!, "Generated", "LaunchRejectedThing.cs");

        CliResult create = await RunCliAsync(
            "edit",
            "new",
            "--file",
            newFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, create.ExitCode);
        using JsonDocument createDocument = JsonDocument.Parse(create.StdOut);
        string workingFilePath = createDocument.RootElement.GetProperty("workingFilePath").GetString()
            ?? throw new InvalidOperationException("Missing working file path.");
        await File.WriteAllTextAsync(workingFilePath, "namespace Example.Generated { internal sealed class LaunchRejectedThing { } }");

        CliResult stage = await RunCliAsync(
            "edit",
            "stage",
            "--file",
            newFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, stage.ExitCode);
        using JsonDocument stageDocument = JsonDocument.Parse(stage.StdOut);
        string stagedRecordId = stageDocument.RootElement.GetProperty("stagedRecordId").GetString()
            ?? throw new InvalidOperationException("Missing staged record id.");
        Assert.False(File.Exists(newFilePath));

        await LaunchDiffAsync(fixture, stagedRecordId);
        Assert.True(File.Exists(newFilePath));
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(newFilePath));

        CliResult decision = await RunCliAsync(
            "edit",
            "record-decision",
            "--staged-record-id",
            stagedRecordId,
            "--decision",
            "rejected",
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, decision.ExitCode);
        using JsonDocument decisionDocument = JsonDocument.Parse(decision.StdOut);
        Assert.Equal("rejected", decisionDocument.RootElement.GetProperty("classification").GetString());
        Assert.False(File.Exists(newFilePath));
    }

    [Fact]
    public async Task Edit_new_file_rejects_when_watched_file_remains_missing()
    {
        CliFixture fixture = CreateFixture();
        string newFilePath = Path.Combine(Path.GetDirectoryName(fixture.ProgramFilePath)!, "Generated", "RejectedThing.cs");

        CliResult create = await RunCliAsync(
            "edit",
            "new",
            "--file",
            newFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, create.ExitCode);
        using JsonDocument createDocument = JsonDocument.Parse(create.StdOut);
        string workingFilePath = createDocument.RootElement.GetProperty("workingFilePath").GetString()
            ?? throw new InvalidOperationException("Missing working file path.");
        await File.WriteAllTextAsync(workingFilePath, "namespace Example.Generated { internal sealed class RejectedThing { } }");

        CliResult stage = await RunCliAsync(
            "edit",
            "stage",
            "--file",
            newFilePath,
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, stage.ExitCode);
        using JsonDocument stageDocument = JsonDocument.Parse(stage.StdOut);
        string stagedRecordId = stageDocument.RootElement.GetProperty("stagedRecordId").GetString()
            ?? throw new InvalidOperationException("Missing staged record id.");

        CliResult decision = await RunCliAsync(
            "edit",
            "record-decision",
            "--staged-record-id",
            stagedRecordId,
            "--decision",
            "rejected",
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, decision.ExitCode);
        using JsonDocument decisionDocument = JsonDocument.Parse(decision.StdOut);
        Assert.Equal("rejected", decisionDocument.RootElement.GetProperty("classification").GetString());
        Assert.False(File.Exists(newFilePath));
    }

    private static CliFixture CreateFixture(bool runtimeUnderWatchedRoot = false)
    {
        string repositoryRoot = FindRepositoryRoot();
        string tempRoot = Path.Combine(Path.GetTempPath(), "AIMonitorCliTests", Guid.NewGuid().ToString("N"));
        string settingsPath = Path.Combine(tempRoot, "config", "appsettings.json");
        string watchedSolutionPath = Path.Combine(tempRoot, "Watched", "Example.csproj");
        string programFilePath = Path.Combine(tempRoot, "Watched", "Program.cs");
        string programSymbolStableKey = "symbol:program";

        Directory.CreateDirectory(Path.GetDirectoryName(watchedSolutionPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(
            watchedSolutionPath,
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
        File.WriteAllText(programFilePath, "namespace Example { internal static class Program { } }");

        MonitorSettingsLoader.SaveLocal(
            repositoryRoot,
            watchedSolutionPath,
            runtimeUnderWatchedRoot
                ? Path.Combine(tempRoot, "Watched", "runtime")
                : Path.Combine(tempRoot, "runtime"),
            settingsPath);
        MonitorSettings settings = MonitorSettingsLoader.Load(repositoryRoot, settingsPath);
        SolutionIndexStore store = new(new SolutionIndexDatabase(MonitorDataPaths.GetDefaultIndexDatabasePath(settings)));
        store.SaveSnapshot(new MSBuildSolutionSnapshot(
            watchedSolutionPath,
            [
                new MSBuildProjectSnapshot(
                    "project:example",
                    "Example",
                    Path.Combine(tempRoot, "Watched", "Example.csproj"),
                    "C#",
                    "net10.0",
                    "",
                    "Exe",
                    "Microsoft.NET.Sdk",
                    "Example",
                    "Example",
                    "enable",
                    "enable",
                    "latest",
                    [
                        new MSBuildDocumentSnapshot("document:program", "Program.cs", programFilePath, [])
                    ],
                    [
                        new MSBuildSymbolSnapshot(
                            programSymbolStableKey,
                            "Program",
                            "NamedType",
                            "Example",
                            "",
                            programFilePath,
                            1,
                            1,
                            "Example.Program")
                    ],
                    [
                        new MSBuildReferenceSnapshot(
                            programSymbolStableKey,
                            programFilePath,
                            1,
                            45,
                            "IdentifierName",
                            "Program")
                    ],
                    [],
                    [],
                    [new MSBuildPackageReferenceSnapshot("Microsoft.Data.Sqlite", "10.0.0")],
                    [],
                    [],
                    ["DEBUG"])
            ],
            []));

        return new CliFixture(
            repositoryRoot,
            settingsPath,
            watchedSolutionPath,
            programFilePath,
            programSymbolStableKey);
    }

    private static async Task<CliResult> RunCliAsync(params string[] arguments)
    {
        string repositoryRoot = FindRepositoryRoot();
        string cliDll = Path.Combine(repositoryRoot, "src", "AIMonitor.Cli", "bin", GetBuildConfiguration(), "net10.0", "AIMonitor.Cli.dll");
        string joinedArguments = string.Join(" ", new[] { Quote(cliDll) }.Concat(arguments.Select(Quote)));
        using Process process = new();
        process.StartInfo = new ProcessStartInfo("dotnet", joinedArguments)
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        process.StartInfo.Environment["AIMONITOR_DISABLE_VALIDATION_DIALOG"] = "1";

        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return new CliResult(process.ExitCode, await stdout, await stderr);
    }

    private static async Task LaunchDiffAsync(CliFixture fixture, string stagedRecordId)
    {
        CliResult launch = await RunCliAsync(
            "edit",
            "launch-diff",
            "--staged-record-id",
            stagedRecordId,
            "--diff-tool",
            GetFakeDiffToolPath(),
            "--repo-root",
            fixture.RepositoryRoot,
            "--config",
            fixture.SettingsPath);

        Assert.Equal(0, launch.ExitCode);
        using JsonDocument launchDocument = JsonDocument.Parse(launch.StdOut);
        Assert.Equal("passed", launchDocument.RootElement.GetProperty("preMergeValidation").GetProperty("status").GetString());
        Assert.True(launchDocument.RootElement.GetProperty("diffLaunch").GetProperty("launched").GetBoolean());
    }

    private static string GetFakeDiffToolPath()
    {
        if (OperatingSystem.IsWindows())
        {
            string windowsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe");
            if (File.Exists(windowsPath))
            {
                return windowsPath;
            }
        }

        foreach (string candidate in new[] { "/usr/bin/true", "/bin/true" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Unable to find a harmless executable for diff-launch tests.");
    }

    private static string GetBuildConfiguration()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AIMonitor.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find AIMonitor.slnx.");
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static int CountBareLf(string text)
    {
        string withoutCrLf = text.Replace("\r\n", string.Empty, StringComparison.Ordinal);
        return withoutCrLf.Count(character => character == '\n');
    }

    private sealed record CliFixture(
        string RepositoryRoot,
        string SettingsPath,
        string WatchedSolutionPath,
        string ProgramFilePath,
        string ProgramSymbolStableKey);

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);
}
