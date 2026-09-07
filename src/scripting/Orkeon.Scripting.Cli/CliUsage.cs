using System.Reflection;
using System.Text;

namespace Orkeon.Scripting.Cli;

/// <summary>
/// The top-level verb table, the usage text built from it, and the classification of the first
/// token on the command line.
/// <para>
/// It exists because the entry point used to hand every invocation it did not recognise to the
/// <c>run</c> parser: <c>orkeon --help</c> printed the run option table without ever naming
/// <c>init</c>, <c>doctor</c>, <c>llm</c>, <c>rag</c> or <c>forge</c> — and exited 1 while doing
/// it — so the first command a newcomer types hid the very verb every quickstart opens with.
/// </para>
/// </summary>
internal static class CliUsage
{
    /// <summary>One top-level command: the token the user types, and the line describing it.</summary>
    /// <param name="Name">The verb as typed, lowercase.</param>
    /// <param name="Summary">The single line shown next to it in the command list.</param>
    internal sealed record CliVerb(string Name, string Summary);

    /// <summary>Reported when the assembly carries no version attribute at all.</summary>
    public const string UnknownVersion = "0.0.0";

    /// <summary>Spellings of "tell me what this tool does".</summary>
    private static readonly string[] HelpTokens = ["--help", "-h", "help"];

    /// <summary>Spellings of "tell me which build this is". <c>-v</c> is taken: it is verbosity.</summary>
    private static readonly string[] VersionTokens = ["--version", "version"];

    /// <summary>
    /// Suffixes <c>orkeon run</c> dispatches on. A first token wearing one is a crew the user
    /// wrote without the optional <c>run</c> verb, never a mistyped command.
    /// </summary>
    private static readonly string[] CrewSuffixes = [".ork.ts", ".ork.js", ".ts", ".js", ".yaml", ".yml"];

    /// <summary>Every verb the CLI dispatches, ordered the way a newcomer meets them.</summary>
    public static IReadOnlyList<CliVerb> Verbs { get; } =
    [
        new("run", "Run a crew: a YAML crew, a .ork.ts script, or a crew directory."),
        new("init", "Configure this machine: pick an LLM provider, write the settings file."),
        new("doctor", "Diagnose the installation: runtime, settings, esbuild, embeddings, tree-sitter."),
        new("llm", "Probe an LLM provider against the test protocol."),
        new("rag", "Ingest, search and evaluate a RAG collection."),
        new("forge", "The Atelier: turn a need in plain words into a validated crew."),
    ];

    /// <summary>This build's version, without the build metadata SourceLink appends.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The single line <c>orkeon --version</c> writes: the tool name and its version.</summary>
    public static string VersionLine => "orkeon " + Version;

    /// <summary>Whether <paramref name="token"/> asks for the usage listing.</summary>
    public static bool IsHelpToken(string token) => Matches(HelpTokens, token);

    /// <summary>Whether <paramref name="token"/> asks for the version.</summary>
    public static bool IsVersionToken(string token) => Matches(VersionTokens, token);

    /// <summary>
    /// Whether <paramref name="token"/> reads as a crew to run rather than as a command: it
    /// carries a path separator, wears an extension the run verb dispatches on, or simply
    /// exists on disk.
    /// </summary>
    public static bool LooksLikeCrewTarget(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (token.Contains('/', StringComparison.Ordinal) || token.Contains('\\', StringComparison.Ordinal))
            return true;

        foreach (var suffix in CrewSuffixes)
        {
            if (token.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // OUT-OF-SCOPE: probing a user-supplied crew path; the CLI entry point runs before any
        // VFS mount exists, and crews live wherever the user invokes us from.
        return File.Exists(token) || Directory.Exists(token);
    }

    /// <summary>Renders the whole usage page, verb table included.</summary>
    public static string Render()
    {
        var width = Verbs.Max(v => v.Name.Length);
        var text = new StringBuilder();

        text.Append(VersionLine).AppendLine(" — build and run AI agent crews.");
        text.AppendLine();
        text.AppendLine("Usage:");
        text.AppendLine("  orkeon <command> [options]");
        text.AppendLine("  orkeon <crew.yaml | crew.ork.ts | crew-directory/> [options]   same as `orkeon run`");
        text.AppendLine();
        text.AppendLine("Commands:");
        foreach (var verb in Verbs)
            text.Append("  ").Append(verb.Name.PadRight(width)).Append("   ").AppendLine(verb.Summary);

        text.AppendLine();
        text.Append("  orkeon <command> --help".PadRight(width + 24)).AppendLine("the options of that command");
        text.Append("  orkeon --version".PadRight(width + 24)).AppendLine("the version of this build");
        text.AppendLine();
        text.AppendLine("Documentation: https://github.com/Orkeon/orkeon");

        return text.ToString();
    }

    private static bool Matches(string[] tokens, string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        foreach (var token in tokens)
        {
            if (string.Equals(candidate, token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string ReadVersion()
    {
        var informational = typeof(CliUsage).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return typeof(CliUsage).Assembly.GetName().Version?.ToString() ?? UnknownVersion;

        // SourceLink appends "+<commit sha>"; the release is what a person asked for, and the
        // commit belongs to `orkeon doctor`.
        var metadata = informational.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? informational : informational[..metadata];
    }
}
