using System.Xml.Linq;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IForgeOsCommands"/>: the three schedulers the schedule verbs speak to,
/// simulated from the exact argument lists they send — a Task Scheduler library, a systemd user
/// manager, a crontab. Nothing is ever spawned; every invocation is recorded, so a test can prove
/// what was asked, and that nothing went through a shell. A command this double does not know is
/// answered with exit 99, which no adapter treats as success.
/// </summary>
internal sealed class FakeScheduleOs : IForgeOsCommands
{
    private readonly string _unitDirectory;

    /// <summary>A scheduler with nothing registered; <paramref name="unitDirectory"/> is where the systemd adapter copies its units.</summary>
    public FakeScheduleOs(string unitDirectory) => _unitDirectory = unitDirectory;

    /// <summary>One command as the adapter asked for it.</summary>
    /// <param name="FileName">The binary.</param>
    /// <param name="Arguments">Its argument list.</param>
    /// <param name="StandardInput">What was written on its stdin, when anything was.</param>
    public sealed record Invocation(string FileName, IReadOnlyList<string> Arguments, string? StandardInput);

    /// <summary>Every command, in order.</summary>
    public List<Invocation> Invocations { get; } = [];

    /// <summary>The Task Scheduler library: task name → the XML it was created from.</summary>
    public Dictionary<string, string> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tasks disabled by hand in the Task Scheduler.</summary>
    public HashSet<string> DisabledTasks { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The user timers systemd holds enabled.</summary>
    public HashSet<string> EnabledUnits { get; } = new(StringComparer.Ordinal);

    /// <summary>The user's crontab; null while the user has none.</summary>
    public string? Crontab { get; set; }

    /// <summary>Binaries that are not installed: starting one fails.</summary>
    public HashSet<string> MissingPrograms { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Commands the OS refuses, keyed <c>"&lt;binary&gt; &lt;first meaningful argument&gt;"</c> —
    /// <c>"schtasks /Create"</c>, <c>"systemctl enable"</c>, <c>"crontab -"</c> — with the message it
    /// prints on stderr.
    /// </summary>
    public Dictionary<string, string> Refusals { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ForgeOsCommandResult Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null)
    {
        Invocations.Add(new Invocation(fileName, [.. arguments], standardInput));

        if (MissingPrograms.Contains(fileName))
            return ForgeOsCommandResult.NotStarted($"'{fileName}' could not be started: No such file or directory");

        var key = $"{fileName} {Verb(fileName, arguments)}";
        if (Refusals.TryGetValue(key, out var refusal))
            return new ForgeOsCommandResult(true, 1, "", refusal);

        return fileName switch
        {
            WindowsTaskScheduleAdapter.Program => Schtasks(arguments),
            SystemdUserScheduleAdapter.Program => Systemctl(arguments),
            CronScheduleAdapter.Program => CrontabCommand(arguments, standardInput),
            _ => Unknown(),
        };
    }

    /// <summary>The first argument that names what the command does: <c>/Create</c>, <c>enable</c>, <c>-l</c>.</summary>
    private static string Verb(string fileName, IReadOnlyList<string> arguments)
    {
        var skipped = fileName == SystemdUserScheduleAdapter.Program && arguments.Count > 0 && arguments[0] == "--user" ? 1 : 0;
        return arguments.Count > skipped ? arguments[skipped] : "";
    }

    private ForgeOsCommandResult Schtasks(IReadOnlyList<string> arguments)
    {
        var name = ValueAfter(arguments, "/TN");
        switch (arguments.Count > 0 ? arguments[0] : null)
        {
            case "/Create" when name is not null && ValueAfter(arguments, "/XML") is { } xml && arguments.Contains("/F"):
                if (!File.Exists(xml))
                    return new ForgeOsCommandResult(true, 1, "", "ERROR: The system cannot find the file specified.");
                Tasks[name] = File.ReadAllText(xml);
                DisabledTasks.Remove(name);
                return Ok("SUCCESS: The scheduled task has successfully been created.");

            case "/Query" when name is not null && arguments.Contains("/XML"):
                if (!Tasks.TryGetValue(name, out var registered))
                    return new ForgeOsCommandResult(true, 1, "", "ERROR: The system cannot find the file specified.");
                return Ok(AsQueried(registered, DisabledTasks.Contains(name)));

            case "/Delete" when name is not null && arguments.Contains("/F"):
                if (!Tasks.Remove(name))
                    return new ForgeOsCommandResult(true, 1, "", "ERROR: The system cannot find the file specified.");
                DisabledTasks.Remove(name);
                return Ok("SUCCESS: The scheduled task was successfully deleted.");

            default:
                return Unknown();
        }
    }

    /// <summary>What <c>/Query /XML</c> prints: the definition, declared UTF-16, disabled when it was disabled by hand.</summary>
    private static string AsQueried(string registered, bool disabled)
    {
        var task = XDocument.Parse(registered);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        if (disabled)
        {
            var settings = task.Root!.Element(ns + "Settings");
            if (settings is null)
            {
                settings = new XElement(ns + "Settings");
                task.Root.Add(settings);
            }

            settings.SetElementValue(ns + "Enabled", "false");
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" + task.Root!.ToString();
    }

    private ForgeOsCommandResult Systemctl(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || arguments[0] != "--user")
            return Unknown();

        var rest = arguments.Skip(1).ToList();
        switch (rest.Count > 0 ? rest[0] : null)
        {
            case "daemon-reload" when rest.Count == 1:
                return Ok("");

            case "enable" when rest.Count == 3 && rest[1] == "--now":
                if (!File.Exists(Path.Combine(_unitDirectory, rest[2])))
                    return new ForgeOsCommandResult(true, 1, "", $"Failed to enable unit: Unit file {rest[2]} does not exist.");
                EnabledUnits.Add(rest[2]);
                return Ok($"Created symlink … {rest[2]}.");

            case "disable" when rest.Count == 3 && rest[1] == "--now":
                EnabledUnits.Remove(rest[2]);
                return Ok("");

            case "is-enabled" when rest.Count == 2:
                if (EnabledUnits.Contains(rest[1]))
                    return Ok("enabled\n");
                return File.Exists(Path.Combine(_unitDirectory, rest[1]))
                    ? new ForgeOsCommandResult(true, 1, "disabled\n", "")
                    : new ForgeOsCommandResult(true, 1, "", $"Failed to get unit file state for {rest[1]}: No such file or directory");

            default:
                return Unknown();
        }
    }

    private ForgeOsCommandResult CrontabCommand(IReadOnlyList<string> arguments, string? standardInput)
    {
        switch (arguments)
        {
            case ["-l"]:
                return Crontab is null
                    ? new ForgeOsCommandResult(true, 1, "", "crontab: no crontab for tester")
                    : Ok(Crontab);

            case ["-"] when standardInput is not null:
                Crontab = standardInput;
                return Ok("");

            default:
                return Unknown();
        }
    }

    private static string? ValueAfter(IReadOnlyList<string> arguments, string option)
    {
        for (var i = 0; i < arguments.Count - 1; i++)
        {
            if (arguments[i] == option)
                return arguments[i + 1];
        }

        return null;
    }

    private static ForgeOsCommandResult Ok(string output) => new(true, 0, output, "");

    private static ForgeOsCommandResult Unknown() => new(true, 99, "", "the fake OS does not know this command");
}
