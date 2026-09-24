using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orkeon.Studio.Wpf.ViewModels.Services;

/// <summary>
/// <c>ui-preferences.json</c> as a document: every write MERGES into what the file holds
/// (STUDIO-32 D-05, STUDIO-35 D-06). The theme, the language and the mode are written by three
/// gestures that know nothing of Settings › Studio, and Settings › Studio knows nothing of
/// them; a write that rebuilt the whole file from what its caller knows — which is what the
/// file used to get — would erase the other's choices, and any key a later Studio adds.
/// <para>
/// Pure text in, text out: the app's <c>UiPreferences</c> reads and writes the file, and this
/// type only turns one text into the next, which is what lets the merge be asserted without a
/// disk. Reading is as tolerant as the file always was: an empty, corrupt or foreign text is an
/// empty document, and a value of the wrong shape is a value not set.
/// </para>
/// </summary>
public sealed class UiPreferencesDocument
{
    private const string ThemeKey = "Theme";
    private const string LanguageKey = "Language";
    private const string ModeKey = "Mode";
    private const string StudioKey = "Studio";
    private const string BalanceRefreshMinutesKey = "BalanceRefreshMinutes";
    private const string BalanceThresholdsKey = "BalanceThresholds";

    /// <summary>
    /// What the first writer also put in the file: it serialized the whole preferences record,
    /// computed <c>IsDark</c> included. Nothing reads it, and a copy left behind by a theme
    /// switch would contradict <c>Theme</c>, so an appearance write takes it out.
    /// </summary>
    private const string RetiredIsDarkKey = "IsDark";

    /// <summary>Hand-edited files are read the way people type them: <c>"theme"</c> is <c>"Theme"</c>.</summary>
    private static readonly JsonNodeOptions Reading = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions Writing = new() { WriteIndented = true };

    private readonly JsonObject _root;

    private UiPreferencesDocument(JsonObject root) => _root = root;

    /// <summary>The document a text holds; empty for no text, a corrupt text or anything but a JSON object.</summary>
    public static UiPreferencesDocument Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new UiPreferencesDocument(new JsonObject(Reading));

        try
        {
            return new UiPreferencesDocument(JsonNode.Parse(json, Reading) as JsonObject ?? new JsonObject(Reading));
        }
        catch (JsonException)
        {
            return new UiPreferencesDocument(new JsonObject(Reading));
        }
    }

    /// <summary>"dark" or "light", as last written; null when never written.</summary>
    public string? Theme => Text(_root[ThemeKey]);

    /// <summary>The language the user explicitly picked; null when nobody did.</summary>
    public string? Language => Text(_root[LanguageKey]);

    /// <summary>"novice" or "expert", as last written; null when never written.</summary>
    public string? Mode => Text(_root[ModeKey]);

    /// <summary>What Settings › Studio holds; the defaults for whatever the file does not say, or says in a shape it cannot read.</summary>
    public StudioSettings Studio
    {
        get
        {
            if (_root[StudioKey] is not JsonObject section)
                return StudioSettings.Default;

            var minutes = Number(section[BalanceRefreshMinutesKey]) is { } value
                          && value >= 1 && value <= int.MaxValue && decimal.Truncate(value) == value
                ? (int?)(int)value
                : null;

            var thresholds = ImmutableDictionary.CreateBuilder<string, decimal>(StringComparer.Ordinal);
            if (section[BalanceThresholdsKey] is JsonObject byProvider)
            {
                foreach (var (provider, node) in byProvider)
                {
                    if (Number(node) is { } threshold && threshold >= 0)
                        thresholds[provider] = threshold;
                }
            }

            return new StudioSettings { BalanceRefreshMinutes = minutes, BalanceThresholds = thresholds.ToImmutable() };
        }
    }

    /// <summary>
    /// Writes the window's appearance: the theme, the language — null unless the user picked
    /// one, so a detected language is never frozen into the file — and the mode. Every other
    /// key stays as it was.
    /// </summary>
    public void SetAppearance(string theme, string? language, string mode)
    {
        _root[ThemeKey] = theme;
        _root[LanguageKey] = language;
        _root[ModeKey] = mode;
        _root.Remove(RetiredIsDarkKey);
    }

    /// <summary>
    /// Writes Settings › Studio into its section, key by key: a key of the section this version
    /// does not know stays as it was, and so does everything outside the section.
    /// </summary>
    public void SetStudio(StudioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (_root[StudioKey] is not JsonObject section)
        {
            section = new JsonObject(Reading);
            _root[StudioKey] = section;
        }

        section[BalanceRefreshMinutesKey] = settings.BalanceRefreshMinutes;

        var thresholds = new JsonObject(Reading);
        foreach (var (provider, threshold) in settings.BalanceThresholds.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            thresholds[provider] = threshold;

        section[BalanceThresholdsKey] = thresholds;
    }

    /// <summary>The document as the file stores it, indented for whoever opens it.</summary>
    public string ToJson() => _root.ToJsonString(Writing);

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static decimal? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : null;
}
