using System.Globalization;

namespace Orkeon.Application.Crew.Skills;

/// <summary>
/// How an agent's <c>skills:</c> reach its prompt, bound to the <c>Orkeon:Skills</c> section by
/// <c>AddOrkeonInfrastructure()</c>. Each skill is a markdown file read through the virtual file system and
/// appended to the agent's system prompt under a <c>## Skills</c> heading, cut at <see cref="MaxSkillChars"/>,
/// the whole block at <see cref="MaxBlockChars"/>. An agent with <c>skills: { auto: true }</c> has the
/// decision model pick, for each task, at most three skills of <see cref="Library"/>.
/// </summary>
public sealed class AgentSkillsOptions
{
    /// <summary>The configuration section: <c>Orkeon:Skills</c>.</summary>
    public const string SectionName = "Orkeon:Skills";

    /// <summary>The library by default: <c>/workspace/.orkeon/skills</c>.</summary>
    public const string DefaultLibrary = "/workspace/.orkeon/skills";

    /// <summary>The cap of one skill by default, in characters: 4000.</summary>
    public const int DefaultMaxSkillChars = 4_000;

    /// <summary>The cap of the whole block by default, in characters: 12000, three skills at their cap.</summary>
    public const int DefaultMaxBlockChars = 12_000;

    /// <summary>
    /// The virtual folder an <c>auto</c> agent's skills are picked from, one <c>&lt;name&gt;.md</c> file per
    /// skill (sub-folders are not read). <c>/workspace/.orkeon/skills</c> by default.
    /// </summary>
    public string Library { get; set; } = DefaultLibrary;

    /// <summary>
    /// The characters of one skill the prompt carries; the rest is cut, and the cut is said in the prompt and
    /// in <c>agent.skills</c>. 4000 by default.
    /// </summary>
    public int MaxSkillChars { get; set; } = DefaultMaxSkillChars;

    /// <summary>
    /// The characters the whole skills block carries, every skill's text included; a skill past it is cut to
    /// what remains, or left out. 12000 by default, three skills at their cap.
    /// </summary>
    public int MaxBlockChars { get; set; } = DefaultMaxBlockChars;

    /// <summary>What is wrong with <paramref name="options"/>, empty when nothing is.</summary>
    /// <param name="options">The options to judge.</param>
    public static IReadOnlyList<string> Problems(AgentSkillsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Library) || !options.Library.StartsWith('/'))
            problems.Add($"{SectionName}:Library is '{options.Library}', which is not a virtual folder: write an absolute virtual path, such as {DefaultLibrary}.");
        if (options.MaxSkillChars <= 0)
            problems.Add(Positive("MaxSkillChars", options.MaxSkillChars, DefaultMaxSkillChars));
        if (options.MaxBlockChars <= 0)
            problems.Add(Positive("MaxBlockChars", options.MaxBlockChars, DefaultMaxBlockChars));
        return problems;
    }

    private static string Positive(string key, int value, int example) =>
        $"{SectionName}:{key} is '{value.ToString(CultureInfo.InvariantCulture)}', which is not a number of characters: " +
        $"write 1 or more, such as {example.ToString(CultureInfo.InvariantCulture)}.";
}
