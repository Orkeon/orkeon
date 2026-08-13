using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Storage;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Run.Launcher;

/// <summary>
/// The launch form's fields, and the <see cref="RunLaunchOptions"/> they add up to.
/// <para>
/// Fields of both dialects live here at once, so switching target keeps what was typed;
/// <see cref="ToLaunchOptions"/> then emits only what the detected shape accepts, which is
/// what makes greying an option out and dropping it the same decision
/// (<see cref="RunOptionAvailability"/>).
/// </para>
/// </summary>
internal sealed class LaunchOptionsModel
{
    private readonly MountValidator _mountValidator;
    private readonly List<RunVariable> _variables = [];
    private readonly List<MountDefinition> _mounts = [];

    /// <summary>Creates the form over <paramref name="mountValidator"/> (defaults to the real disk).</summary>
    public LaunchOptionsModel(MountValidator? mountValidator = null) =>
        _mountValidator = mountValidator ?? new MountValidator();

    /// <summary>
    /// True while the settings file is left to the CLI's own resolution chain; false once
    /// the user pins a file with <c>--settings</c>.
    /// </summary>
    public bool UseAutomaticSettings { get; set; } = true;

    /// <summary>The pinned settings file, meaningful only when <see cref="UseAutomaticSettings"/> is false.</summary>
    public string? ExplicitSettingsPath { get; set; }

    /// <summary>What goes on the command line: null in automatic mode.</summary>
    public string? EffectiveSettingsPath =>
        UseAutomaticSettings || string.IsNullOrWhiteSpace(ExplicitSettingsPath) ? null : ExplicitSettingsPath;

    /// <summary>The CLI's settings resolution chain, for the "auto" explanation panel.</summary>
    public static IReadOnlyList<SettingsResolutionStep> ResolutionChain => SettingsLocations.ResolutionChain;

    /// <summary>The resolution chain as numbered display lines.</summary>
    public static IReadOnlyList<string> DescribeResolutionChain() =>
    [
        .. ResolutionChain.Select(step => string.Create(
            CultureInfo.InvariantCulture,
            $"{step.Order}. {step.Title} — {step.Description}")),
    ];

    /// <summary><c>-V KEY=VALUE</c> entries, YAML targets only.</summary>
    public IReadOnlyList<RunVariable> Variables => _variables;

    /// <summary><c>--initial-context</c>, YAML targets only.</summary>
    public string? InitialContext { get; set; }

    /// <summary><c>--inputs</c>, script targets only.</summary>
    public string? InputsJson { get; set; }

    /// <summary><c>--inputs-file</c>, script targets only.</summary>
    public string? InputsFilePath { get; set; }

    /// <summary><c>--verbose</c>, 0 to <see cref="RunLaunchOptions.MaxVerbosity"/>.</summary>
    public int Verbosity { get; set; }

    /// <summary><c>--llm-log</c>.</summary>
    public bool LlmLogEnabled { get; set; }

    /// <summary><c>--llm-log-path</c>.</summary>
    public string? LlmLogPath { get; set; }

    /// <summary><c>--allow-external-mounts</c>.</summary>
    public bool AllowExternalMounts { get; set; }

    /// <summary>Mounts added for this launch only, edited with the §4.5 form.</summary>
    public IReadOnlyList<MountDefinition> Mounts => _mounts;

    /// <summary>The launch mounts as the strings <c>--mount</c> receives.</summary>
    public IReadOnlyList<string> MountStrings => [.. _mounts.Select(mount => mount.ToMountString())];

    /// <summary>Why command-line mounts are not merged with the appsettings ones.</summary>
    public static string MountOverrideExplanation => MountOverrideSemantics.Explanation;

    /// <summary>What ticking <c>--allow-external-mounts</c> actually opens up.</summary>
    public static string ExternalMountsExplanation => MountOverrideSemantics.ExternalMountsExplanation;

    /// <summary>Appends a mount to this launch.</summary>
    public void AddMount(MountDefinition mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        _mounts.Add(mount);
    }

    /// <summary>Replaces the mount at <paramref name="index"/>.</summary>
    public void ReplaceMountAt(int index, MountDefinition mount)
    {
        ArgumentNullException.ThrowIfNull(mount);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _mounts.Count);
        _mounts[index] = mount;
    }

    /// <summary>Drops the mount at <paramref name="index"/>.</summary>
    public void RemoveMountAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _mounts.Count);
        _mounts.RemoveAt(index);
    }

    /// <summary>
    /// Validates the launch mounts. <c>requireAtLeastOne</c> is false here: the launcher adds
    /// mounts on top of an appsettings file that already declares its own, so an empty list
    /// is the ordinary case, unlike in the appsettings editor.
    /// </summary>
    public IReadOnlyList<ValidationMessage> ValidateMounts() =>
        _mountValidator.Validate(_mounts, requireAtLeastOne: false);

    /// <summary>
    /// The mount list the runtime will really see, given the mounts already declared in the
    /// selected appsettings file. The target is required because the runner injects its own
    /// mounts ahead of every <c>--mount</c>, which is what decides the index each one occupies.
    /// </summary>
    public IReadOnlyList<EffectiveMount> ComputeEffectiveMounts(
        RunTarget target,
        IReadOnlyList<string> settingsMounts)
    {
        ArgumentNullException.ThrowIfNull(target);

        return MountOverrideSemantics.ComputeEffectiveMounts(
            MountStrings,
            settingsMounts,
            MountAutoInjection.For(target, ToLaunchOptions(target)));
    }

    /// <summary>Appends one <c>-V</c> variable.</summary>
    public void AddVariable(string key, string value) => _variables.Add(new RunVariable(key, value));

    /// <summary>Forgets every <c>-V</c> variable.</summary>
    public void ClearVariables() => _variables.Clear();

    /// <summary>
    /// Replaces the variable list from the spelling the terminal form offers: one
    /// <c>KEY=VALUE</c> per line, so a value may hold spaces and still be one variable.
    /// Blank lines are ignored; a line with no <c>=</c> is reported and skipped.
    /// </summary>
    public IReadOnlyList<ValidationMessage> SetVariablesFromText(string? text)
    {
        _variables.Clear();
        var messages = new List<ValidationMessage>();
        var lines = (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            var separator = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                messages.Add(ValidationMessage.Error(
                    LaunchCodes.InvalidVariable,
                    string.Create(CultureInfo.InvariantCulture, $"'{trimmed}' is not a KEY=VALUE pair."),
                    RunOptionAvailability.ToCommandLineName(RunOption.Variables)));
                continue;
            }

            _variables.Add(new RunVariable(trimmed[..separator].Trim(), trimmed[(separator + 1)..]));
        }

        return messages;
    }

    /// <summary>The variables as the form's multi-line field spells them.</summary>
    public string VariablesAsText() => string.Join(Environment.NewLine, _variables.Select(v => v.ToToken()));

    /// <summary>Options the form should leave enabled for <paramref name="target"/>.</summary>
    public static IReadOnlyList<RunOption> AvailableOptions(RunTarget? target) =>
        target is null ? [] : RunOptionAvailability.For(target);

    /// <summary>True when <paramref name="option"/> applies to <paramref name="target"/>.</summary>
    public static bool IsAvailable(RunTarget? target, RunOption option) =>
        target is not null && RunOptionAvailability.IsAvailable(target.Dialect, option);

    /// <summary>
    /// Collects the form into the options <see cref="RunArgumentsBuilder"/> consumes. Fields
    /// of the other dialect are dropped rather than passed on: the user keeps what they
    /// typed, and the command line stays the one the CLI would accept.
    /// </summary>
    /// <param name="target">The detected target the options are for.</param>
    /// <param name="validate">True to add <c>--validate</c> (the dry-run button).</param>
    public RunLaunchOptions ToLaunchOptions(RunTarget target, bool validate = false)
    {
        ArgumentNullException.ThrowIfNull(target);

        var dialect = target.Dialect;

        return new RunLaunchOptions
        {
            SettingsPath = EffectiveSettingsPath,
            Variables = RunOptionAvailability.IsAvailable(dialect, RunOption.Variables) ? [.. _variables] : [],
            InitialContext = RunOptionAvailability.IsAvailable(dialect, RunOption.InitialContext) ? InitialContext : null,
            InputsJson = RunOptionAvailability.IsAvailable(dialect, RunOption.Inputs) ? InputsJson : null,
            InputsFilePath = RunOptionAvailability.IsAvailable(dialect, RunOption.InputsFile) ? InputsFilePath : null,
            Mounts = MountStrings,
            AllowExternalMounts = AllowExternalMounts,
            Verbosity = Verbosity,
            LlmLogEnabled = LlmLogEnabled,
            LlmLogPath = LlmLogPath,
            Validate = validate,
        };
    }
}
