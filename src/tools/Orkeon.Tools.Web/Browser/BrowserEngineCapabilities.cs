namespace Orkeon.Tools.Web.Browser;

/// <summary>What an engine can do. What it declares out is refused with the reason, never faked.</summary>
internal sealed record BrowserEngineCapabilities
{
    /// <summary><c>true</c> when the browser can run without a window.</summary>
    public bool Headless { get; init; } = true;

    /// <summary><c>true</c> when the browser can be started on a profile other than a disposable one.</summary>
    public bool NamedProfiles { get; init; } = true;
}
