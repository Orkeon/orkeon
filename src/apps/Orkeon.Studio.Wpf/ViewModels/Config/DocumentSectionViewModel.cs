using System.Runtime.CompilerServices;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// Base for the section forms. Each form is a thin facade over the live
/// <see cref="AppSettingsDocument"/>: a setter writes straight into the JSON tree, which is what
/// keeps the round-trip lossless (unknown keys are never re-serialized from a typed model).
/// </summary>
public abstract class DocumentSectionViewModel : ObservableObject
{
    private readonly Func<AppSettingsDocument> _document;
    private readonly Action _onChanged;

    /// <summary>
    /// Binds the form to the document accessor and to the callback that marks it dirty. The document
    /// is reached through a delegate rather than captured, so opening another file replaces it
    /// without invalidating the forms the view is already bound to.
    /// </summary>
    protected DocumentSectionViewModel(Func<AppSettingsDocument> document, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(onChanged);

        _document = document;
        _onChanged = onChanged;
    }

    /// <summary>The document being edited.</summary>
    protected AppSettingsDocument Document => _document();

    /// <summary>Whether the section is present in the document at all.</summary>
    public abstract bool Exists { get; }

    /// <summary>Writes a value through <paramref name="write"/> and notifies, unless it is unchanged.</summary>
    protected void SetValue<T>(T current, T value, Action<T> write, [CallerMemberName] string? propertyName = null)
    {
        ArgumentNullException.ThrowIfNull(write);

        if (EqualityComparer<T>.Default.Equals(current, value))
            return;

        write(value);
        OnPropertyChanged(propertyName);
        NotifyDocumentChanged();
    }

    /// <summary>
    /// Signals that the underlying JSON was rewritten by something other than a
    /// <see cref="SetValue{T}"/> call — a list rebuilt wholesale, for instance.
    /// </summary>
    protected void NotifyDocumentChanged()
    {
        OnSectionChanged();
        _onChanged();
    }

    /// <summary>Re-reads every bound property, after the document was replaced or rewritten.</summary>
    public virtual void Refresh()
    {
        OnPropertyChanged(string.Empty);
        OnSectionChanged();
    }

    /// <summary>Hook for the computed properties a section derives from its own fields.</summary>
    protected virtual void OnSectionChanged() => OnPropertyChanged(nameof(Exists));
}
