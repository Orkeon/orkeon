using System.Text.RegularExpressions;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

public sealed class PythonAdapter : ILanguageAdapter
{
    private const string VirtualPathSeparator = "/";

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));

    private static readonly HashSet<string> DunderOperators = new(StringComparer.Ordinal)
    {
        "__add__", "__sub__", "__mul__", "__truediv__", "__floordiv__",
        "__mod__", "__pow__", "__eq__", "__ne__", "__lt__", "__le__",
        "__gt__", "__ge__", "__and__", "__or__", "__xor__", "__lshift__",
        "__rshift__", "__neg__", "__pos__", "__invert__", "__contains__",
        "__getitem__", "__setitem__", "__delitem__", "__len__", "__iter__",
        "__next__", "__call__", "__hash__",
    };

    public string LanguageName => "python";
    public IReadOnlyList<string> FileExtensions { get; } = [".py"];

    public string DeclarationQuery => """
        (class_definition) @decl
        (function_definition) @decl
        (assignment) @decl
        """;

    public string ImportQuery => """
        (import_statement) @import
        (import_from_statement) @import
        """;

    public string CallQuery => """
        (call) @call
        """;

    public string InheritanceQuery => """
        (class_definition
          superclasses: (argument_list) @extends)
        """;

    public string DocCommentQuery => """
        (expression_statement (string) @doc)
        """;

    public string DecoratorQuery => """
        (decorator) @decorator
        """;

    public string StatementQuery => """
        (assignment) @stmt
        (augmented_assignment) @stmt
        (if_statement) @stmt
        (elif_clause) @stmt
        (else_clause) @stmt
        (for_statement) @stmt
        (while_statement) @stmt
        (try_statement) @stmt
        (except_clause) @stmt
        (finally_clause) @stmt
        (with_statement) @stmt
        (return_statement) @stmt
        (raise_statement) @stmt
        (yield) @stmt
        (await) @stmt
        (call) @stmt
        (assert_statement) @stmt
        (list_comprehension) @stmt
        (dictionary_comprehension) @stmt
        (set_comprehension) @stmt
        (generator_expression) @stmt
        (lambda) @stmt
        (break_statement) @stmt
        (continue_statement) @stmt
        """;

    public UniversalNodeKind MapNodeKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "class_definition" => UniversalNodeKind.Class,
        "function_definition" => UniversalNodeKind.Function,
        "assignment" => UniversalNodeKind.Variable,
        "decorator" => UniversalNodeKind.Decorator,
        _ => UniversalNodeKind.Unknown,
    };

    public StatementKind MapStatementKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "assignment" => StatementKind.Assignment,
        "augmented_assignment" => StatementKind.Assignment,
        "if_statement" => StatementKind.If,
        "elif_clause" => StatementKind.ElseIf,
        "else_clause" => StatementKind.Else,
        "for_statement" => StatementKind.ForOf,
        "while_statement" => StatementKind.While,
        "try_statement" => StatementKind.TryCatch,
        "except_clause" => StatementKind.Catch,
        "finally_clause" => StatementKind.Finally,
        "with_statement" => StatementKind.FunctionCall,
        "return_statement" => StatementKind.Return,
        "raise_statement" => StatementKind.Throw,
        "yield" => StatementKind.Yield,
        "await" => StatementKind.Await,
        "call" => StatementKind.FunctionCall,
        "assert_statement" => StatementKind.Assertion,
        "list_comprehension" or "dictionary_comprehension"
            or "set_comprehension" or "generator_expression" => StatementKind.Closure,
        "lambda" => StatementKind.ArrowFunction,
        "break_statement" => StatementKind.Break,
        "continue_statement" => StatementKind.Continue,
        _ => StatementKind.Unknown,
    };

    public UniversalNodeKind RefineKind(UniversalNodeKind initialKind, TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (initialKind == UniversalNodeKind.Variable && declarationNode.Type == "assignment")
        {
            return RefineAssignmentKind(declarationNode);
        }

        if (initialKind == UniversalNodeKind.Function && declarationNode.Type == "function_definition")
        {
            return RefineFunctionKind(declarationNode);
        }

        if (initialKind == UniversalNodeKind.Class && IsAbstractBaseClass(declarationNode))
        {
            return UniversalNodeKind.Interface;
        }

        return initialKind;
    }

    private UniversalNodeKind RefineAssignmentKind(TsNode declarationNode)
    {
        var parent = declarationNode.Parent;
        if (parent is not null && parent.Type != "module") return UniversalNodeKind.Unknown;

        var name = ExtractName(declarationNode);
        if (!string.IsNullOrEmpty(name) && IsAllCaps(name))
            return UniversalNodeKind.Constant;
        return UniversalNodeKind.Variable;
    }

    private static UniversalNodeKind RefineFunctionKind(TsNode declarationNode)
    {
        var enclosing = FindEnclosingDeclaration(declarationNode);
        if (enclosing is null || enclosing.Type != "class_definition")
        {
            return UniversalNodeKind.Function;
        }

        var name = declarationNode.GetChildForField("name")?.Text ?? string.Empty;
        if (HasPropertyDecorator(declarationNode)) return UniversalNodeKind.Property;
        if (IsConstructor(name)) return UniversalNodeKind.Constructor;
        if (IsDestructor(name)) return UniversalNodeKind.Destructor;
        if (IsOperator(name)) return UniversalNodeKind.Operator;
        return UniversalNodeKind.Method;
    }

    public string? ResolveDocComment(TsNode declarationNode, IReadOnlyList<TsNode> docs, string fullSource)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (declarationNode.Type != "function_definition" && declarationNode.Type != "class_definition")
            return null;
        var body = declarationNode.GetChildForField("body");
        if (body is null) return null;
        foreach (var child in body.Children)
        {
            if (!child.IsNamed) continue;
            if (child.Type == "string")
            {
                return (child.Text ?? string.Empty).Trim();
            }
            if (child.Type == "expression_statement")
            {
                var str = child.Children.FirstOrDefault(c => c.Type == "string");
                if (str is not null)
                {
                    return (str.Text ?? string.Empty).Trim();
                }
            }
            break;
        }
        return null;
    }

    public string ExtractName(TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (declarationNode.Type == "assignment")
        {
            var left = declarationNode.GetChildForField("left");
            if (left is not null && !string.IsNullOrEmpty(left.Text))
                return left.Text;
        }
        return declarationNode.GetChildForField("name")?.Text ?? string.Empty;
    }

    public string ExtractSignature(TsNode declarationNode, string fullSource)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        ArgumentNullException.ThrowIfNull(fullSource);
        var target = UnwrapDecorated(declarationNode);
        var text = target.Text ?? string.Empty;
        var startByte = target.StartIndex;

        var body = target.GetChildForField("body");
        string signature;
        if (body is not null && body.StartIndex > startByte)
        {
            var localLength = body.StartIndex - startByte;
            if (localLength > text.Length) localLength = text.Length;
            signature = text[..localLength];
            signature = signature.TrimEnd().TrimEnd(':');
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

        if (trimmed.StartsWith('.'))
        {
            return ResolveRelativeImport(trimmed, currentVirtualFilePath);
        }

        return $"ext::pypi::{trimmed.Split('.')[0]}";
    }

    private static string ResolveRelativeImport(string trimmed, string currentVirtualFilePath)
    {
        var dots = 0;
        while (dots < trimmed.Length && trimmed[dots] == '.') dots++;
        var remainder = trimmed[dots..].TrimStart('.');

        // Virtual paths always use '/'; Path.GetDirectoryName / Path.Combine /
        // Path.DirectorySeparatorChar normalize to '\' on Windows and would corrupt
        // the key used by DependencyGraphBuilder.ResolveInIndex.
        var normalizedCurrent = currentVirtualFilePath.Replace('\\', '/');
        var lastSlash = normalizedCurrent.LastIndexOf('/');
        var dir = lastSlash > 0 ? normalizedCurrent[..lastSlash] : string.Empty;
        for (var i = 1; i < dots; i++)
        {
            var parentSlash = dir.LastIndexOf('/');
            dir = parentSlash > 0 ? dir[..parentSlash] : dir;
        }

        var relPath = remainder.Replace('.', '/');
        var basePath = CombineRelativePath(dir, relPath);
        // Return the module path without disk probing — DependencyGraphBuilder.ResolveInIndex
        // resolves against the in-memory VFS index (same approach as TypeScriptAdapter).
        return NormalizePath(basePath + ".py");
    }

    private static string CombineRelativePath(string dir, string relPath)
    {
        if (string.IsNullOrEmpty(relPath)) return dir;
        if (string.IsNullOrEmpty(dir)) return relPath;
        return $"{dir}{VirtualPathSeparator}{relPath}";
    }

    internal static TsNode UnwrapDecorated(TsNode node)
    {
        if (node.Type != "decorated_definition") return node;
        foreach (var child in node.Children)
        {
            if (child.Type == "class_definition" || child.Type == "function_definition")
                return child;
        }
        return node;
    }

    internal static bool IsOperator(string name) => DunderOperators.Contains(name);

    internal static bool IsConstructor(string name) => name == "__init__";

    internal static bool IsDestructor(string name) => name == "__del__";

    private static TsNode? FindEnclosingDeclaration(TsNode node)
    {
        var parent = node.Parent;
        while (parent is not null)
        {
            var type = parent.Type;
            if (type == "class_definition" || type == "function_definition") return parent;
            if (type == "module") return null;
            parent = parent.Parent;
        }
        return null;
    }

    private static bool HasPropertyDecorator(TsNode functionNode)
    {
        var parent = functionNode.Parent;
        if (parent is null || parent.Type != "decorated_definition") return false;
        foreach (var child in parent.Children)
        {
            if (child.Type != "decorator") continue;
            var text = child.Text ?? string.Empty;
            if (text.Contains("@property", StringComparison.Ordinal)) return true;
            if (text.Contains(".setter", StringComparison.Ordinal)) return true;
            if (text.Contains(".getter", StringComparison.Ordinal)) return true;
            if (text.Contains(".deleter", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static bool IsAbstractBaseClass(TsNode classNode)
    {
        var superclasses = classNode.GetChildForField("superclasses");
        if (superclasses is null) return false;
        var text = superclasses.Text ?? string.Empty;
        return text.Contains("ABC", StringComparison.Ordinal) || text.Contains("Protocol", StringComparison.Ordinal);
    }

    private static bool IsAllCaps(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var c in name)
        {
            if (char.IsLetter(c) && !char.IsUpper(c)) return false;
        }
        return name.Any(char.IsLetter);
    }

    private static string NormalizePath(string absolute) => absolute.Replace('\\', '/');

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var collapsed = WhitespaceRegex.Replace(input, " ");
        return collapsed.Trim();
    }
}
