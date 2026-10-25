using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

public sealed class CppAdapter : ILanguageAdapter
{
    private static readonly HashSet<string> BodyTypes = new(StringComparer.Ordinal)
    {
        "compound_statement", "field_declaration_list", "enumerator_list", "declaration_list", "field_initializer_list",
    };

    private static readonly string[] TypeBodies = ["field_declaration_list"];

    public string LanguageName => "cpp";

    // C headers parse with the C++ grammar: a superset of C's declarations.
    public IReadOnlyList<string> FileExtensions { get; } = [".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx", ".h"];

    public string DeclarationQuery => """
        (function_definition) @decl
        (translation_unit (declaration) @decl)
        (declaration_list (declaration) @decl)
        (field_declaration_list (declaration) @decl)
        (field_declaration) @decl
        (class_specifier body: (_)) @decl
        (struct_specifier body: (_)) @decl
        (union_specifier body: (_)) @decl
        (enum_specifier body: (_)) @decl
        (namespace_definition) @decl
        (type_definition) @decl
        (alias_declaration) @decl
        (preproc_def) @decl
        (preproc_function_def) @decl
        """;

    public string ImportQuery => """
        (preproc_include) @import
        """;

    public string CallQuery => """
        (call_expression) @call
        (new_expression) @call
        """;

    public string InheritanceQuery => """
        (base_class_clause) @extends
        """;

    public string DocCommentQuery => """
        ((comment) @doc (#match? @doc "^(/\\*\\*|///)"))
        """;

    public string DecoratorQuery => """
        (attribute_declaration) @decorator
        """;

    public string StatementQuery => """
        (declaration) @stmt
        (expression_statement (assignment_expression) @stmt)
        (if_statement) @stmt
        (else_clause) @stmt
        (switch_statement) @stmt
        (case_statement) @stmt
        (for_statement) @stmt
        (for_range_loop) @stmt
        (while_statement) @stmt
        (do_statement) @stmt
        (return_statement) @stmt
        (break_statement) @stmt
        (continue_statement) @stmt
        (throw_statement) @stmt
        (try_statement) @stmt
        (catch_clause) @stmt
        (call_expression) @stmt
        (new_expression) @stmt
        (lambda_expression) @stmt
        (co_await_expression) @stmt
        """;

    public UniversalNodeKind MapNodeKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "function_definition" or "preproc_function_def" => UniversalNodeKind.Function,
        "declaration" => UniversalNodeKind.Variable,
        "field_declaration" => UniversalNodeKind.Field,
        "class_specifier" => UniversalNodeKind.Class,
        "struct_specifier" or "union_specifier" => UniversalNodeKind.Struct,
        "enum_specifier" => UniversalNodeKind.Enum,
        "namespace_definition" => UniversalNodeKind.Namespace,
        "type_definition" or "alias_declaration" => UniversalNodeKind.Type,
        "preproc_def" => UniversalNodeKind.Constant,
        "attribute_declaration" => UniversalNodeKind.Decorator,
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
        "for_range_loop" => StatementKind.ForOf,
        "while_statement" => StatementKind.While,
        "do_statement" => StatementKind.DoWhile,
        "return_statement" => StatementKind.Return,
        "break_statement" => StatementKind.Break,
        "continue_statement" => StatementKind.Continue,
        "throw_statement" => StatementKind.Throw,
        "try_statement" => StatementKind.TryCatch,
        "catch_clause" => StatementKind.Catch,
        "call_expression" => StatementKind.FunctionCall,
        "new_expression" => StatementKind.ConstructorCall,
        "lambda_expression" => StatementKind.Closure,
        "co_await_expression" => StatementKind.Await,
        _ => StatementKind.Unknown,
    };

    public UniversalNodeKind RefineKind(UniversalNodeKind initialKind, TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (initialKind is not (UniversalNodeKind.Function or UniversalNodeKind.Variable or UniversalNodeKind.Field)) return initialKind;
        if (initialKind != UniversalNodeKind.Function && !CDeclarators.DeclaresFunction(declarationNode))
        {
            return initialKind == UniversalNodeKind.Variable
                   && declarationNode.Children.Any(c => c.Type == "type_qualifier" && c.Text is "const" or "constexpr")
                ? UniversalNodeKind.Constant
                : initialKind;
        }

        var declarator = CDeclarators.Innermost(declarationNode);
        var leaf = declarator?.Type == "qualified_identifier" ? declarator.GetChildForField("name") : declarator;
        if (leaf?.Type == "destructor_name") return UniversalNodeKind.Destructor;
        if (leaf?.Type == "operator_name") return UniversalNodeKind.Operator;

        // A member is declared in a class body or defined out of line as `Type::name`.
        var owner = AdapterText.Ancestor(declarationNode, TypeBodies)?.Parent?.GetChildForField("name")?.Text
            ?? (declarator?.Type == "qualified_identifier" ? declarator.GetChildForField("scope")?.Text : null);
        if (owner is null) return UniversalNodeKind.Function;
        return leaf?.Text == owner ? UniversalNodeKind.Constructor : UniversalNodeKind.Method;
    }

    public string ExtractName(TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        return declarationNode.Type is "function_definition" or "declaration" or "field_declaration" or "type_definition"
            ? CDeclarators.NameOf(declarationNode)
            : declarationNode.GetChildForField("name")?.Text ?? string.Empty;
    }

    public string ExtractSignature(TsNode declarationNode, string fullSource)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        // A template's signature carries its `template <...>` head.
        var anchor = declarationNode.Parent?.Type == "template_declaration" ? declarationNode.Parent : declarationNode;
        if (ReferenceEquals(anchor, declarationNode)) return AdapterText.Signature(declarationNode, BodyTypes);
        var head = anchor.GetChildForField("parameters")?.Text ?? string.Empty;
        return $"template {head} {AdapterText.Signature(declarationNode, BodyTypes)}";
    }

    public string? ResolveImportPath(string importPath, string currentVirtualFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(importPath);
        ArgumentException.ThrowIfNullOrEmpty(currentVirtualFilePath);
        return CDeclarators.ResolveInclude(importPath, currentVirtualFilePath);
    }

    public IReadOnlyList<string> GetExtraModifiers(TsNode declarationNode, string name)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        var modifiers = new List<string>();
        foreach (var child in declarationNode.Children)
        {
            if (child.Type is "storage_class_specifier" or "virtual" or "explicit_function_specifier")
                modifiers.Add((child.Text ?? string.Empty).Trim());
        }

        var declarator = declarationNode.GetChildForField("declarator");
        if (declarator?.Children.Any(c => c.Type == "virtual_specifier" && c.Text == "override") == true) modifiers.Add("override");

        var access = AccessOf(declarationNode);
        if (access is not null) modifiers.Add(access);
        return modifiers;
    }

    /// <summary>The access of a class member: the last <c>public:</c>/<c>private:</c> label before it, else the default of its class.</summary>
    private static string? AccessOf(TsNode declarationNode)
    {
        // A member template sits in the body through its `template_declaration`.
        var member = declarationNode.Parent?.Type == "template_declaration" ? declarationNode.Parent : declarationNode;
        var body = member.Parent;
        if (body?.Type != "field_declaration_list") return null;
        string? access = null;
        foreach (var sibling in body.NamedChildren)
        {
            if (sibling.StartIndex >= member.StartIndex) break;
            if (sibling.Type == "access_specifier") access = (sibling.Text ?? string.Empty).Trim();
        }
        return access ?? (body.Parent?.Type == "class_specifier" ? "private" : "public");
    }
}
