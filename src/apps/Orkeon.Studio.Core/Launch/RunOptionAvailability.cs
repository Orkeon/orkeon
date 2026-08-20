using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Core.Launch;

/// <summary>The <c>orkeon run</c> options a launcher form exposes.</summary>
public enum RunOption
{
    /// <summary><c>--settings</c>.</summary>
    Settings,

    /// <summary><c>-V KEY=VALUE</c>.</summary>
    Variables,

    /// <summary><c>--initial-context</c>.</summary>
    InitialContext,

    /// <summary><c>--inputs</c>.</summary>
    Inputs,

    /// <summary><c>--inputs-file</c>.</summary>
    InputsFile,

    /// <summary><c>--mount</c>.</summary>
    Mounts,

    /// <summary><c>--allow-external-mounts</c>.</summary>
    AllowExternalMounts,

    /// <summary><c>--verbose</c>.</summary>
    Verbose,

    /// <summary><c>--llm-log</c>.</summary>
    LlmLog,

    /// <summary><c>--llm-log-path</c>.</summary>
    LlmLogPath,

    /// <summary><c>--validate</c>.</summary>
    Validate,

    /// <summary><c>--events</c>.</summary>
    Events,

    /// <summary><c>--stream</c>.</summary>
    Stream,

    /// <summary><c>--client</c>.</summary>
    Client,
}

/// <summary>
/// Which options apply to which target dialect, so a UI greys out the rest instead of
/// offering flags the CLI would ignore: <c>-V</c>/<c>--initial-context</c> are read only by
/// the YAML path, <c>--inputs</c>/<c>--inputs-file</c> only by the scripting path
/// (<c>RunCommand.ToRunnerOptions</c> deliberately drops the script-only fields).
/// </summary>
public static class RunOptionAvailability
{
    private static readonly RunOption[] Shared =
    [
        RunOption.Settings,
        RunOption.Mounts,
        RunOption.AllowExternalMounts,
        RunOption.Verbose,
        RunOption.LlmLog,
        RunOption.LlmLogPath,
        RunOption.Validate,
    ];

    private static readonly RunOption[] YamlOnly = [RunOption.Variables, RunOption.InitialContext];

    private static readonly RunOption[] ScriptOnly = [RunOption.Inputs, RunOption.InputsFile];

    /// <summary>Options that apply to the given dialect, in form order.</summary>
    public static IReadOnlyList<RunOption> For(RunTargetDialect dialect) =>
        dialect == RunTargetDialect.Yaml
            ? [.. YamlOnly, .. Shared]
            : [.. ScriptOnly, .. Shared];

    /// <summary>Options that apply to the given target.</summary>
    public static IReadOnlyList<RunOption> For(RunTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return For(target.Dialect);
    }

    /// <summary>True when the option applies to the dialect.</summary>
    public static bool IsAvailable(RunTargetDialect dialect, RunOption option) =>
        option switch
        {
            RunOption.Variables or RunOption.InitialContext => dialect == RunTargetDialect.Yaml,
            RunOption.Inputs or RunOption.InputsFile => dialect == RunTargetDialect.Script,
            _ => true,
        };

    /// <summary>The CLI spelling of an option, for labels and error messages.</summary>
    public static string ToCommandLineName(RunOption option) =>
        option switch
        {
            RunOption.Settings => "--settings",
            RunOption.Variables => "-V",
            RunOption.InitialContext => "--initial-context",
            RunOption.Inputs => "--inputs",
            RunOption.InputsFile => "--inputs-file",
            RunOption.Mounts => "--mount",
            RunOption.AllowExternalMounts => "--allow-external-mounts",
            RunOption.Verbose => "--verbose",
            RunOption.LlmLog => "--llm-log",
            RunOption.LlmLogPath => "--llm-log-path",
            RunOption.Validate => "--validate",
            RunOption.Events => "--events",
            RunOption.Stream => "--stream",
            RunOption.Client => "--client",
            _ => throw new ArgumentOutOfRangeException(nameof(option), option, "Unknown run option."),
        };
}
