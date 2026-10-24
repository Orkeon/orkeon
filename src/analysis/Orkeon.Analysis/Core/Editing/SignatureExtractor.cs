using Orkeon.Analysis.TreeSitter;
using TsNode = TreeSitter.Node;

namespace Orkeon.Analysis.Core.Editing;

/// <summary>Parameter types of a C# or TypeScript callable, read from its tree-sitter node.</summary>
internal static class SignatureExtractor
{
    public static IReadOnlyList<string>? ParameterTypes(TsNode callable)
    {
        var parameters = callable.ChildByField("parameters");
        if (parameters is null) return null;
        return parameters.Children
            .Where(p => p.IsNamed && p.Type != "comment")
            .Select(TypeOf)
            .ToList();
    }

    private static string TypeOf(TsNode parameter)
    {
        var type = parameter.ChildByField("type");
        if (type is null) return "any";
        return NodeSelector.Compact(type.Text ?? string.Empty).TrimStart(':');
    }
}
