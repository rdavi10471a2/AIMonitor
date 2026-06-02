using AIMonitor.Core;
using System.Security.Cryptography;

namespace AIMonitor.Data;

public sealed class SolutionIndexQueryService
{
    private const int MaxFileLimit = 5000;
    private const int MaxSymbolLimit = 50000;

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
        IReadOnlyList<IndexedDocumentRow> documents = databaseExists ? store.ListDocuments() : [];
        IReadOnlyList<IndexedSymbolRow> symbols = databaseExists ? store.ListSymbols() : [];
        IReadOnlyList<IndexedReferenceRow> references = databaseExists ? store.ListReferences() : [];
        IReadOnlyList<IndexedCallSiteRow> callSites = databaseExists ? store.ListCallSites() : [];
        IReadOnlyList<IndexedRelationshipRow> relationships = databaseExists ? store.ListRelationships() : [];

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
            SymbolCount = symbols.Count,
            ReferenceCount = references.Count,
            CallSiteCount = callSites.Count,
            RelationshipCount = relationships.Count,
            StaleFileCount = documents.Count(IsStale),
            DiagnosticCount = summary.DiagnosticCount
        };
    }

    public SolutionIndexSummary GetSummary()
    {
        return store.GetSummary();
    }

    public SolutionIndexQueryResult QueryIndex(
        string scope = "solution",
        string? value = null,
        int maxFiles = 200,
        int maxSymbols = 500)
    {
        IReadOnlyList<IndexedDocumentRow> allDocuments = ListDocuments();
        IReadOnlyList<IndexedSymbolRow> allSymbols = ListSymbols();
        IEnumerable<IndexedDocumentRow> documents = allDocuments;
        IEnumerable<IndexedSymbolRow> symbols = allSymbols;
        string normalizedScope = NormalizeScope(scope);
        if (normalizedScope == "file")
        {
            string filePath = ResolveWatchedPath(RequireScopeValue(value, normalizedScope));
            documents = documents.Where(row => PathEquals(row.FilePath, filePath));
            symbols = symbols.Where(row => PathEquals(row.FilePath, filePath));
        }
        else if (normalizedScope == "folder")
        {
            string folderPath = ResolveWatchedPath(RequireScopeValue(value, normalizedScope));
            documents = documents.Where(row => PathIsUnderFolder(row.FilePath, folderPath));
            symbols = symbols.Where(row => PathIsUnderFolder(row.FilePath, folderPath));
        }
        else if (normalizedScope == "namespace")
        {
            string namespaceName = RequireScopeValue(value, normalizedScope);
            symbols = symbols.Where(row => row.Namespace.Equals(namespaceName, StringComparison.Ordinal));
            HashSet<string> files = symbols.Select(row => row.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            documents = documents.Where(row => files.Contains(row.FilePath));
        }
        else if (normalizedScope != "solution")
        {
            throw new ArgumentException("Scope must be solution, namespace, folder, or file.", nameof(scope));
        }

        IndexedDocumentRow[] scopedDocuments = documents.ToArray();
        IndexedSymbolRow[] scopedSymbols = symbols.ToArray();
        (int clampedFileLimit, bool filesClamped) = ClampLimit(maxFiles, MaxFileLimit);
        (int clampedSymbolLimit, bool symbolsClamped) = ClampLimit(maxSymbols, MaxSymbolLimit);

        return new SolutionIndexQueryResult(
            GetMonitorStatus(),
            scopedDocuments.Take(clampedFileLimit).ToArray(),
            scopedSymbols.Take(clampedSymbolLimit).ToArray(),
            normalizedScope,
            value,
            clampedFileLimit,
            clampedSymbolLimit,
            scopedDocuments.Length,
            scopedSymbols.Length,
            filesClamped || symbolsClamped);
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

    public IReadOnlyList<IndexedCallSiteRow> ListCallSites(string? stableKey = null)
    {
        return store.ListCallSites(stableKey);
    }

    public IReadOnlyList<IndexedRelationshipRow> ListRelationships(
        string? stableKey = null,
        string direction = "both",
        string? relationshipKind = null)
    {
        return store.ListRelationships(stableKey, direction, relationshipKind);
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

    private static bool IsStale(IndexedDocumentRow document)
    {
        if (string.IsNullOrWhiteSpace(document.ContentHash))
        {
            return false;
        }

        if (!File.Exists(document.FilePath))
        {
            return true;
        }

        using FileStream stream = File.OpenRead(document.FilePath);
        string currentHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return !currentHash.Equals(document.ContentHash, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveWatchedPath(string path)
    {
        return Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(settings.WatchedProjectFolder, path));
    }

    private static string NormalizeScope(string scope)
    {
        return string.IsNullOrWhiteSpace(scope)
            ? "solution"
            : scope.Trim().ToLowerInvariant();
    }

    private static string RequireScopeValue(string? value, string scope)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Scope '{scope}' requires a value.", nameof(value))
            : value.Trim();
    }

    private static bool PathEquals(string left, string right)
    {
        return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathIsUnderFolder(string filePath, string folderPath)
    {
        string fullFilePath = Path.GetFullPath(filePath);
        string fullFolderPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullFilePath.Equals(fullFolderPath, StringComparison.OrdinalIgnoreCase)
            || fullFilePath.StartsWith(fullFolderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullFilePath.StartsWith(fullFolderPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static (int Value, bool Clamped) ClampLimit(int requested, int maximum)
    {
        int clamped = Math.Clamp(requested, 0, maximum);
        return (clamped, clamped != requested);
    }
}
