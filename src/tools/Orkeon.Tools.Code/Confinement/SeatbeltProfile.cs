using System.Text;

namespace Orkeon.Tools.Code.Confinement;

/// <summary>
/// The Seatbelt profile (SBPL, the language of <c>sandbox-exec -p</c>) that confines a shell session
/// on macOS to the VFS's mounts. Pure: it reads no file and resolves no link, so the paths it is
/// given must already be canonical (<c>/private/var/...</c>, not <c>/var/...</c>), since Seatbelt
/// matches the path a link resolves to.
/// </summary>
/// <remarks>
/// <para>
/// Everything is denied (<c>(deny default)</c>), then allowed piece by piece: running programs, the
/// system directories read-only, the metadata of every ancestor of an allowed path (so a lookup can
/// walk down to it), each mount read-only or read-write, and the network only when allowed.
/// </para>
/// <para>
/// In SBPL the LAST rule that matches an operation wins. The mounts are therefore written parents
/// first (by depth): a sub path's own rule comes after its mount's and overrides it. A read-only path
/// also denies writing, so a read-only sub path of a read-write mount stays read-only; a path the VFS
/// grants no right on is denied both, after the allow of the mount that holds it.
/// </para>
/// </remarks>
internal static class SeatbeltProfile
{
    /// <summary>
    /// The host directories every confined process reads: the programs and their libraries, the
    /// dynamic loader's shared cache (under <c>/System</c> on recent systems,
    /// <c>/private/var/db/dyld</c> on older ones), the frameworks and the developer tools
    /// (<c>/Library</c>), the system configuration, the shell <c>/bin/sh</c> picks
    /// (<c>/private/var/select</c>), the time zones and the devices.
    /// </summary>
    public static readonly IReadOnlyList<string> SystemPaths =
    [
        "/usr", "/bin", "/sbin", "/System", "/Library", "/private/etc",
        "/private/var/db/dyld", "/private/var/db/timezone", "/private/var/select", "/dev",
    ];

    /// <summary>The links at the root that a path naming the system's directories goes through.</summary>
    public static readonly IReadOnlyList<string> RootLinks = ["/etc", "/var", "/tmp"];

    /// <summary>
    /// The devices a confined process may write to; <c>/dev/fd</c> (where <c>/dev/stdout</c> and
    /// <c>/dev/stderr</c> lead) is added as a whole.
    /// </summary>
    public static readonly IReadOnlyList<string> WritableDevices =
        ["/dev/null", "/dev/zero", "/dev/tty", "/dev/dtracehelper"];

    /// <summary>
    /// The Mach services every confined process may look up, kept minimal: the user and group
    /// database (<c>getpwuid</c>, which the shell, <c>id</c>, <c>ls -l</c> and <c>git</c> call) and
    /// the notification center the C library's caches listen on.
    /// </summary>
    public static readonly IReadOnlyList<string> MachServices =
        ["com.apple.system.opendirectoryd.libinfo", "com.apple.system.notification_center"];

    /// <summary>
    /// The Mach services added when the network is allowed: name resolution, the network
    /// configuration it reads, and the certificate trust a TLS client asks for.
    /// </summary>
    public static readonly IReadOnlyList<string> NetworkMachServices =
    [
        "com.apple.dnssd.service", "com.apple.SystemConfiguration.configd",
        "com.apple.SystemConfiguration.DNSConfiguration", "com.apple.trustd.agent",
    ];

    /// <summary>The files added read-only when the network is allowed: the resolver's configuration.</summary>
    public static readonly IReadOnlyList<string> NetworkPaths = ["/private/var/run/resolv.conf"];

    /// <summary>The profile that confines a process to <paramref name="paths"/>.</summary>
    /// <param name="paths">
    /// The mounts and their sub paths, each at its canonical physical path (<see cref="ConfinedPath.Source"/>;
    /// the process sees the physical paths, so <see cref="ConfinedPath.Destination"/> is not used).
    /// </param>
    /// <param name="readOnlyPaths">Extra canonical host directories the process reads.</param>
    /// <param name="allowNetwork">Whether the process may reach the network.</param>
    /// <exception cref="ArgumentException">A path is not absolute or holds a NUL character.</exception>
    public static string Build(IEnumerable<ConfinedPath> paths, IEnumerable<string> readOnlyPaths, bool allowNetwork)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(readOnlyPaths);
        var mounts = paths.ToList();
        var extras = readOnlyPaths.Select(Normalize).Distinct(StringComparer.Ordinal).ToList();
        foreach (var mount in mounts)
            Normalize(mount.Source);

        var builder = new StringBuilder();
        builder.Append("(version 1)\n(deny default)\n");

        // Programs: the session forks and runs the commands; it may signal and inspect only the
        // processes of its own sandbox (and inspect only itself).
        builder.Append("(allow process-fork)\n(allow process-exec)\n");
        builder.Append("(allow signal (target same-sandbox))\n(allow process-info* (target self))\n");
        builder.Append("(allow sysctl-read)\n");
        AppendMachLookup(builder, allowNetwork ? MachServices.Concat(NetworkMachServices) : MachServices);
        builder.Append("(allow ipc-posix-shm-read-data (ipc-posix-name \"apple.shm.notification_center\"))\n");

        // The system, read-only.
        AppendRule(builder, "allow file-read*", SystemPaths.Concat(allowNetwork ? NetworkPaths : []).Select(Subpath));
        AppendRule(builder, "allow file-read*", RootLinks.Select(Literal));
        AppendRule(builder, "allow file-write*", WritableDevices.Select(Literal).Append(Subpath("/dev/fd")));

        // The metadata (not the content) of every directory above an allowed path, so the lookup of
        // a mount, of the working directory or of a parent (git looking for its repository) succeeds.
        var allowed = SystemPaths.Concat(extras).Concat(mounts.Where(m => !IsHidden(m.Access)).Select(m => Normalize(m.Source)));
        var ancestors = allowed.SelectMany(Ancestors).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        AppendRule(builder, "allow file-read-metadata", ancestors.Select(Literal));

        // The extra read-only directories the operator named.
        AppendRule(builder, "allow file-read*", extras.Select(Subpath));

        // The mounts, parents first: a later rule wins, so a sub path overrides its mount.
        foreach (var mount in mounts.OrderBy(m => Depth(Normalize(m.Source))).ThenBy(m => Normalize(m.Source), StringComparer.Ordinal))
            AppendMount(builder, mount);

        builder.Append(allowNetwork ? "(allow network*)\n" : "(deny network*)\n");
        return builder.ToString();
    }

    /// <summary>
    /// <paramref name="value"/> as an SBPL string literal: between double quotes, each backslash and
    /// double quote escaped by a backslash.
    /// </summary>
    /// <exception cref="ArgumentException">The value holds a NUL character.</exception>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("An SBPL string cannot hold a NUL character.", nameof(value));

        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static void AppendMount(StringBuilder builder, ConfinedPath mount)
    {
        var path = Normalize(mount.Source);
        switch (mount.Access)
        {
            case ConfinedAccess.ReadOnly:
                AppendRule(builder, "allow file-read*", [Subpath(path)]);
                AppendRule(builder, "deny file-write*", [Subpath(path)]);
                break;
            case ConfinedAccess.ReadWrite:
                AppendRule(builder, "allow file-read* file-write*", [Subpath(path)]);
                break;
            case ConfinedAccess.HiddenDirectory:
                AppendRule(builder, "deny file-read* file-write*", [Subpath(path)]);
                break;
            case ConfinedAccess.HiddenFile:
                AppendRule(builder, "deny file-read* file-write*", [Literal(path)]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mount), mount.Access, "Unknown confined access.");
        }
    }

    private static void AppendRule(StringBuilder builder, string operation, IEnumerable<string> filters)
    {
        var list = filters.ToList();
        if (list.Count == 0)
            return;
        builder.Append('(').Append(operation);
        foreach (var filter in list)
            builder.Append("\n  ").Append(filter);
        builder.Append(")\n");
    }

    private static void AppendMachLookup(StringBuilder builder, IEnumerable<string> services)
        => AppendRule(builder, "allow mach-lookup", services.Select(service => $"(global-name {Quote(service)})"));

    private static string Subpath(string path) => $"(subpath {Quote(path)})";

    private static string Literal(string path) => $"(literal {Quote(path)})";

    private static bool IsHidden(ConfinedAccess access)
        => access is ConfinedAccess.HiddenDirectory or ConfinedAccess.HiddenFile;

    /// <summary>The absolute path without a trailing slash; the root stays <c>/</c>.</summary>
    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path[0] != '/')
            throw new ArgumentException($"A confined path must be absolute: '{path}'.", nameof(path));
        if (path.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("A confined path cannot hold a NUL character.", nameof(path));

        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    /// <summary>Every directory above <paramref name="path"/>, the root included.</summary>
    private static IEnumerable<string> Ancestors(string path)
    {
        yield return "/";
        for (var index = path.IndexOf('/', 1); index > 0; index = path.IndexOf('/', index + 1))
            yield return path[..index];
    }

    private static int Depth(string path) => path == "/" ? 0 : path.Count(c => c == '/');
}
