using System.Globalization;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>
/// How a balance reading is said — on the status bar, on a profile row and in the profile
/// editor alike (STUDIO-35). The amounts and the state are in the user's language; the probe's
/// own detail line stays English, like the connectivity probe's (STUDIO-33).
/// </summary>
internal static class BalanceText
{
    /// <summary>«110.00 CNY», or «110.00 CNY · 5.00 USD» for an account in two currencies — as the provider returned them, never converted.</summary>
    public static string Amounts(ProviderBalanceResult reading) =>
        StatusBarText.Join(reading.Amounts.Select(Amount), StatusBarText.Separator) ?? string.Empty;

    /// <summary>A threshold as the settings show it: no trailing zeros, no grouping.</summary>
    public static string Threshold(decimal threshold) => threshold.ToString("0.##########", CultureInfo.CurrentCulture);

    /// <summary>The provider as the profile editor names it — its card's title — or its key when no card has it.</summary>
    public static string ProviderTitle(string provider, IStudioStrings strings) =>
        LlmPresets.ProviderCatalogFor(strings)
            .FirstOrDefault(card => string.Equals(card.Name, provider, StringComparison.Ordinal))?.Title
        ?? provider;

    /// <summary>Whether the reading's only way on is the vendor's console: a balance no inference key reads, and a console to read it in.</summary>
    public static bool OffersConsole(ProviderBalanceResult reading) =>
        reading is { Status: ProviderBalanceStatus.NotExposed or ProviderBalanceStatus.AdminKeyRequired, ConsoleUrl: not null };

    /// <summary>A reading with no amount, in words: «key refused», «no answer», «not exposed by the API»…</summary>
    public static string State(ProviderBalanceStatus status, IStudioStrings strings) => strings[status switch
    {
        ProviderBalanceStatus.NotExposed => StudioStringKeys.BalanceNotExposed,
        ProviderBalanceStatus.AdminKeyRequired => StudioStringKeys.BalanceAdminKey,
        ProviderBalanceStatus.AuthenticationRefused => StudioStringKeys.BalanceKeyRefused,
        ProviderBalanceStatus.NetworkError => StudioStringKeys.BalanceNoAnswer,
        ProviderBalanceStatus.UnexpectedAnswer => StudioStringKeys.BalanceUnexpected,
        _ => StudioStringKeys.BalanceNoAccount,
    }];

    /// <summary>
    /// The reading in one line: «110.00 CNY available · under your alert threshold of 50 · read
    /// at 10:31», or «key refused · read at 10:31».
    /// </summary>
    public static string Summary(ProviderBalanceResult reading, BalanceReadings readings, IStudioStrings strings)
    {
        if (reading.Status != ProviderBalanceStatus.Available)
            return Join([State(reading.Status, strings), ReadAt(reading, strings)]);

        var amounts = string.Format(
            CultureInfo.CurrentCulture,
            strings[reading.Scope == ProviderBalanceScope.ApiKey ? StudioStringKeys.BalanceKeyLimit : StudioStringKeys.BalanceAvailable],
            Amounts(reading));

        var threshold = readings.IsUnderThreshold(reading) && readings.ThresholdOf(reading.Provider) is { } value
            ? string.Format(CultureInfo.CurrentCulture, strings[StudioStringKeys.BalanceUnderThreshold], Threshold(value))
            : null;

        return Join([amounts, threshold, ReadAt(reading, strings)]);
    }

    /// <summary>«read at 10:31», local time.</summary>
    public static string ReadAt(ProviderBalanceResult reading, IStudioStrings strings) => string.Format(
        CultureInfo.CurrentCulture,
        strings[StudioStringKeys.BalanceReadAt],
        reading.CheckedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture));

    private static string Amount(ProviderBalanceAmount amount) =>
        $"{amount.Available.ToString("N2", CultureInfo.CurrentCulture)} {amount.Currency}";

    private static string Join(IEnumerable<string?> parts) => StatusBarText.Join(parts, StatusBarText.Separator) ?? string.Empty;
}
