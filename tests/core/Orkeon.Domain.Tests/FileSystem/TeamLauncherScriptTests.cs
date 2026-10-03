using System.Text;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.Launchers;

namespace Orkeon.Domain.Tests.FileSystem;

/// <summary>
/// STUDIO-51: one composer writes a team's <c>run.cmd</c> and <c>run.sh</c> for both writers —
/// <c>forge promote</c> and Orkeon Studio. <c>cmd</c> reads <c>run.cmd</c> first and the runner
/// splits what it hands over second, so every value is written for the C runtime, then for
/// <c>cmd</c>: held here against models of both readings (<see cref="CmdBatchModel"/>,
/// <see cref="CrtArgv"/>), for every hostile value under every hostile launcher folder. The
/// runner's grammar, which Domain does not reference, reads the result in the writers' suites.
/// </summary>
public sealed class TeamLauncherScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-launcher-script-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    public static TheoryData<string, string> ValuesUnderLauncherFolders()
    {
        var data = new TheoryData<string, string>();
        foreach (var value in HostileLauncherInputs.Values)
        {
            foreach (var directory in HostileLauncherInputs.LauncherDirectories)
                data.Add(value, directory);
        }

        return data;
    }

    /// <summary>Every kind of argument a launcher writes, carrying <paramref name="value"/>.</summary>
    private static TeamLauncherSpec Spec(string value, string teamDirectory = @"C:\teams\veille-docs", bool studio = false)
    {
        var segments = new List<IReadOnlyList<TeamLauncherArgument>>
        {
            new[] { TeamLauncherArgument.Word("run"), TeamLauncherArgument.InTeam("", "crew", "") },
        };
        if (value.Length > 0)
            segments.Add([TeamLauncherArgument.Assign("--settings", value)]);

        segments.Add([
            TeamLauncherArgument.Word("--mount"),
            TeamLauncherArgument.Literal(value),
            .. HostileLauncherInputs.TeamFolders.Select(folder => TeamLauncherArgument.InTeam("\"", folder, "\":/x:rw")),
        ]);
        segments.Add([TeamLauncherArgument.Word("--var"), TeamLauncherArgument.Literal("k=" + value)]);
        if (value.Length > 0)
            segments.Add([TeamLauncherArgument.Assign("--initial-context", value)]);

        return new TeamLauncherSpec
        {
            TeamName = "veille-docs",
            TeamDirectory = teamDirectory,
            WrittenByStudio = studio,
            Segments = segments,
        };
    }

    /// <summary>
    /// The arguments <see cref="Spec"/> gives when <paramref name="handed"/> is what reaches the
    /// runner of the value and <paramref name="folder"/> the launcher's folder, separator included.
    /// </summary>
    private static List<string> Expected(string handed, string folder)
    {
        var expected = new List<string> { "run", folder + "crew" };
        if (handed.Length > 0)
            expected.Add("--settings=" + handed);

        expected.Add("--mount");
        expected.Add(handed);
        expected.AddRange(HostileLauncherInputs.TeamFolders.Select(team => $"\"{folder}{team}\":/x:rw"));
        expected.Add("--var");
        expected.Add("k=" + handed);
        if (handed.Length > 0)
            expected.Add("--initial-context=" + handed);

        return expected;
    }

    /// <summary>The arguments <c>orkeon.exe</c> receives from <paramref name="runCmd"/> run from <paramref name="launcherDirectory"/>.</summary>
    private static IReadOnlyList<string> WindowsArgv(string runCmd, string launcherDirectory, IReadOnlyDictionary<string, string>? variables = null)
    {
        var line = CmdBatchModel.OrkeonLine(runCmd, launcherDirectory, variables);
        Assert.NotNull(line);
        var argv = CrtArgv.Split(line);
        Assert.Equal("orkeon", argv[0]);
        return [.. argv.Skip(1)];
    }

    // ── values ──

    /// <summary>
    /// Decisions 2, 3 and 7: for every value and every launcher folder, cmd then the C runtime hand
    /// the runner the value as written — a <c>%</c> kept, a <c>"</c> kept, an operator kept, a
    /// trailing backslash kept, a value starting with <c>-</c> attached to its option — with the
    /// folder's <c>&amp;</c>, <c>^</c>, parentheses and <c>!</c> inside quotes; a line break, and
    /// every control character but the tab, becomes a space.
    /// </summary>
    [Theory]
    [MemberData(nameof(ValuesUnderLauncherFolders))]
    public void Every_value_reaches_the_runner_as_written_from_run_cmd(string value, string launcherDirectory)
    {
        var runCmd = TeamLauncherScript.Windows(Spec(value));

        var argv = WindowsArgv(runCmd, launcherDirectory, new Dictionary<string, string> { ["PATH"] = @"C:\Windows" });

        Assert.Equal(Expected(HostileLauncherInputs.CmdHandsOver(value), launcherDirectory), argv);
    }

    /// <summary>
    /// Decision 5: <c>run.sh</c>, run by a real <c>/bin/sh</c> from a folder whose name a shell would
    /// read — <c>&amp;</c>, <c>%</c>, <c>$</c>, a quote, a space —, hands the runner every value
    /// exactly, line breaks included.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void Every_value_reaches_the_runner_exactly_from_run_sh()
    {
        Assert.SkipUnless(PosixLauncherShell.IsAvailable, "run.sh needs a POSIX shell at /bin/sh.");

        var team = Path.Combine(_root, "R&D 100% $x \"y\" `z`", "équipe");
        Directory.CreateDirectory(team);
        foreach (var value in HostileLauncherInputs.Values)
        {
            var launcher = Path.Combine(team, "run.sh");
            File.WriteAllText(launcher, TeamLauncherScript.Posix(Spec(value, team)), new UTF8Encoding(false));

            var argv = PosixLauncherShell.Run(launcher, _root);

            Assert.Equal(Expected(value, team + "/"), argv);
        }
    }

    /// <summary>
    /// Decision 9: one argument written for a line pasted at the <c>cmd</c> prompt, where <c>%%</c>
    /// stays two characters and a defined <c>%NAME%</c> is replaced, comes back as written — a line
    /// break as a space.
    /// </summary>
    [Theory]
    [MemberData(nameof(HostileLauncherInputs.ValueData), MemberType = typeof(HostileLauncherInputs))]
    public void Every_value_written_for_the_prompt_comes_back_as_written(string value)
    {
        var line = "orkeon " + TeamLauncherScript.QuoteForCmdPrompt(value);

        var argv = CrtArgv.Split(CmdBatchModel.PromptLine(line, new Dictionary<string, string> { ["PATH"] = @"C:\Windows" }));

        Assert.Equal(["orkeon", HostileLauncherInputs.CmdHandsOver(value)], argv);
    }

    [Theory]
    [InlineData("crew.yaml")]
    [InlineData(@"C:\crews\crew.yaml")]
    [InlineData("--settings=C:\\x\\appsettings.json")]
    public void A_word_without_a_special_character_stays_bare_at_the_prompt(string word)
    {
        Assert.Equal(word, TeamLauncherScript.QuoteForCmdPrompt(word));
    }

    [Theory]
    [InlineData("R&D", "\"R&D\"")]
    [InlineData("a^b", "\"a^b\"")]
    [InlineData("%PATH%", "^\"%^PATH%^\"")]
    [InlineData("dit \"oui\" & part", "^\"dit \\^\"oui\\^\" ^& part^\"")]
    [InlineData("", "\"\"")]
    public void The_prompt_writing_quotes_for_cmd_and_breaks_every_variable_name(string value, string expected)
    {
        Assert.Equal(expected, TeamLauncherScript.QuoteForCmdPrompt(value));
    }

    [Theory]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("a b\nc", "'a b\nc'")]
    [InlineData("", "''")]
    public void The_posix_writing_is_single_quotes(string value, string expected)
    {
        Assert.Equal(expected, TeamLauncherScript.QuoteForPosixShell(value));
    }

    // ── the frame ──

    /// <summary>
    /// Decision 4: <c>cmd</c> decodes each line in the console's code page until <c>chcp 65001</c>
    /// has run — so every byte up to that line is ASCII, and no BOM, which <c>cmd</c> reads as text.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_byte_up_to_chcp_65001_is_ascii(bool script)
    {
        var text = TeamLauncherScript.Windows(Spec("Économie 日本語 🚀") with { IsScript = script, WrittenByStudio = true });

        var bytes = new UTF8Encoding(false).GetBytes(text);
        var marker = Encoding.ASCII.GetBytes("chcp 65001 >nul\r\n");
        var end = bytes.AsSpan().IndexOf(marker);
        Assert.True(end > 0, "run.cmd sets no UTF-8 code page");
        Assert.All(bytes[..(end + marker.Length)], b => Assert.True(b < 0x80, $"byte 0x{b:X2} before chcp 65001"));
        Assert.StartsWith("@echo off\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Économie 日本語 🚀", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Decision 4: <c>setlocal</c> fixes the parser whatever the registry says before the first
    /// <c>%~dp0</c>; the caller's code page is captured before <c>chcp 65001</c> and given back by the
    /// block's first command — <c>cmd</c> decodes the whole block before running it; <c>exit /b</c>,
    /// the block's last command, keeps <c>orkeon</c>'s exit code and never lets <c>cmd</c> read the
    /// file again; every line ends with CR LF.
    /// </summary>
    [Fact]
    public void The_frame_fixes_the_parser_captures_the_code_page_and_gives_it_back()
    {
        var text = TeamLauncherScript.Windows(Spec("x"));
        var lines = text.Split("\r\n");

        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        var setlocal = Array.IndexOf(lines, "setlocal EnableExtensions DisableDelayedExpansion");
        var capture = Array.IndexOf(lines, "for /f \"tokens=2 delims=:.\" %%p in ('chcp') do set \"LAUNCHER_CP=%%p\"");
        var utf8 = Array.IndexOf(lines, "chcp 65001 >nul");
        var block = Array.IndexOf(lines, "(");
        Assert.True(
            text.IndexOf("setlocal EnableExtensions DisableDelayedExpansion\r\n", StringComparison.Ordinal) < text.IndexOf("%~dp0", StringComparison.Ordinal),
            "setlocal precedes the first %~dp0");
        Assert.True(setlocal >= 0 && setlocal < capture && capture < utf8 && utf8 < block, "setlocal, capture, chcp 65001, block");
        Assert.Equal("  chcp %LAUNCHER_CP% >nul 2>&1", lines[block + 1]);
        Assert.Equal("  cd /d \"%~dp0\" || exit /b 1", lines[block + 2]);
        Assert.StartsWith("  orkeon run ", lines[block + 3], StringComparison.Ordinal);
        Assert.Equal("  exit /b", lines[block + 4]);
        Assert.Equal(")", lines[block + 5]);
        Assert.Equal("", lines[block + 6]);
        Assert.Equal(block + 7, lines.Length);
        Assert.DoesNotContain("ORKEON_", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ligne 1\nligne 2", true)]
    [InlineData("ligne 1\r\nligne 2", true)]
    [InlineData("avant\u001Aaprès", true)]
    [InlineData("une\tligne", false)]
    [InlineData("une ligne", false)]
    public void The_file_says_a_line_break_was_written_as_a_space_when_one_was(string value, bool said)
    {
        var text = TeamLauncherScript.Windows(Spec(value));

        Assert.Equal(said, text.Contains("\r\nrem A line break in a value below is written as a space: a cmd command holds one line.\r\n", StringComparison.Ordinal));
        Assert.DoesNotContain("line break", TeamLauncherScript.Posix(Spec(value)), StringComparison.Ordinal);
    }

    /// <summary>
    /// Decisions 1 and 5: the whole of both files, for a sample the engine's model wrote — a
    /// <c>%</c>, a quote and an operator, a context in two lines that starts with a dash —, a
    /// settings file under an accented profile in a folder with <c>&amp;</c>, and the team's output.
    /// </summary>
    [Fact]
    public void Both_launchers_read_as_the_reference()
    {
        var spec = new TeamLauncherSpec
        {
            TeamName = "veille-docs",
            TeamDirectory = @"C:\teams\veille-docs",
            Segments =
            [
                [TeamLauncherArgument.Word("run"), TeamLauncherArgument.InTeam("", "crew", "")],
                [TeamLauncherArgument.Assign("--settings", @"C:\Users\Zoé\R&D\appsettings.json")],
                [TeamLauncherArgument.Word("--mount"), TeamLauncherArgument.InTeam("\"", "output", "\":/output:rw")],
                [TeamLauncherArgument.Word("--var"), TeamLauncherArgument.Literal("seuil=10%"), TeamLauncherArgument.Literal("titre=dit \"oui\" & part")],
                [TeamLauncherArgument.Assign("--initial-context", "- puce un\n- puce deux")],
            ],
        };

        Assert.Equal(
            string.Join("\r\n",
                "@echo off",
                "rem Generated by Orkeon Forge for the team 'veille-docs'.",
                "rem The --var/--initial-context values below are the test sample: adapt them to the real run.",
                "rem A line break in a value below is written as a space: a cmd command holds one line.",
                "setlocal EnableExtensions DisableDelayedExpansion",
                "for /f \"tokens=2 delims=:.\" %%p in ('chcp') do set \"LAUNCHER_CP=%%p\"",
                "chcp 65001 >nul",
                "(",
                "  chcp %LAUNCHER_CP% >nul 2>&1",
                "  cd /d \"%~dp0\" || exit /b 1",
                "  orkeon run \"%~dp0crew\" --settings=\"C:\\Users\\Zoé\\R&D\\appsettings.json\" --mount ^\"\\\"%~dp0output\\\":/output:rw^\""
                + " --var \"seuil=10%%\" ^\"titre=dit \\^\"oui\\^\" ^& part^\" --initial-context=\"- puce un - puce deux\"",
                "  exit /b",
                ")",
                ""),
            TeamLauncherScript.Windows(spec));

        Assert.Equal(
            """
            #!/usr/bin/env sh
            # Generated by Orkeon Forge for the team 'veille-docs'.
            # The --var/--initial-context lines below are the test sample: adapt them to the real run.
            SELF="$0"
            while [ -L "$SELF" ]; do
              LINK="$(readlink "$SELF")"
              case "$LINK" in
                /*) SELF="$LINK" ;;
                *) SELF="$(dirname "$SELF")/$LINK" ;;
              esac
            done
            DIR="$(cd "$(dirname "$SELF")" && pwd)" || exit 1
            cd "$DIR" || exit 1
            exec orkeon \
              run "$DIR/crew" \
              --settings='C:\Users\Zoé\R&D\appsettings.json' \
              --mount "\"$DIR/output\":/output:rw" \
              --var 'seuil=10%' 'titre=dit "oui" & part' \
              --initial-context='- puce un
            - puce deux'

            """.ReplaceLineEndings("\n"),
            TeamLauncherScript.Posix(spec));
    }

    [Fact]
    public void Studio_says_it_writes_the_file_again_and_a_script_crew_says_where_it_lives()
    {
        var studio = Spec("x", studio: true) with { IsScript = true };

        const string Note = "Orkeon Studio writes this file again from studio-team.json whenever the team's model setting or folders change: an edit made here is lost then.";
        Assert.Contains($"\r\nrem {Note}\r\nrem The crew lives in crew\\crew.ork.ts - edit it there, this script only launches it.\r\n",
            TeamLauncherScript.Windows(studio), StringComparison.Ordinal);
        Assert.Contains($"\n# {Note}\n# The crew lives in crew/crew.ork.ts — edit it there, this script only launches it.\n",
            TeamLauncherScript.Posix(studio), StringComparison.Ordinal);
        Assert.DoesNotContain("Orkeon Studio", TeamLauncherScript.Windows(Spec("x")), StringComparison.Ordinal);
    }

    /// <summary>The header <c>forge rename</c> finds the team's name in: not one character changes.</summary>
    [Fact]
    public void The_header_names_the_team_as_forge_rename_finds_it()
    {
        Assert.Equal("Generated by Orkeon Forge for the team 'veille-docs'.", TeamLauncherScript.Header("veille-docs"));
        Assert.StartsWith("@echo off\r\nrem Generated by Orkeon Forge for the team 'veille-docs'.\r\n", TeamLauncherScript.Windows(Spec("x")), StringComparison.Ordinal);
        Assert.StartsWith("#!/usr/bin/env sh\n# Generated by Orkeon Forge for the team 'veille-docs'.\n", TeamLauncherScript.Posix(Spec("x")), StringComparison.Ordinal);
    }

    // ── 8 191 characters ──

    /// <summary>
    /// Decision 8: beyond 8 191 characters once expanded, <c>cmd</c> runs nothing. The block says so
    /// on stderr — the length, the bound, the longest option, names and numbers only — and exits 1;
    /// <c>run.sh</c>, which no such bound limits, stays complete.
    /// </summary>
    [Fact]
    public void Beyond_8191_characters_run_cmd_says_why_and_exits_1_and_run_sh_stays_complete()
    {
        var spec = new TeamLauncherSpec
        {
            TeamName = "veille-docs",
            TeamDirectory = @"C:\teams\veille-docs",
            Segments =
            [
                [TeamLauncherArgument.Word("run"), TeamLauncherArgument.InTeam("", "crew", "")],
                [TeamLauncherArgument.Assign("--initial-context", HostileLauncherInputs.Long())],
            ],
        };

        var measure = TeamLauncherScript.MeasureWindows(spec);
        Assert.False(measure.Fits);
        Assert.Equal("--initial-context", measure.LongestOption);
        Assert.Equal("orkeon run \"C:\\teams\\veille-docs\\crew\" --initial-context=\"\"".Length + HostileLauncherInputs.OverLimitLength, measure.Length);

        var text = TeamLauncherScript.Windows(spec);
        Assert.Null(CmdBatchModel.OrkeonLine(text, @"C:\teams\veille-docs\"));
        var block = CmdBatchModel.BlockLines(text);
        Assert.Equal("chcp %LAUNCHER_CP% >nul 2>&1", block[0]);
        var said = Assert.Single(block, line => line.StartsWith(">&2 echo ", StringComparison.Ordinal));
        Assert.Contains($"{measure.Length} characters", said, StringComparison.Ordinal);
        Assert.Contains("8191", said, StringComparison.Ordinal);
        Assert.Contains("--initial-context", said, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghij", said, StringComparison.Ordinal);
        Assert.Equal("exit /b 1", block[^1]);
        Assert.DoesNotContain("abcdefghij", text, StringComparison.Ordinal);

        Assert.Contains(HostileLauncherInputs.Long(), TeamLauncherScript.Posix(spec), StringComparison.Ordinal);
    }

    [Fact]
    public void Seven_thousand_characters_run()
    {
        var spec = new TeamLauncherSpec
        {
            TeamName = "veille-docs",
            TeamDirectory = @"C:\teams\veille-docs",
            Segments =
            [
                [TeamLauncherArgument.Word("run"), TeamLauncherArgument.InTeam("", "crew", "")],
                [TeamLauncherArgument.Assign("--initial-context", HostileLauncherInputs.Long(7000))],
            ],
        };

        Assert.True(TeamLauncherScript.MeasureWindows(spec).Fits);
        Assert.Equal(["run", @"C:\teams\veille-docs\crew", "--initial-context=" + HostileLauncherInputs.Long(7000)],
            WindowsArgv(TeamLauncherScript.Windows(spec), @"C:\teams\veille-docs\"));
    }

    /// <summary>
    /// The measure expands <c>%~dp0</c> with the team's folder at each occurrence, and counts a
    /// <c>%%</c> once: a deep folder anchoring many mounts goes over where a short one does not.
    /// </summary>
    [Fact]
    public void The_measure_counts_the_folder_at_every_anchor_and_a_doubled_percent_once()
    {
        TeamLauncherSpec Mounts(string directory) => new()
        {
            TeamName = "veille-docs",
            TeamDirectory = directory,
            Segments =
            [
                [TeamLauncherArgument.Word("run"), TeamLauncherArgument.InTeam("", "crew", "")],
                [
                    TeamLauncherArgument.Word("--mount"),
                    .. Enumerable.Range(0, 40).Select(i => TeamLauncherArgument.InTeam("\"", $"dossier-{i:00}", "\":/x:rw")),
                ],
                [TeamLauncherArgument.Word("--var"), TeamLauncherArgument.Literal("seuil=10%")],
            ],
        };

        var shallow = @"C:\t";
        var deep = @"C:\" + new string('d', 200);
        var near = TeamLauncherScript.MeasureWindows(Mounts(shallow));
        var far = TeamLauncherScript.MeasureWindows(Mounts(deep));

        Assert.True(near.Fits);
        Assert.False(far.Fits);
        Assert.Equal("--mount", far.LongestOption);
        Assert.Equal(41 * (deep.Length - shallow.Length), far.Length - near.Length);

        // The command as cmd holds it once the percents are read: every anchor the folder, %% one %.
        var command = TeamLauncherScript.Windows(Mounts(shallow)).Split("\r\n").Single(l => l.StartsWith("  orkeon ", StringComparison.Ordinal)).TrimStart();
        Assert.Equal(
            command.Replace("%~dp0", shallow + "\\", StringComparison.Ordinal).Replace("%%", "%", StringComparison.Ordinal).Length,
            near.Length);
    }

    // ── what the composer refuses ──

    [Fact]
    public void An_empty_value_after_an_option_and_an_anchor_followed_by_nothing_are_refused()
    {
        Assert.Throws<ArgumentException>(() => TeamLauncherArgument.Assign("--initial-context", ""));
        Assert.Throws<ArgumentException>(() => TeamLauncherArgument.InTeam("\"", "", "\":/x:rw"));
        Assert.Throws<ArgumentException>(() => TeamLauncherArgument.InTeam("", "\\crew", ""));
        Assert.Throws<ArgumentException>(() => TeamLauncherArgument.Word("a b"));
        Assert.Throws<ArgumentException>(() => TeamLauncherArgument.Word("&"));
        Assert.Throws<ArgumentException>(() => TeamLauncherScript.Header("Zoé & co"));
    }
}
