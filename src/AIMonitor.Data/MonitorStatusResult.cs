namespace AIMonitor.Data;

public sealed class MonitorStatusResult
{
    public string WatchedSolutionPath { get; set; } = string.Empty;

    public string RuntimeRoot { get; set; } = string.Empty;

    public string DatabasePath { get; set; } = string.Empty;

    public bool DatabaseExists { get; set; }

    public string IndexedInputPath { get; set; } = string.Empty;

    public DateTimeOffset IndexedAtUtc { get; set; } = DateTimeOffset.MinValue;

    public int ProjectCount { get; set; }

    public int DocumentCount { get; set; }

    public int DiagnosticCount { get; set; }
}
