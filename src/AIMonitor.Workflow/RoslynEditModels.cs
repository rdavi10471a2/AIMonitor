using System.Text.Json.Serialization;

namespace AIMonitor.Workflow;

public sealed record RoslynEditResult(
    string Operation,
    string WatchedFilePath,
    string WorkingFilePath,
    string RelativePath,
    string Status,
    string Message,
    string WorkingHash);

public sealed record RoslynSymbolSelector(
    string? ContainingNamespace = null,
    string? ContainingType = null,
    string? MemberKind = null,
    string? Name = null,
    IReadOnlyList<string>? ParameterTypes = null,
    int? Arity = null,
    string? StableSymbolKey = null);

public sealed record RoslynSymbolReadResult(
    string WatchedFilePath,
    string WorkingFilePath,
    string RelativePath,
    string Kind,
    string Name,
    int StartLine,
    int EndLine,
    string Text);

public sealed record RoslynSourceMapResult(
    string Scope,
    string Mode,
    string ModePurpose,
    string? RequestedPath,
    string? RequestedNamespace,
    int FileCount,
    int SymbolCount,
    IReadOnlyList<RoslynSourceMapFile> Files);

public sealed record RoslynSourceMapFile(
    string SourceFilePath,
    string RelativePath,
    string ParseStatus,
    int DiagnosticCount,
    IReadOnlyList<string> Usings,
    IReadOnlyList<string> Namespaces,
    IReadOnlyList<RoslynSourceMapSymbol> Symbols);

public sealed record RoslynSourceMapSymbol(
    string Kind,
    string Name,
    string StableSymbolKey,
    string Signature,
    string? Namespace,
    string? ContainingType,
    int StartLine,
    int EndLine,
    string TextHash,
    IReadOnlyList<string> Modifiers,
    string? ReturnType,
    IReadOnlyList<string> ParameterTypes,
    IReadOnlyList<string> ParameterNames,
    int Arity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SyntaxKind = null);

public sealed record RoslynFileOutlineResult(
    string SourceFilePath,
    string RelativePath,
    string ParseStatus,
    int DiagnosticCount,
    IReadOnlyList<RoslynFileOutlineItem> Items);

public sealed record RoslynFileOutlineItem(
    string Kind,
    string Name,
    int StartLine,
    int EndLine,
    string Signature,
    string? Namespace,
    string? ContainingType,
    string? SyntaxKind);
