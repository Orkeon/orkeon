using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

public sealed class PhpAdapter : ILanguageAdapter
{
    private static readonly HashSet<string> BodyTypes = new(StringComparer.Ordinal)
    {
        "compound_statement", "declaration_list", "enum_declaration_list",
    };

    public string LanguageName => "php";
    public IReadOnlyList<string> FileExtensions { get; } = [".php"];

    public string DeclarationQuery => """
        (namespace_definition) @decl
        (class_declaration) @decl
        (interface_declaration) @decl
        (trait_declaration) @decl
        (enum_declaration) @decl
        (function_definition) @decl
        (method_declaration) @decl
        (property_declaration) @decl
        (const_declaration) @decl
        (enum_case) @decl
        """;

    public string ImportQuery => """
        (include_expression) @import
        (include_once_expression) @import
        (require_expression) @import
        (require_once_expression) @import
        """;

    public string CallQuery => """
        (function_call_expression) @call
        (member_call_expression) @call
        (scoped_call_expression) @call
        (object_creation_expression) @call
        """;

    public string InheritanceQuery => """
        (base_clause) @extends
        (class_interface_clause) @implements
        """;

    public string DocCommentQuery => """
        ((comment) @doc (#match? @doc "^/\\*\\*"))
        """;

    public string DecoratorQuery => """
        (attribute_list) @decorator
        """;

    public string StatementQuery => """
        (expression_statement (assignment_expression) @stmt)
        (expression_statement (augmented_assignment_expression) @stmt)
        (if_statement) @stmt
        (else_if_clause) @stmt
        (else_clause) @stmt
        (switch_statement) @stmt
        (case_statement) @stmt
        (default_statement) @stmt
        (match_expression) @stmt
        (for_statement) @stmt
        (foreach_statement) @stmt
        (while_statement) @stmt
        (do_statement) @stmt
        (return_statement) @stmt
        (break_statement) @stmt
        (continue_statement) @stmt
        (throw_expression) @stmt
        (try_statement) @stmt
        (catch_clause) @stmt
        (finally_clause) @stmt
        (function_call_expression) @stmt
        (member_call_expression) @stmt
        (scoped_call_expression) @stmt
        (object_creation_expression) @stmt
        (yield_expression) @stmt
        (anonymous_function) @stmt
        (arrow_function) @stmt
        """;

    public UniversalNodeKind MapNodeKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "namespace_definition" => UniversalNodeKind.Namespace,
        "class_declaration" => UniversalNodeKind.Class,
        "interface_declaration" => UniversalNodeKind.Interface,
        "trait_declaration" => UniversalNodeKind.Trait,
        "enum_declaration" => UniversalNodeKind.Enum,
        "function_definition" => UniversalNodeKind.Function,
        "method_declaration" => UniversalNodeKind.Method,
        "property_declaration" => UniversalNodeKind.Property,
        "const_declaration" or "enum_case" => UniversalNodeKind.Constant,
        "attribute_list" => UniversalNodeKind.Decorator,
        _ => UniversalNodeKind.Unknown,
    };

    public StatementKind MapStatementKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "assignment_expression" or "augmented_assignment_expression" => StatementKind.Assignment,
        "if_statement" => StatementKind.If,
        "else_if_clause" => StatementKind.ElseIf,
        "else_clause" => StatementKind.Else,
        "switch_statement" or "match_expression" => StatementKind.Switch,
        "case_statement" => StatementKind.SwitchCase,
        "default_statement" => StatementKind.SwitchDefault,
        "for_statement" => StatementKind.For,
        "foreach_statement" => StatementKind.ForOf,
        "while_statement" => StatementKind.While,
        "do_statement" => StatementKind.DoWhile,
        "return_statement" => StatementKind.Return,
        "break_statement" => StatementKind.Break,
        "continue_statement" => StatementKind.Continue,
        "throw_expression" => StatementKind.Throw,
        "try_statement" => StatementKind.TryCatch,
        "catch_clause" => StatementKind.Catch,
        "finally_clause" => StatementKind.Finally,
        "function_call_expression" => StatementKind.FunctionCall,
        "member_call_expression" or "scoped_call_expression" => StatementKind.MethodCall,
        "object_creation_expression" => StatementKind.ConstructorCall,
        "yield_expression" => StatementKind.Yield,
        "anonymous_function" => StatementKind.Closure,
        "arrow_function" => StatementKind.ArrowFunction,
        _ => StatementKind.Unknown,
    };

    public UniversalNodeKind RefineKind(UniversalNodeKind initialKind, TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        return initialKind == UniversalNodeKind.Method && declarationNode.GetChildForField("name")?.Text == "__construct"
            ? UniversalNodeKind.Constructor
            : initialKind;
    }

    public string ExtractName(TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        return declarationNode.Type switch
        {
            // `private float $radius;` is named `radius`, `const PI = 3.14;` is named `PI`.
            "property_declaration" => declarationNode.NamedChildren.FirstOrDefault(c => c.Type == "property_element")
                ?.GetChildForField("name")?.FirstNamedChild?.Text ?? string.Empty,
            "const_declaration" => declarationNode.NamedChildren.FirstOrDefault(c => c.Type == "const_element")
                ?.NamedChildren.FirstOrDefault(c => c.Type == "name")?.Text ?? string.Empty,
            _ => declarationNode.GetChildForField("name")?.Text ?? string.Empty,
        };
    }

    public string ExtractSignature(TsNode declarationNode, string fullSource)
        => AdapterText.Signature(declarationNode, BodyTypes);

    public string? ResolveImportPath(string importPath, string currentVirtualFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(importPath);
        ArgumentException.ThrowIfNullOrEmpty(currentVirtualFilePath);
        var target = AdapterText.Unquote(importPath);
        return target.Length == 0 ? null : AdapterText.JoinRelative(currentVirtualFilePath, target);
    }
}
