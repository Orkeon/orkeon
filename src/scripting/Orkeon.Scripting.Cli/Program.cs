using CommandLine;
using Orkeon.Compliance.Vfs;
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

    /// <summary>Main entry; parses args and dispatches to a command.</summary>
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        // `orkeon rag ingest|search|eval` — RAG subsystem verbs (RAG-03/C3, RAG-04/C1)
        // get their own dispatch branch with their own verb parser.
        if (args.Length > 0 && string.Equals(args[0], "rag", StringComparison.OrdinalIgnoreCase))
            return await RagCommand.DispatchAsync(args[1..]).ConfigureAwait(false);

        // `orkeon llm probe` — provider test-protocol harness (LLM-08/C1).
        if (args.Length > 0 && string.Equals(args[0], "llm", StringComparison.OrdinalIgnoreCase))
            return await LlmCommand.DispatchAsync(args[1..]).ConfigureAwait(false);

        // Strip a leading "run" verb so users can write `orkeon run script.ork.ts`.
        // Future verbs (e.g. `test`) will get their own dispatch branch here.
        var effective = args;
        if (args.Length > 0 && string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
            effective = args[1..];

        using var parser = new Parser(s =>
        {
            s.HelpWriter = Console.Out;
            s.CaseInsensitiveEnumValues = true;
        });

        return await parser.ParseArguments<RunCommandOptions>(effective)
            .MapResult(
                async (RunCommandOptions o) => await RunCommand.ExecuteAsync(o).ConfigureAwait(false),
                _ => Task.FromResult(ExitScriptError))
            .ConfigureAwait(false);
    }
}
