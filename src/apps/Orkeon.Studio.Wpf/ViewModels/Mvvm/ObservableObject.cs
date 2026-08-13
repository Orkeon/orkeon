using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Orkeon.Studio.Wpf.ViewModels.Mvvm;

/// <summary>
/// Minimal hand-written <see cref="INotifyPropertyChanged"/> base.
/// No MVVM package is referenced: the ViewModels must compile and run on a plain
/// <c>net10.0</c> test assembly, so they may not depend on anything WPF-specific.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Assigns <paramref name="value"/> and notifies, unless it is already the current value.</summary>
    /// <returns><see langword="true"/> when the value actually changed.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/> for <paramref name="propertyName"/>.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Raises <see cref="PropertyChanged"/> for several computed properties at once.</summary>
    protected void OnPropertiesChanged(params string[] propertyNames)
    {
        ArgumentNullException.ThrowIfNull(propertyNames);

        foreach (var propertyName in propertyNames)
            OnPropertyChanged(propertyName);
    }
}
