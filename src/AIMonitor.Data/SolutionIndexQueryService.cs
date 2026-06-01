using AIMonitor.Core;

namespace AIMonitor.Data;

public sealed class SolutionIndexQueryService
{
    private readonly MonitorSettings settings;
    private readonly SolutionIndexStore store;

    public SolutionIndexQueryService(
        MonitorSettings settings,
        SolutionIndexStore store,
        string databasePath)
    {
        this.settings = settings;
        this.store = store;
        DatabasePath = databasePath;
    }

    public string DatabasePath { get; }

    public static SolutionIndexQueryService Create(MonitorSettings settings)
    {
        string databasePath = MonitorDataPaths.GetDefaultIndexDatabasePath(settings);
        SolutionIndexStore store = new(new SolutionIndexDatabase(databasePath));
        return new SolutionIndexQueryService(settings, store, databasePath);
    }

    public MonitorStatusResult GetMonitorStatus()
    {
        bool databaseExists = File.Exists(DatabasePath);
        SolutionIndexSummary summary = databaseExists
            ? store.GetSummary()
            : new SolutionIndexSummary(string.Empty, DateTimeOffset.MinValue, 0, 0, 0);

        return new MonitorStatusResult
        {
            WatchedSolutionPath = settings.WatchedSolutionPath,
            RuntimeRoot = settings.RuntimeRoot,
            DatabasePath = DatabasePath,
            DatabaseExists = databaseExists,
            IndexedInputPath = summary.InputPath,
            IndexedAtUtc = summary.IndexedAtUtc,
            ProjectCount = summary.ProjectCount,
            DocumentCount = summary.DocumentCount,
            DiagnosticCount = summary.DiagnosticCount
        };
    }

    public SolutionIndexSummary GetSummary()
    {
        return store.GetSummary();
    }

    public IReadOnlyList<IndexedProjectRow> ListProjects()
    {
        return store.ListProjects();
    }

    public IReadOnlyList<IndexedDocumentRow> ListDocuments(string? projectPath = null, string? filePath = null)
    {
        IEnumerable<IndexedDocumentRow> rows = store.ListDocuments();

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            rows = rows.Where(row => string.Equals(row.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            rows = rows.Where(row => string.Equals(row.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        }

        return rows.ToList();
    }

    public IReadOnlyList<IndexedSymbolRow> ListSymbols(string? filePath = null, string? name = null)
    {
        IEnumerable<IndexedSymbolRow> rows = store.ListSymbols();

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            rows = rows.Where(row => string.Equals(row.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            rows = rows.Where(row => string.Equals(row.Name, name, StringComparison.Ordinal));
        }

        return rows.ToList();
    }

    public IReadOnlyList<IndexedReferenceRow> ListReferences(string? stableKey = null)
    {
        return store.ListReferences(stableKey);
    }

    public IReadOnlyList<IndexedReferenceRow> ListReferencesInFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A file path is required.", nameof(filePath));
        }

        return store.ListReferences()
            .Where(row => string.Equals(row.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IReadOnlyList<IndexedPackageReferenceRow> ListPackageReferences()
    {
        return store.ListPackageReferences();
    }
}
