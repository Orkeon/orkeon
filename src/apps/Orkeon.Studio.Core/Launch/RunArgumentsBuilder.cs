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

        if (!string.IsNullOrWhiteSpace(effective.SettingsPath))
        {
            arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.Settings));
            arguments.Add(effective.SettingsPath);
        }

        if (target.Dialect == RunTargetDialect.Yaml)
        {
            foreach (var variable in effective.Variables)
            {
                arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.Variables));
                arguments.Add(variable.ToToken());
            }

            if (!string.IsNullOrWhiteSpace(effective.InitialContext))
            {
                arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.InitialContext));
                arguments.Add(effective.InitialContext);
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(effective.InputsJson))
            {
                arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.Inputs));
                arguments.Add(effective.InputsJson);
            }

            if (!string.IsNullOrWhiteSpace(effective.InputsFilePath))
            {
                arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.InputsFile));
                arguments.Add(effective.InputsFilePath);
            }
        }

        foreach (var mount in effective.Mounts)
        {
            arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.Mounts));
            arguments.Add(mount);
        }

        if (effective.AllowExternalMounts)
            arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.AllowExternalMounts));

        // 0 is the CLI default: a user leaving the slider alone types nothing.
        if (effective.Verbosity > 0)
        {
            arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.Verbose));
            arguments.Add(effective.Verbosity.ToString(CultureInfo.InvariantCulture));
        }

        if (effective.LlmLogEnabled)
            arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.LlmLog));

        if (!string.IsNullOrWhiteSpace(effective.LlmLogPath))
        {
            arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.LlmLogPath));
            arguments.Add(effective.LlmLogPath);
        }

        if (effective.Validate)
            arguments.Add(RunOptionAvailability.ToCommandLineName(RunOption.Validate));

        return arguments;
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
