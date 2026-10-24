using CommandLine;
using Orkeon.Compliance.Vfs;
using Orkeon.Hosting;
using Orkeon.Scripting.Cli.Commands;

[assembly: SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: CLI entrypoint resolves user-supplied script paths before any VFS mounts exist.")]

namespace Orkeon.Scripting.Cli;

/// <summary>Entry point for the <c>orkeon</c> CLI.</summary>
internal static class Program
{
    /// <summary>Standard exit codes used by the CLI.</summary>
    public const int ExitOk = 0;
    /// <summary>Script-level error: missing file, invalid script, validation failure.</summary>
    public const int ExitScriptError = 1;
    /// <summary>Runtime/system error: unexpected exception.</summary>
    public const int ExitRuntimeError = 2;
    /// <summary>Cancelled via SIGINT (Ctrl+C).</summary>
    public const int ExitCancelled = 130;

    /// <summary>
    /// The verb table: every top-level command and the entry point that owns it. Each verb
    /// parses its own tail — `orkeon rag ingest` (RAG-03/C3, RAG-04/C1), `orkeon llm probe`
    /// (LLM-08/C1), `orkeon init` (WIN-02), `orkeon doctor` (WIN-03), `orkeon forge`
    /// (FORGE-03), `orkeon usecases search` (STUDIO-38), `orkeon email login` (MAIL-05), `orkeon drive login` (DRIVE-05),
    /// `orkeon typings` (GAP-13), `orkeon quickstart`, `orkeon mcp serve` (GAP-24), `orkeon settings` — so the
    /// option grammars never collide. A verb is looked up before a first word is taken for a crew
    /// to run: `orkeon settings` stays the verb where a `settings/` folder exists.
    /// </summary>
    private static readonly Dictionary<string, Func<IRunnerConsole, string[], Task<int>>> Verbs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["run"] = RunAsync,
            ["quickstart"] = QuickstartCommand.DispatchAsync,
            ["init"] = InitCommand.DispatchAsync,
            ["doctor"] = DoctorCommand.DispatchAsync,
            ["settings"] = SettingsCommand.DispatchAsync,
            ["llm"] = LlmCommand.DispatchAsync,
            ["rag"] = RagCommand.DispatchAsync,
            // A lambda rather than a method group: the forge dispatch carries an optional
            // working-directory override its tests use, and an optional parameter forbids the
            // conversion.
            ["forge"] = (console, tail) => Commands.Forge.ForgeCommand.DispatchAsync(console, tail),
            ["usecases"] = Commands.UseCases.UseCasesCommand.DispatchAsync,
            ["email"] = EmailCommand.DispatchAsync,
            ["drive"] = DriveCommand.DispatchAsync,
            ["typings"] = TypingsCommand.DispatchAsync,
            ["mcp"] = McpCommand.DispatchAsync,
        };

    /// <summary>The verbs the dispatch answers to, so the usage listing can be checked against it.</summary>
    internal static IReadOnlyCollection<string> KnownVerbs => Verbs.Keys;

    /// <summary>
    /// Main entry: the CLI's console sink. It prepares the process console, then hands it on as the
    /// one <see cref="IRunnerConsole"/> every verb writes, reads and takes its interrupt key from.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        UseUtf8Console();
        return await DispatchAsync(SystemRunnerConsole.Instance, args).ConfigureAwait(false);
    }

    /// <summary>
    /// Routes one command line to the verb that owns it. Separate from <see cref="Main"/>
    /// because the console preparation above rebinds the process streams, which a test
    /// driving the dispatch in-process cannot afford: a test hands its own <paramref name="console"/>.
    /// </summary>
    internal static async Task<int> DispatchAsync(IRunnerConsole console, string[] args)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(args);

        // The bare tool name is a question, not a run without a target: it is answered with the
        // verb listing, and with exit 0 — asking what a tool does is not a failure.
        if (args.Length == 0 || CliUsage.IsHelpToken(args[0]))
        {
            await console.Output.WriteAsync(CliUsage.Render()).ConfigureAwait(false);
            return ExitOk;
        }

        if (CliUsage.IsVersionToken(args[0]))
        {
            // The bare form keeps its single line: scripts read it. --verbose adds to it.
            if (args.Skip(1).Any(CliUsage.IsVerboseToken))
                await console.Output.WriteAsync(CliUsage.RenderVerboseVersion(InstallChannelReader.Read())).ConfigureAwait(false);
            else
                await console.Output.WriteLineAsync(CliUsage.VersionLine).ConfigureAwait(false);

            return ExitOk;
        }

        if (Verbs.TryGetValue(args[0], out var verb))
            return await verb(console, args[1..]).ConfigureAwait(false);

        // The `run` verb stays optional: an option written first (`orkeon --list-tools`) and
        // anything shaped like a crew path go to the run parser exactly as before. Everything
        // else is a mistyped command, and answering it with the run parser's "script not found"
        // hides the only useful fact — that the word itself is not a command.
        if (!args[0].StartsWith('-') && !CliUsage.LooksLikeCrewTarget(args[0]))
        {
            await console.ErrorOutput.WriteLineAsync(
                $"orkeon: unknown command '{args[0]}'; run `orkeon --help` for the list.").ConfigureAwait(false);
            return ExitScriptError;
        }

        return await RunAsync(console, args).ConfigureAwait(false);
    }

    /// <summary>
    /// Parses the `run` option grammar and executes it. A single-value option written
    /// <c>--option=value</c> — how the team launchers and Studio write one — is read with its value
    /// as written, a line break and a leading space included (<see cref="RunnerArguments"/>).
    /// </summary>
    private static async Task<int> RunAsync(IRunnerConsole console, string[] args)
    {
        return await CliGrammar.DispatchAsync<RunCommandOptions>(console, "run", args, RunCommand.ExecuteAsync).ConfigureAwait(false);
    }

    /// <summary>
    /// Speaks UTF-8 on every console stream. Without this, .NET encodes redirected
    /// stdout/stderr with the Windows OEM codepage (850 on a French machine), and every
    /// accented character in the event stream reaches the watching process as mojibake
    /// (an accented word arrives as "d‚j…"). Stdin gets the mirror treatment so a typed reply with accents
    /// survives the trip down. No BOM anywhere: the protocol is one JSON document per
    /// line and a BOM would corrupt the first one.
    /// </summary>
    [SuppressConsoleCompliance("CONSOLE-SINK: the one place the orkeon CLI touches the process console; Main hands it on as SystemRunnerConsole.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "The writers/readers become the process-lifetime console streams via " +
                        "Console.SetOut/SetError/SetIn; disposing them here would close stdio.")]
    private static void UseUtf8Console()
    {
        var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        try
        {
            Console.OutputEncoding = utf8;
        }
        catch (IOException)
        {
            // No usable console handle (rare service contexts): writers below still cover it.
        }

        if (Console.IsOutputRedirected)
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
        if (Console.IsErrorRedirected)
            Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
        if (Console.IsInputRedirected)
            Console.SetIn(new StreamReader(Console.OpenStandardInput(), utf8));
    }
}
