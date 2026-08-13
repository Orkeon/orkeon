using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>
/// The preset chooser: the five presets of <c>orkeon init</c>, with the overrides the user
/// may type on top. Applying one rewrites only the <c>Llm</c> section of the document being
/// edited — every other key stays where it was.
/// </summary>
internal sealed class PresetForm
{
    /// <summary>The presets, in the order the wizard lists them.</summary>
    public static IReadOnlyList<LlmPresetInfo> Catalog => LlmPresets.Catalog;

    /// <summary>One line per preset, for the chooser list.</summary>
    public static IReadOnlyList<string> Choices { get; } = LlmPresets.Catalog
        .Select(preset => string.Create(CultureInfo.InvariantCulture, $"{preset.Title} — {preset.Description}"))
        .ToList()
        .AsReadOnly();

    private int _selectedIndex;

    /// <summary>Index of the selected preset in <see cref="Catalog"/>.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < 0 || value >= Catalog.Count || value == _selectedIndex)
                return;

            _selectedIndex = value;
            ResetOverridesToDefaults();
        }
    }

    /// <summary>The selected preset's metadata.</summary>
    public LlmPresetInfo Selected => Catalog[_selectedIndex];

    /// <summary>Base URL override; pre-filled with the preset's default.</summary>
    [SuppressMessage("Design", "CA1056",
        Justification = "Form text on its way to the JSON 'Llm:BaseUrl' string field; the user's " +
                        "exact text is what gets written, which System.Uri cannot round-trip.")]
    public string BaseUrl { get; set; } = "";

    /// <summary>Model override; pre-filled with the preset's default.</summary>
    public string Model { get; set; } = "";

    /// <summary>API key to write into the file — discouraged.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Environment variable the key is read from instead.</summary>
    public string ApiKeyEnv { get; set; } = LlmPresets.DefaultApiKeyEnv;

    /// <summary>Creates the chooser with the first preset pre-filled.</summary>
    public PresetForm() => ResetOverridesToDefaults();

    /// <summary>Resolves the current selection and overrides into the plan that will be applied.</summary>
    public bool TryBuildPlan([NotNullWhen(true)] out LlmPresetPlan? plan, [NotNullWhen(false)] out string? error)
    {
        var overrides = new LlmPresetOverrides
        {
            BaseUrl = FieldText.ToStringOrNull(BaseUrl),
            Model = FieldText.ToStringOrNull(Model),
            ApiKey = FieldText.ToStringOrNull(ApiKey),
            ApiKeyEnv = FieldText.ToStringOrNull(ApiKeyEnv),
        };

        return LlmPresets.TryCreatePlan(Selected.Name, overrides, out plan, out error);
    }

    /// <summary>The guidance <c>orkeon init</c> prints for a plan, as lines to display.</summary>
    public static IReadOnlyList<string> Guidance(LlmPresetPlan plan) => LlmPresets.Guidance(plan);

    private void ResetOverridesToDefaults()
    {
        var preset = Selected;
        BaseUrl = FieldText.FromString(preset.DefaultBaseUrl);
        Model = FieldText.FromString(preset.DefaultModel);
        ApiKey = "";
        ApiKeyEnv = preset.RequiresApiKey ? LlmPresets.DefaultApiKeyEnv : "";
    }
}
