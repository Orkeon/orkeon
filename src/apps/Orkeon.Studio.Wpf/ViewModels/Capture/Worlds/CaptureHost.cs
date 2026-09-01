using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

/// <summary>
/// The WPF-shaped seams the shell builder cannot make for itself, handed in from the outside.
/// <para>
/// This is what keeps the builder — and therefore the whole catalogue — free of WPF: the app
/// passes its dispatcher and its resx-backed strings, the Linux test passes inline ones, and the
/// same code builds the same shell in both.
/// </para>
/// </summary>
internal sealed record CaptureHost
{
    /// <summary>Where ViewModel work is marshalled.</summary>
    public required IUiDispatcher Dispatcher { get; init; }

    /// <summary>
    /// The assistant's timed beats. Immediate on purpose: a campaign that waited on wall-clock
    /// delays would photograph whichever beat happened to have landed.
    /// </summary>
    public IUiDelay Delay { get; init; } = ImmediateUiDelay.Instance;

    /// <summary>The string catalogue — resx-backed in the app, English inline in the tests.</summary>
    public IStudioStrings? Strings { get; init; }

    /// <summary>How a language choice reaches the views' own bindings.</summary>
    public Action<string>? ApplyLanguage { get; init; }

    /// <summary>The language the shell starts in.</summary>
    public required string Language { get; init; }

    /// <summary>The mode the shell starts in.</summary>
    public required string Mode { get; init; }
}
