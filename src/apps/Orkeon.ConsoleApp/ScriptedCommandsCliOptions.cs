namespace Orkeon.ConsoleApp;

/// <summary>
/// Parses CLI flags that drive the scripted-commands subsystem (spec §6.4):
/// <list type="bullet">
///   <item><description><c>--runner=scripted-commands</c> — boots into <c>ScriptedCommandsRunner</c> instead of the main menu.</description></item>
///   <item><description><c>--commands-dir &lt;path&gt;</c> (repeatable) — physical directories scanned for <c>*.cmd.ts</c>; routed through <c>CliCommandMountBootstrapper</c>.</description></item>
///   <item><description><c>--no-script-commands</c> — disables discovery entirely.</description></item>
///   <item><description><c>--strict-commands</c> — sets <c>FailFastOnInvalidScript=true + ContinueOnConflict=false</c>.</description></item>
///   <item><description><c>--settings &lt;path&gt;</c> (repeatable) — extra JSON config files layered over the app's own <c>appsettings.json</c> (LLM/limits/etc.), mirroring <c>Scripting.Cli</c>'s <c>-s</c> flag.</description></item>
///   <item><description><c>--mount &lt;physical:virtual:rights&gt;</c> (repeatable) — VFS mounts appended to <c>Orkeon:FileSystem:Mounts</c> so the agent has a <c>/workspace</c> (etc.) to read/edit, mirroring <c>Scripting.Cli</c>'s <c>-m</c> flag.</description></item>
///   <item><description><c>--crews-dir &lt;path&gt;</c> (repeatable) — physical directories holding <c>&lt;name&gt;/crew.ork.ts</c> crews; mounted read-only and registered with the <c>script-host</c> so commands can resolve crews by name (e.g. <c>assistant</c> → <c>main-loop</c>).</description></item>
/// </list>
/// </summary>
internal sealed record ScriptedCommandsCliOptions(
    bool BootIntoScriptedRunner,
    IReadOnlyList<string> CommandDirs,
    bool Disabled,
    bool Strict,
    IReadOnlyList<string> SettingsFiles,
    IReadOnlyList<string> Mounts,
    IReadOnlyList<string> CrewDirs)
{
    public static ScriptedCommandsCliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var boot = false;
        var dirs = new List<string>();
        var disabled = false;
        var strict = false;
        var settings = new List<string>();
        var mounts = new List<string>();
        var crewDirs = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--runner=scripted-commands") boot = true;
            else if (a == "--no-script-commands") disabled = true;
            else if (a == "--strict-commands") strict = true;
            // Value options: TryAddValueOption captures the value (and advances i) as its side
            // effect; a true return only stops the chain, so the bodies are intentionally empty.
            else if (TryAddValueOption(a, "--commands-dir", args, ref i, dirs)) { /* captured */ }
            else if (TryAddValueOption(a, "--settings", args, ref i, settings)) { /* captured */ }
            else if (TryAddValueOption(a, "--mount", args, ref i, mounts)) { /* captured */ }
            else if (TryAddValueOption(a, "--crews-dir", args, ref i, crewDirs)) { /* captured */ }
        }

        return new ScriptedCommandsCliOptions(boot, dirs, disabled, strict, settings, mounts, crewDirs);
    }

    /// <summary>
    /// Handles a repeatable value option in both forms: <c>--name=value</c> (inline) and
    /// <c>--name value</c> (separate token, consuming <paramref name="i"/>+1). Appends the
    /// extracted value to <paramref name="target"/> and returns whether the argument matched.
    /// </summary>
    private static bool TryAddValueOption(string arg, string name, string[] args, ref int i, List<string> target)
    {
        var inlinePrefix = name + "=";
        if (arg.StartsWith(inlinePrefix, StringComparison.Ordinal))
        {
            target.Add(arg[inlinePrefix.Length..]);
            return true;
        }

        if (arg == name && i + 1 < args.Length)
        {
            target.Add(args[++i]);
            return true;
        }

        return false;
    }
}
