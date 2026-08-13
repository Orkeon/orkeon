using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Studio.Core.Storage;

namespace Orkeon.Studio.Config.Presentation;

/// <summary>Which of the two save targets the user picked.</summary>
internal enum SaveLocationMode
{
    /// <summary>The per-user file <c>orkeon init</c> writes — the default.</summary>
    Global,

    /// <summary>A path the user chose, typically next to a crew.</summary>
    CustomPath,
}

/// <summary>
/// The save-target chooser. It shows the whole resolution chain next to the two targets,
/// so the user can tell which file a later <c>orkeon run</c> will actually load.
/// </summary>
internal sealed class SaveLocationModel
{
    /// <summary>Creates the chooser, resolving the global path once.</summary>
    public SaveLocationModel()
    {
        if (SettingsLocations.TryGetGlobalSettingsPath(out var path, out var error))
            GlobalPath = path;
        else
            GlobalPathError = error;
    }

    /// <summary>The selected target.</summary>
    public SaveLocationMode Mode { get; set; } = SaveLocationMode.Global;

    /// <summary>Free path typed or picked by the user.</summary>
    public string CustomPath { get; set; } = "";

    /// <summary>The per-user global file, when this machine has one.</summary>
    public string? GlobalPath { get; }

    /// <summary>Why there is no global path (a container without <c>HOME</c>).</summary>
    public string? GlobalPathError { get; }

    /// <summary>The resolution chain, one line per step, in the order the runtime tries them.</summary>
    public static IReadOnlyList<string> ResolutionChainLines { get; } = SettingsLocations.ResolutionChain
        .Select(step => string.Create(
            CultureInfo.InvariantCulture,
            $"{step.Order}. {step.Title} — {step.Description}"))
        .ToList()
        .AsReadOnly();

    /// <summary>Pre-selects the custom target on the file that is currently open.</summary>
    public void SelectCurrentFile(string? currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
            return;

        CustomPath = currentPath;
        Mode = string.Equals(currentPath, GlobalPath, StringComparison.Ordinal)
            ? SaveLocationMode.Global
            : SaveLocationMode.CustomPath;
    }

    /// <summary>
    /// Resolves the chosen target to an absolute file path. A directory is normalized to
    /// the <c>appsettings.json</c> inside it, the way <c>orkeon init --path</c> does.
    /// </summary>
    public bool TryResolve([NotNullWhen(true)] out string? path, [NotNullWhen(false)] out string? error)
    {
        path = null;
        error = null;

        if (Mode == SaveLocationMode.Global)
        {
            if (GlobalPath is null)
            {
                error = GlobalPathError ?? "No per-user configuration directory could be determined on this machine.";
                return false;
            }

            path = GlobalPath;
            return true;
        }

        var custom = FieldText.ToStringOrNull(CustomPath);
        if (custom is null)
        {
            error = "Type or pick a path to save to.";
            return false;
        }

        try
        {
            path = SettingsLocations.NormalizeTargetPath(custom);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
