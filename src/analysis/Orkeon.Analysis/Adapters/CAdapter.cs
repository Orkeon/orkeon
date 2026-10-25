using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

public sealed class CAdapter : ILanguageAdapter
{
    private static readonly HashSet<string> BodyTypes = new(StringComparer.Ordinal)
    {
        "compound_statement", "field_declaration_list", "enumerator_list",
    };

    public string LanguageName => "c";
    public IReadOnlyList<string> FileExtensions { get; } = [".c"];

    public string DeclarationQuery => """
        (function_definition) @decl
        (translation_unit (declaration) @decl)
        (struct_specifier body: (_)) @decl
        (union_specifier body: (_)) @decl
        (enum_specifier body: (_)) @decl
        (type_definition) @decl
        (preproc_def) @decl
        (preproc_function_def) @decl
        """;

    public string ImportQuery => """
        (preproc_include) @import
        """;

    public string CallQuery => """
        (call_expression) @call
        """;

    public string InheritanceQuery => string.Empty;

    public string DocCommentQuery => """
        ((comment) @doc (#match? @doc "^(/\\*\\*|///)"))
        """;

    public string DecoratorQuery => string.Empty;

    public string StatementQuery => """
        (declaration) @stmt
        (expression_statement (assignment_expression) @stmt)
        (if_statement) @stmt
        (else_clause) @stmt
        (switch_statement) @stmt
        (case_statement) @stmt
        (for_statement) @stmt
        (while_statement) @stmt
        (do_statement) @stmt
        (return_statement) @stmt
        (break_statement) @stmt
        (continue_statement) @stmt
        (goto_statement) @stmt
        (call_expression) @stmt
        """;

    public UniversalNodeKind MapNodeKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "function_definition" or "preproc_function_def" => UniversalNodeKind.Function,
        "declaration" => UniversalNodeKind.Variable,
        "struct_specifier" or "union_specifier" => UniversalNodeKind.Struct,
        "enum_specifier" => UniversalNodeKind.Enum,
        "type_definition" => UniversalNodeKind.Type,
        "preproc_def" => UniversalNodeKind.Constant,
        _ => UniversalNodeKind.Unknown,
    };

    public StatementKind MapStatementKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "declaration" => StatementKind.VariableDecl,
        "assignment_expression" => StatementKind.Assignment,
        "if_statement" => StatementKind.If,
        "else_clause" => StatementKind.Else,
        "switch_statement" => StatementKind.Switch,
        "case_statement" => StatementKind.SwitchCase,
        "for_statement" => StatementKind.For,
        "while_statement" => StatementKind.While,
        "do_statement" => StatementKind.DoWhile,
        "return_statement" => StatementKind.Return,
        "break_statement" or "goto_statement" => StatementKind.Break,
        "continue_statement" => StatementKind.Continue,
        "call_expression" => StatementKind.FunctionCall,
        _ => StatementKind.Unknown,
    };

    public UniversalNodeKind RefineKind(UniversalNodeKind initialKind, TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        // A top-level declaration is a prototype or a global; `const` makes the global a constant.
        if (initialKind != UniversalNodeKind.Variable) return initialKind;
        if (CDeclarators.DeclaresFunction(declarationNode)) return UniversalNodeKind.Function;
        return declarationNode.Children.Any(c => c.Type == "type_qualifier" && c.Text == "const")
            ? UniversalNodeKind.Constant
            : initialKind;
    }

    public string ExtractName(TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        return declarationNode.Type is "function_definition" or "declaration" or "type_definition"
            ? CDeclarators.NameOf(declarationNode)
            : declarationNode.GetChildForField("name")?.Text ?? string.Empty;
    }

    public string ExtractSignature(TsNode declarationNode, string fullSource)
        => AdapterText.Signature(declarationNode, BodyTypes);

    public string? ResolveImportPath(string importPath, string currentVirtualFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(importPath);
        ArgumentException.ThrowIfNullOrEmpty(currentVirtualFilePath);
        return CDeclarators.ResolveInclude(importPath, currentVirtualFilePath);
    }

    public IReadOnlyList<string> GetExtraModifiers(TsNode declarationNode, string name)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        return declarationNode.Children
            .Where(c => c.Type == "storage_class_specifier")
            .Select(c => (c.Text ?? string.Empty).Trim())
            .ToList();
    }
}
