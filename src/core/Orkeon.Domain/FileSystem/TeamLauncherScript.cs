using System.Globalization;
using System.Text;

namespace Orkeon.Domain.FileSystem;

/// <summary>
/// One argument of the <c>orkeon</c> command a team's launcher writes (STUDIO-51): a word written
/// bare, a literal value, a path inside the team anchored to the launcher's own folder, or the
/// value of a single-value option attached to its name. The composer
/// (<see cref="TeamLauncherScript"/>) writes each one for the shell that reads it.
/// </summary>
public abstract class TeamLauncherArgument
{
    private protected TeamLauncherArgument()
    {
    }

    /// <summary>
    /// Written bare in both shells: the verb or an option name — <c>run</c>, <c>--mount</c>. ASCII
    /// letters, digits, <c>-</c>, <c>_</c>, <c>.</c>, <c>/</c> and <c>:</c> only: nothing either
    /// shell reads.
    /// </summary>
    public static TeamLauncherArgument Word(string text) => new WordArgument(Bare(text, nameof(text)));

    /// <summary>A value handed to the runner as it is: a sample input, a path, an id.</summary>
    public static TeamLauncherArgument Literal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new LiteralArgument(text);
    }

    /// <summary>
    /// <paramref name="before"/>, the launcher's own folder, <paramref name="relative"/> and
    /// <paramref name="after"/>, as one argument — the team stays relocatable: <c>("", "crew", "")</c>,
    /// or a mount between the mount grammar's quotes, <c>("\"", "output", "\":/output:rw")</c>.
    /// <paramref name="relative"/> is never empty and never starts with a backslash or a quote: the
    /// folder's trailing backslash would escape it for the runner.
    /// </summary>
    public static TeamLauncherArgument InTeam(string before, string relative, string after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentException.ThrowIfNullOrEmpty(relative);
        if (relative[0] is '\\' or '"')
            throw new ArgumentException("A path inside the team starts with neither a backslash nor a quote.", nameof(relative));

        return new InTeamArgument(before, relative, after);
    }

    /// <summary>
    /// <c>--option=value</c>, one argument: how a single-value option is written, so a value
    /// starting with <c>-</c> is never read as the next option. Never empty: the runner's grammar
    /// refuses <c>--option=</c>.
    /// </summary>
    public static TeamLauncherArgument Assign(string option, string value) => Assign(option, Literal(value));

    /// <summary><c>--option=value</c>, one argument, the value a <see cref="Literal"/> or an <see cref="InTeam"/>.</summary>
    public static TeamLauncherArgument Assign(string option, TeamLauncherArgument value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is not (LiteralArgument or InTeamArgument))
            throw new ArgumentException("An option's value is a literal or a path inside the team.", nameof(value));
        if (value is LiteralArgument { Text.Length: 0 })
            throw new ArgumentException("An option's value is never empty: the runner refuses '--option='.", nameof(value));

        return new AssignArgument(Bare(option, nameof(option)), value);
    }

    private static string Bare(string text, string parameter)
    {
        ArgumentException.ThrowIfNullOrEmpty(text, parameter);
        if (!text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/' or ':'))
            throw new ArgumentException($"'{text}' is not written bare: letters, digits, '-', '_', '.', '/' and ':' only.", parameter);

        return text;
    }

    internal sealed class WordArgument(string text) : TeamLauncherArgument
    {
        public string Text { get; } = text;
    }

    internal sealed class LiteralArgument(string text) : TeamLauncherArgument
    {
        public string Text { get; } = text;
    }

    internal sealed class InTeamArgument(string before, string relative, string after) : TeamLauncherArgument
    {
        public string Before { get; } = before;

        public string Relative { get; } = relative;

        public string After { get; } = after;
    }

    internal sealed class AssignArgument(string option, TeamLauncherArgument value) : TeamLauncherArgument
    {
        public string Option { get; } = option;

        public TeamLauncherArgument Value { get; } = value;
    }
}

/// <summary>
/// What a team's launchers launch, and for whom (STUDIO-51): the composer
/// (<see cref="TeamLauncherScript"/>) turns it into <c>run.cmd</c> and <c>run.sh</c>. The writers
/// say what only they know — what to launch —; the composer writes it.
/// </summary>
public sealed record TeamLauncherSpec
{
    /// <summary>The team's name in the header — its folder through the folder rule (<see cref="FolderSlug"/>).</summary>
    public required string TeamName { get; init; }

    /// <summary>
    /// The team's folder, where the launchers live: <c>%~dp0</c> and <c>$DIR</c> stand for it, and the
    /// length of <c>run.cmd</c>'s command is measured with it (<see cref="TeamLauncherScript.MeasureWindows"/>).
    /// </summary>
    public required string TeamDirectory { get; init; }

    /// <summary>Whether Orkeon Studio writes the launchers: they say an edit is lost when it writes them again.</summary>
    public bool WrittenByStudio { get; init; }

    /// <summary>Whether the crew is a script (<c>crew/crew.ork.ts</c>), which takes no sample input.</summary>
    public bool IsScript { get; init; }

    /// <summary>The <c>orkeon</c> command, one segment per option: the verb and the crew first, each value after its option.</summary>
    public IReadOnlyList<IReadOnlyList<TeamLauncherArgument>> Segments { get; init; } = [];
}

/// <summary>How long the <c>orkeon</c> command of a <c>run.cmd</c> is once <c>cmd</c> expanded it (STUDIO-51, decision 8).</summary>
/// <param name="Length">Its length, in characters: every <c>%~dp0</c> the team's folder, every <c>%%</c> one character.</param>
/// <param name="LongestOption">The option whose segment is the longest — the verb, <c>run</c>, when the crew's path is.</param>
public sealed record TeamLauncherCommandLength(int Length, string LongestOption)
{
    /// <summary>Whether <c>cmd</c> runs it: at most <see cref="TeamLauncherScript.WindowsCommandLimit"/> characters.</summary>
    public bool Fits => Length <= TeamLauncherScript.WindowsCommandLimit;
}

/// <summary>
/// The text of a team's launchers — <c>run.cmd</c> and <c>run.sh</c> — for both writers: <c>forge
/// promote</c> and Orkeon Studio (STUDIO-51). Pure: it renders text, the writers write the files.
/// One composer, because the two writers used to keep a copy each of the frame and of the way a
/// value is written, and drifted: one lost a <c>%</c> the other doubled.
/// <para>
/// <c>run.cmd</c> is read twice. <c>cmd</c> reads the file — its percents, then its special
/// characters, following its quotes — and hands the line to <c>orkeon.exe</c>, whose C runtime
/// splits it — following its own quotes, where <c>\"</c> is a literal quote that, for <c>cmd</c>,
/// leaves the quotes. So each value is written for the runner first, between quotes, then for
/// <c>cmd</c>: every <c>%</c> doubled; a value without a quote stays between <c>cmd</c>'s quotes
/// from end to end; a value with one is written outside them — each quote <c>^"</c>, each special
/// character escaped —, except the stretch that carries <c>%~dp0</c>, which a bare quote opens and
/// the next one closes: the launcher's folder, which nobody controls, is always between quotes.
/// A line break, which ends a <c>cmd</c> command, is written as a space, and the file says so.
/// </para>
/// <para>
/// The frame: everything up to <c>chcp 65001</c> is ASCII — <c>cmd</c> decodes a line in the
/// console's code page until then —, <c>setlocal</c> fixes the parser whatever the registry says,
/// the caller's code page is captured, and the command runs in a block <c>cmd</c> decodes whole in
/// UTF-8, whose first command gives the code page back and whose last, <c>exit /b</c>, keeps
/// <c>orkeon</c>'s exit code and never lets <c>cmd</c> read the file again — a launcher written
/// again during a run is not read half-way. Beyond <see cref="WindowsCommandLimit"/> characters
/// the block launches nothing and says why. <c>run.sh</c> needs none of it: single quotes keep any
/// value, line breaks included.
/// </para>
/// </summary>
public static class TeamLauncherScript
{
    /// <summary>The longest command <c>cmd</c> runs, once its percents are expanded.</summary>
    public const int WindowsCommandLimit = 8191;

    private const string Anchor = "%~dp0";

    private const string WindowsSampleComment = "The --var/--initial-context values below are the test sample: adapt them to the real run.";

    private const string PosixSampleComment = "The --var/--initial-context lines below are the test sample: adapt them to the real run.";

    private const string StudioNote =
        "Orkeon Studio writes this file again from studio-team.json whenever the team's model setting "
        + "or folders change, at its import and before its schedule is installed: an edit made here is lost then.";

    private const string LineBreakComment = "A line break in a value below is written as a space: a cmd command holds one line.";

    /// <summary>
    /// The launchers' header, after each shell's comment marker: the team they launch. <c>forge
    /// rename</c> finds it again to rename the team in it (STUDIO-28) — not one character of it
    /// may change. ASCII: <c>cmd</c> reads it before <c>chcp 65001</c>.
    /// </summary>
    public static string Header(string teamName)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamName);
        if (!teamName.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new ArgumentException($"'{teamName}' is not a team's folder name: ASCII letters, digits, '-', '_' and '.' only.", nameof(teamName));

        return HeaderMarker + teamName + "'.";
    }

    /// <summary>
    /// What every header starts with, the team's name aside (STUDIO-63): the signature of a launcher
    /// Orkeon wrote — <c>forge promote</c> or Studio. A launcher without it was written by something
    /// else, and Studio leaves it as it is (<see cref="CarriesHeader"/>).
    /// </summary>
    public const string HeaderMarker = "Generated by Orkeon Forge for the team '";

    /// <summary>
    /// Whether <paramref name="launcherText"/> is a launcher Orkeon wrote (STUDIO-63): one of its first
    /// three lines, after an optional <c>rem </c>, <c>REM </c> or <c># </c> and blanks, starts with
    /// <see cref="HeaderMarker"/> — the header both writers put on line 2. CR LF, LF and a leading BOM
    /// are tolerated; the name after the marker is not read. Pure: the caller reads the file.
    /// </summary>
    public static bool CarriesHeader(string launcherText)
    {
        ArgumentNullException.ThrowIfNull(launcherText);

        var text = launcherText.AsSpan().TrimStart('\uFEFF');
        for (var line = 0; line < 3 && !text.IsEmpty; line++)
        {
            var end = text.IndexOf('\n');
            var current = end < 0 ? text : text[..end];
            text = end < 0 ? ReadOnlySpan<char>.Empty : text[(end + 1)..];

            current = current.TrimEnd('\r').TrimStart();
            foreach (var marker in (ReadOnlySpan<string>)["rem ", "REM ", "# "])
            {
                if (current.StartsWith(marker, StringComparison.Ordinal))
                {
                    current = current[marker.Length..].TrimStart();
                    break;
                }
            }

            if (current.StartsWith(HeaderMarker, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary><c>run.cmd</c>'s text: UTF-8 without a BOM, CR LF line breaks.</summary>
    public static string Windows(TeamLauncherSpec spec)
    {
        var segments = Validate(spec);

        var lineBreaks = false;
        var tokens = segments
            .Select(segment => segment.Select(argument => BatchToken(argument, ref lineBreaks)).ToList())
            .ToList();
        var command = "orkeon " + string.Join(' ', tokens.Select(segment => string.Join(' ', segment)));

        var text = new StringBuilder();
        void Line(string line) => text.Append(line).Append("\r\n");

        Line("@echo off");
        Line("rem " + Header(spec.TeamName));
        if (spec.WrittenByStudio)
            Line("rem " + StudioNote);
        Line(spec.IsScript
            ? "rem The crew lives in crew\\crew.ork.ts - edit it there, this script only launches it."
            : "rem " + WindowsSampleComment);
        if (lineBreaks)
            Line("rem " + LineBreakComment);
        // Whatever the registry says: %~dp0 and cd /d exist, and a '!' is a character.
        Line("setlocal EnableExtensions DisableDelayedExpansion");
        // The caller's code page — the number after the colon, in any language —, given back before
        // orkeon starts. No ORKEON_ name: the runner would read the variable as a setting.
        Line("for /f \"tokens=2 delims=:.\" %%p in ('chcp') do set \"LAUNCHER_CP=%%p\"");
        Line("chcp 65001 >nul");
        // cmd reads and decodes the whole block before running it: in UTF-8 now.
        Line("(");
        Line("  chcp %LAUNCHER_CP% >nul 2>&1");

        var measure = Measure(spec, tokens);
        if (measure.Fits)
        {
            Line("  cd /d \"%~dp0\" || exit /b 1");
            Line("  " + command);
            // Inside the block: orkeon's exit code, and cmd never reads the file again — a launcher
            // written again during the run is not read from the middle.
            Line("  exit /b");
        }
        else
        {
            // Names and numbers only: no value of the command is written, nothing cmd would read.
            Line(string.Create(
                CultureInfo.InvariantCulture,
                $"  >&2 echo run.cmd runs nothing: its orkeon command is {measure.Length} characters long, over the {WindowsCommandLimit} a cmd command holds - the longest option is {measure.LongestOption}."));
            Line("  exit /b 1");
        }

        Line(")");
        return text.ToString();
    }

    /// <summary><c>run.sh</c>'s text: LF line breaks; the writer makes it executable.</summary>
    public static string Posix(TeamLauncherSpec spec)
    {
        var segments = Validate(spec);

        var text = new StringBuilder();
        void Line(string line) => text.Append(line).Append('\n');

        Line("#!/usr/bin/env sh");
        Line("# " + Header(spec.TeamName));
        if (spec.WrittenByStudio)
            Line("# " + StudioNote);
        Line(spec.IsScript
            ? "# The crew lives in crew/crew.ork.ts — edit it there, this script only launches it."
            : "# " + PosixSampleComment);
        // Symlinks resolved before taking the directory: a launcher linked onto PATH — ordinary for
        // a folder people are told they can share — would otherwise take ~/bin for the team's
        // folder. Plain readlink (no -f) is portable where GNU's -f is not, and `|| exit 1` keeps a
        // failed cd from running the team in the caller's directory.
        Line("SELF=\"$0\"");
        Line("while [ -L \"$SELF\" ]; do");
        Line("  LINK=\"$(readlink \"$SELF\")\"");
        Line("  case \"$LINK\" in");
        Line("    /*) SELF=\"$LINK\" ;;");
        Line("    *) SELF=\"$(dirname \"$SELF\")/$LINK\" ;;");
        Line("  esac");
        Line("done");
        Line("DIR=\"$(cd \"$(dirname \"$SELF\")\" && pwd)\" || exit 1");
        // Run FROM the team's folder: the runner reads a crew and mounts folders under the working
        // directory, and a scheduler starts the launcher anywhere.
        Line("cd \"$DIR\" || exit 1");
        text.Append("exec orkeon");
        foreach (var segment in segments)
            text.Append(" \\\n  ").Append(string.Join(' ', segment.Select(PosixToken)));

        Line("");
        return text.ToString();
    }

    /// <summary>
    /// The length of <c>run.cmd</c>'s <c>orkeon</c> command once <c>cmd</c> has expanded it, with
    /// the team's folder as it is: beyond <see cref="WindowsCommandLimit"/>, <c>run.cmd</c> launches
    /// nothing and says why (decision 8). A team moved deeper later is measured again when its
    /// launchers are written again.
    /// </summary>
    public static TeamLauncherCommandLength MeasureWindows(TeamLauncherSpec spec)
    {
        var segments = Validate(spec);

        var lineBreaks = false;
        return Measure(spec, [.. segments.Select(segment => segment.Select(argument => BatchToken(argument, ref lineBreaks)).ToList())]);
    }

    /// <summary>
    /// One argument as a line typed — or pasted — at the <c>cmd</c> prompt reads it back
    /// (decision 9): the line Studio shows to be copied. A word without a special character stays
    /// bare. Otherwise the rules of <c>run.cmd</c>, but for the percents: at the prompt
    /// <c>%%</c> stays two characters, so a value with a <c>%</c> is written outside the quotes,
    /// like a value with a quote, and the character after each <c>%</c> takes a caret —
    /// <c>%^PATH%</c> names no variable, and <c>cmd</c> hands over <c>%PATH%</c>. A line break is
    /// written as a space. PowerShell reads the line otherwise.
    /// </summary>
    public static string QuoteForCmdPrompt(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);

        var flattened = false;
        var value = OneLine(argument, ref flattened);
        if (value.Length > 0 && !value.Any(c => c is ' ' or '\t' or '"' or '%' || IsCmdSpecial(c)))
            return value;

        var units = CrtUnits([new Piece(value)]);
        var text = new StringBuilder();
        if (!value.Contains('"', StringComparison.Ordinal) && !value.Contains('%', StringComparison.Ordinal))
        {
            foreach (var unit in units)
                text.Append(unit.Character);
            return text.ToString();
        }

        var afterPercent = false;
        foreach (var unit in units)
        {
            var c = unit.Character;
            if (c == '"' || IsCmdSpecial(c) || afterPercent)
                text.Append('^');
            text.Append(c);
            afterPercent = c == '%';
        }

        return text.ToString();
    }

    /// <summary>One argument as a POSIX shell reads it back: between single quotes, a quote closed, escaped and reopened.</summary>
    public static string QuoteForPosixShell(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        return "'" + argument.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private static IReadOnlyList<IReadOnlyList<TeamLauncherArgument>> Validate(TeamLauncherSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.TeamDirectory);
        ArgumentNullException.ThrowIfNull(spec.Segments);
        if (spec.Segments.Count == 0 || spec.Segments.Any(segment => segment is null || segment.Count == 0 || segment.Any(argument => argument is null)))
            throw new ArgumentException("A launcher runs one command: segments, each with at least one argument.", nameof(spec));

        return spec.Segments;
    }

    private static TeamLauncherCommandLength Measure(TeamLauncherSpec spec, List<List<string>> tokens)
    {
        var folder = spec.TeamDirectory.TrimEnd('\\', '/').Length + 1;
        var length = "orkeon".Length;
        var longest = (Length: -1, Option: "");
        for (var i = 0; i < tokens.Count; i++)
        {
            var segment = tokens[i].Sum(token => ExpandedLength(token, folder)) + tokens[i].Count;
            length += segment;
            if (segment > longest.Length)
                longest = (segment, OptionOf(spec.Segments[i][0]));
        }

        return new TeamLauncherCommandLength(length, longest.Option);
    }

    /// <summary>The length of <paramref name="token"/> once <c>cmd</c> read its percents.</summary>
    private static int ExpandedLength(string token, int folder)
    {
        var length = 0;
        for (var i = 0; i < token.Length; i++)
        {
            if (token[i] == '%' && string.CompareOrdinal(token, i, Anchor, 0, Anchor.Length) == 0)
            {
                length += folder;
                i += Anchor.Length - 1;
            }
            else if (token[i] == '%')
            {
                // Every other percent the composer writes is doubled.
                length++;
                i++;
            }
            else
            {
                length++;
            }
        }

        return length;
    }

    private static string OptionOf(TeamLauncherArgument argument) => argument switch
    {
        TeamLauncherArgument.WordArgument word => word.Text,
        TeamLauncherArgument.AssignArgument assign => assign.Option,
        _ => "",
    };

    // ── run.cmd ──

    /// <summary>One argument of <c>run.cmd</c>'s command, written for the C runtime then for <c>cmd</c>.</summary>
    private static string BatchToken(TeamLauncherArgument argument, ref bool lineBreaks)
    {
        switch (argument)
        {
            case TeamLauncherArgument.WordArgument word:
                return word.Text;

            case TeamLauncherArgument.AssignArgument assign:
                return assign.Option + "=" + BatchToken(assign.Value, ref lineBreaks);

            case TeamLauncherArgument.LiteralArgument literal:
                return BatchQuoted([new Piece(OneLine(literal.Text, ref lineBreaks))]);

            case TeamLauncherArgument.InTeamArgument inTeam:
                return BatchQuoted(
                [
                    new Piece(OneLine(inTeam.Before, ref lineBreaks)),
                    Piece.Folder,
                    new Piece(OneLine(inTeam.Relative, ref lineBreaks)),
                    new Piece(OneLine(inTeam.After, ref lineBreaks)),
                ]);

            default:
                throw new ArgumentException("Unknown launcher argument.", nameof(argument));
        }
    }

    /// <summary>
    /// The pieces between the C runtime's quotes (decision 2): the runner's quoting first, then
    /// <c>cmd</c>'s — every <c>%</c> doubled; without a quote in the value, between <c>cmd</c>'s
    /// quotes end to end; with one, outside them but for the stretch that carries the folder.
    /// </summary>
    private static string BatchQuoted(IReadOnlyList<Piece> pieces)
    {
        var units = CrtUnits(pieces);
        var text = new StringBuilder();

        // The stretch between cmd's quotes, its two quotes included: the whole token when the
        // value holds no quote of its own; else the folder's, from the last quote before it to the
        // first after it — the C runtime's delimiters at worst —; else none.
        int opener = 0, closer = units.Count - 1;
        if (units.Skip(1).SkipLast(1).Any(unit => !unit.IsFolder && unit.Character == '"'))
        {
            var folder = units.FindIndex(unit => unit.IsFolder);
            opener = folder < 0 ? -1 : units.FindLastIndex(folder, unit => !unit.IsFolder && unit.Character == '"');
            closer = folder < 0 ? -1 : units.FindIndex(folder, unit => !unit.IsFolder && unit.Character == '"');
        }

        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (unit.IsFolder)
            {
                text.Append(Anchor);
                continue;
            }

            var c = unit.Character;
            if (i >= opener && i <= closer)
            {
                // Between cmd's quotes: only the percents are read there.
                text.Append(c);
            }
            else if (c == '"' || IsCmdSpecial(c))
            {
                text.Append('^').Append(c);
            }
            else
            {
                text.Append(c);
            }

            if (c == '%')
                text.Append('%');
        }

        return text.ToString();
    }

    // ── run.sh ──

    private static string PosixToken(TeamLauncherArgument argument) => argument switch
    {
        TeamLauncherArgument.WordArgument word => word.Text,
        TeamLauncherArgument.LiteralArgument literal => QuoteForPosixShell(literal.Text),
        TeamLauncherArgument.InTeamArgument inTeam =>
            $"\"{DoubleQuoted(inTeam.Before)}$DIR/{DoubleQuoted(inTeam.Relative)}{DoubleQuoted(inTeam.After)}\"",
        TeamLauncherArgument.AssignArgument assign => assign.Option + "=" + PosixToken(assign.Value),
        _ => throw new ArgumentException("Unknown launcher argument.", nameof(argument)),
    };

    /// <summary>Text inside a POSIX double-quoted word: what the shell would expand or end the word on, escaped.</summary>
    private static string DoubleQuoted(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '"' or '$' or '`')
                builder.Append('\\');
            builder.Append(c);
        }

        return builder.ToString();
    }

    // ── shared ──

    /// <summary>
    /// The characters the C runtime reads back as <paramref name="pieces"/>, between its quotes: a
    /// quote escaped with a backslash, the backslashes before it doubled, the backslashes before the
    /// closing quote doubled (decision 2, step 1). The folder is one unit: the relative path that
    /// follows it never starts with a backslash or a quote, so its own trailing backslash escapes
    /// nothing.
    /// </summary>
    private static List<Unit> CrtUnits(IReadOnlyList<Piece> pieces)
    {
        var units = new List<Unit> { new('"') };
        var backslashes = 0;
        foreach (var piece in pieces)
        {
            if (piece.IsFolder)
            {
                units.AddRange(Enumerable.Repeat(new Unit('\\'), backslashes));
                backslashes = 0;
                units.Add(Unit.Folder);
                continue;
            }

            foreach (var c in piece.Text)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                units.AddRange(Enumerable.Repeat(new Unit('\\'), c == '"' ? (backslashes * 2) + 1 : backslashes));
                units.Add(new Unit(c));
                backslashes = 0;
            }
        }

        units.AddRange(Enumerable.Repeat(new Unit('\\'), backslashes * 2));
        units.Add(new Unit('"'));
        return units;
    }

    /// <summary>What <c>cmd</c> reads outside quotes: a caret escapes it.</summary>
    private static bool IsCmdSpecial(char c) => c is '^' or '&' or '|' or '<' or '>' or '(' or ')';

    /// <summary>
    /// <paramref name="text"/> on one line: a line break and every control character but the tab
    /// become a space — a <c>cmd</c> command is one line, and Ctrl-Z reads as its end (decision 3).
    /// </summary>
    private static string OneLine(string text, ref bool replaced)
    {
        if (!text.Any(c => char.IsControl(c) && c != '\t'))
            return text;

        replaced = true;
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = char.IsControl(source[i]) && source[i] != '\t' ? ' ' : source[i];
        });
    }

    /// <summary>A stretch of an argument: text, or the launcher's folder.</summary>
    private readonly record struct Piece(string Text, bool IsFolder = false)
    {
        public static Piece Folder { get; } = new("", IsFolder: true);
    }

    /// <summary>One character the C runtime reads, or the launcher's folder.</summary>
    private readonly record struct Unit(char Character, bool IsFolder = false)
    {
        public static Unit Folder { get; } = new('\0', IsFolder: true);
    }
}
