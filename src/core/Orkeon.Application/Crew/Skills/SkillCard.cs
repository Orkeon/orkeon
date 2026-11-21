using System.Text.RegularExpressions;

namespace Orkeon.Application.Crew.Skills;

/// <summary>
/// One skill of the library as the decision model is told of it (#434): one line, its name and what it is
/// for, read from the head of its file. Never its body: the front matter's <c>name</c> and
/// <c>description</c>, else the file's first heading and the first line of text under it.
/// </summary>
/// <param name="Path">The skill's virtual path.</param>
/// <param name="Name">Its name: the front matter's, else the first heading, else the file name.</param>
/// <param name="Description">What it is for, on one line and cut at <see cref="DescriptionChars"/>; may be empty.</param>
internal sealed partial record SkillCard(string Path, string Name, string Description)
{
    /// <summary>The characters of a description the model reads.</summary>
    internal const int DescriptionChars = 300;

    /// <summary>The line the decision model reads for this skill.</summary>
    internal string Line => Description.Length == 0 ? Name : $"{Name}: {Description}";

    /// <summary>The card of the skill at <paramref name="path"/>, from the head of its file.</summary>
    /// <param name="path">The skill's virtual path.</param>
    /// <param name="head">The first characters of the file.</param>
    internal static SkillCard Of(string path, string head)
    {
        var lines = head.TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var start = 0;
        if (lines.Count > 0 && lines[0].Trim() == "---")
        {
            var i = 1;
            for (; i < lines.Count && lines[i].Trim() != "---"; i++)
            {
                var field = FieldPattern().Match(lines[i]);
                if (!field.Success)
                    continue;
                var value = field.Groups[2].Value.Trim();
                if (FoldedPattern().IsMatch(value))
                {
                    var folded = new List<string>();
                    while (i + 1 < lines.Count && IndentedPattern().IsMatch(lines[i + 1]))
                        folded.Add(lines[++i].Trim());
                    value = string.Join(' ', folded);
                }
                fields[field.Groups[1].Value] = value.Trim('"', '\'');
            }
            start = i + 1;
        }

        var prose = Prose(lines.Skip(start));
        var heading = prose.FirstOrDefault(l => HeadingPattern().IsMatch(l)) is { } h ? h.TrimStart('#').Trim() : null;
        var paragraph = prose.FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#') && l != "---") ?? string.Empty;
        var name = Present(fields, "name") ?? heading ?? FileName(path);
        var description = WhitespacePattern().Replace(Present(fields, "description") ?? paragraph, " ").Trim();
        return new SkillCard(path, name, Execution.TextCut.Head(description, DescriptionChars, "..."));
    }

    /// <summary>The name a path gives a skill: its file name, less <c>.md</c>.</summary>
    /// <param name="path">The skill's virtual path.</param>
    internal static string FileName(string path)
    {
        var file = path[(path.LastIndexOf('/') + 1)..];
        return file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? file[..^3] : file;
    }

    private static string? Present(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    // The lines outside code fences, trimmed.
    private static List<string> Prose(IEnumerable<string> lines)
    {
        var prose = new List<string>();
        var fenced = false;
        foreach (var line in lines.Select(l => l.Trim()))
        {
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
                fenced = !fenced;
            else if (!fenced)
                prose.Add(line);
        }
        return prose;
    }

    [GeneratedRegex(@"^([A-Za-z][\w-]*):\s*(.*)$")]
    private static partial Regex FieldPattern();

    [GeneratedRegex(@"^[>|][+-]?$")]
    private static partial Regex FoldedPattern();

    [GeneratedRegex(@"^\s+\S")]
    private static partial Regex IndentedPattern();

    [GeneratedRegex(@"^#{1,6}\s+\S")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
