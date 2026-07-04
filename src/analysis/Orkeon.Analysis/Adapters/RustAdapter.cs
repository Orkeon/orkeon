using System.Text.RegularExpressions;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

public sealed class RustAdapter : ILanguageAdapter
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromMilliseconds(1000));

    public string LanguageName => "rust";
    public IReadOnlyList<string> FileExtensions { get; } = [".rs"];

    public string DeclarationQuery => """
        (function_item) @decl
        (struct_item) @decl
        (enum_item) @decl
        (trait_item) @decl
        (type_item) @decl
        (const_item) @decl
        (static_item) @decl
        (mod_item) @decl
        """;

    public string ImportQuery => """
        (use_declaration) @import
        """;

    public string CallQuery => """
        (call_expression) @call
        (macro_invocation) @call
        """;

    public string InheritanceQuery => """
        (impl_item
          trait: (_) @implements)
        (trait_item) @extends
        """;

    public string DocCommentQuery => """
        ((line_comment) @doc (#match? @doc "^///"))
        ((block_comment) @doc (#match? @doc "^/\\*\\*"))
        """;

    public string DecoratorQuery => """
        (attribute_item) @decorator
        """;

    public string StatementQuery => """
        (let_declaration) @stmt
        (expression_statement) @stmt
        (assignment_expression) @stmt
        (compound_assignment_expr) @stmt
        (if_expression) @stmt
        (else_clause) @stmt
        (match_expression) @stmt
        (match_arm) @stmt
        (for_expression) @stmt
        (while_expression) @stmt
        (loop_expression) @stmt
        (return_expression) @stmt
        (break_expression) @stmt
        (continue_expression) @stmt
        (call_expression) @stmt
        (macro_invocation) @stmt
        (await_expression) @stmt
        (try_expression) @stmt
        (closure_expression) @stmt
        (yield_expression) @stmt
        """;

    public UniversalNodeKind MapNodeKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "function_item" => UniversalNodeKind.Function,
        "struct_item" => UniversalNodeKind.Struct,
        "enum_item" => UniversalNodeKind.Enum,
        "trait_item" => UniversalNodeKind.Trait,
        "type_item" => UniversalNodeKind.Type,
        "const_item" => UniversalNodeKind.Constant,
        "static_item" => UniversalNodeKind.Variable,
        "mod_item" => UniversalNodeKind.Namespace,
        "attribute_item" => UniversalNodeKind.Decorator,
        _ => UniversalNodeKind.Unknown,
    };

    public StatementKind MapStatementKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "let_declaration" => StatementKind.VariableDecl,
        "assignment_expression" or "compound_assignment_expr" => StatementKind.Assignment,
        "if_expression" => StatementKind.If,
        "else_clause" => StatementKind.Else,
        "match_expression" => StatementKind.Switch,
        "match_arm" => StatementKind.SwitchCase,
        "for_expression" => StatementKind.ForOf,
        "while_expression" => StatementKind.While,
        "loop_expression" => StatementKind.While,
        "return_expression" => StatementKind.Return,
        "break_expression" => StatementKind.Break,
        "continue_expression" => StatementKind.Continue,
        "call_expression" or "macro_invocation" => StatementKind.FunctionCall,
        "await_expression" => StatementKind.Await,
        "try_expression" => StatementKind.TryCatch,
        "closure_expression" => StatementKind.Closure,
        "yield_expression" => StatementKind.Yield,
        _ => StatementKind.Unknown,
    };

    public UniversalNodeKind RefineKind(UniversalNodeKind initialKind, TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (initialKind == UniversalNodeKind.Function && IsInsideImpl(declarationNode))
            return UniversalNodeKind.Method;
        return initialKind;
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
        var signature = text[..endOffset].TrimEnd().TrimEnd(';').TrimEnd();
        signature = Normalize(signature);
        if (signature.Length > 500) signature = signature[..500] + "…";
        return signature;
    }

    public string? ResolveImportPath(string importPath, string currentVirtualFilePath) => null;

    public IReadOnlyList<string> GetExtraModifiers(TsNode declarationNode, string name)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        var modifiers = new List<string>();
        foreach (var child in declarationNode.Children)
        {
            if (child.Type == "visibility_modifier")
            {
                var text = (child.Text ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(text)) modifiers.Add(text);
            }
        }
        return modifiers;
    }

    private static bool IsInsideImpl(TsNode node)
    {
        var parent = node.Parent;
        while (parent is not null)
        {
            var type = parent.Type;
            if (type == "impl_item") return true;
            if (type == "function_item" || type == "source_file") return false;
            parent = parent.Parent;
        }
        return false;
    }

    private static int FindBodyLikeChildOffset(TsNode node)
    {
        var startByte = node.StartIndex;
        var bodyLike = node.Children.FirstOrDefault(child => IsBodyLike(child.Type));
        return bodyLike is not null ? bodyLike.StartIndex - startByte : -1;
    }

    private static bool IsBodyLike(string type) => type switch
    {
        "block" or "declaration_list" or "enum_variant_list" or "field_declaration_list" => true,
        _ => false,
    };

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var collapsed = WhitespaceRegex.Replace(input, " ");
        return collapsed.Trim();
    }
}
