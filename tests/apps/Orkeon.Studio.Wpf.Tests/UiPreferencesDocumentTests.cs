using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// <c>ui-preferences.json</c> is written by merge (STUDIO-32 D-05, STUDIO-35 D-06): the theme,
/// the language and the mode come from three gestures that know nothing of Settings › Studio,
/// and each write must leave the others' choices — and any key a later Studio adds — where they
/// were. The merge is pure text to text, so it is asserted here without a disk.
/// </summary>
public sealed class UiPreferencesDocumentTests
{
    private const string FileWithEverything = """
        {
          "Theme": "dark",
          "Language": "fr",
          "Mode": "expert",
          "Studio": {
            "BalanceRefreshMinutes": 15,
            "BalanceThresholds": { "deepseek": 12.5 },
            "ArchiveSuggestionDays": 60
          },
          "SomethingLater": { "Kept": true }
        }
        """;

    private static JsonObject Json(string text) => (JsonObject)JsonNode.Parse(text)!;

    [Fact]
    public void Saving_the_theme_the_language_or_the_mode_keeps_the_studio_settings_and_every_unknown_key()
    {
        var file = UiPreferencesDocument.Parse(FileWithEverything);

        file.SetAppearance("light", language: null, "novice");

        var written = UiPreferencesDocument.Parse(file.ToJson());
        Assert.Equal<(string?, string?, string?)>(("light", null, "novice"), (written.Theme, written.Language, written.Mode));
        Assert.Equal(15, written.Studio.BalanceRefreshMinutes);
        Assert.Equal(12.5m, written.Studio.BalanceThresholds["deepseek"]);
        var json = Json(file.ToJson());
        Assert.Equal(60, (int)json["Studio"]!["ArchiveSuggestionDays"]!);
        Assert.True((bool)json["SomethingLater"]!["Kept"]!);
    }

    [Fact]
    public void Saving_the_studio_settings_keeps_the_appearance_and_every_unknown_key()
    {
        var file = UiPreferencesDocument.Parse(FileWithEverything);

        file.SetStudio(new StudioSettings
        {
            BalanceRefreshMinutes = null,
            BalanceThresholds = ImmutableDictionary.CreateRange(
                StringComparer.Ordinal, [KeyValuePair.Create("openrouter", 2m)]),
        });

        var written = UiPreferencesDocument.Parse(file.ToJson());
        Assert.Equal<(string?, string?, string?)>(("dark", "fr", "expert"), (written.Theme, written.Language, written.Mode));
        Assert.Null(written.Studio.BalanceRefreshMinutes);
        Assert.Equal(["openrouter"], written.Studio.BalanceThresholds.Keys);
        var json = Json(file.ToJson());
        Assert.Equal(60, (int)json["Studio"]!["ArchiveSuggestionDays"]!);
        Assert.True((bool)json["SomethingLater"]!["Kept"]!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    public void A_missing_corrupt_or_foreign_file_reads_as_the_defaults(string? text)
    {
        var file = UiPreferencesDocument.Parse(text);

        Assert.Null(file.Theme);
        Assert.Null(file.Language);
        Assert.Null(file.Mode);
        Assert.Equal(StudioSettings.Default.BalanceRefreshMinutes, file.Studio.BalanceRefreshMinutes);
        Assert.Empty(file.Studio.BalanceThresholds);

        // And writing over it starts a sound file rather than failing.
        file.SetAppearance("dark", "en", "novice");
        Assert.Equal("dark", UiPreferencesDocument.Parse(file.ToJson()).Theme);
    }

    [Fact]
    public void A_value_of_the_wrong_shape_reads_as_not_set()
    {
        var file = UiPreferencesDocument.Parse("""
            {
              "Theme": 3,
              "Studio": {
                "BalanceRefreshMinutes": "often",
                "BalanceThresholds": { "deepseek": "five", "kimi": -1, "openrouter": 4 }
              }
            }
            """);

        Assert.Null(file.Theme);
        Assert.Null(file.Studio.BalanceRefreshMinutes);
        Assert.Equal(["openrouter"], file.Studio.BalanceThresholds.Keys);
    }

    [Fact]
    public void A_hand_edited_file_is_read_whatever_the_case_of_its_keys()
    {
        var file = UiPreferencesDocument.Parse("""{ "theme": "dark", "studio": { "balanceRefreshMinutes": 30 } }""");

        Assert.Equal("dark", file.Theme);
        Assert.Equal(30, file.Studio.BalanceRefreshMinutes);

        file.SetAppearance("light", null, "novice");
        Assert.Equal("light", UiPreferencesDocument.Parse(file.ToJson()).Theme);
    }

    /// <summary>
    /// The first writer serialized the whole preferences record, computed <c>IsDark</c> included;
    /// left behind by a theme switch, it would contradict the theme it was computed from.
    /// </summary>
    [Fact]
    public void The_computed_dark_flag_the_first_writer_left_goes_with_the_next_appearance_write()
    {
        var file = UiPreferencesDocument.Parse("""{ "Theme": "dark", "Language": null, "Mode": "novice", "IsDark": true }""");

        file.SetAppearance("light", null, "novice");

        Assert.False(Json(file.ToJson()).ContainsKey("IsDark"));
    }
}
