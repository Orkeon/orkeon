using System.Collections.Generic;
using System.Collections.ObjectModel;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>One <c>Logging:LogLevel</c> entry: a category and the level that applies to it.</summary>
public sealed class LogCategoryViewModel : ObservableObject
{
    private string _category;
    private string _level;

    /// <summary>Creates a row for a category / level pair.</summary>
    public LogCategoryViewModel(string category, string level)
    {
        _category = category;
        _level = level;
    }

    /// <summary>Raised whenever the row changes, so the section can rewrite the document.</summary>
    public event EventHandler? Edited;

    /// <summary>The logger category, e.g. <c>Default</c> or <c>Microsoft.AspNetCore</c>.</summary>
    public string Category
    {
        get => _category;
        set
        {
            if (SetProperty(ref _category, value))
                Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The minimum level logged for this category.</summary>
    public string Level
    {
        get => _level;
        set
        {
            if (SetProperty(ref _level, value))
                Edited?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>
/// The <c>Logging:LogLevel</c> form (spec §4.1): the <c>Default</c> level promoted to its own combo
/// box, plus a free list for the other categories.
/// </summary>
public sealed class LoggingSectionViewModel : DocumentSectionViewModel
{
    private LogCategoryViewModel? _selectedCategory;
    private bool _suspendWrite;

    /// <summary>Binds the form to the <c>Logging:LogLevel</c> section of the document.</summary>
    public LoggingSectionViewModel(Func<AppSettingsDocument> document, Action onChanged)
        : base(document, onChanged)
    {
        AddCategoryCommand = new RelayCommand(() => AddCategory());
        RemoveCategoryCommand = new RelayCommand(RemoveSelected, () => SelectedCategory is not null);
        ReloadCategories();
    }

    private LoggingSection Section => Document.Logging;

    /// <inheritdoc />
    public override bool Exists => Section.Exists;

    /// <summary>The closed list of levels offered by the combo boxes.</summary>
    public static IReadOnlyList<string> KnownLevels => LoggingSection.KnownLevels;

    /// <summary>The level applied to categories with no entry of their own.</summary>
    public string? DefaultLevel
    {
        get => Section.DefaultLevel;
        set => SetValue(Section.DefaultLevel, Blank(value), v =>
        {
            Section.DefaultLevel = v;
            ReloadCategories();
        });
    }

    /// <summary>Every category present in the file, <c>Default</c> included.</summary>
    public ObservableCollection<LogCategoryViewModel> Categories { get; } = [];

    /// <summary>The row targeted by <see cref="RemoveCategoryCommand"/>.</summary>
    public LogCategoryViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
                RemoveCategoryCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Appends an empty category row.</summary>
    public RelayCommand AddCategoryCommand { get; }

    /// <summary>Removes <see cref="SelectedCategory"/> from the document.</summary>
    public RelayCommand RemoveCategoryCommand { get; }

    /// <summary>Appends a category row and selects it.</summary>
    public LogCategoryViewModel AddCategory(string category = "", string level = "Information")
    {
        var row = new LogCategoryViewModel(category, level);
        row.Edited += OnCategoryEdited;
        Categories.Add(row);
        SelectedCategory = row;
        WriteCategories();
        return row;
    }

    /// <summary>Removes a category row and rewrites the document map.</summary>
    public void RemoveCategory(LogCategoryViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.Edited -= OnCategoryEdited;
        if (Categories.Remove(row))
        {
            Section.SetLevel(row.Category is { Length: > 0 } name ? name : LoggingSection.DefaultCategory, null);
            WriteCategories();
        }
    }

    /// <summary>Drops the whole section from the document.</summary>
    public void RemoveSection()
    {
        Section.Remove();
        ReloadCategories();
        Refresh();
    }

    /// <inheritdoc />
    public override void Refresh()
    {
        ReloadCategories();
        base.Refresh();
    }

    private void ReloadCategories()
    {
        _suspendWrite = true;
        try
        {
            foreach (var row in Categories)
                row.Edited -= OnCategoryEdited;

            Categories.Clear();
            foreach (var (category, level) in Section.Levels)
            {
                var row = new LogCategoryViewModel(category, level);
                row.Edited += OnCategoryEdited;
                Categories.Add(row);
            }
        }
        finally
        {
            _suspendWrite = false;
        }

        SelectedCategory = Categories.Count > 0 ? Categories[0] : null;
        OnPropertyChanged(nameof(DefaultLevel));
    }

    private void OnCategoryEdited(object? sender, EventArgs e) => WriteCategories();

    private void WriteCategories()
    {
        if (_suspendWrite)
            return;

        // The map is rewritten wholesale: a renamed category would otherwise leave its old key behind.
        Section.Remove();
        foreach (var row in Categories)
        {
            if (row.Category is { Length: > 0 } category)
                Section.SetLevel(category, row.Level);
        }

        OnPropertyChanged(nameof(Categories));
        NotifyDocumentChanged();
    }

    private void RemoveSelected()
    {
        if (SelectedCategory is { } row)
            RemoveCategory(row);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
