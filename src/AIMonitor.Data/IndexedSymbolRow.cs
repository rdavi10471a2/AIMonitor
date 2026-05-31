namespace AIMonitor.Data;

public sealed record IndexedSymbolRow(
    string ProjectPath,
    string StableKey,
    string Name,
    string Kind,
    string Namespace,
    string ContainingType,
    string FilePath,
    int StartLine,
    int EndLine,
    string Signature);
