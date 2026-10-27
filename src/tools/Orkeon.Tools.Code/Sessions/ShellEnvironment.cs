using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>Which sockets of the operator's session a shell session receives.</summary>
internal enum ShellSessionSockets
{
    /// <summary>None of them.</summary>
    None,

    /// <summary>The ssh agent and the runtime directory, without the session bus.</summary>
    WithoutBus,

    /// <summary>
    /// All of them, the session bus included: only where nothing confines the command, or when the
    /// operator opted in (<c>Orkeon:Tools:Shell:Environment:SessionBus</c>).
    /// </summary>
    All,
}

/// <summary>The environment a shell session, and every command it runs, starts with.</summary>
internal static partial class ShellEnvironment
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
    /// address, not a secret, but what lies behind it is powerful: the agent signs with every key it
    /// holds, and the bus reads the keyring and starts processes outside any sandbox. A sandbox that
    /// cuts the network leaves them out, and a confined session gets the bus only on the operator's
    /// opt-in (<see cref="SessionBusVariable"/>).
    /// </summary>
    internal static readonly IReadOnlyList<string> SessionSocketVariables =
        [SessionBusVariable, "XDG_RUNTIME_DIR", "SSH_AUTH_SOCK"];

    /// <summary>
    /// The address of the session bus. Through it a command reaches every service of the operator's
    /// session: <c>secret-tool search --all</c> reads the keyring, <c>systemd-run --user</c> (or a
    /// call to <c>org.freedesktop.systemd1</c>) starts a process outside the sandbox. A confined
    /// session receives it only when <c>Orkeon:Tools:Shell:Environment:SessionBus</c> is true.
    /// </summary>
    internal const string SessionBusVariable = "DBUS_SESSION_BUS_ADDRESS";

    /// <summary>
    /// How two variable names compare: case-sensitively, as the kernel does, except on Windows,
    /// whose environment ignores case. <c>http_proxy</c> and <c>HTTP_PROXY</c> are two variables on
    /// Linux and macOS.
    /// </summary>
    internal static StringComparer NameComparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// The words a name the operator adds may not contain (<c>Orkeon:Tools:Shell:Environment:Propagate</c>),
    /// compared without case: such a variable most likely holds a secret, and the secret filter wins
    /// over the operator's list. A built-in name is checked before them, so <c>SSH_AUTH_SOCK</c> is
    /// merely a duplicate. Twin of <c>HostProcessRunner.SecretWords</c>.
    /// </summary>
    internal static readonly IReadOnlyList<string> SecretWords =
        ["KEY", "TOKEN", "SECRET", "PASSWORD", "PASSWD", "CREDENTIAL", "AUTH"];

    /// <summary>The prefix of the runner's own variables, its LLM API keys among them; never added.</summary>
    internal const string ReservedPrefix = "ORKEON_";

    // The two variables RestrictedVariables sets rather than propagates.
    private static readonly string[] s_setVariables = ["DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO"];

    /// <summary>
    /// The names the operator added (<c>Orkeon:Tools:Shell:Environment:Propagate</c>) that a session
    /// may receive, read from the process environment. See the overload that takes a reader.
    /// </summary>
    /// <param name="configured">The names as configured.</param>
    /// <param name="logger">Receives one warning per refused name; null logs nothing.</param>
    public static IReadOnlyList<string> SelectPropagated(IEnumerable<string> configured, ILogger? logger)
        => SelectPropagated(configured, Environment.GetEnvironmentVariable, logger);

    /// <summary>
    /// The names the operator added (<c>Orkeon:Tools:Shell:Environment:Propagate</c>) that a session
    /// may receive, in their order, each once (<see cref="NameComparer"/>). A name that is invalid
    /// (blank, holding <c>=</c> or a NUL), starts with <c>ORKEON_</c> or contains one of the
    /// <see cref="SecretWords"/>, or whose value carries URL credentials
    /// (<c>scheme://user:password@host</c>), is refused with a warning that never quotes the value;
    /// a name already built in is ignored, since it changes nothing.
    /// </summary>
    /// <param name="configured">The names as configured.</param>
    /// <param name="read">Reads one variable of the parent environment; null when it is unset.</param>
    /// <param name="logger">Receives one warning per refused name; null logs nothing.</param>
    public static IReadOnlyList<string> SelectPropagated(IEnumerable<string> configured, Func<string, string?> read, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(read);
        var accepted = new List<string>();
        foreach (var name in configured)
        {
            if ((RefusalOf(name) ?? ValueRefusalOf(name, read)) is { } reason)
            {
                if (logger is not null)
                    LogNameRefused(logger, name.Replace("\0", "\\0", StringComparison.Ordinal), reason);
                continue;
            }

            if (!IsBuiltIn(name) && !accepted.Contains(name, NameComparer))
                accepted.Add(name);
        }

        return accepted;
    }

    /// <summary>
    /// Logs, once per host, that the session bus is handed to confined sessions
    /// (<c>Orkeon:Tools:Shell:Environment:SessionBus</c>).
    /// </summary>
    /// <param name="logger">Receives the warning.</param>
    public static void WarnSessionBus(ILogger logger) => LogSessionBusEnabled(logger);

    /// <summary>
    /// Whether <paramref name="value"/> carries the userinfo of a URL (<c>scheme://user:password@host</c>,
    /// or a lone user standing for a token): a proxy or remote address that embeds its credential.
    /// </summary>
    internal static bool CarriesUrlCredentials(string value) => UrlUserInfo().IsMatch(value);

    // Why an added name is refused for its value, or null when the value may pass (an unset one does).
    private static string? ValueRefusalOf(string name, Func<string, string?> read)
        => !IsBuiltIn(name) && read(name) is { } value && CarriesUrlCredentials(value)
            ? "its value carries URL credentials (scheme://user:password@host)"
            : null;

    // A scheme, '//', then an authority part that ends with '@' before any '/', '?', '#', blank or list separator.
    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+.\-]*://[^/?#@\s,;]+@", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UrlUserInfo();

    // Why an added name is refused, or null when it may pass (a built-in name passes here).
    private static string? RefusalOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('=', StringComparison.Ordinal) || name.Contains('\0', StringComparison.Ordinal))
            return "a variable name may not be blank nor hold '=' or a NUL";
        if (IsBuiltIn(name))
            return null;
        if (name.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase))
            return "the ORKEON_ variables are the runner's own and carry its secrets";
        if (SecretWords.FirstOrDefault(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)) is { } word)
            return $"it contains '{word}', which names a secret";
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Orkeon:Tools:Shell:Environment:Propagate: '{Name}' is not propagated: {Reason}.")]
    private static partial void LogNameRefused(ILogger logger, string name, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Orkeon:Tools:Shell:Environment:SessionBus is true: a confined shell session reaches the operator's session bus, through which a command can leave the sandbox (systemd-run --user) and read every keyring secret (secret-tool).")]
    private static partial void LogSessionBusEnabled(ILogger logger);

    private static bool IsBuiltIn(string name)
        => s_propagatedEnvironmentVariables.Contains(name, NameComparer)
           || SessionSocketVariables.Contains(name, NameComparer)
           || s_setVariables.Contains(name, NameComparer);

    /// <summary>
    /// Replaces the inherited environment of <paramref name="startInfo"/> with the propagated
    /// variables of the process environment, the session sockets <paramref name="sockets"/> names and
    /// the names the operator added included, so a command the allowlist permits can read its own
    /// environment without reading the runner's secrets.
    /// </summary>
    /// <param name="startInfo">The start of the session or of one of its jobs.</param>
    /// <param name="sockets">Which <see cref="SessionSocketVariables"/> follow.</param>
    /// <param name="propagated">The names the operator added, as <c>SelectPropagated</c> kept them.</param>
    public static void ConfigureRestricted(ProcessStartInfo startInfo, ShellSessionSockets sockets, IReadOnlyList<string>? propagated = null)
        => ConfigureRestricted(startInfo, Environment.GetEnvironmentVariable, sockets, propagated);

    /// <summary>
    /// Replaces the inherited environment of <paramref name="startInfo"/> with the propagated
    /// variables <paramref name="read"/> answers.
    /// </summary>
    /// <param name="startInfo">The start of the session or of one of its jobs.</param>
    /// <param name="read">Reads one variable of the parent environment; null when it is unset.</param>
    /// <param name="sockets">Which <see cref="SessionSocketVariables"/> follow.</param>
    /// <param name="propagated">The names the operator added; they follow whatever <paramref name="sockets"/> says.</param>
    public static void ConfigureRestricted(
        ProcessStartInfo startInfo, Func<string, string?> read, ShellSessionSockets sockets, IReadOnlyList<string>? propagated = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.Environment.Clear();
        foreach (var (name, value) in RestrictedVariables(read, sockets, propagated))
            startInfo.Environment[name] = value;
    }

    /// <summary>
    /// The variables a session starts with, read from the process environment, the session sockets
    /// <paramref name="sockets"/> names and the names the operator added included. The keys compare
    /// as <see cref="NameComparer"/> does.
    /// </summary>
    /// <param name="sockets">Which <see cref="SessionSocketVariables"/> follow.</param>
    /// <param name="propagated">The names the operator added, as <c>SelectPropagated</c> kept them.</param>
    public static Dictionary<string, string> RestrictedVariables(ShellSessionSockets sockets, IReadOnlyList<string>? propagated = null)
        => RestrictedVariables(Environment.GetEnvironmentVariable, sockets, propagated);

    /// <summary>
    /// The variables a session starts with: the propagated ones <paramref name="read"/> answers, the
    /// session sockets <paramref name="sockets"/> names, the names the operator added, then the two
    /// that keep <c>dotnet</c> quiet. An added name the filter of <c>SelectPropagated</c> refuses, or
    /// whose value carries URL credentials, is dropped here too, silently. The keys compare as
    /// <see cref="NameComparer"/> does.
    /// </summary>
    /// <param name="read">Reads one variable of the parent environment; null when it is unset.</param>
    /// <param name="sockets">Which <see cref="SessionSocketVariables"/> follow.</param>
    /// <param name="propagated">The names the operator added; they follow whatever <paramref name="sockets"/> says.</param>
    public static Dictionary<string, string> RestrictedVariables(
        Func<string, string?> read, ShellSessionSockets sockets, IReadOnlyList<string>? propagated = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        var variables = new Dictionary<string, string>(NameComparer);
        var names = sockets switch
        {
            ShellSessionSockets.All => s_propagatedEnvironmentVariables.Concat(SessionSocketVariables),
            ShellSessionSockets.WithoutBus => s_propagatedEnvironmentVariables.Concat(SessionSocketVariables.Where(name => name != SessionBusVariable)),
            _ => s_propagatedEnvironmentVariables,
        };
        foreach (var name in names)
        {
            var value = read(name);
            if (!string.IsNullOrEmpty(value))
                variables[name] = value;
        }

        // Defence in depth: whoever built the list, a refused name or a value carrying a credential
        // never reaches the session.
        foreach (var name in (propagated ?? []).Where(name => RefusalOf(name) is null && !IsBuiltIn(name)))
        {
            var value = read(name);
            if (!string.IsNullOrEmpty(value) && !CarriesUrlCredentials(value))
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
    /// then, when <paramref name="includeBus"/>, each <c>unix:path=</c> of
    /// <c>DBUS_SESSION_BUS_ADDRESS</c>. Only normalized absolute paths
    /// below the root count. A <c>unix:abstract=</c> bus names no path: an abstract socket belongs to
    /// the network namespace, which a view with the network shares. <c>XDG_RUNTIME_DIR</c> is never
    /// one: the directory holds far more than the sockets. Pure: it reads no file.
    /// </summary>
    /// <param name="read">Reads one variable of the parent environment; null when it is unset.</param>
    /// <param name="includeBus">Whether the session bus is bound too (<c>Orkeon:Tools:Shell:Environment:SessionBus</c>).</param>
    public static IReadOnlyList<string> SessionSocketPaths(Func<string, string?> read, bool includeBus)
    {
        ArgumentNullException.ThrowIfNull(read);
        var paths = new List<string>();
        if (read("SSH_AUTH_SOCK") is { } agent && IsBindableSocketPath(agent))
            paths.Add(agent);
        if (!includeBus)
            return paths;

        foreach (var address in (read(SessionBusVariable) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
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
