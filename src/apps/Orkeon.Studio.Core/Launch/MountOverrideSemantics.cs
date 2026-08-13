using System.Globalization;

namespace Orkeon.Studio.Core.Launch;

/// <summary>Where an effective mount entry comes from.</summary>
public enum MountOrigin
{
    /// <summary>From the appsettings file's <c>Orkeon:FileSystem:Mounts</c> array.</summary>
    Settings,

    /// <summary>From a <c>--mount</c> argument, overriding the settings entry of the same index.</summary>
    CommandLine,
}

/// <summary>One entry of the mount list the runtime will actually see.</summary>
/// <param name="Index">Position in <c>Orkeon:FileSystem:Mounts</c>.</param>
/// <param name="Value">The mount string in force.</param>
/// <param name="Origin">Which of the two sources won.</param>
/// <param name="ReplacedSettingsMount">
/// The appsettings entry this overrides, when there was one at the same index.
/// </param>
public sealed record EffectiveMount(
    int Index,
    string Value,
    MountOrigin Origin,
    string? ReplacedSettingsMount = null)
{
    /// <summary>Configuration key this entry occupies.</summary>
    public string ConfigurationKey => MountOverrideSemantics.ConfigurationKey(Index);

    /// <summary>True when a settings entry was silently replaced by a command-line one.</summary>
    public bool OverridesSettings => Origin == MountOrigin.CommandLine && ReplacedSettingsMount is not null;
}

/// <summary>
/// What <c>--mount</c> really does at launch: the runner injects each argument as the
/// configuration override <c>Orkeon:FileSystem:Mounts:{i}</c>
/// (<c>src/hosting/Orkeon.Hosting/RunnerHost.cs:119-120</c>), so command-line mounts
/// <em>replace the appsettings entries of the same index</em> — they are not merged, and
/// appsettings entries past the last command-line index survive untouched. Studio states
/// this rather than letting a UI imply a merge that does not happen.
/// </summary>
public static class MountOverrideSemantics
{
    /// <summary>Configuration path of the mount array.</summary>
    public const string ConfigurationSection = "Orkeon:FileSystem:Mounts";

    /// <summary>Configuration path whitelisted by <c>--allow-external-mounts</c>.</summary>
    public const string ExternalMountsConfigurationSection = "PathSecurity:AdditionalAllowedDirectories";

    /// <summary>UI-ready statement of the override rule.</summary>
    public const string Explanation =
        "Each --mount argument is injected as 'Orkeon:FileSystem:Mounts:{index}', so it replaces " +
        "the appsettings mount at that same index; the two lists are not merged. Appsettings " +
        "entries beyond the last --mount index stay in force.";

    /// <summary>UI-ready statement of what <c>--allow-external-mounts</c> adds.</summary>
    public const string ExternalMountsExplanation =
        "--allow-external-mounts additionally whitelists each --mount base path under " +
        "'PathSecurity:AdditionalAllowedDirectories', letting mounts point outside the working " +
        "directory (same effect as ORKEON_ALLOW_EXTERNAL_MOUNTS=1).";

    /// <summary>Configuration key of the mount at the given index.</summary>
    public static string ConfigurationKey(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return string.Create(CultureInfo.InvariantCulture, $"{ConfigurationSection}:{index}");
    }

    /// <summary>
    /// The mount list the runtime will see for this launch: command-line entries at their
    /// own indices, appsettings entries everywhere else.
    /// </summary>
    /// <param name="commandLineMounts">The <c>--mount</c> arguments, in order.</param>
    /// <param name="settingsMounts">The appsettings <c>Orkeon:FileSystem:Mounts</c> array, in order.</param>
    public static IReadOnlyList<EffectiveMount> ComputeEffectiveMounts(
        IReadOnlyList<string> commandLineMounts,
        IReadOnlyList<string> settingsMounts)
    {
        ArgumentNullException.ThrowIfNull(commandLineMounts);
        ArgumentNullException.ThrowIfNull(settingsMounts);

        var count = Math.Max(commandLineMounts.Count, settingsMounts.Count);
        var effective = new List<EffectiveMount>(count);

        for (var index = 0; index < count; index++)
        {
            var fromSettings = index < settingsMounts.Count ? settingsMounts[index] : null;

            effective.Add(index < commandLineMounts.Count
                ? new EffectiveMount(index, commandLineMounts[index], MountOrigin.CommandLine, fromSettings)
                : new EffectiveMount(index, fromSettings!, MountOrigin.Settings));
        }

        return effective;
    }
}
