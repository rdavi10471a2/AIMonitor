namespace AIMonitor.Planning
{
    public sealed class PlanningDecisionEvidence
    {
        public string StagedRecordId { get; set; } = string.Empty;

        public string SessionId { get; set; } = string.Empty;

        public string RelativePath { get; set; } = string.Empty;

        public string WatchedFilePath { get; set; } = string.Empty;

        public string StagedHash { get; set; } = string.Empty;

        public string Decision { get; set; } = string.Empty;

        public string Classification { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string DecidedAtUtc { get; set; } = string.Empty;

        public bool IsNewFile { get; set; }

        public string PreMergeValidationStatus { get; set; } = string.Empty;

        public int PreMergeValidationDiagnosticCount { get; set; }

        public bool PreMergeValidationForceApproved { get; set; }

        public string IndexRefreshStatus { get; set; } = string.Empty;

        public bool IndexRefreshIsError { get; set; }
    }
}
