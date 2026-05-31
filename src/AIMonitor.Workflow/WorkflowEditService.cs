using AIMonitor.Core;
using System.Text.Json;

namespace AIMonitor.Workflow;

public sealed class WorkflowEditService
{
    private const string NewFileHash = "<new-file>";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly WorkflowEditPaths paths;

    public WorkflowEditService(MonitorSettings settings)
    {
        paths = new WorkflowEditPaths(settings);
    }

    public EditSessionStatus Refresh(string watchedFilePath)
    {
        string fullWatchedPath = Path.GetFullPath(watchedFilePath);
        if (!File.Exists(fullWatchedPath))
        {
            throw new FileNotFoundException("Watched file was not found.", fullWatchedPath);
        }

        string workingFilePath = paths.GetWorkingFilePath(fullWatchedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(workingFilePath) ?? ".");
        File.Copy(fullWatchedPath, workingFilePath, overwrite: true);

        EditSessionManifest manifest = new()
        {
            WatchedFilePath = fullWatchedPath,
            WorkingFilePath = workingFilePath,
            RelativePath = paths.GetRelativeWatchedPath(fullWatchedPath),
            OriginalHash = FileHash.Compute(fullWatchedPath),
            OriginalNormalizedHash = FileHash.ComputeNormalizedFile(fullWatchedPath),
            RequiresRefresh = false,
            RefreshedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        SaveManifest(fullWatchedPath, manifest);
        return GetStatus(fullWatchedPath);
    }

    public EditSessionStatus NewFile(string watchedFilePath)
    {
        string fullWatchedPath = Path.GetFullPath(watchedFilePath);
        paths.GetRelativeWatchedPath(fullWatchedPath);
        if (File.Exists(fullWatchedPath))
        {
            throw new InvalidOperationException("Watched file already exists. Run edit refresh for existing files.");
        }

        string workingFilePath = paths.GetWorkingFilePath(fullWatchedPath);
        Directory.CreateDirectory(Path.GetDirectoryName(workingFilePath) ?? ".");
        if (!File.Exists(workingFilePath))
        {
            File.WriteAllText(workingFilePath, string.Empty);
        }

        EditSessionManifest manifest = new()
        {
            WatchedFilePath = fullWatchedPath,
            WorkingFilePath = workingFilePath,
            RelativePath = paths.GetRelativeWatchedPath(fullWatchedPath),
            OriginalHash = NewFileHash,
            OriginalNormalizedHash = NewFileHash,
            IsNewFile = true,
            RequiresRefresh = false,
            RefreshedAtUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        SaveManifest(fullWatchedPath, manifest);
        return GetStatus(fullWatchedPath);
    }

    public EditSessionStatus GetStatus(string watchedFilePath)
    {
        string fullWatchedPath = Path.GetFullPath(watchedFilePath);
        string workingFilePath = paths.GetWorkingFilePath(fullWatchedPath);
        EditSessionManifest? manifest = LoadManifest(fullWatchedPath);

        EditSessionStatus status = new()
        {
            WatchedFilePath = fullWatchedPath,
            WorkingFilePath = workingFilePath,
            RelativePath = paths.GetRelativeWatchedPath(fullWatchedPath),
            HasSession = manifest is not null,
            WatchedFileExists = File.Exists(fullWatchedPath),
            WorkingFileExists = File.Exists(workingFilePath),
            IsNewFile = manifest?.IsNewFile ?? false,
            RequiresRefresh = manifest?.RequiresRefresh ?? false,
            OriginalHash = manifest?.OriginalHash ?? string.Empty,
            LastDecision = manifest?.LastDecision ?? string.Empty,
            LastDecisionAtUtc = manifest?.LastDecisionAtUtc ?? string.Empty,
            LastCompareRunId = manifest?.LastCompareRunId ?? string.Empty,
            LastCompareSnapshotPath = manifest?.LastCompareSnapshotPath ?? string.Empty,
            LastLedgerPath = manifest?.LastLedgerPath ?? string.Empty,
            LastStagedRecordId = manifest?.LastStagedRecordId ?? string.Empty,
            LastStagedRecordPath = manifest?.LastStagedRecordPath ?? string.Empty
        };

        if (status.WatchedFileExists)
        {
            status.WatchedHash = FileHash.Compute(fullWatchedPath);
        }

        if (status.WorkingFileExists)
        {
            status.StagedHash = FileHash.Compute(workingFilePath);
        }

        if (manifest is null)
        {
            status.Classification = "no-session";
            status.Message = "No edit session exists for this file. Run edit refresh first.";
            return status;
        }

        if (manifest.RequiresRefresh)
        {
            status.Classification = "refresh-required";
            status.Message = "Previous decision was accepted. Run edit refresh before editing or staging again so hashes and saved line endings reflect watched source.";
            return status;
        }

        if (!status.WorkingFileExists)
        {
            status.Classification = "missing-working";
            status.Message = "The working candidate file is missing.";
            return status;
        }

        if (!status.WatchedFileExists && !status.IsNewFile)
        {
            status.Classification = "missing-watched";
            status.Message = "The watched source file is missing.";
            return status;
        }

        if (!status.WatchedFileExists && status.IsNewFile)
        {
            status.Classification = string.IsNullOrWhiteSpace(status.StagedHash)
                ? "new-file-empty-working"
                : "new-file-pending";
            status.Message = "New-file candidate exists only in the monitor-owned Working folder.";
            return status;
        }

        ReviewDecisionResult pending = new ReviewDecisionClassifier().Classify(
            new ReviewDecisionInput(
                "rejected",
                manifest.OriginalHash,
                status.StagedHash,
                status.WatchedHash,
                FileHash.ComputeNormalizedFile(fullWatchedPath),
                FileHash.ComputeNormalizedFile(workingFilePath),
                manifest.IsNewFile,
                status.WatchedFileExists));
        if (pending.Classification == "rejected")
        {
            status.Classification = status.StagedHash.Equals(manifest.OriginalHash, StringComparison.OrdinalIgnoreCase)
                ? "unchanged"
                : "pending";
            status.Message = status.Classification == "unchanged"
                ? "Working candidate matches the original watched file."
                : "Working candidate differs from the watched file and has not been accepted.";
            return status;
        }

        ReviewDecisionResult accepted = new ReviewDecisionClassifier().Classify(
            new ReviewDecisionInput(
                "accepted",
                manifest.OriginalHash,
                status.StagedHash,
                status.WatchedHash,
                FileHash.ComputeNormalizedFile(fullWatchedPath),
                FileHash.ComputeNormalizedFile(workingFilePath),
                manifest.IsNewFile,
                status.WatchedFileExists));
        status.Classification = accepted.Classification;
        status.Message = accepted.Message;
        return status;
    }

    public ReplaceTextResult ReplaceText(
        string watchedFilePath,
        string oldText,
        string newText,
        int? expectedMatches = null,
        string? expectedWorkingHash = null)
    {
        if (string.IsNullOrEmpty(oldText))
        {
            throw new InvalidOperationException("Replacement old text must not be empty.");
        }

        string fullWatchedPath = Path.GetFullPath(watchedFilePath);
        EditSessionManifest manifest = LoadManifest(fullWatchedPath)
            ?? throw new InvalidOperationException("No edit session exists for this file. Run edit refresh first.");
        EnsureSessionCanEdit(manifest);
        if (!File.Exists(manifest.WorkingFilePath))
        {
            throw new FileNotFoundException("Working candidate file was not found.", manifest.WorkingFilePath);
        }

        string originalWorkingHash = FileHash.Compute(manifest.WorkingFilePath);
        if (!string.IsNullOrWhiteSpace(expectedWorkingHash)
            && !originalWorkingHash.Equals(expectedWorkingHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Working candidate hash does not match --expected-working-hash. Refresh status before replacing text.");
        }

        string workingText = File.ReadAllText(manifest.WorkingFilePath);
        string lineEnding = DetectDominantLineEnding(workingText);
        string normalizedOldText = NormalizeLineEndingsForFile(oldText, lineEnding);
        string normalizedNewText = NormalizeLineEndingsForFile(newText, lineEnding);
        int matchCount = CountOccurrences(workingText, oldText);
        string textToFind = oldText;
        if (matchCount == 0 && !oldText.Equals(normalizedOldText, StringComparison.Ordinal))
        {
            matchCount = CountOccurrences(workingText, normalizedOldText);
            textToFind = normalizedOldText;
        }

        if (expectedMatches.HasValue && matchCount != expectedMatches.Value)
        {
            throw new InvalidOperationException($"Replacement match count was {matchCount}, expected {expectedMatches.Value}.");
        }

        if (matchCount == 0)
        {
            throw new InvalidOperationException("Replacement old text was not found in the working candidate.");
        }

        string updatedText = workingText.Replace(textToFind, normalizedNewText, StringComparison.Ordinal);
        File.WriteAllText(manifest.WorkingFilePath, updatedText);
        return new ReplaceTextResult
        {
            WatchedFilePath = fullWatchedPath,
            WorkingFilePath = manifest.WorkingFilePath,
            RelativePath = manifest.RelativePath,
            OldWorkingHash = originalWorkingHash,
            NewWorkingHash = FileHash.Compute(manifest.WorkingFilePath),
            LineEnding = lineEnding == "\r\n" ? "CRLF" : "LF",
            ActualMatches = matchCount,
            ExpectedMatches = expectedMatches,
            Changed = true,
            Message = "Working candidate text was replaced."
        };
    }

    public CompareSnapshotResult Compare(string watchedFilePath, string? ledgerSummary = null)
    {
        string fullWatchedPath = Path.GetFullPath(watchedFilePath);
        EditSessionManifest manifest = LoadManifest(fullWatchedPath)
            ?? throw new InvalidOperationException("No edit session exists for this file. Run edit refresh first.");
        EnsureSessionCanEdit(manifest);
        if (!File.Exists(manifest.WorkingFilePath))
        {
            throw new FileNotFoundException("Working candidate file was not found.", manifest.WorkingFilePath);
        }

        return CreateCompareSnapshot(
            fullWatchedPath,
            manifest,
            manifest.WorkingFilePath,
            ledgerSummary);
    }

    private CompareSnapshotResult CreateCompareSnapshot(
        string fullWatchedPath,
        EditSessionManifest manifest,
        string candidateFilePath,
        string? ledgerSummary)
    {
        string runId = $"{DateTimeOffset.Now:yyyyMMddTHHmmssfff}-{Environment.ProcessId}-{Guid.NewGuid():N}"[..44];
        WorkflowRunRecorder recorder = new(paths.HistoryRoot, runId);
        recorder.Stage(runId, "compare-start", new Dictionary<string, string>
        {
            ["watchedFile"] = fullWatchedPath,
            ["workingFile"] = manifest.WorkingFilePath,
            ["candidateFile"] = candidateFilePath
        });

        string reviewBaselineFilePath = fullWatchedPath;
        if (manifest.IsNewFile)
        {
            reviewBaselineFilePath = CreateBlankReviewBaseline(manifest.RelativePath, runId);
        }

        if (!manifest.IsNewFile && FilesAreIdentical(fullWatchedPath, candidateFilePath))
        {
            recorder.Stage(runId, "compare-skipped-identical");
            return new CompareSnapshotResult
            {
                WatchedFilePath = reviewBaselineFilePath,
                WorkingFilePath = candidateFilePath,
                RunId = runId,
                RunLogPath = recorder.RunLogPath,
                TelemetryPath = recorder.TelemetryPath,
                Classification = "no-differences",
                Message = "No differences found between watched source and working candidate.",
                HasDifferences = false
            };
        }

        string historyDirectory = Path.Combine(paths.HistoryRoot, Path.GetDirectoryName(manifest.RelativePath) ?? string.Empty);
        Directory.CreateDirectory(historyDirectory);
        string baseName = Path.GetFileNameWithoutExtension(candidateFilePath);
        string extension = Path.GetExtension(candidateFilePath);
        string proposedSnapshotPath = Path.Combine(historyDirectory, $"{baseName}_{DateTimeOffset.Now:yyyyMMdd_HHmmssfff}_{Sanitize(runId)}{extension}");
        File.Copy(candidateFilePath, proposedSnapshotPath, overwrite: false);
        string ledgerPath = new FileLedgerWriter().AppendEntry(
            paths.HistoryRoot,
            manifest.RelativePath,
            fullWatchedPath,
            candidateFilePath,
            proposedSnapshotPath,
            ledgerSummary);

        manifest.LastCompareRunId = runId;
        manifest.LastCompareSnapshotPath = proposedSnapshotPath;
        manifest.LastLedgerPath = ledgerPath;
        SaveManifest(fullWatchedPath, manifest);

        recorder.Stage(runId, "compare-snapshot-created", new Dictionary<string, string>
        {
            ["snapshot"] = proposedSnapshotPath,
            ["ledger"] = ledgerPath
        });
        recorder.Stage(runId, "compare-ready");

        return new CompareSnapshotResult
        {
            WatchedFilePath = reviewBaselineFilePath,
            WorkingFilePath = candidateFilePath,
            ProposedSnapshotPath = proposedSnapshotPath,
            LedgerPath = ledgerPath,
            RunLogPath = recorder.RunLogPath,
            TelemetryPath = recorder.TelemetryPath,
            RunId = runId,
            Classification = "compare-ready",
            Message = "Compare snapshot created.",
            HasDifferences = true
        };
    }

    public StagedEditRecord Stage(string watchedFilePath, string? ledgerSummary = null)
    {
        string fullWatchedPath = Path.GetFullPath(watchedFilePath);
        EditSessionManifest manifest = LoadManifest(fullWatchedPath)
            ?? throw new InvalidOperationException("No edit session exists for this file. Run edit refresh first.");
        EnsureSessionCanEdit(manifest);
        if (!File.Exists(manifest.WorkingFilePath))
        {
            throw new FileNotFoundException("Working candidate file was not found.", manifest.WorkingFilePath);
        }

        if (!File.Exists(fullWatchedPath) && !manifest.IsNewFile)
        {
            throw new FileNotFoundException("Watched file was not found.", fullWatchedPath);
        }

        if (manifest.IsNewFile && File.Exists(fullWatchedPath))
        {
            throw new InvalidOperationException("New-file target now exists in watched source. Refresh or choose another path before staging.");
        }

        string watchedHash = File.Exists(fullWatchedPath) ? FileHash.Compute(fullWatchedPath) : string.Empty;
        if (!manifest.IsNewFile && !watchedHash.Equals(manifest.OriginalHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Watched file changed since refresh. Refresh before staging.");
        }

        if (!manifest.IsNewFile && FilesAreIdentical(fullWatchedPath, manifest.WorkingFilePath))
        {
            throw new InvalidOperationException("Working candidate matches watched source. There is nothing to stage.");
        }

        string stagedRecordId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}"[..42];
        string stagedFilePath = paths.GetStagedFilePath(stagedRecordId, manifest.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(stagedFilePath) ?? ".");
        File.Copy(manifest.WorkingFilePath, stagedFilePath, overwrite: false);

        CompareSnapshotResult compare = CreateCompareSnapshot(
            fullWatchedPath,
            manifest,
            stagedFilePath,
            ledgerSummary);
        StagedEditRecord record = new()
        {
            StagedRecordId = stagedRecordId,
            WatchedFilePath = fullWatchedPath,
            WorkingFilePath = manifest.WorkingFilePath,
            StagedFilePath = stagedFilePath,
            RelativePath = manifest.RelativePath,
            OriginalHash = manifest.OriginalHash,
            OriginalNormalizedHash = manifest.OriginalNormalizedHash,
            IsNewFile = manifest.IsNewFile,
            ReviewBaselineFilePath = manifest.IsNewFile
                ? compare.WatchedFilePath
                : fullWatchedPath,
            StagedHash = FileHash.Compute(stagedFilePath),
            StagedNormalizedHash = FileHash.ComputeNormalizedFile(stagedFilePath),
            CreatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Status = "staged",
            Message = "Working candidate was snapshotted for WinMerge review.",
            LastCompareRunId = compare.RunId,
            LastCompareSnapshotPath = compare.ProposedSnapshotPath,
            LastLedgerPath = compare.LedgerPath
        };
        SaveStagedRecord(record);

        manifest.LastStagedRecordId = stagedRecordId;
        manifest.LastStagedRecordPath = paths.GetStagedRecordPath(stagedRecordId);
        manifest.LastCompareRunId = compare.RunId;
        manifest.LastCompareSnapshotPath = compare.ProposedSnapshotPath;
        manifest.LastLedgerPath = compare.LedgerPath;
        SaveManifest(fullWatchedPath, manifest);
        return record;
    }

    public StagedEditRecord GetStagedRecord(string stagedRecordId)
    {
        return LoadStagedRecord(stagedRecordId)
            ?? throw new InvalidOperationException($"Staged edit record was not found: {stagedRecordId}");
    }

    public StagedEditRecord RecordDiffLaunch(string stagedRecordId, bool launched, string message)
    {
        StagedEditRecord record = GetStagedRecord(stagedRecordId);
        record.LaunchStatus = launched ? "launched" : "not-launched";
        record.LaunchMessage = message;
        record.LaunchedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        SaveStagedRecord(record);
        return record;
    }

    public StagedEditRecord RecordDecision(string stagedRecordId, string decision, string? expectedStagedHash = null)
    {
        StagedEditRecord record = GetStagedRecord(stagedRecordId);
        if (!File.Exists(record.WatchedFilePath) && !record.IsNewFile)
        {
            throw new FileNotFoundException("Watched file was not found.", record.WatchedFilePath);
        }

        if (!File.Exists(record.StagedFilePath))
        {
            throw new FileNotFoundException("Staged candidate file was not found.", record.StagedFilePath);
        }

        string normalizedDecision = decision.Trim().ToLowerInvariant();
        string currentStagedHash = FileHash.Compute(record.StagedFilePath);
        if (!currentStagedHash.Equals(record.StagedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Staged candidate content changed after staging. Edit the Working file and run edit stage again.");
        }

        if (normalizedDecision == "accepted")
        {
            if (!record.LaunchStatus.Equals("launched", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Cannot accept a staged record before a successful diff review launch.");
            }

            if (string.IsNullOrWhiteSpace(expectedStagedHash))
            {
                throw new InvalidOperationException("--expected-staged-hash is required when recording an accepted decision.");
            }

            if (!record.StagedHash.Equals(expectedStagedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Staged record hash does not match --expected-staged-hash.");
            }
        }

        string reviewedFilePath = GetReviewedFilePath(record);
        bool reviewedFileExists = File.Exists(reviewedFilePath);
        string reviewedHash = reviewedFileExists ? FileHash.Compute(reviewedFilePath) : string.Empty;
        ReviewDecisionResult result = new ReviewDecisionClassifier().Classify(
            new ReviewDecisionInput(
                decision,
                record.OriginalHash,
                record.StagedHash,
                reviewedHash,
                reviewedFileExists ? FileHash.ComputeNormalizedFile(reviewedFilePath) : null,
                string.IsNullOrWhiteSpace(record.StagedNormalizedHash) ? null : record.StagedNormalizedHash,
                record.IsNewFile,
                reviewedFileExists));

        record.Decision = decision;
        record.DecisionAtUtc = DateTimeOffset.UtcNow.ToString("O");
        record.Classification = result.Classification;
        record.Message = result.Message;
        record.Status = result.Classification;
        SaveStagedRecord(record);

        EditSessionManifest? manifest = LoadManifest(record.WatchedFilePath);
        if (manifest is not null)
        {
            manifest.LastDecision = decision;
            manifest.LastDecisionAtUtc = record.DecisionAtUtc;
            manifest.RequiresRefresh = result.Classification is "accepted" or "accepted-normalized";
            SaveManifest(record.WatchedFilePath, manifest);
        }

        return record;
    }

    private static string GetReviewedFilePath(StagedEditRecord record)
    {
        return record.WatchedFilePath;
    }

    public EditSessionStatus Accept(string watchedFilePath, string expectedStagedHash)
    {
        string fullWatchedPath = Path.GetFullPath(watchedFilePath);
        EditSessionManifest manifest = LoadManifest(fullWatchedPath)
            ?? throw new InvalidOperationException("No edit session exists for this file. Run edit refresh first.");
        if (string.IsNullOrWhiteSpace(manifest.LastStagedRecordId))
        {
            throw new InvalidOperationException("No staged record exists for this file. Run edit stage first.");
        }

        StagedEditRecord record = GetStagedRecord(manifest.LastStagedRecordId);
        if (!record.StagedHash.Equals(expectedStagedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Staged record hash does not match --expected-hash.");
        }

        RecordDecision(record.StagedRecordId, "accepted", expectedStagedHash);
        return GetStatus(fullWatchedPath);
    }

    public EditSessionStatus Reject(string watchedFilePath)
    {
        string fullWatchedPath = Path.GetFullPath(watchedFilePath);
        EditSessionManifest manifest = LoadManifest(fullWatchedPath)
            ?? throw new InvalidOperationException("No edit session exists for this file. Run edit refresh first.");
        if (string.IsNullOrWhiteSpace(manifest.LastStagedRecordId))
        {
            throw new InvalidOperationException("No staged record exists for this file. Run edit stage first.");
        }

        RecordDecision(manifest.LastStagedRecordId, "rejected");
        return GetStatus(fullWatchedPath);
    }

    private EditSessionManifest? LoadManifest(string watchedFilePath)
    {
        string metadataPath = paths.GetMetadataPath(watchedFilePath);
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<EditSessionManifest>(File.ReadAllText(metadataPath), JsonOptions);
    }

    private void SaveManifest(string watchedFilePath, EditSessionManifest manifest)
    {
        string metadataPath = paths.GetMetadataPath(watchedFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath) ?? ".");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private StagedEditRecord? LoadStagedRecord(string stagedRecordId)
    {
        string recordPath = paths.GetStagedRecordPath(stagedRecordId);
        if (!File.Exists(recordPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<StagedEditRecord>(File.ReadAllText(recordPath), JsonOptions);
    }

    private void SaveStagedRecord(StagedEditRecord record)
    {
        string recordPath = paths.GetStagedRecordPath(record.StagedRecordId);
        Directory.CreateDirectory(Path.GetDirectoryName(recordPath) ?? ".");
        File.WriteAllText(recordPath, JsonSerializer.Serialize(record, JsonOptions));
    }

    private static void EnsureSessionCanEdit(EditSessionManifest manifest)
    {
        if (manifest.RequiresRefresh)
        {
            throw new InvalidOperationException("Previous decision was accepted. Run edit refresh before editing or staging this file again.");
        }
    }

    private static bool FilesAreIdentical(string leftPath, string rightPath)
    {
        FileInfo leftInfo = new(leftPath);
        FileInfo rightInfo = new(rightPath);
        if (!leftInfo.Exists || !rightInfo.Exists || leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        const int BufferSize = 16 * 1024;
        byte[] leftBuffer = new byte[BufferSize];
        byte[] rightBuffer = new byte[BufferSize];
        using FileStream left = File.OpenRead(leftPath);
        using FileStream right = File.OpenRead(rightPath);
        while (true)
        {
            int leftRead = left.Read(leftBuffer, 0, leftBuffer.Length);
            int rightRead = right.Read(rightBuffer, 0, rightBuffer.Length);
            if (leftRead != rightRead)
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }

            for (int index = 0; index < leftRead; index++)
            {
                if (leftBuffer[index] != rightBuffer[index])
                {
                    return false;
                }
            }
        }
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private string CreateBlankReviewBaseline(string relativePath, string runId)
    {
        string blankPath = Path.Combine(paths.HistoryRoot, "NewFileBaselines", Sanitize(runId), relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(blankPath) ?? ".");
        File.WriteAllText(blankPath, string.Empty);
        return blankPath;
    }

    private static string DetectDominantLineEnding(string text)
    {
        int crlfCount = CountOccurrences(text, "\r\n");
        string withoutCrLf = text.Replace("\r\n", string.Empty, StringComparison.Ordinal);
        int lfOnlyCount = CountOccurrences(withoutCrLf, "\n");
        return crlfCount >= lfOnlyCount ? "\r\n" : "\n";
    }

    private static string NormalizeLineEndingsForFile(string text, string lineEnding)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", lineEnding, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        int count = 0;
        int startIndex = 0;
        while (true)
        {
            int index = text.IndexOf(value, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            startIndex = index + value.Length;
        }
    }
}
