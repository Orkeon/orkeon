namespace Orkeon.Studio.Core.Launch;

/// <summary>One <c>-V KEY=VALUE</c> variable of a YAML crew's <c>CrewInput</c>.</summary>
/// <param name="Key">Template key, substituted as <c>{KEY}</c> in task templates.</param>
/// <param name="Value">Value substituted for the key.</param>
public sealed record RunVariable(string Key, string Value)
{
    /// <summary>The <c>KEY=VALUE</c> token as the CLI expects it.</summary>
    public string ToToken() => Key + "=" + Value;
}

/// <summary>
/// What the launcher form collects, one field per <c>RunCommandOptions</c> option
/// (<c>src/scripting/Orkeon.Scripting.Cli/Commands/RunCommand.cs:20-128</c>). Fields that
/// only apply to one dialect are marked as such; <see cref="RunArgumentsBuilder"/> refuses
/// them on the other dialect rather than emitting an option the CLI would ignore.
/// </summary>
public sealed record RunLaunchOptions
{
    /// <summary>
    /// <c>--settings &lt;path&gt;</c>. Null or empty means "auto": the CLI's own settings
    /// resolution chain applies.
    /// </summary>
    public string? SettingsPath { get; init; }

    /// <summary><c>-V KEY=VALUE</c>, repeatable — YAML crews only.</summary>
    public IReadOnlyList<RunVariable> Variables { get; init; } = [];

    /// <summary><c>--initial-context &lt;text&gt;</c> — YAML crews only.</summary>
    public string? InitialContext { get; init; }

    /// <summary><c>--inputs &lt;json&gt;</c> — scripts only.</summary>
    public string? InputsJson { get; init; }

    /// <summary><c>--inputs-file &lt;path&gt;</c> — scripts only.</summary>
    public string? InputsFilePath { get; init; }

    /// <summary>
    /// <c>--mount</c>, repeatable, in the Docker-style mount format. These override the
    /// appsettings entries of the same index — see <see cref="MountOverrideSemantics"/>.
    /// </summary>
    public IReadOnlyList<string> Mounts { get; init; } = [];

    /// <summary><c>--allow-external-mounts</c>: accept mount roots outside the working directory.</summary>
    public bool AllowExternalMounts { get; init; }

    /// <summary><c>--verbose 0|1|2</c>; 0 is the CLI default and emits no argument.</summary>
    public int Verbosity { get; init; }

    /// <summary><c>--llm-log</c>: write LLM exchanges as JSONL.</summary>
    public bool LlmLogEnabled { get; init; }

    /// <summary><c>--llm-log-path &lt;dir&gt;</c>: custom log directory (implies <c>--llm-log</c>).</summary>
    public string? LlmLogPath { get; init; }

    /// <summary><c>--validate</c>: strict crew load, no LLM probe, no kickoff.</summary>
    public bool Validate { get; init; }

    /// <summary>Highest accepted <see cref="Verbosity"/>.</summary>
    public const int MaxVerbosity = 2;
}
