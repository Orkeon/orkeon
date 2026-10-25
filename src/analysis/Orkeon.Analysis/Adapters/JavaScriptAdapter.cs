using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

public sealed class JavaScriptAdapter : ILanguageAdapter
{
    private static readonly HashSet<string> BodyTypes = new(StringComparer.Ordinal) { "statement_block", "class_body" };

    // Import specifiers resolve as TypeScript's do: relative paths in the index, bare ones to npm.
    private static readonly TypeScriptAdapter ModuleResolver = new();

    public string LanguageName => "javascript";
    public IReadOnlyList<string> FileExtensions { get; } = [".js", ".jsx", ".mjs", ".cjs"];

    public string DeclarationQuery => """
        (class_declaration) @decl
        (function_declaration) @decl
        (generator_function_declaration) @decl
        (method_definition) @decl
        (field_definition) @decl
        (lexical_declaration
            (variable_declarator) @decl)
        (variable_declaration
            (variable_declarator) @decl)
        """;

    public string ImportQuery => """
        (import_statement) @import
        (export_statement source: (string) @reexport)
        ((call_expression
            function: (identifier) @_require
            arguments: (arguments (string))) @import
          (#eq? @_require "require"))
        """;

    public string CallQuery => """
        (call_expression) @call
        (new_expression) @call
        """;

    public string InheritanceQuery => """
        (class_heritage) @extends
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
        "class_declaration" => UniversalNodeKind.Class,
        "function_declaration" or "generator_function_declaration" => UniversalNodeKind.Function,
        "method_definition" => UniversalNodeKind.Method,
        "field_definition" => UniversalNodeKind.Property,
        "variable_declarator" => UniversalNodeKind.Variable,
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

    public UniversalNodeKind RefineKind(UniversalNodeKind initialKind, TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (initialKind == UniversalNodeKind.Method && declarationNode.GetChildForField("name")?.Text == "constructor")
            return UniversalNodeKind.Constructor;
        if (initialKind != UniversalNodeKind.Variable) return initialKind;

        // A declarator inside a function body is a local, not a symbol of the module.
        if (AdapterText.Ancestor(declarationNode, "statement_block", "class_body") is not null) return UniversalNodeKind.Unknown;
        return declarationNode.GetChildForField("value")?.Type is "arrow_function" or "function_expression" or "generator_function"
            ? UniversalNodeKind.Function
            : initialKind;
    }

    public string ExtractSignature(TsNode declarationNode, string fullSource)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (declarationNode.Type == "variable_declarator"
            && declarationNode.GetChildForField("value") is { Type: "arrow_function" or "function_expression" } function)
        {
            var arrow = AdapterText.Signature(function, BodyTypes);
            return $"{declarationNode.GetChildForField("name")?.Text} = {arrow}";
        }
        return AdapterText.Signature(declarationNode, BodyTypes);
    }

    public string? ResolveImportPath(string importPath, string currentVirtualFilePath)
        => ModuleResolver.ResolveImportPath(importPath, currentVirtualFilePath);
}
