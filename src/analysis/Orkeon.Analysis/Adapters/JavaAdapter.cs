using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

public sealed class JavaAdapter : ILanguageAdapter
{
    private static readonly HashSet<string> BodyTypes = new(StringComparer.Ordinal)
    {
        "block", "constructor_body", "class_body", "interface_body", "enum_body", "annotation_type_body",
    };

    public string LanguageName => "java";
    public IReadOnlyList<string> FileExtensions { get; } = [".java"];

    public string DeclarationQuery => """
        (class_declaration) @decl
        (interface_declaration) @decl
        (enum_declaration) @decl
        (record_declaration) @decl
        (annotation_type_declaration) @decl
        (method_declaration) @decl
        (constructor_declaration) @decl
        (compact_constructor_declaration) @decl
        (field_declaration) @decl
        (constant_declaration) @decl
        """;

    public string ImportQuery => """
        (import_declaration) @import
        """;

    public string CallQuery => """
        (method_invocation) @call
        (object_creation_expression) @call
        """;

    public string InheritanceQuery => """
        (superclass) @extends
        (extends_interfaces) @extends
        (super_interfaces) @implements
        """;

    public string DocCommentQuery => """
        ((block_comment) @doc (#match? @doc "^/\\*\\*"))
        """;

    public string DecoratorQuery => """
        (marker_annotation) @decorator
        (annotation) @decorator
        """;

    public string StatementQuery => """
        (local_variable_declaration) @stmt
        (expression_statement (assignment_expression) @stmt)
        (if_statement) @stmt
        (switch_expression) @stmt
        (switch_block_statement_group) @stmt
        (switch_rule) @stmt
        (for_statement) @stmt
        (enhanced_for_statement) @stmt
        (while_statement) @stmt
        (do_statement) @stmt
        (return_statement) @stmt
        (break_statement) @stmt
        (continue_statement) @stmt
        (throw_statement) @stmt
        (yield_statement) @stmt
        (try_statement) @stmt
        (try_with_resources_statement) @stmt
        (catch_clause) @stmt
        (finally_clause) @stmt
        (method_invocation) @stmt
        (object_creation_expression) @stmt
        (lambda_expression) @stmt
        """;

    public UniversalNodeKind MapNodeKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "class_declaration" => UniversalNodeKind.Class,
        "interface_declaration" or "annotation_type_declaration" => UniversalNodeKind.Interface,
        "enum_declaration" => UniversalNodeKind.Enum,
        "record_declaration" => UniversalNodeKind.Record,
        "method_declaration" => UniversalNodeKind.Method,
        "constructor_declaration" or "compact_constructor_declaration" => UniversalNodeKind.Constructor,
        "field_declaration" => UniversalNodeKind.Field,
        "constant_declaration" => UniversalNodeKind.Constant,
        "marker_annotation" or "annotation" => UniversalNodeKind.Decorator,
        _ => UniversalNodeKind.Unknown,
    };

    public StatementKind MapStatementKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "local_variable_declaration" => StatementKind.VariableDecl,
        "assignment_expression" => StatementKind.Assignment,
        "if_statement" => StatementKind.If,
        "switch_expression" => StatementKind.Switch,
        "switch_block_statement_group" or "switch_rule" => StatementKind.SwitchCase,
        "for_statement" => StatementKind.For,
        "enhanced_for_statement" => StatementKind.ForOf,
        "while_statement" => StatementKind.While,
        "do_statement" => StatementKind.DoWhile,
        "return_statement" => StatementKind.Return,
        "break_statement" => StatementKind.Break,
        "continue_statement" => StatementKind.Continue,
        "throw_statement" => StatementKind.Throw,
        "yield_statement" => StatementKind.Yield,
        "try_statement" or "try_with_resources_statement" => StatementKind.TryCatch,
        "catch_clause" => StatementKind.Catch,
        "finally_clause" => StatementKind.Finally,
        "method_invocation" => StatementKind.MethodCall,
        "object_creation_expression" => StatementKind.ConstructorCall,
        "lambda_expression" => StatementKind.Closure,
        _ => StatementKind.Unknown,
    };

    public UniversalNodeKind RefineKind(UniversalNodeKind initialKind, TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        // A field declared `static final` is a constant.
        if (initialKind == UniversalNodeKind.Field && HasModifier(declarationNode, "static") && HasModifier(declarationNode, "final"))
            return UniversalNodeKind.Constant;
        return initialKind;
    }

    public string ExtractName(TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        var holder = declarationNode.Type is "field_declaration" or "constant_declaration"
            ? declarationNode.GetChildForField("declarator")
            : declarationNode;
        return holder?.GetChildForField("name")?.Text ?? string.Empty;
    }

    public string ExtractSignature(TsNode declarationNode, string fullSource)
        => AdapterText.Signature(declarationNode, BodyTypes);

    public string? ResolveImportPath(string importPath, string currentVirtualFilePath) => null;

    public IReadOnlyList<string> GetExtraModifiers(TsNode declarationNode, string name)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        var modifiers = declarationNode.Children.FirstOrDefault(c => c.Type == "modifiers");
        if (modifiers is null) return [];
        return modifiers.Children
            .Where(c => c.Type is not ("marker_annotation" or "annotation" or "line_comment" or "block_comment"))
            .Select(c => (c.Text ?? string.Empty).Trim())
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static bool HasModifier(TsNode declarationNode, string modifier)
        => declarationNode.Children.FirstOrDefault(c => c.Type == "modifiers")?.Children.Any(c => c.Type == modifier) == true;
}
