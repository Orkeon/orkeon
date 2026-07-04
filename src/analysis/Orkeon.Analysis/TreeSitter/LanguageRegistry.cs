using TsLanguage = TreeSitter.Language;

namespace Orkeon.Analysis.TreeSitter;

internal static class LanguageRegistry
{
    private static readonly Dictionary<string, (string Library, string Function)> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["typescript"] = ("tree-sitter-typescript", "tree_sitter_typescript"),
            ["tsx"] = ("tree-sitter-typescript", "tree_sitter_tsx"),
            ["python"] = ("tree-sitter-python", "tree_sitter_python"),
            ["csharp"] = ("tree-sitter-c-sharp", "tree_sitter_c_sharp"),
            ["c-sharp"] = ("tree-sitter-c-sharp", "tree_sitter_c_sharp"),
            ["go"] = ("tree-sitter-go", "tree_sitter_go"),
            ["rust"] = ("tree-sitter-rust", "tree_sitter_rust"),
        };

    public static TsLanguage Create(string language)
    {
        if (!Map.TryGetValue(language, out var entry))
            throw new ArgumentException($"Unsupported tree-sitter language '{language}'.", nameof(language));
        return new TsLanguage(entry.Library, entry.Function);
    }

    public static bool IsSupported(string language) => Map.ContainsKey(language);

    public static IEnumerable<string> Supported => Map.Keys.Where(k => k != "c-sharp");
}
