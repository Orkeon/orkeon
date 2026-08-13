using System.Collections.Generic;
using System.Globalization;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Mounts;

/// <summary>
/// One <c>;&lt;sub-path&gt;:&lt;rights&gt;</c> segment of a mount string: a relative path inside the
/// mount whose rights differ from the mount default.
/// </summary>
public sealed class SubPathOverrideViewModel : ObservableObject
{
    private string _relativePath = "";
    private MountRights _rights = MountRights.ReadOnly;

    /// <summary>Creates an empty override row.</summary>
    public SubPathOverrideViewModel()
    {
    }

    /// <summary>Creates a row from an existing Core override.</summary>
    public SubPathOverrideViewModel(SubPathRightsOverride source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _relativePath = source.RelativePath;
        _rights = source.Rights;
    }

    /// <summary>Raised whenever the row changes, so the owning editor can re-validate.</summary>
    public event EventHandler? Edited;

    /// <summary>The path relative to the mount root.</summary>
    public string RelativePath
    {
        get => _relativePath;
        set
        {
            if (SetProperty(ref _relativePath, value))
                RaiseEdited();
        }
    }

    /// <summary>The rights that apply under <see cref="RelativePath"/>.</summary>
    public MountRights Rights
    {
        get => _rights;
        set
        {
            if (SetProperty(ref _rights, value))
                RaiseEdited();
        }
    }

    /// <summary>The closed list of rights offered by the combo box.</summary>
    public static IReadOnlyList<MountRightsChoice> RightsChoices => MountRightsTokens.Choices;

    /// <summary>Converts back to the Core record.</summary>
    public SubPathRightsOverride ToOverride() => new(RelativePath, Rights);

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{RelativePath}:{MountRightsTokens.ToToken(Rights)}");

    private void RaiseEdited()
    {
        OnPropertyChanged(nameof(ToString));
        Edited?.Invoke(this, EventArgs.Empty);
    }
}
