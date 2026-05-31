namespace AIMonitor.Data;

public sealed record IndexedDocumentRow(
    string ProjectPath,
    string Name,
    string FilePath,
    string Folders);
