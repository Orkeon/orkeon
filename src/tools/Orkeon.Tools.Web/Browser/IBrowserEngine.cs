using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Browser;

/// <summary>
/// One kind of browser the tool can drive: finds its program, opens a profile, starts it and hands back a
/// WebDriver BiDi channel. The session never knows which browser it drives.
/// </summary>
internal interface IBrowserEngine
{
    /// <summary>The value of <c>Engine</c> that picks it, carried by every answer.</summary>
    string Id { get; }

    /// <summary>The browser's name, as errors and logs write it.</summary>
    string DisplayName { get; }

    /// <summary>What the engine can do.</summary>
    BrowserEngineCapabilities Capabilities { get; }

    /// <summary>Starts a browser on <paramref name="profile"/> and connects to its channel.</summary>
    /// <exception cref="FirefoxException">The browser is not installed, did not start, or the profile is missing or held.</exception>
    Task<IBrowserProcess> LaunchAsync(BrowserProfileChoice profile, CancellationToken cancellationToken);
}

/// <summary>
/// A started browser: its process, the profile it runs with, and the WebDriver BiDi channel to it.
/// Disposing it ends the process and removes a disposable profile.
/// </summary>
internal interface IBrowserProcess : IAsyncDisposable
{
    /// <summary>The channel to the browser.</summary>
    IBiDiTransport Transport { get; }

    /// <summary><c>true</c> when the browser runs without a window.</summary>
    bool Headless { get; }

    /// <summary>The profile the browser was started with.</summary>
    BrowserProfileChoice Profile { get; }
}
