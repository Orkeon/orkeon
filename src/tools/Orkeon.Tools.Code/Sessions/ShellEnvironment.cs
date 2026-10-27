using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.Code.Sessions;

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
    /// address, never a secret: the secret stays behind the socket's own access control. A sandbox
    /// that cuts the network leaves them out.
    /// </summary>
    internal static readonly IReadOnlyList<string> SessionSocketVariables =
        ["DBUS_SESSION_BUS_ADDRESS", "XDG_RUNTIME_DIR", "SSH_AUTH_SOCK"];

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
    /// may receive, in their order, each once. A name that is invalid (blank, holding <c>=</c> or a
    /// NUL), starts with <c>ORKEON_</c> or contains one of the <see cref="SecretWords"/> is refused
    /// with a warning; a name already built in is ignored, since it changes nothing.
    /// </summary>
    /// <param name="configured">The names as configured.</param>
    /// <param name="logger">Receives one warning per refused name; null logs nothing.</param>
    public static IReadOnlyList<string> SelectPropagated(IEnumerable<string> configured, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(configured);
        var accepted = new List<string>();
        foreach (var name in configured)
        {
            if (RefusalOf(name) is { } reason)
            {
                if (logger is not null)
                    LogNameRefused(logger, name.Replace("\0", "\\0", StringComparison.Ordinal), reason);
                continue;
            }

            if (!IsBuiltIn(name) && !accepted.Contains(name, StringComparer.OrdinalIgnoreCase))
                accepted.Add(name);
        }

        return accepted;
    }

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

    private static bool IsBuiltIn(string name)
        => s_propagatedEnvironmentVariables.Contains(name, StringComparer.OrdinalIgnoreCase)
           || SessionSocketVariables.Contains(name, StringComparer.OrdinalIgnoreCase)
           || s_setVariables.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Replaces the inherited environment of <paramref name="startInfo"/> with the propagated
    /// variables of the process environment, the session sockets and the names the operator added
    /// included, so a command the allowlist permits can read its own environment without reading the
    /// runner's secrets.
    /// </summary>
    /// <param name="startInfo">The start of the session or of one of its jobs.</param>
    /// <param name="propagated">The names the operator added, as <see cref="SelectPropagated"/> kept them.</param>
    public static void ConfigureRestricted(ProcessStartInfo startInfo, IReadOnlyList<string>? propagated = null)
        => ConfigureRestricted(startInfo, Environment.GetEnvironmentVariable, includeSessionSockets: true, propagated);

    /// <summary>
    /// Replaces the inherited environment of <paramref name="startInfo"/> with the propagated
    /// variables <paramref name="read"/> answers.
    /// </summary>
    /// <param name="startInfo">The start of the session or of one of its jobs.</param>
    /// <param name="read">Reads one variable of the parent environment; null when it is unset.</param>
    /// <param name="includeSessionSockets">Whether the <see cref="SessionSocketVariables"/> follow.</param>
    /// <param name="propagated">The names the operator added; they follow whatever <paramref name="includeSessionSockets"/> says.</param>
    public static void ConfigureRestricted(
        ProcessStartInfo startInfo, Func<string, string?> read, bool includeSessionSockets, IReadOnlyList<string>? propagated = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.Environment.Clear();
        foreach (var (name, value) in RestrictedVariables(read, includeSessionSockets, propagated))
            startInfo.Environment[name] = value;
    }

    /// <summary>
    /// The variables a session starts with, read from the process environment, the session sockets
    /// and the names the operator added included. The keys compare without case, as Windows does.
    /// </summary>
    /// <param name="propagated">The names the operator added, as <see cref="SelectPropagated"/> kept them.</param>
    public static Dictionary<string, string> RestrictedVariables(IReadOnlyList<string>? propagated = null)
        => RestrictedVariables(Environment.GetEnvironmentVariable, includeSessionSockets: true, propagated);

    /// <summary>
    /// The variables a session starts with: the propagated ones <paramref name="read"/> answers, the
    /// session sockets when <paramref name="includeSessionSockets"/>, the names the operator added,
    /// then the two that keep <c>dotnet</c> quiet. An added name the filter of
    /// <see cref="SelectPropagated"/> refuses is dropped here too, silently. The keys compare without
    /// case, as Windows does.
    /// </summary>
    /// <param name="read">Reads one variable of the parent environment; null when it is unset.</param>
    /// <param name="includeSessionSockets">Whether the <see cref="SessionSocketVariables"/> follow.</param>
    /// <param name="propagated">The names the operator added; they follow whatever <paramref name="includeSessionSockets"/> says.</param>
    public static Dictionary<string, string> RestrictedVariables(
        Func<string, string?> read, bool includeSessionSockets, IReadOnlyList<string>? propagated = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = includeSessionSockets
            ? s_propagatedEnvironmentVariables.Concat(SessionSocketVariables)
            : s_propagatedEnvironmentVariables;
        // Defence in depth: whoever built the list, a refused name never reaches the session.
        names = names.Concat((propagated ?? []).Where(name => RefusalOf(name) is null && !IsBuiltIn(name)));
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
