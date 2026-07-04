using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

public sealed class TypeScriptAdapter : ILanguageAdapter
{
    public string LanguageName => "typescript";
    public IReadOnlyList<string> FileExtensions { get; } = [".ts", ".tsx"];

    public string DeclarationQuery => """
        (class_declaration) @decl
        (abstract_class_declaration) @decl
        (function_declaration) @decl
        (method_definition) @decl
        (interface_declaration) @decl
        (type_alias_declaration) @decl
        (enum_declaration) @decl
        (public_field_definition) @decl
        (lexical_declaration
            (variable_declarator) @decl)
        """;

    public string ImportQuery => """
        (import_statement) @import
        (export_statement source: (string) @reexport)
        """;

    public string CallQuery => """
        (call_expression) @call
        (new_expression) @call
        """;

    public string InheritanceQuery => """
        (class_heritage (extends_clause) @extends)
        (class_heritage (implements_clause) @implements)
        (interface_declaration (extends_type_clause) @extends)
        """;

    public string DocCommentQuery => """
        ((comment) @doc (#match? @doc "^/\\*\\*"))
        """;

    public string DecoratorQuery => """
        (decorator) @decorator
        """;

    public string StatementQuery => """
        (lexical_declaration) @stmt
        (variable_declaration) @stmt
        (if_statement) @stmt
        (else_clause) @stmt
        (switch_statement) @stmt
        (switch_case) @stmt
        (switch_default) @stmt
        (for_statement) @stmt
        (for_in_statement) @stmt
        (while_statement) @stmt
        (do_statement) @stmt
        (return_statement) @stmt
        (break_statement) @stmt
        (continue_statement) @stmt
        (throw_statement) @stmt
        (try_statement) @stmt
        (catch_clause) @stmt
        (finally_clause) @stmt
        (call_expression) @stmt
        (new_expression) @stmt
        (await_expression) @stmt
        (yield_expression) @stmt
        (arrow_function) @stmt
        (function_expression) @stmt
        (assignment_expression) @stmt
        (augmented_assignment_expression) @stmt
        """;

    public UniversalNodeKind MapNodeKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "class_declaration" or "abstract_class_declaration" => UniversalNodeKind.Class,
        "interface_declaration" => UniversalNodeKind.Interface,
        "type_alias_declaration" => UniversalNodeKind.Type,
        "enum_declaration" => UniversalNodeKind.Enum,
        "function_declaration" => UniversalNodeKind.Function,
        "arrow_function" => UniversalNodeKind.Function,
        "function_expression" => UniversalNodeKind.Function,
        "method_definition" => UniversalNodeKind.Method,
        "constructor" => UniversalNodeKind.Constructor,
        "public_field_definition" => UniversalNodeKind.Property,
        "variable_declarator" => UniversalNodeKind.Variable,
        "lexical_declaration" => UniversalNodeKind.Variable,
        "namespace_declaration" or "internal_module" => UniversalNodeKind.Namespace,
        "decorator" => UniversalNodeKind.Decorator,
        _ => UniversalNodeKind.Unknown,
    };

    public StatementKind MapStatementKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "lexical_declaration" or "variable_declaration" => StatementKind.VariableDecl,
        "assignment_expression" or "augmented_assignment_expression" => StatementKind.Assignment,
        "if_statement" => StatementKind.If,
        "else_clause" => StatementKind.Else,
        "switch_statement" => StatementKind.Switch,
        "switch_case" => StatementKind.SwitchCase,
        "switch_default" => StatementKind.SwitchDefault,
        "for_statement" => StatementKind.For,
        "for_in_statement" => StatementKind.ForOf,
        "while_statement" => StatementKind.While,
        "do_statement" => StatementKind.DoWhile,
        "return_statement" => StatementKind.Return,
        "break_statement" => StatementKind.Break,
        "continue_statement" => StatementKind.Continue,
        "throw_statement" => StatementKind.Throw,
        "try_statement" => StatementKind.TryCatch,
        "catch_clause" => StatementKind.Catch,
        "finally_clause" => StatementKind.Finally,
        "call_expression" => StatementKind.FunctionCall,
        "new_expression" => StatementKind.ConstructorCall,
        "await_expression" => StatementKind.Await,
        "yield_expression" => StatementKind.Yield,
        "arrow_function" => StatementKind.ArrowFunction,
        "function_expression" => StatementKind.Closure,
        _ => StatementKind.Unknown,
    };

    public string ExtractSignature(TsNode declarationNode, string fullSource)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        ArgumentNullException.ThrowIfNull(fullSource);
        var text = declarationNode.Text ?? string.Empty;
        var startByte = declarationNode.StartIndex;

        var bodyStart = FindBodyStart(declarationNode);
        string signature;
        if (bodyStart > 0 && bodyStart > startByte)
        {
            var localLength = bodyStart - startByte;
            if (localLength > text.Length) localLength = text.Length;
            signature = text[..localLength];
        }
        else
        {
            signature = text;
        }

        signature = Normalize(signature);
        if (signature.Length > 500) signature = signature[..500] + "…";
        return signature;
    }

    public string? ResolveImportPath(string importPath, string currentVirtualFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(importPath);
        ArgumentException.ThrowIfNullOrEmpty(currentVirtualFilePath);

        var trimmed = importPath.Trim().Trim('\'', '"');
        if (trimmed.Length == 0) return null;

        if (IsRelative(trimmed))
        {
            // Virtual paths always use '/'; Path.GetDirectoryName normalizes to '\' on Windows,
            // which would corrupt the key used by DependencyGraphBuilder.ResolveInIndex.
            var normalizedCurrent = currentVirtualFilePath.Replace('\\', '/');
            var lastSlash = normalizedCurrent.LastIndexOf('/');
            var dir = lastSlash > 0 ? normalizedCurrent[..lastSlash] : string.Empty;
            // Extension probing (index.ts barrels, .tsx, etc.) is done by
            // DependencyGraphBuilder.ResolveInIndex against the in-memory index,
            // which is VFS-safe and handles the case where File.Exists returned
            // false for virtual paths (root cause of P2-RT-FIX-08 regression).
            var combined = ResolveVirtualPath(dir, trimmed);
            return NormalizePath(combined);
        }

        return $"ext::npm::{trimmed}";
    }

    private static bool IsRelative(string path)
        => path.StartsWith("./", StringComparison.Ordinal)
           || path.StartsWith("../", StringComparison.Ordinal)
           || path.StartsWith('/')
           || path.StartsWith(".\\", StringComparison.Ordinal)
           || path.StartsWith("..\\", StringComparison.Ordinal);

    private static string NormalizePath(string absolute) => absolute.Replace('\\', '/');

    /// <summary>
    /// Resolves a relative import specifier against a virtual directory using only
    /// string manipulation — no File.Exists or System.IO disk access. The resulting
    /// bare path (without extension) is returned so that
    /// <c>DependencyGraphBuilder.ResolveInIndex</c> can probe extension variants
    /// (.ts, .tsx, index.ts …) against the in-memory VFS index.
    /// Virtual paths always use '/'; Path.GetFullPath would normalize to '\' on Windows.
    /// </summary>
    private static string ResolveVirtualPath(string virtualDir, string importSpecifier)
    {
        var specifier = importSpecifier.Replace('\\', '/');
        var baseDir = virtualDir.Replace('\\', '/');
        var combined = specifier.StartsWith('/')
            ? specifier
            : baseDir.TrimEnd('/') + "/" + specifier;

        // Preserve the root style of the input: virtual paths start with '/', physical
        // Windows paths start with a drive letter (C:/…), bare module specifiers have no root.
        var isAbsolute = combined.StartsWith('/');

        var parts = combined.Split('/', StringSplitOptions.None);
        var stack = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part.Length == 0 || part == ".")
                continue;
            if (part == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(part);
        }

        var joined = string.Join('/', stack);
        return isAbsolute ? "/" + joined : joined;
    }

    private static int FindBodyStart(TsNode node)
    {
        foreach (var fieldName in new[] { "body", "value" })
        {
            var body = node.GetChildForField(fieldName);
            if (body is not null && IsBodyLike(body.Type)) return body.StartIndex;
        }

        var bodyLike = node.Children.FirstOrDefault(child => IsBodyLike(child.Type));
        return bodyLike is not null ? bodyLike.StartIndex : -1;
    }

    private static bool IsBodyLike(string type) => type switch
    {
        "statement_block" or "class_body" or "enum_body" or "interface_body" or "object_type" => true,
        _ => false,
    };

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var collapsed = System.Text.RegularExpressions.Regex.Replace(input, @"\s+", " ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(1000));
        return collapsed.Trim();
    }
}
