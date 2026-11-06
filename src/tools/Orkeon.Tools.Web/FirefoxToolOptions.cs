using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Orkeon.Tools.Web;

/// <summary>
/// The `Orkeon:Tools:Firefox` section: how the `firefox` tool starts its browser and how much of a page
/// one answer carries. Read once when the tool is registered.
/// </summary>
public sealed class FirefoxToolOptions
{
    /// <summary>The configuration section the firefox tool reads.</summary>
    public const string SectionName = "Orkeon:Tools:Firefox";

    /// <summary>
    /// Path of the Firefox program, or its name on the PATH. Not set: `firefox` on the PATH, then the
    /// usual install folder of the platform.
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// `true` starts Firefox without a window. The default shows the window; under Linux with neither
    /// `DISPLAY` nor `WAYLAND_DISPLAY` the browser starts headless anyway, and a warning says so.
    /// </summary>
    public bool Headless { get; set; }

    /// <summary>
    /// Folder under which each browser gets its own profile folder, removed when the browser closes.
    /// Not set: `~/snap/firefox/common/orkeon/profiles` when Firefox is a snap (a snap cannot read the
    /// system's temp folder), else `orkeon-firefox` under the system's temp folder.
    /// </summary>
    public string? ProfileRoot { get; set; }

    /// <summary>
    /// The profile a browser starts with when the agent names none: `disposable` (the default, a fresh
    /// folder removed when the browser closes), the name of a profile of the Firefox installed on the
    /// host (`profiles.ini`), or the path of a profile folder. A named profile is used in place, with
    /// its cookies and sessions, and never removed; a profile another Firefox holds open is refused.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>
    /// The profiles an agent may name with the `profile` argument, beside `disposable` and `Profile`.
    /// Empty: an agent names no other profile. A name in `DeniedProfiles` stays refused.
    /// </summary>
    public Collection<string> AllowedProfiles { get; } = [];

    /// <summary>
    /// The profiles no browser ever starts with, whether an agent names one or `Profile` does: the
    /// operator's own profiles, for instance.
    /// </summary>
    public Collection<string> DeniedProfiles { get; } = [];

    /// <summary>
    /// `true` shares one browser per profile between the agent runs of the host: each run drives tabs
    /// of its own in it, and the browser closes when the last run leaves it. The default gives each
    /// run a browser of its own.
    /// </summary>
    public bool SharedInstance { get; set; }

    /// <summary>Seconds Firefox is given to start and open its remote protocol.</summary>
    public int StartupTimeoutSeconds { get; set; } = 30;

    /// <summary>Seconds one browser command (a page load, a script, a click) is given to answer.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Most nodes one `snapshot` or `find` answer lists; the rest is read with `offset`.</summary>
    public int MaxNodes { get; set; } = 40;

    /// <summary>Most characters of page text one `read` answer carries.</summary>
    public int MaxTextLength { get; set; } = 3000;

    /// <summary>
    /// The options of the section <see cref="SectionName"/>. A value that is absent, unreadable or not
    /// positive keeps its default.
    /// </summary>
    /// <param name="configuration">The host's configuration, or <see langword="null"/> when there is none.</param>
    public static FirefoxToolOptions From(IConfiguration? configuration)
    {
        var options = new FirefoxToolOptions();
        var section = configuration?.GetSection(SectionName);
        if (section is null)
            return options;

        options.ExecutablePath = Text(section[nameof(ExecutablePath)]);
        options.ProfileRoot = Text(section[nameof(ProfileRoot)]);
        options.Profile = Text(section[nameof(Profile)]);
        foreach (var name in Names(section.GetSection(nameof(AllowedProfiles))))
            options.AllowedProfiles.Add(name);
        foreach (var name in Names(section.GetSection(nameof(DeniedProfiles))))
            options.DeniedProfiles.Add(name);
        if (bool.TryParse(section[nameof(Headless)], out var headless))
            options.Headless = headless;
        if (bool.TryParse(section[nameof(SharedInstance)], out var shared))
            options.SharedInstance = shared;
        options.StartupTimeoutSeconds = Positive(section[nameof(StartupTimeoutSeconds)], options.StartupTimeoutSeconds);
        options.CommandTimeoutSeconds = Positive(section[nameof(CommandTimeoutSeconds)], options.CommandTimeoutSeconds);
        options.MaxNodes = Positive(section[nameof(MaxNodes)], options.MaxNodes);
        options.MaxTextLength = Positive(section[nameof(MaxTextLength)], options.MaxTextLength);
        return options;
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The names of a list section (`:0`, `:1`...), or of one value holding them separated by commas or semicolons.</summary>
    private static IEnumerable<string> Names(IConfigurationSection section)
    {
        var values = section.GetChildren().Select(child => child.Value).ToList();
        if (values.Count == 0 && section.Value is { } single)
            values = [.. single.Split([',', ';'])];
        return values.Select(Text).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static int Positive(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : fallback;
}
