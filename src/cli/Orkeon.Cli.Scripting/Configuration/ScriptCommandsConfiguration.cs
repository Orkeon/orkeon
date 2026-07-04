using System.Collections.Immutable;

namespace Orkeon.Cli.Scripting.Configuration;

/// <summary>
/// Full appsettings binding for the CLI scripted-commands subsystem (spec §6.4).
/// Bound to the <c>Orkeon:Cli:ScriptCommands</c> section by
/// <c>ScriptingCliServiceCollectionExtensions.AddScriptCommands</c>.
/// </summary>
/// <remarks>
/// Properties are <c>get; set;</c> (not <c>init;</c>) on purpose: <c>Configure</c> /
/// <c>PostConfigure</c> callbacks mutate the bound instance, and init-only setters would
/// force callers into reflection workarounds.
/// </remarks>
public sealed record ScriptCommandsConfiguration
{
    /// <summary>Top-level configuration section name.</summary>
    public const string SectionName = "Orkeon:Cli:ScriptCommands";

    /// <summary>Master switch. False ⇒ discovery is skipped, registry is empty.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Virtual paths scanned for <c>*.cmd.ts</c>. First directory wins on conflict.</summary>
    public ImmutableArray<string> Directories { get; set; } = ImmutableArray<string>.Empty;

    /// <summary>When true, the first invalid script aborts the loader (use in CI).</summary>
    public bool FailFastOnInvalidScript { get; set; }

    /// <summary>When false, <c>*.cmd.js</c> only (no TS transpile). Test/debug; production keeps this true.</summary>
    public bool EsbuildTranspile { get; set; } = true;

    /// <summary>Hard cap on the number of engines kept in cache.</summary>
    public int MaxScripts { get; set; } = 50;

    /// <summary>When false, two scripts declaring the same command name is a fatal startup error.</summary>
    public bool ContinueOnConflict { get; set; } = true;

    /// <summary>
    /// Name of the scripted command that handles free text (a REPL line not prefixed with <c>/</c>).
    /// In the coding-agent surface this is the interactive assistant wired to the <c>main-loop</c> crew.
    /// Empty/unknown ⇒ free text yields the unknown-command message instead of being routed.
    /// </summary>
    public string FallbackCommandName { get; set; } = "assistant";

    /// <summary>CLI-specific sandbox limits applied to the dedicated <c>JsEngineFactory</c>.</summary>
    public CliScriptLimitsOptions Limits { get; set; } = new();
}
