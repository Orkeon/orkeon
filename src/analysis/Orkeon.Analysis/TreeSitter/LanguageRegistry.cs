using TsLanguage = TreeSitter.Language;

namespace Orkeon.Analysis.TreeSitter;

internal sealed record GrammarInfo(string Language, IReadOnlyList<string> Extensions);

internal static class LanguageRegistry
{
    /// <summary>The languages the index serves and the file extensions each one claims.</summary>
    public static IReadOnlyList<GrammarInfo> Shipped { get; } =
    [
        new("typescript", [".ts", ".tsx"]),
        new("python", [".py"]),
        new("csharp", [".cs"]),
        new("go", [".go"]),
        new("rust", [".rs"]),
        new("javascript", [".js", ".jsx", ".mjs", ".cjs"]),
        new("java", [".java"]),
        new("c", [".c"]),
        new("cpp", [".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx", ".h"]),
        new("php", [".php"]),
        new("ruby", [".rb"]),
        new("bash", [".sh", ".bash"]),
        new("markdown", [".md", ".markdown"]),
    ];

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
            ["javascript"] = ("tree-sitter-javascript", "tree_sitter_javascript"),
            ["java"] = ("tree-sitter-java", "tree_sitter_java"),
            ["c"] = ("tree-sitter-c", "tree_sitter_c"),
            ["cpp"] = ("tree-sitter-cpp", "tree_sitter_cpp"),
            ["php"] = ("tree-sitter-php", "tree_sitter_php"),
            ["ruby"] = ("tree-sitter-ruby", "tree_sitter_ruby"),
            ["bash"] = ("tree-sitter-bash", "tree_sitter_bash"),
            // Parsed on request only: no adapter indexes these, so they claim no extension.
            // OCaml is left out — TreeSitter.DotNet ships no macOS build of it.
            ["agda"] = ("tree-sitter-agda", "tree_sitter_agda"),
            ["css"] = ("tree-sitter-css", "tree_sitter_css"),
            ["embedded-template"] = ("tree-sitter-embedded-template", "tree_sitter_embedded_template"),
            ["haskell"] = ("tree-sitter-haskell", "tree_sitter_haskell"),
            ["html"] = ("tree-sitter-html", "tree_sitter_html"),
            ["jsdoc"] = ("tree-sitter-jsdoc", "tree_sitter_jsdoc"),
            ["json"] = ("tree-sitter-json", "tree_sitter_json"),
            ["julia"] = ("tree-sitter-julia", "tree_sitter_julia"),
            ["ql"] = ("tree-sitter-ql", "tree_sitter_ql"),
            ["razor"] = ("tree-sitter-razor", "tree_sitter_razor"),
            ["scala"] = ("tree-sitter-scala", "tree_sitter_scala"),
            ["verilog"] = ("tree-sitter-verilog", "tree_sitter_verilog"),
        };

    public static TsLanguage Create(string language)
    {
        if (!Map.TryGetValue(language, out var entry))
            throw new ArgumentException($"Unsupported tree-sitter language '{language}'.", nameof(language));
        return new TsLanguage(entry.Library, entry.Function);
    }

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = "csharp",
        ["ts"] = "typescript",
        ["md"] = "markdown",
        ["js"] = "javascript",
        ["c++"] = "cpp",
        ["rb"] = "ruby",
        ["sh"] = "bash",
    };

    /// <summary>The canonical language name of an alias (<c>cs</c>, <c>ts</c>, <c>js</c>); any other name is returned as given.</summary>
    public static string Resolve(string language) => Aliases.GetValueOrDefault(language, language);

    public static bool IsSupported(string language) => Map.ContainsKey(language);

    public static IEnumerable<string> Supported => Map.Keys.Where(k => k != "c-sharp");

    /// <summary>
    /// Native tree-sitter libraries this registry can load, deduplicated (several
    /// languages share one library — <c>tsx</c> rides on <c>tree-sitter-typescript</c>).
    /// Consumed by the publish-time prune guard-rail so the MSBuild whitelist in
    /// <c>src/Directory.Build.targets</c> can never drift away from the registry.
    /// </summary>
    public static IEnumerable<string> Libraries =>
        Map.Values.Select(v => v.Library).Distinct(StringComparer.Ordinal);
}
