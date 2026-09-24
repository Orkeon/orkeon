using System.Collections.Immutable;

namespace Orkeon.Studio.Wpf.ViewModels.Services;

/// <summary>
/// What Settings › Studio holds: how Studio itself behaves on this machine. Presentation state,
/// like the theme — it lives in <c>ui-preferences.json</c> under its own <c>Studio</c> section
/// (<see cref="UiPreferencesDocument"/>), and never in the settings file the CLI and the teams
/// read.
/// <para>
/// STUDIO-35 gives it the balance settings; the section is built to take more (STUDIO-32's
/// archiving suggestion): a new setting is one more property here, one more key in
/// <see cref="UiPreferencesDocument"/>, and one more card on the tab.
/// </para>
/// </summary>
public sealed record StudioSettings
{
    /// <summary>What a machine that never opened the tab runs on: no automatic reading, no threshold.</summary>
    public static StudioSettings Default { get; } = new();

    /// <summary>
    /// Minutes between two automatic readings of the balance, or null — the default — for none:
    /// the balance is then read at startup, at the end of each activity and on a click only, so
    /// Studio sends nothing the user did not ask for (STUDIO-35 D-02).
    /// </summary>
    public int? BalanceRefreshMinutes { get; init; }

    /// <summary>
    /// The alert thresholds, by provider (the key <c>LlmProviderDetector</c> reports), each in the
    /// currency that provider returns — nothing is converted. A balance under its provider's
    /// threshold takes the warning tone (STUDIO-35 D-03); a provider absent here never does.
    /// </summary>
    public ImmutableDictionary<string, decimal> BalanceThresholds { get; init; } =
        ImmutableDictionary.Create<string, decimal>(StringComparer.Ordinal);
}
