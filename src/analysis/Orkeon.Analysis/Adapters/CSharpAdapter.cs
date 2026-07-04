using System.Text.RegularExpressions;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

public sealed class CSharpAdapter : ILanguageAdapter
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));

    public string LanguageName => "csharp";
    public IReadOnlyList<string> FileExtensions { get; } = [".cs"];

    public string DeclarationQuery => """
        (class_declaration) @decl
        (struct_declaration) @decl
        (record_declaration) @decl
        (interface_declaration) @decl
        (enum_declaration) @decl
        (namespace_declaration) @decl
        (file_scoped_namespace_declaration) @decl
        (method_declaration) @decl
        (constructor_declaration) @decl
        (destructor_declaration) @decl
        (operator_declaration) @decl
        (property_declaration) @decl
        (field_declaration) @decl
        (delegate_declaration) @decl
        """;

    public string ImportQuery => """
        (using_directive) @import
        """;

    public string CallQuery => """
        (invocation_expression) @call
        (object_creation_expression) @call
        """;

    public string InheritanceQuery => """
        (class_declaration (base_list) @extends)
        (struct_declaration (base_list) @extends)
        (interface_declaration (base_list) @extends)
        (record_declaration (base_list) @extends)
        """;

    public string DocCommentQuery => """
        ((comment) @doc (#match? @doc "^///"))
        """;

    public string DecoratorQuery => """
        (attribute_list) @decorator
        """;

    public string StatementQuery => """
        (local_declaration_statement) @stmt
        (expression_statement) @stmt
        (assignment_expression) @stmt
        (if_statement) @stmt
        (else_clause) @stmt
        (switch_statement) @stmt
        (switch_section) @stmt
        (for_statement) @stmt
        (foreach_statement) @stmt
        (while_statement) @stmt
        (do_statement) @stmt
        (try_statement) @stmt
        (catch_clause) @stmt
        (finally_clause) @stmt
        (throw_statement) @stmt
        (throw_expression) @stmt
        (return_statement) @stmt
        (yield_statement) @stmt
        (break_statement) @stmt
        (continue_statement) @stmt
        (using_statement) @stmt
        (lock_statement) @stmt
        (await_expression) @stmt
        (invocation_expression) @stmt
        (object_creation_expression) @stmt
        (lambda_expression) @stmt
        (anonymous_method_expression) @stmt
        """;

    public UniversalNodeKind MapNodeKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "class_declaration" => UniversalNodeKind.Class,
        "struct_declaration" => UniversalNodeKind.Struct,
        "record_declaration" => UniversalNodeKind.Record,
        "interface_declaration" => UniversalNodeKind.Interface,
        "enum_declaration" => UniversalNodeKind.Enum,
        "namespace_declaration" => UniversalNodeKind.Namespace,
        "file_scoped_namespace_declaration" => UniversalNodeKind.Namespace,
        "method_declaration" => UniversalNodeKind.Method,
        "constructor_declaration" => UniversalNodeKind.Constructor,
        "destructor_declaration" => UniversalNodeKind.Destructor,
        "operator_declaration" => UniversalNodeKind.Operator,
        "property_declaration" => UniversalNodeKind.Property,
        "field_declaration" => UniversalNodeKind.Field,
        "delegate_declaration" => UniversalNodeKind.Type,
        "attribute_list" or "attribute" => UniversalNodeKind.Decorator,
        _ => UniversalNodeKind.Unknown,
    };

    public StatementKind MapStatementKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "local_declaration_statement" => StatementKind.VariableDecl,
        "assignment_expression" => StatementKind.Assignment,
        "if_statement" => StatementKind.If,
        "else_clause" => StatementKind.Else,
        "switch_statement" => StatementKind.Switch,
        "switch_section" => StatementKind.SwitchCase,
        "for_statement" => StatementKind.For,
        "foreach_statement" => StatementKind.ForOf,
        "while_statement" => StatementKind.While,
        "do_statement" => StatementKind.DoWhile,
        "try_statement" => StatementKind.TryCatch,
        "catch_clause" => StatementKind.Catch,
        "finally_clause" => StatementKind.Finally,
        "throw_statement" or "throw_expression" => StatementKind.Throw,
        "return_statement" => StatementKind.Return,
        "yield_statement" => StatementKind.Yield,
        "break_statement" => StatementKind.Break,
        "continue_statement" => StatementKind.Continue,
        "using_statement" or "lock_statement" => StatementKind.FunctionCall,
        "await_expression" => StatementKind.Await,
        "invocation_expression" => StatementKind.FunctionCall,
        "object_creation_expression" => StatementKind.ConstructorCall,
        "lambda_expression" => StatementKind.ArrowFunction,
        "anonymous_method_expression" => StatementKind.Closure,
        _ => StatementKind.Unknown,
    };

    public string ExtractName(TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (declarationNode.Type == "field_declaration")
        {
            return ExtractFieldName(declarationNode);
        }

        var nameField = declarationNode.GetChildForField("name");
        if (nameField is not null && !string.IsNullOrEmpty(nameField.Text)) return nameField.Text;
        return string.Empty;
    }

    private static string ExtractFieldName(TsNode fieldDeclaration)
    {
        var declaration = FirstChildOfType(fieldDeclaration, "variable_declaration");
        var declarator = declaration is null ? null : FirstChildOfType(declaration, "variable_declarator");
        if (declarator is null) return string.Empty;

        var inner = declarator.GetChildForField("name");
        if (inner is not null && !string.IsNullOrEmpty(inner.Text)) return inner.Text;

        foreach (var c in declarator.Children)
        {
            if (c.Type == "identifier" && !string.IsNullOrEmpty(c.Text)) return c.Text;
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
        int endOffset;
        if (body is not null && body.StartIndex > startByte)
        {
            endOffset = body.StartIndex - startByte;
        }
        else
        {
            endOffset = FindBodyLikeChildOffset(declarationNode);
            if (endOffset < 0) endOffset = text.Length;
        }

        if (endOffset > text.Length) endOffset = text.Length;
        var signature = text[..endOffset];
        signature = signature.TrimEnd().TrimEnd(';').TrimEnd();
        signature = Normalize(signature);
        if (signature.Length > 500) signature = signature[..500] + "…";
        return signature;
    }

    public string? ResolveImportPath(string importPath, string currentVirtualFilePath) => null;

    private static TsNode? FirstChildOfType(TsNode node, string type)
    {
        foreach (var child in node.Children)
        {
            if (child.Type == type) return child;
        }
        return null;
    }

    private static int FindBodyLikeChildOffset(TsNode node)
    {
        var startByte = node.StartIndex;
        var bodyLike = node.Children.FirstOrDefault(child => IsBodyLike(child.Type));
        return bodyLike is not null ? bodyLike.StartIndex - startByte : -1;
    }

    private static bool IsBodyLike(string type) => type switch
    {
        "block" or "declaration_list" or "enum_member_declaration_list"
            or "accessor_list" or "bracketed_parameter_list" => true,
        _ => false,
    };

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var collapsed = WhitespaceRegex.Replace(input, " ");
        return collapsed.Trim();
    }
}
