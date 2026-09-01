using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Examples.RaggableTree.CustomAdapter;

public sealed class JavaAdapter : ILanguageAdapter
{
    public string LanguageName => "java";
    public IReadOnlyList<string> FileExtensions { get; } = [".java"];

    public string DeclarationQuery => """
        (class_declaration) @decl
        (interface_declaration) @decl
        (enum_declaration) @decl
        (method_declaration) @decl
        (constructor_declaration) @decl
        (field_declaration) @decl
        """;

    public string ImportQuery => """
        (import_declaration) @import
        """;

    public string CallQuery => """
        (method_invocation) @call
        """;

    public string InheritanceQuery => """
        (superclass) @extends
        (super_interfaces) @implements
        """;

    public string DocCommentQuery => """
        (block_comment) @doc
        """;

    public string DecoratorQuery => """
        (annotation) @decorator
        (marker_annotation) @decorator
        """;

    public string StatementQuery => """
        (local_variable_declaration) @stmt
        (assignment_expression) @stmt
        (if_statement) @stmt
        (for_statement) @stmt
        (enhanced_for_statement) @stmt
        (while_statement) @stmt
        (do_statement) @stmt
        (switch_expression) @stmt
        (try_statement) @stmt
        (throw_statement) @stmt
        (return_statement) @stmt
        (break_statement) @stmt
        (continue_statement) @stmt
        (method_invocation) @stmt
        """;

    public UniversalNodeKind MapNodeKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "class_declaration" => UniversalNodeKind.Class,
        "interface_declaration" => UniversalNodeKind.Interface,
        "enum_declaration" => UniversalNodeKind.Enum,
        "method_declaration" => UniversalNodeKind.Method,
        "constructor_declaration" => UniversalNodeKind.Constructor,
        "field_declaration" => UniversalNodeKind.Field,
        _ => UniversalNodeKind.Unknown,
    };

    public StatementKind MapStatementKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "local_variable_declaration" => StatementKind.VariableDecl,
        "assignment_expression" => StatementKind.Assignment,
        "if_statement" => StatementKind.If,
        "for_statement" or "enhanced_for_statement" => StatementKind.For,
        "while_statement" => StatementKind.While,
        "do_statement" => StatementKind.DoWhile,
        "switch_expression" => StatementKind.Switch,
        "try_statement" => StatementKind.TryCatch,
        "throw_statement" => StatementKind.Throw,
        "return_statement" => StatementKind.Return,
        "break_statement" => StatementKind.Break,
        "continue_statement" => StatementKind.Continue,
        "method_invocation" => StatementKind.MethodCall,
        _ => StatementKind.Unknown,
    };

    public string ExtractSignature(TsNode declarationNode, string fullSource)
    {
        var nameNode = declarationNode.GetChildForField("name");
        var name = nameNode?.Text ?? "<anonymous>";
        return $"{declarationNode.Type} {name}";
    }

    public string? ResolveImportPath(string importPath, string currentVirtualFilePath)
    {
        if (string.IsNullOrEmpty(importPath)) return null;
        if (importPath.StartsWith("java.", StringComparison.Ordinal)) return null;
        return importPath;
    }
}
