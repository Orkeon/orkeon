using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Adapters;

public sealed class RubyAdapter : ILanguageAdapter
{
    private static readonly HashSet<string> BodyTypes = new(StringComparer.Ordinal) { "body_statement" };

    // The fields a declaration head ends with, last first.
    private static readonly string[] HeadFields = ["parameters", "superclass", "name"];

    public string LanguageName => "ruby";
    public IReadOnlyList<string> FileExtensions { get; } = [".rb"];

    public string DeclarationQuery => """
        (module) @decl
        (class) @decl
        (singleton_class) @decl
        (method) @decl
        (singleton_method) @decl
        (assignment left: (constant)) @decl
        """;

    // Only `require_relative` names a file of the codebase; `require` reaches the load path (gems).
    public string ImportQuery => """
        ((call
            method: (identifier) @_require
            arguments: (argument_list (string))) @import
          (#eq? @_require "require_relative"))
        """;

    public string CallQuery => """
        (call) @call
        """;

    public string InheritanceQuery => """
        (superclass) @extends
        ((call
            method: (identifier) @_mixin
            arguments: (argument_list) @implements)
          (#match? @_mixin "^(include|extend|prepend)$"))
        """;

    public string DocCommentQuery => """
        (comment) @doc
        """;

    public string DecoratorQuery => string.Empty;

    public string StatementQuery => """
        (assignment) @stmt
        (operator_assignment) @stmt
        (if) @stmt
        (unless) @stmt
        (elsif) @stmt
        (else) @stmt
        (if_modifier) @stmt
        (unless_modifier) @stmt
        (case) @stmt
        (when) @stmt
        (while) @stmt
        (until) @stmt
        (while_modifier) @stmt
        (until_modifier) @stmt
        (for) @stmt
        (return) @stmt
        (break) @stmt
        (next) @stmt
        (begin) @stmt
        (rescue) @stmt
        (ensure) @stmt
        (call) @stmt
        (yield) @stmt
        (lambda) @stmt
        (do_block) @stmt
        (block) @stmt
        """;

    public UniversalNodeKind MapNodeKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "module" => UniversalNodeKind.Module,
        "class" or "singleton_class" => UniversalNodeKind.Class,
        "method" => UniversalNodeKind.Function,
        "singleton_method" => UniversalNodeKind.Method,
        "assignment" => UniversalNodeKind.Constant,
        _ => UniversalNodeKind.Unknown,
    };

    public StatementKind MapStatementKind(string treeSitterNodeType) => treeSitterNodeType switch
    {
        "assignment" or "operator_assignment" => StatementKind.Assignment,
        "if" or "unless" or "if_modifier" or "unless_modifier" => StatementKind.If,
        "elsif" => StatementKind.ElseIf,
        "else" => StatementKind.Else,
        "case" => StatementKind.Switch,
        "when" => StatementKind.SwitchCase,
        "while" or "until" or "while_modifier" or "until_modifier" => StatementKind.While,
        "for" => StatementKind.ForOf,
        "return" => StatementKind.Return,
        "break" => StatementKind.Break,
        "next" => StatementKind.Continue,
        "begin" => StatementKind.TryCatch,
        "rescue" => StatementKind.Catch,
        "ensure" => StatementKind.Finally,
        "call" => StatementKind.MethodCall,
        "yield" => StatementKind.Yield,
        "lambda" => StatementKind.Closure,
        "do_block" or "block" => StatementKind.Callback,
        _ => StatementKind.Unknown,
    };

    public UniversalNodeKind RefineKind(UniversalNodeKind initialKind, TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        if (initialKind != UniversalNodeKind.Function) return initialKind;
        // A `def` in a class or module body is a method; `initialize` is the constructor.
        if (AdapterText.Ancestor(declarationNode, "class", "module", "singleton_class") is null) return initialKind;
        return declarationNode.GetChildForField("name")?.Text == "initialize" ? UniversalNodeKind.Constructor : UniversalNodeKind.Method;
    }

    public string ExtractName(TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        return declarationNode.Type switch
        {
            "assignment" => declarationNode.GetChildForField("left")?.Text ?? string.Empty,
            "singleton_class" => "<< " + declarationNode.GetChildForField("value")?.Text,
            _ => declarationNode.GetChildForField("name")?.Text ?? string.Empty,
        };
    }

    public string ExtractSignature(TsNode declarationNode, string fullSource)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        // The head ends with the name, the superclass or the parameters: a comment or the body may follow.
        var head = HeadFields
            .Select(field => declarationNode.GetChildForField(field))
            .FirstOrDefault(child => child is not null);
        return head is null || declarationNode.Type == "assignment"
            ? AdapterText.Signature(declarationNode, BodyTypes)
            : AdapterText.SignatureUpTo(declarationNode, head.EndIndex);
    }

    public string? ResolveImportPath(string importPath, string currentVirtualFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(importPath);
        ArgumentException.ThrowIfNullOrEmpty(currentVirtualFilePath);
        var target = AdapterText.Unquote(importPath);
        if (target.Length == 0) return null;
        if (!target.EndsWith(".rb", StringComparison.Ordinal)) target += ".rb";
        return AdapterText.JoinRelative(currentVirtualFilePath, target);
    }

    public IReadOnlyList<string> GetExtraModifiers(TsNode declarationNode, string name)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        return declarationNode.Type == "singleton_method" ? ["static"] : [];
    }
}
