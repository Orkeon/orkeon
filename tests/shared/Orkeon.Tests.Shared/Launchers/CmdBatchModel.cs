using System.Text;

namespace Orkeon.Tests.Shared.Launchers;

/// <summary>A line <see cref="CmdBatchModel"/> says <c>cmd</c> would not hand over intact.</summary>
public sealed class CmdModelException(string message) : Exception(message);

/// <summary>
/// How <c>cmd.exe</c> reads a line, as the documented rules have it (STUDIO-51 § 2, readings 1 to
/// 7) — a model, not <c>cmd</c>: the proof on <c>cmd</c> is the owner's Windows recipe. Three
/// readings: a line of a batch file (<c>run.cmd</c>), the line of <c>cmd /d /v:off /s /c</c> (what
/// a scheduled task runs) and a line pasted at the prompt (what Studio's « Copy the command »
/// gives). Each gives the line <c>cmd</c> hands to <c>CreateProcess</c> — <see cref="CrtArgv"/>
/// splits it — or throws <see cref="CmdModelException"/> where <c>cmd</c> would cut, run or drop
/// something: an operator or a parenthesis outside quotes, a line break, a lone <c>%</c> in a
/// batch file, a trailing caret.
/// </summary>
public static class CmdBatchModel
{
    /// <summary>The variables a test leaves undefined: none.</summary>
    private static readonly IReadOnlyDictionary<string, string> NoVariables = new Dictionary<string, string>();

    /// <summary>
    /// The <c>CreateProcess</c> line of one line of a batch file, inside a parenthesised block.
    /// Percents first, quotes or not: <c>%%</c> is one <c>%</c>, <c>%~dp0</c> the script's folder
    /// with its trailing backslash, <c>%NAME%</c> its value or nothing, a lone <c>%</c> an error;
    /// the text produced is not read again. Then the special characters, following the quotes.
    /// </summary>
    /// <param name="line">The line, without its line break.</param>
    /// <param name="scriptDirectory">What <c>%~dp0</c> expands to; a missing trailing backslash is added.</param>
    /// <param name="variables">The environment, by case-insensitive name.</param>
    public static string BatchLine(string line, string scriptDirectory, IReadOnlyDictionary<string, string>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(scriptDirectory);

        var directory = scriptDirectory.EndsWith('\\') ? scriptDirectory : scriptDirectory + "\\";
        return SpecialCharacters(ExpandInBatch(line, directory, Lookup(variables)));
    }

    /// <summary>
    /// The <c>CreateProcess</c> line of the <c>orkeon</c> command of a <c>run.cmd</c>, read through its
    /// frame: <c>@echo off</c>, comments, <c>setlocal</c>, the code page captured, then one
    /// parenthesised block whose every line is one the launcher writes. Null when the block launches
    /// nothing (the launcher refuses a command too long).
    /// </summary>
    /// <param name="runCmd">The file's text.</param>
    /// <param name="scriptDirectory">What <c>%~dp0</c> expands to.</param>
    /// <param name="variables">The environment, by case-insensitive name.</param>
    public static string? OrkeonLine(string runCmd, string scriptDirectory, IReadOnlyDictionary<string, string>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(runCmd);

        var lines = BlockLines(runCmd);
        string? orkeon = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("orkeon ", StringComparison.Ordinal))
            {
                if (orkeon is not null)
                    throw new CmdModelException("the block runs orkeon twice");
                orkeon = BatchLine(line, scriptDirectory, variables);
            }
            else if (!IsFrameLine(line))
            {
                // A line the launcher does not write is one a value broke off its command: cmd runs it.
                throw new CmdModelException($"the block runs a line the launcher does not write: '{line}'");
            }
        }

        return orkeon;
    }

    /// <summary>
    /// The lines of <c>run.cmd</c>'s block, indentation removed — its CRLF frame checked: every line
    /// ends with CR LF, and the block closes.
    /// </summary>
    public static IReadOnlyList<string> BlockLines(string runCmd)
    {
        ArgumentNullException.ThrowIfNull(runCmd);

        if (!runCmd.EndsWith("\r\n", StringComparison.Ordinal))
            throw new CmdModelException("run.cmd does not end with CR LF");

        var lines = runCmd[..^2].Split("\r\n");
        if (lines.Any(line => line.Contains('\n', StringComparison.Ordinal) || line.Contains('\r', StringComparison.Ordinal)))
            throw new CmdModelException("a line of run.cmd ends with a bare LF or CR");

        var open = Array.IndexOf(lines, "(");
        if (open < 0)
            throw new CmdModelException("run.cmd holds no block");
        if (lines[^1] != ")")
            throw new CmdModelException("run.cmd's block is not its last line");

        return [.. lines[(open + 1)..^1].Select(line => line.TrimStart(' '))];
    }

    /// <summary>
    /// The batch file <c>cmd /d /v:off /s /c &lt;arguments&gt;</c> runs (reading 6): with <c>/s</c>,
    /// the first and the last quote of what follows <c>/c</c> are removed, and they alone; the rest
    /// is read as a typed line — <c>%%</c> stays, <c>%NAME%</c> is replaced only when defined. Gives
    /// the program's path, its quotes removed; nothing may follow it.
    /// </summary>
    /// <param name="arguments">The task's <c>Arguments</c>.</param>
    /// <param name="variables">The environment, by case-insensitive name.</param>
    public static string SlashCProgram(string arguments, IReadOnlyDictionary<string, string>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var rest = arguments.TrimStart();
        var stripQuotes = false;
        bool? delayed = null;
        while (true)
        {
            var end = rest.IndexOfAny([' ', '\t']);
            var token = end < 0 ? rest : rest[..end];
            rest = end < 0 ? "" : rest[(end + 1)..];
            if (token.Equals("/c", StringComparison.OrdinalIgnoreCase))
                break;
            if (token.Length == 0 || token[0] != '/')
                throw new CmdModelException($"cmd reads '{token}' before /c as its command");

            if (token.Equals("/s", StringComparison.OrdinalIgnoreCase))
                stripQuotes = true;
            else if (token.Equals("/v:off", StringComparison.OrdinalIgnoreCase))
                delayed = false;
            else if (token.Equals("/v:on", StringComparison.OrdinalIgnoreCase))
                delayed = true;
            else if (!token.Equals("/d", StringComparison.OrdinalIgnoreCase))
                throw new CmdModelException($"the model does not read the switch '{token}'");
        }

        if (!stripQuotes)
            throw new CmdModelException("without /s, cmd keeps or strips the quotes by what the path holds: the model reads /s only");

        var line = rest.TrimStart();
        if (line.Length > 0 && line[0] == '"')
        {
            var last = line.LastIndexOf('"');
            line = last > 0 ? line[1..last] + line[(last + 1)..] : line[1..];
        }

        var expanded = ExpandTyped(line, Lookup(variables));
        if (delayed != false && expanded.Contains('!', StringComparison.Ordinal))
            throw new CmdModelException("a '!' under delayed expansion the registry may enable");

        var processed = SpecialCharacters(expanded);
        var program = CrtArgv.Split(processed);
        if (program.Count != 1)
            throw new CmdModelException($"cmd would pass '{string.Join("', '", program.Skip(1))}' to the batch file");

        return program[0];
    }

    /// <summary>
    /// The <c>CreateProcess</c> line of a line pasted at the <c>cmd</c> prompt (reading 7):
    /// <c>%NAME%</c> replaced only when defined, <c>%%</c> and a lone <c>%</c> kept; then the special
    /// characters. Delayed expansion is off, as a prompt starts.
    /// </summary>
    /// <param name="line">The pasted line.</param>
    /// <param name="variables">The environment, by case-insensitive name.</param>
    public static string PromptLine(string line, IReadOnlyDictionary<string, string>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        return SpecialCharacters(ExpandTyped(line, Lookup(variables)));
    }

    /// <summary>Whether <paramref name="line"/> is one the launcher's block writes besides the <c>orkeon</c> command.</summary>
    private static bool IsFrameLine(string line) =>
        line is "chcp %LAUNCHER_CP% >nul 2>&1" or "cd /d \"%~dp0\" || exit /b 1" or "exit /b" or "exit /b 1"
        || line.StartsWith(">&2 echo ", StringComparison.Ordinal);

    private static IReadOnlyDictionary<string, string> Lookup(IReadOnlyDictionary<string, string>? variables) =>
        variables is null
            ? NoVariables
            : variables.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Percent expansion of a batch file's line (reading 2).</summary>
    private static string ExpandInBatch(string line, string directory, IReadOnlyDictionary<string, string> variables)
    {
        var expanded = new StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c != '%')
            {
                expanded.Append(c);
                continue;
            }

            if (i + 1 < line.Length && line[i + 1] == '%')
            {
                expanded.Append('%');
                i++;
                continue;
            }

            if (string.CompareOrdinal(line, i + 1, "~dp0", 0, 4) == 0)
            {
                expanded.Append(directory);
                i += 4;
                continue;
            }

            if (i + 1 < line.Length && line[i + 1] == '~')
                throw new CmdModelException($"the model reads %~dp0 only: '{line[i..]}'");

            // %0 to %9 and %*: the launcher is run without arguments.
            if (i + 1 < line.Length && (char.IsAsciiDigit(line[i + 1]) || line[i + 1] == '*'))
            {
                i++;
                continue;
            }

            var close = line.IndexOf('%', i + 1);
            if (close < 0)
                throw new CmdModelException($"a lone '%' at {i}: cmd drops it");

            if (variables.TryGetValue(line[(i + 1)..close], out var value))
                expanded.Append(value);
            i = close;
        }

        return expanded.ToString();
    }

    /// <summary>
    /// Percent expansion of a typed line, and of <c>cmd /c</c>'s (reading 7): a defined
    /// <c>%NAME%</c> is replaced; an undefined one keeps its text, and reading resumes at its
    /// closing <c>%</c>, which may open the next one.
    /// </summary>
    private static string ExpandTyped(string line, IReadOnlyDictionary<string, string> variables)
    {
        var expanded = new StringBuilder();
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] == '%')
            {
                var close = line.IndexOf('%', i + 1);
                if (close > i + 1 && variables.TryGetValue(line[(i + 1)..close], out var value))
                {
                    expanded.Append(value);
                    i = close + 1;
                    continue;
                }

                if (close > i + 1)
                {
                    expanded.Append(line, i, close - i);
                    i = close;
                    continue;
                }
            }

            expanded.Append(line[i]);
            i++;
        }

        return expanded.ToString();
    }

    /// <summary>
    /// The special characters (reading 3): a <c>"</c> enters or leaves the quotes and stays; inside,
    /// everything is literal; outside, a caret escapes the next character and disappears, and
    /// <c>&amp; | &lt; &gt;</c> and the parentheses end or split the command.
    /// </summary>
    private static string SpecialCharacters(string expanded)
    {
        var line = new StringBuilder(expanded.Length);
        var quoted = false;
        for (var i = 0; i < expanded.Length; i++)
        {
            var c = expanded[i];
            if (c is '\r' or '\n' or '\x1A' or '\0')
                throw new CmdModelException($"U+{(int)c:X4} at {i}: cmd ends the command there");

            if (c == '"')
            {
                quoted = !quoted;
                line.Append(c);
                continue;
            }

            if (quoted)
            {
                line.Append(c);
                continue;
            }

            switch (c)
            {
                case '^':
                    if (i + 1 >= expanded.Length)
                        throw new CmdModelException("a trailing '^' continues the command on the next line");
                    i++;
                    if (expanded[i] is '\r' or '\n' or '\x1A' or '\0')
                        throw new CmdModelException($"U+{(int)expanded[i]:X4} at {i}: cmd ends the command there");
                    line.Append(expanded[i]);
                    break;

                case '&' or '|' or '<' or '>':
                    throw new CmdModelException($"'{c}' outside quotes at {i} is an operator: '{expanded}'");

                case '(' or ')':
                    throw new CmdModelException($"'{c}' outside quotes at {i} opens or closes a block: '{expanded}'");

                default:
                    line.Append(c);
                    break;
            }
        }

        return line.ToString();
    }
}
