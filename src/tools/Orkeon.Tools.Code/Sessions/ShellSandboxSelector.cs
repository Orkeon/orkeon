using Microsoft.Extensions.Logging;
using Orkeon.Tools.Code.Confinement;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// Picks the sandbox of a configured <see cref="ShellConfinementMode"/> on this host, through
/// <see cref="ShellConfinementResolver"/>: unconfined, bubblewrap, Seatbelt, AppContainer, or one that
/// reports why it cannot run (a mode for another platform). The container notice is logged once per
/// selector.
/// </summary>
internal sealed partial class ShellSandboxSelector
{
    private readonly UnconfinedShellSandbox _unconfined;
    private readonly BubblewrapShellSandbox _bubblewrap;
    private readonly SeatbeltShellSandbox _seatbelt;
    private readonly AppContainerShellSandbox _appContainer;
    private readonly IContainerDetector _containers;
    private readonly ShellPlatform _platform;
    private readonly ILogger? _logger;
    private int _noticeLogged;

    /// <summary>A selector over the given sandboxes (the shared AppContainer one by default), container detector and platform.</summary>
    public ShellSandboxSelector(
        UnconfinedShellSandbox unconfined,
        BubblewrapShellSandbox bubblewrap,
        SeatbeltShellSandbox seatbelt,
        IContainerDetector containers,
        ShellPlatform platform,
        ILogger? logger = null,
        AppContainerShellSandbox? appContainer = null)
    {
        ArgumentNullException.ThrowIfNull(unconfined);
        ArgumentNullException.ThrowIfNull(bubblewrap);
        ArgumentNullException.ThrowIfNull(seatbelt);
        ArgumentNullException.ThrowIfNull(containers);
        _unconfined = unconfined;
        _bubblewrap = bubblewrap;
        _seatbelt = seatbelt;
        _appContainer = appContainer ?? AppContainerShellSandbox.Shared;
        _containers = containers;
        _platform = platform;
        _logger = logger;
    }

    /// <summary>The sandbox that runs the sessions of <paramref name="mode"/> on this host.</summary>
    public IShellSandbox Select(ShellConfinementMode mode)
    {
        // The container is probed for Auto only: an explicit mode is what the operator asked for.
        var inContainer = mode == ShellConfinementMode.Auto && _containers.IsInContainer();
        var resolution = ShellConfinementResolver.Resolve(mode, _platform, inContainer);
        if (resolution.Notice is not null && _logger is not null && Interlocked.Exchange(ref _noticeLogged, 1) == 0)
            LogNotice(_logger, resolution.Notice);

        return resolution.Backend switch
        {
            ShellConfinementBackend.None => _unconfined,
            ShellConfinementBackend.Bubblewrap => _bubblewrap,
            ShellConfinementBackend.Seatbelt => _seatbelt,
            ShellConfinementBackend.AppContainer => _appContainer,
            _ => new UnavailableShellSandbox("unavailable", resolution.UnavailableReason ?? "no confinement backend is available"),
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Notice}")]
    private static partial void LogNotice(ILogger logger, string notice);
}
