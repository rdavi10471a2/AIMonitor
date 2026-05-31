namespace AIMonitor.Data;

public sealed record IndexedReferenceRow(
    string ProjectPath,
    string TargetStableKey,
    string FilePath,
    int Line,
    int Column,
    string ReferenceKind,
    string Snippet);
