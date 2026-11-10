namespace Orkeon.ConsoleApp;

/// <summary>
/// Parses CLI flags that drive the scripted-commands subsystem (spec §6.4):
/// <list type="bullet">
///   <item><description><c>--runner=scripted-commands</c> — boots into <c>ScriptedCommandsRunner</c> instead of the main menu.</description></item>
///   <item><description><c>--commands-dir &lt;path&gt;</c> (repeatable) — physical directories scanned for <c>*.cmd.ts</c>; routed through <c>CliCommandMountBootstrapper</c>.</description></item>
///   <item><description><c>--no-script-commands</c> — disables discovery entirely.</description></item>
///   <item><description><c>--strict-commands</c> — sets <c>FailFastOnInvalidScript=true + ContinueOnConflict=false</c>.</description></item>
///   <item><description><c>--settings &lt;path&gt;</c> (repeatable) — the JSON settings files the REPL reads (LLM/limits/etc.), the later ones winning, in place of the global file <c>orkeon init</c> writes — mirroring <c>Scripting.Cli</c>'s <c>-s</c> flag.</description></item>
///   <item><description><c>--mount &lt;physical:virtual:rights&gt;</c> (repeatable) — VFS mounts so the agent has a <c>/workspace</c> (etc.) to read/edit, mirroring <c>Scripting.Cli</c>'s <c>-m</c> flag: placed by virtual root over <c>Orkeon:FileSystem:Mounts</c> and guarded as for <c>orkeon run</c> (GAP-49, decision 3).</description></item>
///   <item><description><c>--allow-external-mounts</c> (or <c>=true</c>/<c>=false</c>) — a <c>--mount</c> outside the working directory is readable, as for a runner; <c>ORKEON_ALLOW_EXTERNAL_MOUNTS=1</c> does the same (GAP-49, decision 3). The <c>--commands-dir</c> and <c>--crews-dir</c> folders always are.</description></item>
///   <item><description><c>--llm-log</c> and <c>--llm-log-path &lt;dir&gt;</c> — the LLM exchange log, as for <c>orkeon run</c>: JSONL under <c>./llm-logs</c>, or the folder named, which implies <c>--llm-log</c>; a folder outside the working directory needs <c>--allow-external-mounts</c>.</description></item>
///   <item><description><c>--replay &lt;file&gt;</c> — replays the file's lines as if typed, then exits (0 passed, 1 stopped on an error, 2 file missing or unreadable); with <c>--interactive</c>, the REPL stays open after it. <c>--interactive</c> alone is a usage error.</description></item>
///   <item><description><c>--log-level &lt;level&gt;</c> — the minimum level of the log (<c>Trace</c> … <c>None</c>): the plain console's, on stderr under <c>--replay</c>, or the starting level of the TUI's logs pane. A value that starts with <c>--</c> is a usage error, for <c>--replay</c> too.</description></item>
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
    private const string AllowExternalMountsFlag = "--" + Orkeon.Constants.Cli.RunOptionNames.AllowExternalMounts;
    private const string LlmLogFlag = "--" + Orkeon.Constants.Cli.RunOptionNames.LlmLog;
    private const string LlmLogPathFlag = "--" + Orkeon.Constants.Cli.RunOptionNames.LlmLogPath;
    private const string ReplayFlag = "--replay";
    private const string InteractiveFlag = "--interactive";
    private const string LogLevelFlag = "--log-level";

    /// <summary><c>--allow-external-mounts</c> on the command line.</summary>
    public bool AllowExternalMounts { get; init; }

    /// <summary>The flag, or <c>ORKEON_ALLOW_EXTERNAL_MOUNTS</c>: the runners' rule (<c>RunnerOptionsBase</c>).</summary>
    public bool EffectiveAllowExternalMounts => AllowExternalMounts || Orkeon.Hosting.RunnerEnvironment.AllowExternalMounts;

    /// <summary><c>--llm-log</c> on the command line.</summary>
    public bool LlmLogEnabled { get; init; }

    /// <summary><c>--llm-log-path</c>, the last one given, or null.</summary>
    public string? LlmLogPath { get; init; }

    /// <summary>The full path of the log folder, or null when logging is off: the runners' rule.</summary>
    public string? ResolvedLlmLogPath => Orkeon.Hosting.RunnerLlmLog.ResolvePath(LlmLogEnabled, LlmLogPath);

    /// <summary><c>--replay</c>: the replay file, as given (resolved against the VFS working directory); null without it.</summary>
    public string? ReplayFile { get; init; }

    /// <summary><c>--interactive</c>: the REPL stays open after the replay.</summary>
    public bool ReplayInteractive { get; init; }

    /// <summary><c>--replay</c> without <c>--interactive</c>: a plain shell command that exits at the end of the file.</summary>
    public bool ReplayNonInteractive => ReplayFile is not null && !ReplayInteractive;

    /// <summary><c>--log-level</c>, the last one given; null without it.</summary>
    public Microsoft.Extensions.Logging.LogLevel? LogLevel { get; init; }

    /// <summary>A usage error of these flags (exit code 2); null when they are valid.</summary>
    public string? UsageError { get; init; }

    /// <summary>The lists and flags the arguments fill in, before they become an options record.</summary>
    private sealed class ParseState
    {
        public bool Boot;
        public bool Disabled;
        public bool Strict;
        public bool AllowExternalMounts;
        public bool LlmLog;
        public bool Interactive;
        public string? UsageError;
        public readonly List<string> Dirs = [];
        public readonly List<string> Settings = [];
        public readonly List<string> Mounts = [];
        public readonly List<string> CrewDirs = [];
        public readonly List<string> LlmLogPaths = [];
        public readonly List<string> ReplayFiles = [];
        public readonly List<string> LogLevels = [];
    }

    /// <summary>Tries each value option in turn; the first that matches captures its value (and advances <paramref name="i"/>).</summary>
    private static bool TryAddAnyValueOption(string a, string[] args, ref int i, ParseState st)
    {
        return TryAddValueOption(a, "--commands-dir", args, ref i, st.Dirs)
            || TryAddValueOption(a, "--settings", args, ref i, st.Settings)
            || TryAddValueOption(a, "--mount", args, ref i, st.Mounts)
            || TryAddValueOption(a, "--crews-dir", args, ref i, st.CrewDirs)
            || TryAddValueOption(a, LlmLogPathFlag, args, ref i, st.LlmLogPaths)
            || TryAddValueOption(a, ReplayFlag, args, ref i, st.ReplayFiles)
            || TryAddValueOption(a, LogLevelFlag, args, ref i, st.LogLevels);
    }

    /// <summary>Sets the plain switch <paramref name="a"/> names; false when it is not one.</summary>
    private static bool TryApplySwitch(string a, ParseState st)
    {
        switch (a)
        {
            case "--runner=scripted-commands": st.Boot = true; return true;
            case "--no-script-commands": st.Disabled = true; return true;
            case "--strict-commands": st.Strict = true; return true;
            case AllowExternalMountsFlag: st.AllowExternalMounts = true; return true;
            case LlmLogFlag: st.LlmLog = true; return true;
            case InteractiveFlag: st.Interactive = true; return true;
            default: return false;
        }
    }

    private static void ApplyArgument(string[] args, ref int i, ParseState st)
    {
        var a = args[i];
        if (TryApplySwitch(a, st)) return;
        if (a is ReplayFlag or LogLevelFlag && i + 1 >= args.Length)
            st.UsageError ??= $"{a} needs a value";
        else if (a.StartsWith(AllowExternalMountsFlag + "=", StringComparison.Ordinal))
            st.AllowExternalMounts = IsTruthy(a[(AllowExternalMountsFlag.Length + 1)..]);
        else
            _ = TryAddAnyValueOption(a, args, ref i, st);
    }

    /// <summary>A value that starts with "--" is the next flag, not a value: `--replay --interactive` names no file.</summary>
    private static void RejectFlagLikeValues(List<string> replayFiles, List<string> logLevels, ref string? usageError)
    {
        foreach (var (flag, values) in new[] { (ReplayFlag, replayFiles), (LogLevelFlag, logLevels) })
        {
            if (values.FirstOrDefault(v => v.StartsWith("--", StringComparison.Ordinal)) is { } flagLike)
                usageError ??= $"{flag} needs a value, not the flag '{flagLike}'";
        }
    }

    private static Microsoft.Extensions.Logging.LogLevel? ParseLogLevel(List<string> logLevels, ref string? usageError)
    {
        if (logLevels.Count == 0) return null;
        var last = logLevels[^1];
        if (Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(last, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed) && !int.TryParse(last, out _))
            return parsed;
        usageError ??= $"{LogLevelFlag} '{last}' is none of Trace, Debug, Information, Warning, Error, Critical, None";
        return null;
    }

    public static ScriptedCommandsCliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var st = new ParseState();
        for (var i = 0; i < args.Length; i++)
            ApplyArgument(args, ref i, st);

        var usageError = st.UsageError;
        RejectFlagLikeValues(st.ReplayFiles, st.LogLevels, ref usageError);

        var replayFile = st.ReplayFiles.Count > 0 ? st.ReplayFiles[^1] : null;
        if (replayFile is { Length: 0 })
            usageError ??= $"{ReplayFlag} needs a file";
        if (st.Interactive && replayFile is null)
            usageError ??= $"{InteractiveFlag} needs {ReplayFlag} <file>";

        var logLevel = ParseLogLevel(st.LogLevels, ref usageError);

        return new ScriptedCommandsCliOptions(st.Boot, st.Dirs, st.Disabled, st.Strict, st.Settings, st.Mounts, st.CrewDirs)
        {
            AllowExternalMounts = st.AllowExternalMounts,
            LlmLogEnabled = st.LlmLog,
            LlmLogPath = st.LlmLogPaths.Count > 0 ? st.LlmLogPaths[^1] : null,
            ReplayFile = replayFile,
            ReplayInteractive = st.Interactive,
            LogLevel = logLevel,
            UsageError = usageError,
        };
    }

    private static bool IsTruthy(string value)
        => value == "1"
           || value.Equals("true", StringComparison.OrdinalIgnoreCase)
           || value.Equals("yes", StringComparison.OrdinalIgnoreCase);

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
