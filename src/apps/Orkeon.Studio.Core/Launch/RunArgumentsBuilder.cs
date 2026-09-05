using System.Globalization;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Launch;

/// <summary>
/// Turns a detected target plus the launcher form into the argument list handed to the
/// <c>orkeon</c> process. A list, never a joined string: the runner passes it as
/// <c>ProcessStartInfo.ArgumentList</c>, so no argument is ever quoted by hand. Use
/// <see cref="ToDisplayCommandLine"/> for the human-readable equivalent.
/// </summary>
public static class RunArgumentsBuilder
{
    /// <summary>The CLI verb these arguments start with.</summary>
    public const string RunVerb = "run";

    /// <summary>The only stream format there is; spelled out because the CLI takes a value.</summary>
    public const string EventsFormat = "jsonl";

    /// <summary>
    /// Builds the arguments for <paramref name="target"/>, in the order a user would type
    /// them: <c>run &lt;path&gt;</c> first, then the dialect options, then the shared ones.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="options"/> holds a value the target refuses (an option of the other
    /// dialect, an out-of-range verbosity, a malformed variable). Call
    /// <see cref="Validate"/> first to show those to the user.
    /// </exception>
    public static IReadOnlyList<string> Build(RunTarget target, RunLaunchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        var effective = options ?? new RunLaunchOptions();
        ThrowIfInvalid(target, effective);

        var arguments = new List<string> { RunVerb, target.RunPath };

        AppendValue(arguments, RunOption.Settings, effective.SettingsPath);
        AppendDialectOptions(arguments, target.Dialect, effective);
        AppendSharedOptions(arguments, effective);
        AppendObservationOptions(arguments, effective);

        return arguments;
    }

    /// <summary>The options only one dialect accepts, in the order a user would type them.</summary>
    private static void AppendDialectOptions(
        List<string> arguments,
        RunTargetDialect dialect,
        RunLaunchOptions options)
    {
        if (dialect == RunTargetDialect.Yaml)
        {
            AppendSequence(arguments, RunOption.Variables, [.. options.Variables.Select(v => v.ToToken())]);
            AppendValue(arguments, RunOption.InitialContext, options.InitialContext);
            return;
        }

        AppendValue(arguments, RunOption.Inputs, options.InputsJson);
        AppendValue(arguments, RunOption.InputsFile, options.InputsFilePath);
    }

    /// <summary>The options both dialects accept: the sandbox, the verbosity, the LLM log.</summary>
    private static void AppendSharedOptions(List<string> arguments, RunLaunchOptions options)
    {
        AppendSequence(arguments, RunOption.Mounts, options.Mounts);
        AppendFlag(arguments, RunOption.AllowExternalMounts, options.AllowExternalMounts);

        // 0 is the CLI default: a user leaving the slider alone types nothing.
        if (options.Verbosity > 0)
        {
            arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.Verbose));
            arguments.Add(options.Verbosity.ToString(CultureInfo.InvariantCulture));
        }

        AppendFlag(arguments, RunOption.LlmLog, options.LlmLogEnabled);
        AppendValue(arguments, RunOption.LlmLogPath, options.LlmLogPath);
        AppendFlag(arguments, RunOption.Validate, options.Validate);
    }

    /// <summary>
    /// How the screen watches the run — last, so the argv a user reads still opens with what
    /// they chose. Nothing is streamed without the events flag: --stream and --client only
    /// mean something on a stream that exists.
    /// </summary>
    private static void AppendObservationOptions(List<string> arguments, RunLaunchOptions options)
    {
        if (!options.Events)
            return;

        arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.Events));
        arguments.Add(EventsFormat);
        AppendFlag(arguments, RunOption.Stream, options.Stream);
        AppendValue(arguments, RunOption.Client, options.ClientName);
    }

    /// <summary>Appends the option and its value; an absent or blank value appends nothing.</summary>
    private static void AppendValue(List<string> arguments, RunOption option, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        arguments.Add(RunOptionAvailability.ToCommandLineName(option));
        arguments.Add(value);
    }

    /// <summary>Appends the bare flag when it is set.</summary>
    private static void AppendFlag(List<string> arguments, RunOption option, bool enabled)
    {
        if (enabled)
            arguments.Add(RunOptionAvailability.ToCommandLineName(option));
    }

    /// <summary>
    /// One flag carrying every value, never one flag per value: the CLI's CommandLineParser
    /// REJECTS a repeated option ("Option 'm, mount' is defined multiple times"), and sequence
    /// options consume the space-separated values that follow the single flag.
    /// </summary>
    private static void AppendSequence(List<string> arguments, RunOption option, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return;

        arguments.Add(RunOptionAvailability.ToCommandLineName(option));
        arguments.AddRange(values);
    }

    /// <summary>The command line equivalent to <see cref="Build"/>, quoted for a shell.</summary>
    public static string ToDisplayCommandLine(
        RunTarget target,
        RunLaunchOptions? options = null,
        string executable = CommandLineDisplay.DefaultExecutable,
        CommandLineQuotingStyle style = CommandLineQuotingStyle.Auto) =>
        CommandLineDisplay.Format(Build(target, options), executable, style);

    /// <summary>
    /// Reports what a UI should show before launching: options set on the wrong dialect and
    /// malformed values as errors, a double <c>--inputs</c> as a warning, and the
    /// directory-dispatch prerequisite as advice. An empty list means <see cref="Build"/>
    /// will succeed.
    /// </summary>
    public static IReadOnlyList<ValidationMessage> Validate(RunTarget target, RunLaunchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        var effective = options ?? new RunLaunchOptions();
        var messages = new List<ValidationMessage>();

        foreach (var option in FindOptionsOutsideDialect(target.Dialect, effective))
        {
            messages.Add(ValidationMessage.Error(
                LaunchCodes.OptionNotApplicable,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{RunOptionAvailability.ToCommandLineName(option)}' does not apply to a " +
                    $"{DescribeDialect(target.Dialect)} target and would be ignored by the CLI."),
                RunOptionAvailability.ToCommandLineName(option)));
        }

        if (effective.Verbosity < 0 || effective.Verbosity > RunLaunchOptions.MaxVerbosity)
        {
            messages.Add(ValidationMessage.Error(
                LaunchCodes.InvalidVerbosity,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Verbosity {effective.Verbosity} is out of range: --verbose accepts 0, 1 or {RunLaunchOptions.MaxVerbosity}."),
                RunOptionAvailability.ToCommandLineName(RunOption.Verbose)));
        }

        if (target.Dialect == RunTargetDialect.Yaml)
            messages.AddRange(ValidateVariables(effective.Variables));

        // The configuration key is NOT Mounts:{i}: the runner injects its own mounts first, so
        // the user's i-th --mount lands that many slots further down the array.
        var autoInjected = MountAutoInjection.For(target, effective).Count;
        for (var i = 0; i < effective.Mounts.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(effective.Mounts[i]))
            {
                messages.Add(ValidationMessage.Error(
                    LaunchCodes.EmptyMount,
                    string.Create(CultureInfo.InvariantCulture, $"The --mount entry at index {i} is empty."),
                    MountOverrideSemantics.ConfigurationKey(autoInjected + i)));
            }
        }

        if (target.Dialect == RunTargetDialect.Script
            && !string.IsNullOrWhiteSpace(effective.InputsJson)
            && !string.IsNullOrWhiteSpace(effective.InputsFilePath))
        {
            messages.Add(ValidationMessage.Warning(
                LaunchCodes.ConflictingInputs,
                "Both --inputs and --inputs-file are set; the script receives only one set of inputs.",
                RunOptionAvailability.ToCommandLineName(RunOption.Inputs)));
        }

        if (target.RequiresDirectoryRunSupport)
        {
            // Advice, not a warning: directory dispatch shipped in
            // RunTargetRequirements.MinimumCliVersion and the co-installed CLI has it.
            messages.Add(ValidationMessage.Information(
                LaunchCodes.DirectoryRunNotice,
                RunTargetRequirements.DirectoryRunNotice,
                target.RunPath));
        }

        return messages;
    }

    private static IEnumerable<RunOption> FindOptionsOutsideDialect(
        RunTargetDialect dialect,
        RunLaunchOptions options)
    {
        if (options.Variables.Count > 0 && !RunOptionAvailability.IsAvailable(dialect, RunOption.Variables))
            yield return RunOption.Variables;

        if (!string.IsNullOrWhiteSpace(options.InitialContext)
            && !RunOptionAvailability.IsAvailable(dialect, RunOption.InitialContext))
        {
            yield return RunOption.InitialContext;
        }

        if (!string.IsNullOrWhiteSpace(options.InputsJson)
            && !RunOptionAvailability.IsAvailable(dialect, RunOption.Inputs))
        {
            yield return RunOption.Inputs;
        }

        if (!string.IsNullOrWhiteSpace(options.InputsFilePath)
            && !RunOptionAvailability.IsAvailable(dialect, RunOption.InputsFile))
        {
            yield return RunOption.InputsFile;
        }
    }

    private static IEnumerable<ValidationMessage> ValidateVariables(IReadOnlyList<RunVariable> variables)
    {
        for (var i = 0; i < variables.Count; i++)
        {
            var key = variables[i].Key;

            if (string.IsNullOrWhiteSpace(key))
            {
                yield return ValidationMessage.Error(
                    LaunchCodes.InvalidVariable,
                    string.Create(CultureInfo.InvariantCulture, $"The variable at index {i} has no name."),
                    RunOptionAvailability.ToCommandLineName(RunOption.Variables));
            }
            else if (key.Contains('=', StringComparison.Ordinal))
            {
                yield return ValidationMessage.Error(
                    LaunchCodes.InvalidVariable,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Variable name '{key}' contains '=', which separates the name from the value in -V KEY=VALUE."),
                    RunOptionAvailability.ToCommandLineName(RunOption.Variables));
            }
        }
    }

    private static string DescribeDialect(RunTargetDialect dialect) =>
        dialect == RunTargetDialect.Yaml ? "YAML crew" : "scripting";

    private static void ThrowIfInvalid(RunTarget target, RunLaunchOptions options)
    {
        var errors = Validate(target, options)
            .Where(message => message.Severity == ValidationSeverity.Error)
            .ToList();

        if (errors.Count == 0)
            return;

        throw new InvalidOperationException(
            "These launch options cannot be turned into an 'orkeon run' command line: "
            + string.Join(" ", errors.Select(message => message.Text)));
    }
}
