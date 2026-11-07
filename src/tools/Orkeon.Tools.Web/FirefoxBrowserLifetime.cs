namespace Orkeon.Tools.Web;

/// <summary>How long a browser the <c>firefox</c> tool starts lives (<c>Orkeon:Tools:Firefox:Lifetime</c>).</summary>
public enum FirefoxBrowserLifetime
{
    /// <summary>The browser closes with the agent run that started it; outside any run, right after the action.</summary>
    Run,

    /// <summary>
    /// The browser belongs to the host: one per profile, kept open with its tabs and its profile from one
    /// run to the next, until <c>close</c>, the user closes the window, or the host stops.
    /// </summary>
    Host,
}
