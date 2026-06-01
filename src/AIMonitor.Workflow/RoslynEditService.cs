using AIMonitor.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIMonitor.Workflow;

public sealed class RoslynEditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly SyntaxAnnotation FormatAnnotation = new("AIMonitorRoslynEditFormat");

    private readonly WorkflowEditService workflowService;
    private readonly WorkflowEditPaths paths;

    public RoslynEditService(MonitorSettings settings)
    {
        workflowService = new WorkflowEditService(settings);
        paths = new WorkflowEditPaths(settings);
    }

    public RoslynSourceMapResult GetSourceMap(string? path, string scope = "auto", string mode = "auto", string? namespaceName = null)
    {
        string effectiveScope = NormalizeScope(scope);
        string effectiveMode = NormalizeMode(mode);
        string? requestedNamespace = effectiveScope.Equals("namespace", StringComparison.OrdinalIgnoreCase)
            ? namespaceName ?? path
            : namespaceName;
        string[] files = ResolveSourceMapFiles(path, effectiveScope, requestedNamespace).ToArray();
        RoslynSourceMapFile[] mappedFiles = files.Select(MapFile).ToArray();
        return new RoslynSourceMapResult(
            effectiveScope,
            effectiveMode,
            GetSourceMapModePurpose(effectiveMode),
            path,
            requestedNamespace,
            mappedFiles.Length,
            mappedFiles.Sum(file => file.Symbols.Count),
            mappedFiles);
    }

    public RoslynSymbolReadResult GetSymbol(string watchedFilePath, string symbolSelectorJson)
    {
        EditSessionStatus status = EnsureSession(watchedFilePath);
        CompilationUnitSyntax root = ParseCompilationUnit(status.WorkingFilePath, status.RelativePath);
        RoslynSymbolSelector selector = ParseSymbolSelector(symbolSelectorJson);
        MemberDeclarationSyntax target = ResolveSingleMember(root, selector, status.RelativePath);
        FileLinePositionSpan span = root.SyntaxTree.GetLineSpan(target.Span);
        return new RoslynSymbolReadResult(
            status.WatchedFilePath,
            status.WorkingFilePath,
            status.RelativePath,
            SymbolKind(target),
            SymbolName(target),
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1,
            target.ToFullString());
    }

    public RoslynEditResult SubmitSymbol(string watchedFilePath, string symbolSelectorJson, string code)
    {
        EditSessionStatus status = EnsureSession(watchedFilePath);
        CompilationUnitSyntax root = ParseCompilationUnit(status.WorkingFilePath, status.RelativePath);
        RoslynSymbolSelector selector = ParseSymbolSelector(symbolSelectorJson);
        MemberDeclarationSyntax target = ResolveSingleMember(root, selector, status.RelativePath);
        MemberDeclarationSyntax replacement = ParseMemberDeclaration(code, "replacement symbol")
            .WithLeadingTrivia(target.GetLeadingTrivia())
            .WithTrailingTrivia(target.GetTrailingTrivia())
            .WithAdditionalAnnotations(FormatAnnotation);
        return WriteRoot("submit_symbol", status, root.ReplaceNode(target, replacement));
    }

    public RoslynEditResult AddUsing(string watchedFilePath, string namespaceName)
    {
        EditSessionStatus status = EnsureSession(watchedFilePath);
        CompilationUnitSyntax root = ParseCompilationUnit(status.WorkingFilePath, status.RelativePath);
        if (root.Usings.Any(usingDirective => string.Equals(usingDirective.Name?.ToString(), namespaceName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Using '{namespaceName}' already exists in {status.RelativePath}.");
        }

        UsingDirectiveSyntax newUsing = SyntaxFactory.ParseCompilationUnit($"using {namespaceName};{Environment.NewLine}")
            .Usings
            .Single();
        UsingDirectiveSyntax[] usings = root.Usings
            .Add(newUsing)
            .OrderBy(usingDirective => usingDirective.Name?.ToString(), StringComparer.Ordinal)
            .ToArray();
        return WriteRoot("add_using", status, root.WithUsings(SyntaxFactory.List(usings)));
    }

    public RoslynEditResult RemoveUsing(string watchedFilePath, string namespaceName)
    {
        EditSessionStatus status = EnsureSession(watchedFilePath);
        CompilationUnitSyntax root = ParseCompilationUnit(status.WorkingFilePath, status.RelativePath);
        UsingDirectiveSyntax target = root.Usings.FirstOrDefault(usingDirective => string.Equals(usingDirective.Name?.ToString(), namespaceName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Using '{namespaceName}' was not found in {status.RelativePath}.");
        CompilationUnitSyntax newRoot = root.RemoveNode(target, SyntaxRemoveOptions.KeepNoTrivia)
            ?? throw new InvalidOperationException($"Using '{namespaceName}' could not be removed from {status.RelativePath}.");
        return WriteRoot("remove_using", status, newRoot);
    }

    public RoslynEditResult SetTypePartial(string watchedFilePath, string containingType, bool isPartial)
    {
        EditSessionStatus status = EnsureSession(watchedFilePath);
        CompilationUnitSyntax root = ParseCompilationUnit(status.WorkingFilePath, status.RelativePath);
        TypeDeclarationSyntax type = ResolveSingleType(root, containingType);
        bool currentlyPartial = type.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
        if (currentlyPartial == isPartial)
        {
            return WriteRoot("set_type_partial", status, root);
        }

        TypeDeclarationSyntax newType = isPartial
            ? type.WithModifiers(type.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
            : type.WithModifiers(SyntaxFactory.TokenList(type.Modifiers.Where(modifier => !modifier.IsKind(SyntaxKind.PartialKeyword))));
        return WriteRoot("set_type_partial", status, root.ReplaceNode(type, newType.WithAdditionalAnnotations(FormatAnnotation)));
    }

    public RoslynEditResult AddSymbol(string watchedFilePath, string containingType, string symbolType, string code, string? afterSymbol = null)
    {
        EditSessionStatus status = EnsureSession(watchedFilePath);
        CompilationUnitSyntax root = ParseCompilationUnit(status.WorkingFilePath, status.RelativePath);
        TypeDeclarationSyntax type = ResolveSingleType(root, containingType);
        MemberDeclarationSyntax newMember = ParseMemberDeclaration(code, "new candidate symbol");
        if (!SymbolKind(newMember).Equals(symbolType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"New symbol kind '{SymbolKind(newMember)}' does not match requested kind '{symbolType}'.");
        }

        SyntaxList<MemberDeclarationSyntax> members = type.Members;
        int insertIndex = members.Count;
        if (!string.IsNullOrWhiteSpace(afterSymbol))
        {
            int afterIndex = IndexOfMember(members, afterSymbol);
            if (afterIndex < 0)
            {
                throw new InvalidOperationException($"afterSymbol '{afterSymbol}' was not found in type '{containingType}'.");
            }

            insertIndex = afterIndex + 1;
        }

        newMember = ApplyInsertionTrivia(newMember, type, insertIndex).WithAdditionalAnnotations(FormatAnnotation);
        TypeDeclarationSyntax newType = type.WithMembers(members.Insert(insertIndex, newMember));
        return WriteRoot("add_symbol", status, root.ReplaceNode(type, newType));
    }

    public RoslynEditResult AddField(string watchedFilePath, string containingType, string declaration, string? afterSymbol = null)
    {
        return AddSymbol(watchedFilePath, containingType, "field", declaration, afterSymbol);
    }

    public RoslynEditResult AddProperty(string watchedFilePath, string containingType, string declaration, string? afterSymbol = null)
    {
        return AddSymbol(watchedFilePath, containingType, "property", declaration, afterSymbol);
    }

    public RoslynEditResult AddMethod(string watchedFilePath, string containingType, string declaration, string? afterSymbol = null)
    {
        return AddSymbol(watchedFilePath, containingType, "method", declaration, afterSymbol);
    }

    public RoslynEditResult AddConstructor(string watchedFilePath, string containingType, string declaration, string? afterSymbol = null)
    {
        return AddSymbol(watchedFilePath, containingType, "constructor", declaration, afterSymbol);
    }

    public RoslynEditResult AddNestedType(string watchedFilePath, string containingType, string declaration, string? afterSymbol = null)
    {
        MemberDeclarationSyntax member = ParseMemberDeclaration(declaration, "new nested type");
        string kind = SymbolKind(member);
        if (kind is not ("class" or "struct" or "interface" or "record" or "enum"))
        {
            throw new InvalidOperationException($"Nested type declaration must be class, struct, interface, record, or enum. Actual kind: '{kind}'.");
        }

        return AddSymbol(watchedFilePath, containingType, kind, declaration, afterSymbol);
    }

    public RoslynEditResult RemoveSymbol(string watchedFilePath, string symbolSelectorJson)
    {
        EditSessionStatus status = EnsureSession(watchedFilePath);
        CompilationUnitSyntax root = ParseCompilationUnit(status.WorkingFilePath, status.RelativePath);
        RoslynSymbolSelector selector = ParseSymbolSelector(symbolSelectorJson);
        MemberDeclarationSyntax target = ResolveSingleMember(root, selector, status.RelativePath);
        CompilationUnitSyntax newRoot = root.RemoveNode(target, SyntaxRemoveOptions.KeepNoTrivia)
            ?? throw new InvalidOperationException($"Symbol '{selector.Name}' could not be removed from {status.RelativePath}.");
        return WriteRoot("remove_symbol", status, newRoot);
    }

    private IEnumerable<string> ResolveSourceMapFiles(string? requestedPath, string scope, string? namespaceName)
    {
        string root = paths.Settings.WatchedProjectFolder;
        if (scope.Equals("project", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("auto", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(requestedPath))
        {
            return EnumerateSourceFiles(root);
        }

        if (scope.Equals("namespace", StringComparison.OrdinalIgnoreCase))
        {
            return EnumerateSourceFiles(root).Where(file => FileContainsNamespace(file, namespaceName));
        }

        string targetPath = string.IsNullOrWhiteSpace(requestedPath)
            ? root
            : Path.IsPathRooted(requestedPath)
                ? Path.GetFullPath(requestedPath)
                : Path.GetFullPath(Path.Combine(root, requestedPath));
        paths.GetRelativeWatchedPath(targetPath);
        if (File.Exists(targetPath))
        {
            return targetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? [targetPath]
                : throw new InvalidOperationException("get_source_map currently supports C# source files only.");
        }

        if (Directory.Exists(targetPath))
        {
            return EnumerateSourceFiles(targetPath);
        }

        throw new FileNotFoundException("Source map target file or folder was not found.", targetPath);
    }

    private RoslynSourceMapFile MapFile(string filePath)
    {
        string relativePath = paths.GetRelativeWatchedPath(filePath);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath), path: filePath);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        Diagnostic[] diagnostics = root.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        RoslynSourceMapSymbol[] symbols = diagnostics.Length == 0
            ? root.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(IsOutlineMember).Select(member => MapSymbol(tree, relativePath, member)).ToArray()
            : [];
        return new RoslynSourceMapFile(
            filePath,
            relativePath,
            diagnostics.Length == 0 ? "parsed" : "parse-error",
            diagnostics.Length,
            root.Usings.Select(usingDirective => usingDirective.Name?.ToString() ?? string.Empty).Where(value => value.Length > 0).ToArray(),
            root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Select(item => item.Name.ToString()).Distinct(StringComparer.Ordinal).ToArray(),
            symbols);
    }

    private static RoslynSourceMapSymbol MapSymbol(SyntaxTree tree, string relativePath, MemberDeclarationSyntax member)
    {
        FileLinePositionSpan span = tree.GetLineSpan(member.Span);
        return new RoslynSourceMapSymbol(
            SymbolKind(member),
            SymbolName(member),
            BuildStableSymbolKey(relativePath, member),
            BuildSignature(member),
            BuildNamespace(member),
            BuildContainingType(member),
            span.StartLinePosition.Line + 1,
            span.EndLinePosition.Line + 1,
            ComputeHash(member.ToFullString()),
            GetModifiers(member),
            GetReturnType(member),
            GetParameterTypes(member),
            GetParameterNames(member),
            GetArity(member),
            member.Kind().ToString());
    }

    private EditSessionStatus EnsureSession(string watchedFilePath)
    {
        string fullPath = Path.GetFullPath(watchedFilePath);
        if (!fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Roslyn edit tools currently support C# source files only.");
        }

        EditSessionStatus status = workflowService.GetStatus(fullPath);
        if (status.HasSession)
        {
            return status;
        }

        return File.Exists(fullPath)
            ? workflowService.Refresh(fullPath)
            : workflowService.NewFile(fullPath);
    }

    private RoslynEditResult WriteRoot(string operation, EditSessionStatus status, CompilationUnitSyntax root)
    {
        CompilationUnitSyntax formatted = FormatAnnotatedNodes(root);
        string existingText = File.Exists(status.WorkingFilePath) ? File.ReadAllText(status.WorkingFilePath) : string.Empty;
        File.WriteAllText(status.WorkingFilePath, NormalizeLineEndings(formatted.ToFullString(), DetectDominantNewLine(existingText)));
        return new RoslynEditResult(
            operation,
            status.WatchedFilePath,
            status.WorkingFilePath,
            status.RelativePath,
            "updated",
            $"{operation} updated the monitor-owned Working candidate.",
            FileHash.Compute(status.WorkingFilePath));
    }

    private static CompilationUnitSyntax ParseCompilationUnit(string filePath, string relativePath)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath), path: filePath);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        Diagnostic[] diagnostics = root.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (diagnostics.Length > 0)
        {
            throw new InvalidOperationException($"C# parse failed for {relativePath}: {diagnostics[0].GetMessage()}");
        }

        return root;
    }

    private static RoslynSymbolSelector ParseSymbolSelector(string symbolSelectorJson)
    {
        return JsonSerializer.Deserialize<RoslynSymbolSelector>(symbolSelectorJson, JsonOptions)
            ?? throw new InvalidOperationException("symbolSelectorJson could not be parsed.");
    }

    private static MemberDeclarationSyntax ParseMemberDeclaration(string code, string label)
    {
        MemberDeclarationSyntax member = SyntaxFactory.ParseMemberDeclaration(code)
            ?? throw new InvalidOperationException($"{label} is not a complete C# member declaration.");
        Diagnostic[] diagnostics = member.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (diagnostics.Length > 0)
        {
            throw new InvalidOperationException($"{label} has syntax errors: {diagnostics[0].GetMessage()}");
        }

        return member;
    }

    private static MemberDeclarationSyntax ResolveSingleMember(CompilationUnitSyntax root, RoslynSymbolSelector selector, string relativePath)
    {
        MemberDeclarationSyntax[] matches = root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(IsOutlineMember)
            .Where(member => MatchesSelector(member, selector, relativePath))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Symbol '{selector.Name}' was not found."),
            _ => throw new InvalidOperationException($"Symbol selector for '{selector.Name}' is ambiguous. Add containingType, memberKind, parameterTypes, or stableSymbolKey.")
        };
    }

    private static bool MatchesSelector(MemberDeclarationSyntax member, RoslynSymbolSelector selector, string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(selector.StableSymbolKey)
            && !BuildStableSymbolKey(relativePath, member).Equals(selector.StableSymbolKey, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.MemberKind)
            && !SymbolKind(member).Equals(selector.MemberKind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.Name)
            && !SymbolName(member).Equals(selector.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.ContainingNamespace)
            && !BuildNamespace(member).Equals(selector.ContainingNamespace, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(selector.ContainingType)
            && !string.Equals(BuildContainingType(member), selector.ContainingType, StringComparison.Ordinal))
        {
            return false;
        }

        if (selector.Arity is not null && GetArity(member) != selector.Arity.Value)
        {
            return false;
        }

        return selector.ParameterTypes is not { Count: > 0 } || ParameterTypesMatch(member, selector.ParameterTypes);
    }

    private static TypeDeclarationSyntax ResolveSingleType(CompilationUnitSyntax root, string containingType)
    {
        bool expectsQualifiedName = containingType.Contains('.', StringComparison.Ordinal);
        TypeDeclarationSyntax[] matches = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(type => expectsQualifiedName
                ? string.Equals(BuildContainingType(type), containingType, StringComparison.Ordinal)
                : type.Identifier.ValueText.Equals(containingType, StringComparison.Ordinal))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Containing type '{containingType}' was not found."),
            _ => throw new InvalidOperationException($"Containing type '{containingType}' is ambiguous.")
        };
    }

    private static int IndexOfMember(SyntaxList<MemberDeclarationSyntax> members, string symbolName)
    {
        for (int index = 0; index < members.Count; index++)
        {
            if (SymbolName(members[index]).Equals(symbolName, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static MemberDeclarationSyntax ApplyInsertionTrivia(MemberDeclarationSyntax newMember, TypeDeclarationSyntax type, int insertIndex)
    {
        SyntaxList<MemberDeclarationSyntax> members = type.Members;
        if (members.Count == 0)
        {
            return newMember
                .WithoutLeadingTrivia()
                .WithLeadingTrivia(SyntaxFactory.Whitespace("        "))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
        }

        MemberDeclarationSyntax indentationSource = insertIndex < members.Count
            ? members[insertIndex]
            : members[^1];
        string indentation = GetDeclarationIndentation(indentationSource);
        return newMember
            .WithoutLeadingTrivia()
            .WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed, SyntaxFactory.Whitespace(indentation))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);
    }

    private static string GetDeclarationIndentation(MemberDeclarationSyntax member)
    {
        string leadingText = member.GetLeadingTrivia().ToFullString();
        int lineStart = Math.Max(leadingText.LastIndexOf('\n'), leadingText.LastIndexOf('\r'));
        string indentation = lineStart >= 0 ? leadingText[(lineStart + 1)..] : leadingText;
        return !string.IsNullOrEmpty(indentation) && indentation.All(char.IsWhiteSpace)
            ? indentation
            : "        ";
    }

    private static CompilationUnitSyntax FormatAnnotatedNodes(CompilationUnitSyntax root)
    {
        using AdhocWorkspace workspace = new();
        SyntaxNode formatted = Formatter.Format(root, FormatAnnotation, workspace);
        return (CompilationUnitSyntax)formatted;
    }

    private IEnumerable<string> EnumerateSourceFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildOrHiddenDirectory(path))
            .Order(StringComparer.OrdinalIgnoreCase);
    }

    private static bool FileContainsNamespace(string filePath, string? namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return true;
        }

        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath), path: filePath).GetCompilationUnitRoot();
        return root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Any(item => item.Name.ToString().Equals(namespaceName, StringComparison.Ordinal));
    }

    private static string NormalizeScope(string? scope)
    {
        string normalized = string.IsNullOrWhiteSpace(scope) ? "auto" : scope.Trim().ToLowerInvariant();
        return normalized is "auto" or "file" or "folder" or "namespace" or "project"
            ? normalized
            : throw new InvalidOperationException("Source map scope must be auto, file, folder, namespace, or project.");
    }

    private static string NormalizeMode(string? mode)
    {
        string normalized = string.IsNullOrWhiteSpace(mode) ? "auto" : mode.Trim().ToLowerInvariant();
        return normalized is "auto" or "navigation" or "selector" or "detail" or "full"
            ? normalized
            : throw new InvalidOperationException("Source map mode must be auto, navigation, selector, detail, or full.");
    }

    private static string GetSourceMapModePurpose(string mode)
    {
        return mode.Equals("navigation", StringComparison.OrdinalIgnoreCase) ? "broad-orientation"
            : mode.Equals("selector", StringComparison.OrdinalIgnoreCase) ? "stable-symbol-selection"
            : mode.Equals("detail", StringComparison.OrdinalIgnoreCase) ? "contract-detail"
            : "audit-debug";
    }

    private static string BuildStableSymbolKey(string relativePath, MemberDeclarationSyntax member)
    {
        string namespaceName = BuildNamespace(member);
        string containingType = BuildContainingType(member) ?? string.Empty;
        string signatureKey = member switch
        {
            MethodDeclarationSyntax method => $"{method.Identifier.ValueText}({string.Join(",", method.ParameterList.Parameters.Select(ParameterKey))})",
            ConstructorDeclarationSyntax constructor => $"{constructor.Identifier.ValueText}({string.Join(",", constructor.ParameterList.Parameters.Select(ParameterKey))})",
            DelegateDeclarationSyntax del => $"{del.Identifier.ValueText}({string.Join(",", del.ParameterList.Parameters.Select(ParameterKey))})",
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            EventDeclarationSyntax evt => evt.Identifier.ValueText,
            EventFieldDeclarationSyntax eventField => string.Join(",", eventField.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            FieldDeclarationSyntax field => string.Join(",", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            _ => SymbolName(member)
        };
        return $"{NormalizePath(relativePath)}::{namespaceName}::{containingType}::{SymbolKind(member)}::{signatureKey}";
    }

    private static string ParameterKey(ParameterSyntax parameter)
    {
        string modifier = parameter.Modifiers.ToFullString().Trim();
        string type = parameter.Type?.ToString() ?? string.Empty;
        return string.IsNullOrWhiteSpace(modifier) ? type : $"{modifier} {type}";
    }

    private static string BuildNamespace(SyntaxNode node)
    {
        string[] names = node.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(namespaceDeclaration => namespaceDeclaration.Name.ToString())
            .ToArray();
        return string.Join(".", names);
    }

    private static string? BuildContainingType(MemberDeclarationSyntax member)
    {
        string[] names = member.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Reverse()
            .Select(type => type.Identifier.ValueText)
            .ToArray();
        return names.Length == 0 ? null : string.Join(".", names);
    }

    private static int GetArity(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.TypeParameterList?.Parameters.Count ?? 0,
            TypeDeclarationSyntax type => type.TypeParameterList?.Parameters.Count ?? 0,
            DelegateDeclarationSyntax del => del.TypeParameterList?.Parameters.Count ?? 0,
            _ => 0
        };
    }

    private static bool ParameterTypesMatch(MemberDeclarationSyntax member, IReadOnlyList<string> expected)
    {
        SeparatedSyntaxList<ParameterSyntax>? parameters = member switch
        {
            MethodDeclarationSyntax method => method.ParameterList.Parameters,
            ConstructorDeclarationSyntax constructor => constructor.ParameterList.Parameters,
            DelegateDeclarationSyntax del => del.ParameterList.Parameters,
            _ => null
        };
        if (parameters is null || parameters.Value.Count != expected.Count)
        {
            return false;
        }

        for (int index = 0; index < expected.Count; index++)
        {
            string actualType = parameters.Value[index].Type?.ToString() ?? string.Empty;
            if (!actualType.Equals(expected[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsOutlineMember(MemberDeclarationSyntax member)
    {
        return member is BaseTypeDeclarationSyntax
            or MethodDeclarationSyntax
            or ConstructorDeclarationSyntax
            or PropertyDeclarationSyntax
            or FieldDeclarationSyntax
            or EventFieldDeclarationSyntax
            or EventDeclarationSyntax
            or DelegateDeclarationSyntax;
    }

    private static string SymbolKind(MemberDeclarationSyntax member)
    {
        return member switch
        {
            ClassDeclarationSyntax => "class",
            StructDeclarationSyntax => "struct",
            InterfaceDeclarationSyntax => "interface",
            RecordDeclarationSyntax => "record",
            EnumDeclarationSyntax => "enum",
            MethodDeclarationSyntax => "method",
            ConstructorDeclarationSyntax => "constructor",
            PropertyDeclarationSyntax => "property",
            FieldDeclarationSyntax => "field",
            EventFieldDeclarationSyntax => "event",
            EventDeclarationSyntax => "event",
            DelegateDeclarationSyntax => "delegate",
            _ => member.Kind().ToString()
        };
    }

    private static string SymbolName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            FieldDeclarationSyntax field => string.Join(", ", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            EventFieldDeclarationSyntax eventField => string.Join(", ", eventField.Declaration.Variables.Select(variable => variable.Identifier.ValueText)),
            EventDeclarationSyntax evt => evt.Identifier.ValueText,
            DelegateDeclarationSyntax del => del.Identifier.ValueText,
            _ => member.Kind().ToString()
        };
    }

    private static string BuildSignature(MemberDeclarationSyntax member)
    {
        MemberDeclarationSyntax cleanMember = member.WithoutLeadingTrivia();
        return cleanMember switch
        {
            PropertyDeclarationSyntax property => property.WithAccessorList(null).WithExpressionBody(null).WithSemicolonToken(default).ToFullString().Trim(),
            MethodDeclarationSyntax method => method.WithBody(null).WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).ToFullString().Trim(),
            ConstructorDeclarationSyntax constructor => constructor.WithBody(null).WithExpressionBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).ToFullString().Trim(),
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            FieldDeclarationSyntax field => field.WithDeclaration(field.Declaration.WithVariables(SyntaxFactory.SeparatedList(field.Declaration.Variables.Select(variable => variable.WithInitializer(null))))).ToFullString().Trim(),
            _ => cleanMember.ToFullString().Split(["\r\n", "\n"], StringSplitOptions.None)[0].Trim()
        };
    }

    private static IReadOnlyList<string> GetModifiers(MemberDeclarationSyntax member)
    {
        SyntaxTokenList modifiers = member switch
        {
            BaseTypeDeclarationSyntax type => type.Modifiers,
            BaseMethodDeclarationSyntax method => method.Modifiers,
            EventDeclarationSyntax evt => evt.Modifiers,
            EventFieldDeclarationSyntax eventField => eventField.Modifiers,
            BasePropertyDeclarationSyntax property => property.Modifiers,
            FieldDeclarationSyntax field => field.Modifiers,
            DelegateDeclarationSyntax del => del.Modifiers,
            _ => default
        };
        return modifiers.Select(modifier => modifier.ValueText).ToArray();
    }

    private static string? GetReturnType(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.ReturnType.ToString(),
            PropertyDeclarationSyntax property => property.Type.ToString(),
            FieldDeclarationSyntax field => field.Declaration.Type.ToString(),
            EventFieldDeclarationSyntax eventField => eventField.Declaration.Type.ToString(),
            EventDeclarationSyntax evt => evt.Type.ToString(),
            DelegateDeclarationSyntax del => del.ReturnType.ToString(),
            _ => null
        };
    }

    private static IReadOnlyList<string> GetParameterTypes(MemberDeclarationSyntax member)
    {
        return GetParameters(member).Select(parameter => parameter.Type?.ToString() ?? string.Empty).ToArray();
    }

    private static IReadOnlyList<string> GetParameterNames(MemberDeclarationSyntax member)
    {
        return GetParameters(member).Select(parameter => parameter.Identifier.ValueText).ToArray();
    }

    private static IEnumerable<ParameterSyntax> GetParameters(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.ParameterList.Parameters,
            ConstructorDeclarationSyntax constructor => constructor.ParameterList.Parameters,
            DelegateDeclarationSyntax del => del.ParameterList.Parameters,
            _ => []
        };
    }

    private static bool IsUnderBuildOrHiddenDirectory(string path)
    {
        string[] parts = Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => part.StartsWith(".", StringComparison.Ordinal)
            || part.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || part.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || part.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string DetectDominantNewLine(string text)
    {
        int crlf = 0;
        int lf = 0;
        int cr = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    crlf++;
                    index++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[index] == '\n')
            {
                lf++;
            }
        }

        if (crlf >= lf && crlf >= cr && crlf > 0)
        {
            return "\r\n";
        }

        if (lf >= cr && lf > 0)
        {
            return "\n";
        }

        return Environment.NewLine;
    }

    private static string NormalizeLineEndings(string content, string newLine)
    {
        string normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        return newLine.Equals("\n", StringComparison.Ordinal)
            ? normalized
            : normalized.Replace("\n", newLine, StringComparison.Ordinal);
    }

    private static string ComputeHash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
