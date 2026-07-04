using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Abstractions.Interfaces;

public interface ILanguageAdapter
{
    string LanguageName { get; }
    IReadOnlyList<string> FileExtensions { get; }

    string DeclarationQuery { get; }
    string ImportQuery { get; }
    string CallQuery { get; }
    string InheritanceQuery { get; }
    string DocCommentQuery { get; }
    string DecoratorQuery { get; }
    string StatementQuery { get; }

    UniversalNodeKind MapNodeKind(string treeSitterNodeType);
    string ExtractSignature(TsNode declarationNode, string fullSource);
    string? ResolveImportPath(string importPath, string currentVirtualFilePath);

    StatementKind MapStatementKind(string treeSitterNodeType);

    UniversalNodeKind RefineKind(UniversalNodeKind initialKind, TsNode declarationNode)
        => initialKind;

    string? ResolveDocComment(TsNode declarationNode, IReadOnlyList<TsNode> docs, string fullSource)
        => null;

    string ExtractName(TsNode declarationNode)
    {
        ArgumentNullException.ThrowIfNull(declarationNode);
        return declarationNode.GetChildForField("name")?.Text ?? string.Empty;
    }

    IReadOnlyList<string> GetExtraModifiers(TsNode declarationNode, string name)
        => [];
}
