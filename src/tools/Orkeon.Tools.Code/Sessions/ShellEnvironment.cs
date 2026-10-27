using System.Diagnostics;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>The environment a shell session, and every command it runs, starts with.</summary>
internal static class ShellEnvironment
{
    /// <summary>
    /// The only environment variables handed to the session. Everything else the runner holds is
    /// dropped, because that environment carries the crew's LLM API keys
    /// (<c>ORKEON_Llm__ApiKey</c> and friends) and an allowed read-only command
    /// (<c>cat /proc/self/environ</c>, <c>set</c>) would otherwise read them straight back into the
    /// model's context.
    /// <para>
    /// Twin of <c>ProcessIsolationSandbox.ConfigureRestrictedEnvironment</c>
    /// (<c>src/core/Orkeon.Infrastructure/Sandbox</c>), which does the same for the code sandbox. The
    /// list is duplicated rather than shared: a tool project cannot reference Infrastructure, and the
    /// shared kernel it could go through (<c>Orkeon.Tools.Abstractions</c>) has no process-execution
    /// surface to hang it on.
    /// </para>
    /// </summary>
    private static readonly string[] s_propagatedEnvironmentVariables =
    [
        // PATH is propagated, not rebuilt: the tool starts its command from a BARE executable
        // name by design, so the host lookup path is what makes git and the opt-in
        // interpreters resolvable wherever an operator installed them.
        "PATH",
        // POSIX: git needs a HOME to find its own configuration, the locale decides how the
        // child encodes what it prints, and TMPDIR gives it a writable scratch directory.
        "HOME", "LANG", "LC_ALL", "TMPDIR",
        // Windows: cmd.exe and the CLR resolve an executable through these.
        "USERPROFILE", "SystemRoot", "SystemDrive", "PATHEXT", "TEMP", "TMP",
        "APPDATA", "LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)",
        // The .NET and NuGet locations, load-bearing once interpreters are opted in.
        "DOTNET_ROOT", "NUGET_PACKAGES"
    ];

    /// <summary>
    /// The variables that name a socket of the operator's session (#216): the session bus, through
    /// which <c>gh</c>, <c>git-credential-libsecret</c> and the GNOME/KDE keyrings hand a credential
    /// over, the runtime directory the bus and the keyrings live in, and the ssh agent. Each holds an
    /// address, never a secret: the secret stays behind the socket's own access control. A sandbox
    /// that cuts the network leaves them out.
    /// </summary>
    internal static readonly IReadOnlyList<string> SessionSocketVariables =
        ["DBUS_SESSION_BUS_ADDRESS", "XDG_RUNTIME_DIR", "SSH_AUTH_SOCK"];

    /// <summary>
    /// Replaces the inherited environment of <paramref name="startInfo"/> with the propagated
    /// variables of the process environment, the session sockets included, so a command the
    /// allowlist permits can read its own environment without reading the runner's secrets.
    /// </summary>
    public static void ConfigureRestricted(ProcessStartInfo startInfo)
        => ConfigureRestricted(startInfo, Environment.GetEnvironmentVariable, includeSessionSockets: true);

    /// <summary>
    /// Replaces the inherited environment of <paramref name="startInfo"/> with the propagated
    /// variables <paramref name="read"/> answers.
    /// </summary>
    /// <param name="startInfo">The start of the session or of one of its jobs.</param>
    /// <param name="read">Reads one variable of the parent environment; null when it is unset.</param>
    /// <param name="includeSessionSockets">Whether the <see cref="SessionSocketVariables"/> follow.</param>
    public static void ConfigureRestricted(ProcessStartInfo startInfo, Func<string, string?> read, bool includeSessionSockets)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.Environment.Clear();
        foreach (var (name, value) in RestrictedVariables(read, includeSessionSockets))
            startInfo.Environment[name] = value;
    }

    /// <summary>
    /// The variables a session starts with, read from the process environment, the session sockets
    /// included. The keys compare without case, as Windows does.
    /// </summary>
    public static Dictionary<string, string> RestrictedVariables()
        => RestrictedVariables(Environment.GetEnvironmentVariable, includeSessionSockets: true);

    /// <summary>
    /// The variables a session starts with: the propagated ones <paramref name="read"/> answers, the
    /// session sockets when <paramref name="includeSessionSockets"/>, then the two that keep
    /// <c>dotnet</c> quiet. The keys compare without case, as Windows does.
    /// </summary>
    public static Dictionary<string, string> RestrictedVariables(Func<string, string?> read, bool includeSessionSockets)
    {
        ArgumentNullException.ThrowIfNull(read);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = includeSessionSockets
            ? s_propagatedEnvironmentVariables.Concat(SessionSocketVariables)
            : s_propagatedEnvironmentVariables;
        foreach (var name in names)
        {
            var value = read(name);
            if (!string.IsNullOrEmpty(value))
                variables[name] = value;
        }

        // Set, not propagated: the child must stay quiet and must not start a telemetry
        // upload of its own on the first `dotnet` invocation of a crew run.
        variables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        variables["DOTNET_NOLOGO"] = "1";
        return variables;
    }

    /// <summary>
    /// The host paths of the session sockets a confined view binds: the <c>SSH_AUTH_SOCK</c> path,
    /// then each <c>unix:path=</c> of <c>DBUS_SESSION_BUS_ADDRESS</c>. Only normalized absolute paths
    /// below the root count. A <c>unix:abstract=</c> bus names no path: an abstract socket belongs to
    /// the network namespace, which a view with the network shares. <c>XDG_RUNTIME_DIR</c> is never
    /// one: the directory holds far more than the sockets. Pure: it reads no file.
    /// </summary>
    /// <param name="read">Reads one variable of the parent environment; null when it is unset.</param>
    public static IReadOnlyList<string> SessionSocketPaths(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var paths = new List<string>();
        if (read("SSH_AUTH_SOCK") is { } agent && IsBindableSocketPath(agent))
            paths.Add(agent);

        foreach (var address in (read("DBUS_SESSION_BUS_ADDRESS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!address.StartsWith("unix:", StringComparison.Ordinal))
                continue;
            foreach (var pair in address["unix:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!pair.StartsWith("path=", StringComparison.Ordinal))
                    continue;
                var path = Uri.UnescapeDataString(pair["path=".Length..]);
                if (IsBindableSocketPath(path) && !paths.Contains(path, StringComparer.Ordinal))
                    paths.Add(path);
            }
        }

        return paths;
    }

    // An absolute POSIX path that is not the root and names no '.' or '..' segment, so a bind of it
    // lands exactly where the variable says.
    private static bool IsBindableSocketPath(string path)
        => path.Length > 1
           && path[0] == '/'
           && !path.EndsWith('/')
           && path.Split('/').Skip(1).All(segment => segment.Length > 0 && segment != "." && segment != "..");
}
