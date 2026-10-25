using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

public sealed class BashAdapter : ILanguageAdapter
{
    private static readonly HashSet<string> BodyTypes = new(StringComparer.Ordinal)
    {
        "compound_statement", "subshell", "test_command",
    };

    public string LanguageName => "bash";
    public IReadOnlyList<string> FileExtensions { get; } = [".sh", ".bash"];

    // Functions, and the variables a script declares at top level (`readonly`, `export`, `declare`).
    public string DeclarationQuery => """
        (function_definition) @decl
        (program (declaration_command) @decl)
        """;

    public string ImportQuery => """
        ((command
            name: (command_name) @_source
            argument: (_)) @import
          (#match? @_source "^(source|\\.)$"))
        """;

    public string CallQuery => """
        (command) @call
        """;

    public string InheritanceQuery => string.Empty;

    public string DocCommentQuery => """
        ((comment) @doc (#not-match? @doc "^#!"))
        """;

    public string DecoratorQuery => string.Empty;

    public string StatementQuery => """
        (variable_assignment) @stmt
        (declaration_command) @stmt
        (if_statement) @stmt
        (elif_clause) @stmt
        (else_clause) @stmt
        (case_statement) @stmt
        (case_item) @stmt
        (for_statement) @stmt
        (c_style_for_statement) @stmt
        (while_statement) @stmt
        (command) @stmt
        """;

    public UniversalNodeKind MapNodeKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "function_definition" => UniversalNodeKind.Function,
        "declaration_command" => UniversalNodeKind.Variable,
        _ => UniversalNodeKind.Unknown,
    };

    public StatementKind MapStatementKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "variable_assignment" or "declaration_command" => StatementKind.VariableDecl,
        "if_statement" => StatementKind.If,
        "elif_clause" => StatementKind.ElseIf,
        "else_clause" => StatementKind.Else,
        "case_statement" => StatementKind.Switch,
        "case_item" => StatementKind.SwitchCase,
        "for_statement" => StatementKind.ForIn,
        "c_style_for_statement" => StatementKind.For,
        "while_statement" => StatementKind.While,
        "command" => StatementKind.FunctionCall,
        _ => StatementKind.Unknown,
    };

    public UniversalNodeKind RefineKind(UniversalNodeKind initialKind, TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        return initialKind == UniversalNodeKind.Variable && declarationNode.Children.Count > 0 && declarationNode.Children[0].Type == "readonly"
            ? UniversalNodeKind.Constant
            : initialKind;
    }

    public string ExtractName(TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (declarationNode.Type != "declaration_command") return declarationNode.GetChildForField("name")?.Text ?? string.Empty;
        // `readonly MAX=10` names MAX; `export PATH` names PATH.
        var declared = declarationNode.NamedChildren.FirstOrDefault(c => c.Type is "variable_assignment" or "variable_name");
        return (declared?.Type == "variable_assignment" ? declared.GetChildForField("name") : declared)?.Text ?? string.Empty;
    }

    public string ExtractSignature(TsNode declarationNode, string fullSource)
        => AdapterText.Signature(declarationNode, BodyTypes);

    public string? ResolveImportPath(string importPath, string currentVirtualFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(importPath);
        ArgumentException.ThrowIfNullOrEmpty(currentVirtualFilePath);
        // A path built from a variable (`. "$HOME/env.sh"`) is unknown until run time.
        var target = AdapterText.Unquote(importPath);
        return target.Length == 0 || target.Contains('$', StringComparison.Ordinal) ? null : AdapterText.JoinRelative(currentVirtualFilePath, target);
    }

    public IReadOnlyList<string> GetExtraModifiers(TsNode declarationNode, string name)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        return declarationNode.Type == "declaration_command" && declarationNode.Children.Count > 0
            ? [declarationNode.Children[0].Type]
            : [];
    }
}
