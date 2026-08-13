using System.Globalization;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>
/// The <c>Logging:LogLevel</c> map. The editor exposes the <c>Default</c> category as a
/// closed list and lists the other categories read-only: they are preserved untouched,
/// like every other key Studio does not model.
/// </summary>
internal sealed class LoggingForm : ISettingsForm
{
    private IReadOnlyList<string> _otherCategories = [];

    /// <inheritdoc />
    public string Title => "Logging";

    /// <summary>Label of the "no Default key" choice, offered first in the list.</summary>
    public const string UnsetLevelLabel = "(unset)";

    /// <summary>The list a level chooser shows: the unset choice, then the .NET levels.</summary>
    public static IReadOnlyList<string> LevelChoices { get; } =
        new List<string> { UnsetLevelLabel }.Concat(LoggingSection.KnownLevels).ToList().AsReadOnly();

    /// <summary>Level of the <c>Default</c> category, or null for "no key".</summary>
    public string? DefaultLevel { get; set; }

    /// <summary>The other configured categories, rendered as <c>category = level</c> lines.</summary>
    public IReadOnlyList<string> OtherCategories => _otherCategories;

    /// <summary>Index of <see cref="DefaultLevel"/> in <see cref="LevelChoices"/> (0 = unset).</summary>
    public int LevelChoiceIndex
    {
        get
        {
            if (DefaultLevel is not { Length: > 0 } level)
                return 0;

            for (var i = 0; i < LoggingSection.KnownLevels.Count; i++)
            {
                if (string.Equals(LoggingSection.KnownLevels[i], level, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }

            return 0;
        }
    }

    /// <summary>Selects a level by its index in <see cref="LevelChoices"/>.</summary>
    public void SelectLevel(int choiceIndex) =>
        DefaultLevel = choiceIndex >= 1 && choiceIndex <= LoggingSection.KnownLevels.Count
            ? LoggingSection.KnownLevels[choiceIndex - 1]
            : null;

    /// <inheritdoc />
    public void LoadFrom(AppSettingsDocument document)
    {
        var section = document.Logging;
        DefaultLevel = section.DefaultLevel;

        _otherCategories = section.Levels
            .Where(entry => !string.Equals(entry.Key, LoggingSection.DefaultCategory, StringComparison.Ordinal))
            .Select(entry => string.Create(CultureInfo.InvariantCulture, $"{entry.Key} = {entry.Value}"))
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ApplyTo(AppSettingsDocument document)
    {
        if (DefaultLevel is { Length: > 0 } level
            && !LoggingSection.KnownLevels.Any(known => string.Equals(known, level, StringComparison.OrdinalIgnoreCase)))
        {
            return
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Logging:LogLevel:Default: '{level}' is not one of {string.Join(", ", LoggingSection.KnownLevels)}."),
            ];
        }

        document.Logging.DefaultLevel = FieldText.ToStringOrNull(DefaultLevel);
        return [];
    }
}
