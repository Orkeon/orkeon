using System.Collections.Generic;

namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>
/// The command line Studio itself accepts. Parsing is a pure function so the CI smoke contract can be
/// asserted without starting WPF.
/// </summary>
public sealed record StartupArguments
{
    /// <summary>The flag the Windows CI smoke passes (spec §8.4).</summary>
    public const string SmokeExitSwitch = "--smoke-exit";

    /// <summary>
    /// Exit code for an argument Studio does not accept — the same contract as the two terminal
    /// front-ends, so a typo in a shortcut or a script fails loudly instead of silently opening
    /// the window as if nothing had been asked for.
    /// </summary>
    public const int UnrecognizedArgumentExitCode = 2;

    /// <summary>
    /// Whether Studio should open its window, close it immediately and exit with code 0. This is the
    /// smoke test: it proves the app starts, resolves its XAML and builds its ViewModels on a real
    /// Windows runner, without needing anyone to click anything.
    /// </summary>
    public bool SmokeExit { get; init; }

    /// <summary>The arguments that were not recognised, kept so they can be reported rather than ignored.</summary>
    public IReadOnlyList<string> Unrecognized { get; init; } = [];

    /// <summary>Parses the process arguments.</summary>
    public static StartupArguments Parse(IReadOnlyList<string>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return new StartupArguments();

        var smokeExit = false;
        var unrecognized = new List<string>();

        foreach (var argument in arguments)
        {
            if (string.Equals(argument, SmokeExitSwitch, StringComparison.Ordinal))
                smokeExit = true;
            else if (!string.IsNullOrWhiteSpace(argument))
                unrecognized.Add(argument);
        }

        return new StartupArguments { SmokeExit = smokeExit, Unrecognized = unrecognized };
    }

    /// <summary>The message written to standard error before exiting on an unknown argument.</summary>
    public static string DescribeUnrecognized(IReadOnlyList<string> unrecognized)
    {
        ArgumentNullException.ThrowIfNull(unrecognized);

        return $"Unrecognized argument(s): {string.Join(", ", unrecognized)}. "
            + $"Orkeon Studio accepts only {SmokeExitSwitch}.";
    }
}
