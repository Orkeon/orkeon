using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>
/// What an AppContainer session leaves on the host until it ends: its profile, its SID and the paths
/// it put ACEs on, with the host process that owns it. Written before the first ACE, deleted once
/// everything is taken back; a ledger whose owner is gone is a session a crash interrupted, which
/// <see cref="AppContainerShellSandbox.SweepStaleSessions(string)"/> cleans up.
/// </summary>
internal sealed record ShellSessionLedger
{
    /// <summary>The AppContainer profile name.</summary>
    [JsonPropertyName("profile_name")]
    public required string ProfileName { get; init; }

    /// <summary>The AppContainer SID, in its <c>S-1-15-2-…</c> form.</summary>
    [JsonPropertyName("container_sid")]
    public required string ContainerSid { get; init; }

    /// <summary>The id of the host process that owns the session.</summary>
    [JsonPropertyName("host_pid")]
    public required int HostPid { get; init; }

    /// <summary>
    /// The start time of that process, in UTC ticks: a pid is reused, a pid and a start time are not.
    /// </summary>
    [JsonPropertyName("host_start_ticks")]
    public required long HostStartTicks { get; init; }

    /// <summary>The host paths that carry an ACE of the SID.</summary>
    [JsonPropertyName("granted_paths")]
    public ImmutableList<string> GrantedPaths { get; init; } = [];

    /// <summary>
    /// The host paths whose DACL the session protected from inheritance (<see cref="AclIsolation"/>),
    /// each with the DACL it had before, which the end of the session writes back.
    /// </summary>
    [JsonPropertyName("isolations")]
    public ImmutableList<ShellSessionIsolation> Isolations { get; init; } = [];

    /// <summary>The ledger as JSON.</summary>
    public string Serialize() => JsonSerializer.Serialize(this, ShellSessionLedgerJsonContext.Default.ShellSessionLedger);

    /// <summary>The ledger a JSON text holds, or null when it is not one (a truncated write).</summary>
    public static ShellSessionLedger? Deserialize(string json)
    {
        try
        {
            var ledger = JsonSerializer.Deserialize(json, ShellSessionLedgerJsonContext.Default.ShellSessionLedger);
            return ledger is not null
                   && ledger.ProfileName.StartsWith(AppContainerShellSandbox.ProfileNamePrefix, StringComparison.Ordinal)
                   && ledger.ContainerSid.StartsWith("S-1-15-2-", StringComparison.Ordinal)
                ? ledger
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the host that owns this ledger is gone: no process has its pid, or the process that
    /// has it started at another time.
    /// </summary>
    /// <param name="startTicksOf">The UTC start ticks of a live process, or null when there is none.</param>
    public bool IsStale(Func<int, long?> startTicksOf)
    {
        ArgumentNullException.ThrowIfNull(startTicksOf);
        return startTicksOf(HostPid) != HostStartTicks;
    }
}

/// <summary>A path a session isolated, and the DACL it had before.</summary>
internal sealed record ShellSessionIsolation
{
    /// <summary>The host path.</summary>
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    /// <summary>The DACL the path had before, as SDDL.</summary>
    [JsonPropertyName("original_dacl")]
    public required string OriginalDacl { get; init; }
}

/// <summary>The source-generated JSON contract of <see cref="ShellSessionLedger"/>.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ShellSessionLedger))]
internal sealed partial class ShellSessionLedgerJsonContext : JsonSerializerContext;
