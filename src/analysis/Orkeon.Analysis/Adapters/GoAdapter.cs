using System.Text.RegularExpressions;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

public sealed class GoAdapter : ILanguageAdapter
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));

    public string LanguageName => "go";
    public IReadOnlyList<string> FileExtensions { get; } = [".go"];

    public string DeclarationQuery => """
        (function_declaration) @decl
        (method_declaration) @decl
        (type_spec) @decl
        (const_spec) @decl
        (var_spec) @decl
        (package_clause) @decl
        """;

    public string ImportQuery => """
        (import_declaration) @import
        """;

    public string CallQuery => """
        (call_expression) @call
        """;

    public string InheritanceQuery => string.Empty;

    public string DocCommentQuery => """
        (comment) @doc
        """;

    public string DecoratorQuery => string.Empty;

    public string StatementQuery => """
        (short_var_declaration) @stmt
        (var_declaration) @stmt
        (const_declaration) @stmt
        (assignment_statement) @stmt
        (if_statement) @stmt
        (for_statement) @stmt
        (expression_switch_statement) @stmt
        (type_switch_statement) @stmt
        (select_statement) @stmt
        (go_statement) @stmt
        (defer_statement) @stmt
        (return_statement) @stmt
        (break_statement) @stmt
        (continue_statement) @stmt
        (call_expression) @stmt
        (func_literal) @stmt
        (expression_case) @stmt
        (default_case) @stmt
        """;

    public UniversalNodeKind MapNodeKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "function_declaration" => UniversalNodeKind.Function,
        "method_declaration" => UniversalNodeKind.Method,
        "type_spec" => UniversalNodeKind.Type,
        "const_spec" => UniversalNodeKind.Constant,
        "var_spec" => UniversalNodeKind.Variable,
        "package_clause" => UniversalNodeKind.Namespace,
        _ => UniversalNodeKind.Unknown,
    };

    public StatementKind MapStatementKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "short_var_declaration" or "var_declaration" => StatementKind.VariableDecl,
        "const_declaration" => StatementKind.ConstantDecl,
        "assignment_statement" => StatementKind.Assignment,
        "if_statement" => StatementKind.If,
        "for_statement" => StatementKind.For,
        "expression_switch_statement" or "type_switch_statement" => StatementKind.Switch,
        "expression_case" => StatementKind.SwitchCase,
        "default_case" => StatementKind.SwitchDefault,
        "select_statement" => StatementKind.Switch,
        "go_statement" or "defer_statement" => StatementKind.FunctionCall,
        "return_statement" => StatementKind.Return,
        "break_statement" => StatementKind.Break,
        "continue_statement" => StatementKind.Continue,
        "call_expression" => StatementKind.FunctionCall,
        "func_literal" => StatementKind.Closure,
        _ => StatementKind.Unknown,
    };

    public UniversalNodeKind RefineKind(UniversalNodeKind initialKind, TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (initialKind == UniversalNodeKind.Type && declarationNode.Type == "type_spec")
        {
            var typeChild = declarationNode.GetChildForField("type");
            if (typeChild is not null)
            {
                return typeChild.Type switch
                {
                    "struct_type" => UniversalNodeKind.Struct,
                    "interface_type" => UniversalNodeKind.Interface,
                    _ => UniversalNodeKind.Type,
                };
            }
        }
        return initialKind;
    }

    public string ExtractName(TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (declarationNode.Type == "var_spec" || declarationNode.Type == "const_spec")
        {
            return ExtractSpecName(declarationNode);
        }

        if (declarationNode.Type == "package_clause")
        {
            return ExtractPackageName(declarationNode);
        }

        var name = declarationNode.GetChildForField("name");
        return name?.Text ?? string.Empty;
    }

    private static string ExtractSpecName(TsNode specNode)
    {
        foreach (var child in specNode.Children)
        {
            if (child.Type == "identifier" && !string.IsNullOrEmpty(child.Text)) return child.Text;
        }
        var nameField = specNode.GetChildForField("name");
        return nameField is not null && !string.IsNullOrEmpty(nameField.Text) ? nameField.Text : string.Empty;
    }

    private static string ExtractPackageName(TsNode packageNode)
    {
        foreach (var child in packageNode.Children)
        {
            if ((child.Type == "package_identifier" || child.Type == "identifier") && !string.IsNullOrEmpty(child.Text))
            {
                return child.Text;
            }
        }
        return string.Empty;
    }

    public string ExtractSignature(TsNode declarationNode, string fullSource)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        ArgumentNullException.ThrowIfNull(fullSource);
        var text = declarationNode.Text ?? string.Empty;
        var startByte = declarationNode.StartIndex;

        var body = declarationNode.GetChildForField("body");
        string signature;
        if (body is not null && body.StartIndex > startByte)
        {
            var localLength = body.StartIndex - startByte;
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

    public string? ResolveImportPath(string importPath, string currentVirtualFilePath) => null;

    public string? ResolveDocComment(TsNode declarationNode, IReadOnlyList<TsNode> docs, string fullSource)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        ArgumentNullException.ThrowIfNull(docs);
        TsNode? best = null;
        foreach (var d in docs)
        {
            var text = d.Text ?? string.Empty;
            if (!text.StartsWith("//", StringComparison.Ordinal)) continue;
            if (d.EndIndex > declarationNode.StartIndex) continue;
            if (d.EndIndex < declarationNode.StartIndex - 500) continue;
            if (best is null || d.EndIndex > best.EndIndex) best = d;
        }
        if (best is null) return null;
        return (best.Text ?? string.Empty).Trim();
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var collapsed = WhitespaceRegex.Replace(input, " ");
        return collapsed.Trim();
    }

    internal static bool IsExported(string name) => !string.IsNullOrEmpty(name) && char.IsUpper(name[0]);

    public IReadOnlyList<string> GetExtraModifiers(TsNode declarationNode, string name)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (string.IsNullOrEmpty(name)) return [];
        var type = declarationNode.Type;
        if (type is "function_declaration" or "method_declaration" or "type_spec"
            or "const_spec" or "var_spec")
        {
            return [IsExported(name) ? "public" : "private"];
        }
        return [];
    }
}
