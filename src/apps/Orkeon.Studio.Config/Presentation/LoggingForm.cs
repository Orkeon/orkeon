using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>One entry of the <c>Logging:LogLevel</c> map, as the screen displays it.</summary>
/// <param name="Category">The logger category, e.g. <c>Microsoft.AspNetCore</c>.</param>
/// <param name="Level">The minimum level logged for that category.</param>
internal sealed record LogCategoryRow(string Category, string Level)
{
    /// <summary>The single line the list shows.</summary>
    public string Display => string.Create(CultureInfo.InvariantCulture, $"{Category} = {Level}");
}

/// <summary>
/// The <c>Logging:LogLevel</c> map, edited row by row. The <c>Default</c> category has its
/// own chooser — it additionally offers "no key at all" — and every other category is
/// added, renamed, re-levelled or removed from the list, out of the same closed set of
/// levels the WPF UI offers.
/// </summary>
internal sealed class LoggingForm : ISettingsForm
{
    private readonly List<LogCategoryRow> _categories = [];

    /// <summary>
    /// The categories this form has put in the document — the ones it may delete. A row the
    /// user removed or renamed is deleted by its old name from this list, so a rename never
    /// leaves the old key behind; a category held in a shape Studio does not model (a nested
    /// object rather than a level string) is in neither list and is therefore never touched.
    /// </summary>
    private List<string> _documentCategories = [];

    /// <inheritdoc />
    public string Title => "Logging";

    /// <summary>Label of the "no Default key" choice, offered first in the list.</summary>
    public const string UnsetLevelLabel = "(unset)";

    /// <summary>Level a category row starts on when the user adds one.</summary>
    public const string NewCategoryLevel = "Information";

    /// <summary>The list the <c>Default</c> chooser shows: the unset choice, then the .NET levels.</summary>
    public static IReadOnlyList<string> LevelChoices { get; } =
        new List<string> { UnsetLevelLabel }.Concat(LoggingSection.KnownLevels).ToList().AsReadOnly();

    /// <summary>The list a category chooser shows — the .NET levels, with no unset choice.</summary>
    public static IReadOnlyList<string> CategoryLevelChoices => LoggingSection.KnownLevels;

    /// <summary>Level of the <c>Default</c> category, or null for "no key".</summary>
    public string? DefaultLevel { get; set; }

    /// <summary>True when the document already carried the log-level map.</summary>
    public bool SectionExisted { get; private set; }

    /// <summary>The configured categories other than <c>Default</c>, in file order.</summary>
    public IReadOnlyList<LogCategoryRow> Categories => _categories;

    /// <summary>Index of <see cref="DefaultLevel"/> in <see cref="LevelChoices"/> (0 = unset).</summary>
    public int LevelChoiceIndex
    {
        get
        {
            if (DefaultLevel is not { Length: > 0 } level)
                return 0;

            var index = IndexOfKnownLevel(level);
            return index < 0 ? 0 : index + 1;
        }
    }

    /// <summary>Selects a level by its index in <see cref="LevelChoices"/>.</summary>
    public void SelectLevel(int choiceIndex) =>
        DefaultLevel = choiceIndex >= 1 && choiceIndex <= LoggingSection.KnownLevels.Count
            ? LoggingSection.KnownLevels[choiceIndex - 1]
            : null;

    /// <summary>
    /// Index of <paramref name="level"/> in <see cref="CategoryLevelChoices"/>, falling back
    /// to <see cref="NewCategoryLevel"/> — the chooser of a category row is a closed list, so
    /// it always stands on something.
    /// </summary>
    public static int CategoryLevelIndex(string? level)
    {
        var index = level is null ? -1 : IndexOfKnownLevel(level);
        return index >= 0 ? index : Math.Max(IndexOfKnownLevel(NewCategoryLevel), 0);
    }

    /// <summary>
    /// Checks what a category row would have to satisfy to be accepted, without changing
    /// anything — the dialog asks this before closing.
    /// </summary>
    /// <param name="category">The category name being typed.</param>
    /// <param name="level">The level picked from the closed list.</param>
    /// <param name="replacingIndex">Row being edited, so it does not collide with itself.</param>
    /// <returns>Why the row is refused, or null when it is acceptable.</returns>
    public string? ValidateCategory(string? category, string? level, int replacingIndex = -1)
    {
        var name = FieldText.ToStringOrNull(category);
        if (name is null)
            return "A log category needs a name.";

        if (name.Contains(':', StringComparison.Ordinal))
            return "A category may not contain ':' — that character separates the levels of a configuration path.";

        if (string.Equals(name, LoggingSection.DefaultCategory, StringComparison.OrdinalIgnoreCase))
            return "The 'Default' category is edited by the default-level chooser.";

        for (var i = 0; i < _categories.Count; i++)
        {
            if (i != replacingIndex
                && string.Equals(_categories[i].Category, name, StringComparison.OrdinalIgnoreCase))
            {
                return string.Create(CultureInfo.InvariantCulture, $"'{name}' is already listed.");
            }
        }

        if (NormalizeLevel(level) is null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"'{level}' is not one of {string.Join(", ", LoggingSection.KnownLevels)}.");
        }

        return null;
    }

    /// <summary>Appends a category row.</summary>
    public bool TryAddCategory(string? category, string? level, [NotNullWhen(false)] out string? error)
    {
        error = ValidateCategory(category, level);
        if (error is not null)
            return false;

        _categories.Add(new LogCategoryRow(FieldText.ToStringOrNull(category)!, NormalizeLevel(level)!));
        return true;
    }

    /// <summary>Replaces the category row at <paramref name="index"/> (rename included).</summary>
    public bool TryReplaceCategory(
        int index,
        string? category,
        string? level,
        [NotNullWhen(false)] out string? error)
    {
        if (index < 0 || index >= _categories.Count)
        {
            error = "That category is no longer listed.";
            return false;
        }

        error = ValidateCategory(category, level, index);
        if (error is not null)
            return false;

        _categories[index] = new LogCategoryRow(FieldText.ToStringOrNull(category)!, NormalizeLevel(level)!);
        return true;
    }

    /// <summary>Removes the category row at <paramref name="index"/>; an out-of-range index is ignored.</summary>
    public void RemoveCategoryAt(int index)
    {
        if (index >= 0 && index < _categories.Count)
            _categories.RemoveAt(index);
    }

    /// <inheritdoc />
    public void LoadFrom(AppSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var section = document.Logging;
        SectionExisted = section.Exists;
        DefaultLevel = section.DefaultLevel;

        _categories.Clear();
        _categories.AddRange(section.Levels
            .Where(entry => !string.Equals(entry.Key, LoggingSection.DefaultCategory, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new LogCategoryRow(entry.Key, entry.Value)));

        _documentCategories = _categories.ConvertAll(row => row.Category);
    }

    /// <summary>
    /// Writes the map back. Only the <c>Default</c> level can be refused here: a category row
    /// comes from <see cref="TryAddCategory"/> or <see cref="TryReplaceCategory"/>, which take
    /// their level from the closed list and their name through <see cref="ValidateCategory"/>,
    /// so a row that reached this point is always writable.
    /// </summary>
    /// <inheritdoc />
    public IReadOnlyList<string> ApplyTo(AppSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (DefaultLevel is { Length: > 0 } level && NormalizeLevel(level) is null)
        {
            return
            [
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Logging:LogLevel:Default: '{level}' is not one of {string.Join(", ", LoggingSection.KnownLevels)}."),
            ];
        }

        // Nothing to write, and nothing was there: an absent section stays absent rather
        // than gaining a key the user never asked for.
        if (!SectionExisted
            && _documentCategories.Count == 0
            && _categories.Count == 0
            && FieldText.ToStringOrNull(DefaultLevel) is null)
        {
            return [];
        }

        document.Logging.DefaultLevel = FieldText.ToStringOrNull(DefaultLevel);

        // Deletions first, by the name the key carries in the file: a renamed category would
        // otherwise be written under its new name and left behind under the old one.
        foreach (var category in _documentCategories)
        {
            if (!_categories.Exists(row => string.Equals(row.Category, category, StringComparison.Ordinal)))
                document.Logging.SetLevel(category, null);
        }

        foreach (var row in _categories)
            document.Logging.SetLevel(row.Category, row.Level);

        _documentCategories = _categories.ConvertAll(row => row.Category);
        SectionExisted = _documentCategories.Count > 0 || FieldText.ToStringOrNull(DefaultLevel) is not null;
        return [];
    }

    /// <summary>The canonical spelling of a level, or null when it is not one of them.</summary>
    private static string? NormalizeLevel(string? level)
    {
        if (FieldText.ToStringOrNull(level) is not { } text)
            return null;

        var index = IndexOfKnownLevel(text);
        return index < 0 ? null : LoggingSection.KnownLevels[index];
    }

    private static int IndexOfKnownLevel(string level)
    {
        for (var i = 0; i < LoggingSection.KnownLevels.Count; i++)
        {
            if (string.Equals(LoggingSection.KnownLevels[i], level, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }
}
