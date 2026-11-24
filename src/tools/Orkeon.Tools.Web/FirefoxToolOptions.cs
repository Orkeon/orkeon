using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Orkeon.Application.Configuration;
using Orkeon.Tools.Web.Firefox;

namespace Orkeon.Tools.Web;

/// <summary>
/// The `Orkeon:Tools:Browser` section: which browser the `browser` tool drives, how it starts and how much
/// of a page one answer carries. The former `Orkeon:Tools:Firefox` section is read when it is absent. Read once when the tool is registered.
/// </summary>
public sealed class FirefoxToolOptions
{
    /// <summary>The configuration section the browser tool reads.</summary>
    public const string BrowserSectionName = "Orkeon:Tools:Browser";

    /// <summary>The former name of <see cref="BrowserSectionName"/>, read when that section is absent.</summary>
    public const string SectionName = "Orkeon:Tools:Firefox";

    /// <summary>The browser the host drives: `firefox` (the default), `chrome`, `edge`, `brave`, or `safari` on macOS. Case ignored; any other value is refused.</summary>
    public string Engine { get; set; } = FirefoxEngine.EngineId;

    /// <summary>The section the options were read from, which errors name.</summary>
    internal string Section { get; private set; } = BrowserSectionName;

    /// <summary>
    /// Path of the browser's program, or its name on the PATH. Not set: the engine's usual names on the
    /// PATH, then the usual install folder of the platform.
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// `true` starts the browser without a window. The default shows the window; under Linux with neither
    /// `DISPLAY` nor `WAYLAND_DISPLAY` the browser starts headless anyway, and a warning says so. Refused with `safari`, which has no such mode.
    /// </summary>
    public bool Headless { get; set; }

    /// <summary>
    /// Folder under which each browser gets its own profile folder, removed when the browser closes.
    /// Not set: `~/snap/&lt;snap&gt;/common/orkeon/profiles` when the browser is a snap (a snap cannot read the
    /// system's temp folder), else `orkeon-&lt;engine&gt;` under the system's temp folder.
    /// </summary>
    public string? ProfileRoot { get; set; }

    /// <summary>
    /// The profile a browser starts with when the agent names none: `disposable` (the default, a fresh
    /// folder removed when the browser closes), the name of a profile of the Firefox installed on the
    /// host (`profiles.ini`), or the path of a profile folder — the only form a Chromium engine
    /// takes, and never the user's own data folder. A named profile is used in place, with its cookies
    /// and sessions, and never removed; a profile another browser holds open is refused.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>
    /// The profiles an agent may name with the `profile` argument, beside `disposable` and `Profile`.
    /// Empty: an agent names no other profile. A name in `DeniedProfiles` stays refused.
    /// </summary>
    [CommaSeparatedSetting]
    public Collection<string> AllowedProfiles { get; } = [];

    /// <summary>
    /// The profiles no browser ever starts with, whether an agent names one or `Profile` does: the
    /// operator's own profiles, for instance.
    /// </summary>
    [CommaSeparatedSetting]
    public Collection<string> DeniedProfiles { get; } = [];

    /// <summary>
    /// `true` shares one browser per profile between the agent runs of the host: each run drives tabs
    /// of its own in it, and the browser closes when the last run leaves it, unless `Lifetime` is
    /// `Host`. The default gives each run a browser of its own.
    /// </summary>
    public bool SharedInstance { get; set; }

    /// <summary>
    /// How long a browser lives: `Run` (the default) closes it with the agent run that started it;
    /// `Host` keeps it open, with its tabs and its profile, from one run to the next, one browser per
    /// profile, until `close`, the user closes the window, or the host stops. Case ignored.
    /// </summary>
    public FirefoxBrowserLifetime Lifetime { get; set; }

    /// <summary>Seconds the browser is given to open its remote protocol; its session, which Firefox answers once its first window has loaded, then waits the larger of this and `CommandTimeoutSeconds`.</summary>
    public int StartupTimeoutSeconds { get; set; } = 30;

    /// <summary>Seconds one browser command (a page load, a script, a click) is given to answer.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Most nodes one `snapshot` or `find` answer lists; the rest is read with `offset`.</summary>
    public int MaxNodes { get; set; } = 40;

    /// <summary>Most characters of page text one `read` answer carries.</summary>
    public int MaxTextLength { get; set; } = 3000;

    /// <summary>
    /// The options of the section <see cref="SectionName"/>. A value that is absent, unreadable or not
    /// positive keeps its default, except a <see cref="Lifetime"/> that names no member, which is refused.
    /// </summary>
    /// <param name="configuration">The host's configuration, or <see langword="null"/> when there is none.</param>
    /// <exception cref="InvalidOperationException"><see cref="Lifetime"/> is not the name of a <see cref="FirefoxBrowserLifetime"/>.</exception>
    public static FirefoxToolOptions From(IConfiguration? configuration)
    {
        var options = new FirefoxToolOptions();
        if (configuration is null)
            return options;

        var section = configuration.GetSection(BrowserSectionName);
        var legacy = configuration.GetSection(SectionName);
        if (section.Exists() && legacy.Exists())
        {
            throw new InvalidOperationException(
                $"{BrowserSectionName} and {SectionName} are both set: keep {BrowserSectionName}, the other one is its former name.");
        }

        if (legacy.Exists())
        {
            // The former section knows one engine: no key of it picks another.
            section = legacy;
            options.Section = SectionName;
        }
        else
        {
            options.Engine = Text(section[nameof(Engine)]) ?? options.Engine;
        }

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
        options.Lifetime = ReadLifetime(section, options.Section, options.Lifetime);
        options.StartupTimeoutSeconds = Positive(section[nameof(StartupTimeoutSeconds)], options.StartupTimeoutSeconds);
        options.CommandTimeoutSeconds = Positive(section[nameof(CommandTimeoutSeconds)], options.CommandTimeoutSeconds);
        options.MaxNodes = Positive(section[nameof(MaxNodes)], options.MaxNodes);
        options.MaxTextLength = Positive(section[nameof(MaxTextLength)], options.MaxTextLength);
        return options;
    }

    /// <summary>
    /// The lifetime by its name only, any case (#464): <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>
    /// read <c>"1"</c> as <see cref="FirefoxBrowserLifetime.Host"/>, a browser kept across runs. Anything
    /// else is refused, naming the key and the members, never the value.
    /// </summary>
    private static FirefoxBrowserLifetime ReadLifetime(IConfigurationSection section, string sectionName, FirefoxBrowserLifetime fallback)
    {
        var value = Text(section[nameof(Lifetime)]);
        if (value is null)
            return fallback;

        var names = Enum.GetNames<FirefoxBrowserLifetime>();
        var name = names.FirstOrDefault(member => string.Equals(member, value, StringComparison.OrdinalIgnoreCase));
        return name is not null
            ? Enum.Parse<FirefoxBrowserLifetime>(name)
            : throw new InvalidOperationException(
                $"{sectionName}:{nameof(Lifetime)} is not a browser lifetime: write one of {string.Join(", ", names)}.");
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
