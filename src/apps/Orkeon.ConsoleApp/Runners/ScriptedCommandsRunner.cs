using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Cli.Abstractions.Commands;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Registry;
using Orkeon.Cli.Scripting.Configuration;
using Orkeon.Cli.Scripting.Registry;

namespace Orkeon.ConsoleApp.Runners;

/// <summary>
/// Interactive REPL that drives the user-supplied <c>*.cmd.ts</c> scripts discovered by
/// <see cref="ScriptCommandRegistry"/>. Sibling of <see cref="MainMenuRunner"/> and
/// <see cref="QaRunner"/> — same pattern, different specific registry.
/// </summary>
/// <remarks>
/// Input grammar (coding-agent surface): <c>/cmd …</c> runs a command, <c>@path …</c> and any other
/// line are free text handed to the <see cref="ResolveFallback">fallback</see> command
/// (the interactive assistant). See <see cref="InteractiveRunnerBase.SlashCommandsOnly"/>.
/// </remarks>
internal sealed class ScriptedCommandsRunner : InteractiveRunnerBase
{
    private readonly ScriptCommandRegistry _scripts;
    private readonly string _fallbackCommandName;

    public ScriptedCommandsRunner(
        DefaultCommandRegistry defaults,
        ScriptCommandRegistry specific,
        IConsoleAdapter console,
        ILogger<ScriptedCommandsRunner> logger,
        IServiceProvider services,
        IOptions<ScriptCommandsConfiguration> scriptOptions)
        : base(defaults, specific, console, logger, services)
    {
        ArgumentNullException.ThrowIfNull(scriptOptions);
        _scripts = specific;
        _fallbackCommandName = scriptOptions.Value.FallbackCommandName ?? string.Empty;
    }

    /// <summary>
    /// Awaits the deferred script load (R10.3 / ANT-002) before the first prompt: resolving
    /// <see cref="ScriptCommandRegistry"/> from DI is pure wiring, so the discovery →
    /// transpile → evaluate pipeline runs here, on the runner's own async path. Load errors
    /// surface at REPL start — where the old resolution-time failure used to appear.
    /// </summary>
    protected override async System.Threading.Tasks.Task OnStartAsync(CancellationToken ct)
    {
        await _scripts.EnsureLoadedAsync(ct).ConfigureAwait(false);
    }

    protected override string Banner => """

==================================
   Orkeon Scripted Commands REPL
==================================

  /<command>     run a command              (e.g. /help, /help-cmd <name>)
  @<path>        reference a file or folder (Tab completes paths; dirs end with /)
  <free text>    ask the coding agent       (anything not starting with / )

Type '/help' for the command list, '/help-cmd <name>' for a single command's signature.

""";

    protected override string Prompt => "scripted> ";

    /// <summary>Coding-agent grammar: only a leading <c>/</c> token is a command; the rest is free text.</summary>
    protected override bool SlashCommandsOnly => true;

    /// <summary>Free text routes to the configured assistant command (default <c>assistant</c>) when present.</summary>
    protected override IInteractiveCommand? ResolveFallback()
    {
        if (string.IsNullOrEmpty(_fallbackCommandName))
            return null;

        return Specific.Commands.FirstOrDefault(
            cmd => string.Equals(cmd.Name, _fallbackCommandName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A mistyped <c>/command</c> reaches here (free text never does); point at the slash form.</summary>
    protected override System.Threading.Tasks.Task OnUnknownCommandAsync(string input, CancellationToken ct)
    {
        Console.WriteLine($"Unknown command: '{input}'. Type '/help' for available commands.");
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
