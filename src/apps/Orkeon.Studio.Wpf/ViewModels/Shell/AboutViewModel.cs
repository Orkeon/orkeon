using System.Reflection;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>
/// The "À propos" overlay: Studio's version, the co-installed CLI's version, and Kama. The CLI
/// version is asked once, lazily, the first time the dialog opens — <c>orkeon --version</c> is
/// a disk-and-fork touch that the window must not pay at startup, and a missing binary simply
/// leaves the CLI segment out of the line rather than surfacing an error in an About box.
/// </summary>
public sealed class AboutViewModel : ObservableObject
{
    private readonly OrkeonProcessRunner _runner;
    private readonly IUiDispatcher _dispatcher;
    private bool _isOpen;
    private string? _cliVersion;
    private bool _cliVersionRequested;

    /// <summary>Builds the dialog state over the shared CLI runner.</summary>
    /// <param name="runner">Used for the one-shot <c>orkeon --version</c>.</param>
    /// <param name="dispatcher">Marshals the async version result; inline when null (tests).</param>
    /// <param name="studioVersion">Overrides the assembly-derived Studio version (tests).</param>
    public AboutViewModel(
        OrkeonProcessRunner runner,
        IUiDispatcher? dispatcher = null,
        string? studioVersion = null)
    {
        ArgumentNullException.ThrowIfNull(runner);

        _runner = runner;
        _dispatcher = dispatcher ?? ImmediateUiDispatcher.Instance;
        StudioVersion = studioVersion ?? DetectStudioVersion();
        OpenCommand = new RelayCommand(Open);
        CloseCommand = new RelayCommand(() => IsOpen = false);
    }

    /// <summary>Whether the overlay is showing.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    /// <summary>Studio's own version, without the build-metadata suffix.</summary>
    public string StudioVersion { get; }

    /// <summary>The CLI's version, or null while unknown (not yet asked, or no binary).</summary>
    public string? CliVersion
    {
        get => _cliVersion;
        private set
        {
            if (SetProperty(ref _cliVersion, value))
                OnPropertyChanged(nameof(VersionLine));
        }
    }

    /// <summary>
    /// The one-line pedigree shown under the title — "Studio 1.0.0-rc.2 · CLI 1.0.0-rc.2 ·
    /// .NET 10". The CLI segment appears only once its version is known.
    /// </summary>
    public string VersionLine =>
        CliVersion is { Length: > 0 } cli
            ? $"Studio {StudioVersion} · CLI {cli} · .NET {Environment.Version.Major}"
            : $"Studio {StudioVersion} · .NET {Environment.Version.Major}";

    /// <summary>Opens the overlay (and asks the CLI its version, the first time).</summary>
    public RelayCommand OpenCommand { get; }

    /// <summary>Closes the overlay.</summary>
    public RelayCommand CloseCommand { get; }

    private void Open()
    {
        IsOpen = true;

        if (_cliVersionRequested)
            return;

        _cliVersionRequested = true;
        _ = LoadCliVersionAsync();
    }

    private async Task LoadCliVersionAsync()
    {
        string? firstLine = null;
        var result = await _runner.RunAsync(
            ["--version"],
            onOutput: line =>
            {
                if (firstLine is null
                    && line.Channel == ProcessOutputChannel.StandardOutput
                    && !string.IsNullOrWhiteSpace(line.Text))
                {
                    firstLine = line.Text.Trim();
                }
            }).ConfigureAwait(false);

        if (result.Outcome != RunOutcome.Success || firstLine is null)
            return;

        var version = TrimBuildMetadata(firstLine);
        _dispatcher.Post(() => CliVersion = version);
    }

    private static string DetectStudioVersion()
    {
        var informational = typeof(AboutViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return informational is { Length: > 0 }
            ? TrimBuildMetadata(informational)
            : typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";
    }

    private static string TrimBuildMetadata(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? version[..plus] : version;
    }
}
