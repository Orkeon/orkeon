namespace Orkeon.Host;

/// <summary>
/// How a hosted crew behaves once the service owns it rather than a terminal.
/// <para>
/// rc.2 ships exactly one axis. The spec sketched four (interactive, persistent, chat,
/// concurrency); the review found the first three bound from configuration and read by
/// nothing — an operator could flip them and change nothing at all, and the documentation
/// credited one of them as a defence. Configuration surface that does nothing is worse than
/// absent, so they are gone until the code behind them exists.
/// </para>
/// </summary>
internal sealed record CrewHostingProfile
{
    /// <summary>
    /// How many runs of this crew may be in flight at once. Bounded on purpose: a daemon
    /// that accepts unlimited concurrent runs is a daemon that dies under its first burst.
    /// </summary>
    public int MaxConcurrentRuns { get; init; } = 4;

    /// <summary>The profile a hosted crew gets when the configuration says nothing else.</summary>
    public static CrewHostingProfile Default => new();
}

/// <summary>One crew the service hosts, as the configuration declares it.</summary>
internal sealed record HostedCrewOptions
{
    /// <summary>The name the channels and the operator use to refer to it.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Path to the crew definition — a YAML file, a multi-file crew directory, or an
    /// <c>.ork.ts</c> script. The same targets <c>orkeon run</c> accepts.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>How it behaves under the host.</summary>
    public CrewHostingProfile Profile { get; init; } = CrewHostingProfile.Default;
}

/// <summary>
/// The service's configuration, bound from the <c>Orkeon:Host</c> section.
/// <para>
/// **No secret is ever written here.** A bot token or an API key is referenced by the *name*
/// of an environment variable, never by value — the same rule the LLM providers already
/// follow, and one a chat bot does not get to break.
/// </para>
/// </summary>
internal sealed record OrkeonHostOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Orkeon:Host";

    /// <summary>The crews the service hosts. rc.2 hosts one; the shape already allows more.</summary>
    public IReadOnlyList<HostedCrewOptions> Crews { get; init; } = [];

    /// <summary>
    /// How long a run may take before the host cancels it. A daemon has no user watching to
    /// press Ctrl-C, so an unbounded run is a stuck daemon.
    /// </summary>
    public TimeSpan RunTimeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How long the host waits for in-flight runs on shutdown before giving up. Systemd sends
    /// SIGKILL after its own timeout, so this must stay under it.
    /// </summary>
    public TimeSpan ShutdownGracePeriod { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// A configuration the host refuses to start on. Distinct from any runtime failure because
/// the exit codes differ on purpose: a config error exits 78 (EX_CONFIG), which the systemd
/// unit lists in RestartPreventExitStatus — restarting on a typo would loop every ten seconds
/// and bury the one message the operator needs to read.
/// </summary>
internal sealed class HostConfigurationException : Exception
{
    /// <summary>The process exit code for a refused configuration (sysexits EX_CONFIG).</summary>
    public const int ExitCode = 78;

    /// <summary>Creates the exception with the operator-facing reason.</summary>
    public HostConfigurationException(string message) : base(message)
    {
    }

    /// <summary>Parameterless form for serializers; prefer the message overload.</summary>
    public HostConfigurationException() : base("The host configuration is invalid.")
    {
    }

    /// <summary>Message-and-cause form.</summary>
    public HostConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
