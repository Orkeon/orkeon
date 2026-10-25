namespace Orkeon.Tools.Code.Confinement;

/// <summary>The platforms a confinement backend is chosen for.</summary>
internal enum ShellPlatform
{
    /// <summary>Linux: bubblewrap.</summary>
    Linux,

    /// <summary>macOS: Seatbelt.</summary>
    MacOS,

    /// <summary>Windows: AppContainer.</summary>
    Windows,

    /// <summary>Any other platform: no backend.</summary>
    Other,
}

/// <summary>The backend that runs the shell session of an agent.</summary>
internal enum ShellConfinementBackend
{
    /// <summary>No confinement: the session runs on the host.</summary>
    None,

    /// <summary>bubblewrap (Linux).</summary>
    Bubblewrap,

    /// <summary>Seatbelt, <c>sandbox-exec</c> (macOS).</summary>
    Seatbelt,

    /// <summary>AppContainer and a Job Object (Windows).</summary>
    AppContainer,
}

/// <summary>What a configured mode resolves to on a host.</summary>
/// <param name="Backend">The backend that runs the session, or null when none can.</param>
/// <param name="UnavailableReason">Why no backend can, when <paramref name="Backend"/> is null.</param>
/// <param name="Notice">An information line for the operator, such as why confinement is off.</param>
internal sealed record ShellConfinementResolution(
    ShellConfinementBackend? Backend, string? UnavailableReason = null, string? Notice = null);

/// <summary>
/// Maps the configured <see cref="ShellConfinementMode"/>, the platform and whether the host runs in a
/// container to the backend that runs the shell session. Pure: the platform and the container are
/// given, so every case is tested on any machine.
/// </summary>
internal static class ShellConfinementResolver
{
    /// <summary>The line logged when <see cref="ShellConfinementMode.Auto"/> turns confinement off in a container.</summary>
    public const string ContainerNotice =
        "shell confinement off: running in a container; set " + ShellToolOptions.SectionName
        + ":Confinement:Mode to force it";

    /// <summary>The platform of the current process.</summary>
    public static ShellPlatform CurrentPlatform { get; } = DetectPlatform();

    private static ShellPlatform DetectPlatform()
    {
        if (OperatingSystem.IsLinux())
            return ShellPlatform.Linux;
        if (OperatingSystem.IsMacOS())
            return ShellPlatform.MacOS;
        if (OperatingSystem.IsWindows())
            return ShellPlatform.Windows;
        return ShellPlatform.Other;
    }

    /// <summary>What <paramref name="mode"/> resolves to on <paramref name="platform"/>.</summary>
    /// <param name="mode">The configured mode.</param>
    /// <param name="platform">The platform of the host.</param>
    /// <param name="inContainer">Whether the host runs in a container; read by <see cref="ShellConfinementMode.Auto"/> only.</param>
    public static ShellConfinementResolution Resolve(ShellConfinementMode mode, ShellPlatform platform, bool inContainer) => mode switch
    {
        ShellConfinementMode.None => new(ShellConfinementBackend.None),
        ShellConfinementMode.Auto when inContainer => new(ShellConfinementBackend.None, Notice: ContainerNotice),
        ShellConfinementMode.Auto => platform switch
        {
            ShellPlatform.Linux => new(ShellConfinementBackend.Bubblewrap),
            ShellPlatform.MacOS => new(ShellConfinementBackend.Seatbelt),
            ShellPlatform.Windows => new(ShellConfinementBackend.AppContainer),
            _ => new(null, "no confinement backend exists for this platform"),
        },
        ShellConfinementMode.Bubblewrap => OnlyOn(ShellPlatform.Linux, platform, ShellConfinementBackend.Bubblewrap, "bubblewrap confines a process on Linux only"),
        ShellConfinementMode.Seatbelt => OnlyOn(ShellPlatform.MacOS, platform, ShellConfinementBackend.Seatbelt, "Seatbelt confines a process on macOS only"),
        ShellConfinementMode.AppContainer => OnlyOn(ShellPlatform.Windows, platform, ShellConfinementBackend.AppContainer, "AppContainer confines a process on Windows only"),
        _ => new(null, $"'{mode}' is not a confinement mode"),
    };

    private static ShellConfinementResolution OnlyOn(
        ShellPlatform required, ShellPlatform platform, ShellConfinementBackend backend, string reason)
        => platform == required ? new(backend) : new(null, reason);
}
