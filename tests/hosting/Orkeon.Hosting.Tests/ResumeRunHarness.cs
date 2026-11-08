using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Crew;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// Drives <c>orkeon run</c>'s one-shot runner (the real entry point) over a crew written to disk and
/// a checkpointing section that keeps its state on disk: a run is started, left failed, and resumed by
/// another call that loads the crew from its file again, as two processes would.
/// </summary>
internal sealed partial class ResumeRunHarness : IDisposable
{
    private const string Settings =
        """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Checkpointing": { "Enabled": true, "Store": "json" } } }""";

    private sealed class Options : RunnerOptionsBase { }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-resume-" + Guid.NewGuid().ToString("N"));

    /// <summary>A crew name no other test uses: the state folder is the process's own and shared.</summary>
    public string CrewName { get; } = "resume-" + Guid.NewGuid().ToString("N")[..12];

    public ResumeRunHarness() => Directory.CreateDirectory(_root);

    /// <summary>Where the runs' checkpoints are written: under the harness, never under the process's folder.</summary>
    public string CheckpointFolder => Path.Combine(_root, ".orkeon", "state", "checkpoints");

    /// <summary>The state store the runs write to, read directly.</summary>
    public JsonFileStateStore Store() => new(new DiskBackedFileSystemService(CheckpointFolder, "/checkpoints"), "/checkpoints");

    /// <summary>The crew key the host gives the crew of <paramref name="crewPath"/>.</summary>
    public string CrewKeyOf(string crewPath, string? name = null) =>
        RunnerRunIdentity.CrewKey(name ?? CrewName, "/crew/crew.yaml", crewPath);

    /// <summary>Writes a session as a killed process left it: a run of this crew still marked Running.</summary>
    public async Task<string> SeedRunningSessionAsync(string crewPath, CancellationToken ct)
    {
        var id = Orkeon.Application.Interfaces.Services.ExecutionId.New().ToString();
        var now = DateTime.UtcNow.AddMinutes(1);
        await Store().SaveAsync(new SessionState
        {
            SessionId = id,
            CrewKey = CrewKeyOf(crewPath),
            Phase = SessionPhase.Running,
            CreatedAt = now,
            UpdatedAt = now,
        }, ct);
        return id;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    /// <summary>Writes a three-task crew (first, second, third, each after the one before) in its own folder.</summary>
    public string WriteCrew(string folder, string process, string? name = null, string extra = "", string settings = Settings, string secondExtra = "")
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "appsettings.json"), settings);
        var manager = process == "hierarchical" ? "managerAgent: boss\n" : "";
        var bossAgent = process == "hierarchical"
            ? "  boss:\n    role: \"Boss\"\n    goal: \"Lead\"\n    backstory: \"The manager.\"\n    maxIter: 1\n"
            : "";
        var yaml = $"""
            name: "{name ?? CrewName}"
            goal: "A crew whose run is resumed"
            process: "{process}"
            {manager}{extra}
            agents:
            {bossAgent}  worker:
                role: "Worker"
                goal: "Work"
                backstory: "A minimal test agent."
                maxIter: 1
            tasks:
              first:
                description: "First."
                expectedOutput: "One."
                agent: "worker"
              second:
                description: "Second."
                expectedOutput: "Two."
                agent: "worker"
                dependencies: ["first"]
            {secondExtra}
              third:
                description: "Third."
                expectedOutput: "Three."
                agent: "worker"
                dependencies: ["second"]
            """;
        var path = Path.Combine(dir, "crew.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

#pragma warning disable CA1822 // an instance member: the harness is the one thing a test holds
    /// <summary>Runs the crew once through the runner, with <paramref name="service"/> as its agents' model.</summary>
    public async Task<(int Exit, string Stderr, string Stdout)> RunAsync(
        string crewPath, FakeAgentExecutionService service, string? resume = null, bool validate = false, FakeCrewExecutionHook? hook = null, CancellationToken ct = default)
    {
        var options = new Options { ConfigPath = crewPath, AllowExternalMounts = true, Resume = resume, Validate = validate, WorkingDirectory = _root };
        var origOut = Console.Out;
        var origErr = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await RunnerExecution.RunOneShotAsync(
                options,
                "Orkeon.Hosting.Tests",
                (_, services) =>
                {
                    services.AddScoped<IAgentExecutionService>(_ => service);
                    services.AddScoped<IManagerAgent>(_ => new StubAcceptingManagerAgent());
                    if (hook is not null)
                        services.AddScoped<ICrewExecutionHook>(_ => hook);
                },
                ct);
            return (exit, stderr.ToString(), stdout.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

#pragma warning restore CA1822

    /// <summary>The run id a failed run told its operator to resume.</summary>
    public static string ResumeIdIn(string stderr)
    {
        var match = ResumeHint().Match(stderr);
        Assert.True(match.Success, $"no '--resume <id>' hint in: {stderr}");
        return match.Groups[1].Value;
    }

    [GeneratedRegex(@"--resume ([0-9A-HJKMNP-TV-Z]{26})")]
    private static partial Regex ResumeHint();
}
