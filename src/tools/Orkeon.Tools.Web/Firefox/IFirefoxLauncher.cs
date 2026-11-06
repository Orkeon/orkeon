using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>Starts a browser and opens the WebDriver BiDi channel to it.</summary>
internal interface IFirefoxLauncher
{
    /// <exception cref="FirefoxException">Firefox is not installed, or did not start.</exception>
    Task<IFirefoxBrowser> LaunchAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A running browser. Disposing it ends the process - at once when it has not left by itself - and
/// removes its profile; the channel is the caller's to dispose first.
/// </summary>
internal interface IFirefoxBrowser : IAsyncDisposable
{
    /// <summary>The open channel to the browser.</summary>
    IBiDiTransport Transport { get; }

    /// <summary><c>true</c> when the browser runs without a window.</summary>
    bool Headless { get; }
}
