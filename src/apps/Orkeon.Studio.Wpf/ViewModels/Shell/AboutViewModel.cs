using System.Reflection;
using System.Text.RegularExpressions;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>
/// The About overlay: Studio's version, the co-installed CLI's version, and Kama. The CLI
/// version is asked once, lazily, the first time the dialog opens — <c>orkeon --version</c> is
/// a disk-and-fork touch that the window must not pay at startup, and a missing binary simply
/// leaves the CLI segment out of the line rather than surfacing an error in an About box.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
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
        FullVersion = studioVersion ?? DetectFullVersion();
        StudioVersion = TrimBuildMetadata(FullVersion);
        OpenCommand = new RelayCommand(Open);
        CloseCommand = new RelayCommand(() => IsOpen = false);
    }

    /// <summary>Whether the overlay is showing.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    /// <summary>Studio's own version, without the build metadata (<c>+sha</c>) nor the local build label.</summary>
    public string StudioVersion { get; }

    /// <summary>The raw <c>AssemblyInformationalVersion</c>, for the tooltip of the Studio line.</summary>
    public string FullVersion { get; }

    /// <summary>The CLI's version, or null while unknown (not yet asked, or no binary).</summary>
    public string? CliVersion
    {
        get => _cliVersion;
        private set
        {
            if (SetProperty(ref _cliVersion, value))
                OnPropertiesChanged(nameof(VersionLine), nameof(CliVersionLine), nameof(VersionLines));
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

    /// <summary>"Studio 1.0.0-rc.6": the splash footer's only line, and the first line of About.</summary>
    public string StudioVersionLine => $"Studio {StudioVersion}";

    /// <summary>"CLI 1.0.0-rc.6", or empty while <c>orkeon --version</c> has not answered.</summary>
    public string CliVersionLine => CliVersion is { Length: > 0 } cli ? $"CLI {cli}" : string.Empty;

    /// <summary>".NET 10": the runtime's major version.</summary>
    public static string RuntimeLine => $".NET {Environment.Version.Major}";

    /// <summary>The versions one per line (008), Studio first, no empty line while the CLI is unknown.</summary>
    public IReadOnlyList<string> VersionLines =>
        [.. new[] { StudioVersionLine, CliVersionLine, RuntimeLine }.Where(line => line.Length > 0)];

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

    private static string DetectFullVersion()
    {
        var informational = typeof(AboutViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return informational is { Length: > 0 }
            ? informational
            : typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";
    }

    /// <summary>
    /// Drops the build metadata (<c>+sha</c>), then the local build label that
    /// <c>scripts/resolve-version.ps1</c> appends: <c>1.0.0-rc.6.local.202611072029</c> and
    /// <c>1.0.1-local.202611072029</c> both read as their release version.
    /// </summary>
    internal static string TrimBuildMetadata(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        var trimmed = plus > 0 ? version[..plus] : version;
        return LocalLabel().Replace(trimmed, string.Empty);
    }

    [GeneratedRegex(@"[.-]local\.\d{12}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LocalLabel();
}
