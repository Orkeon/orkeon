using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Tests.Doubles;

/// <summary>
/// <c>orkeon usecases export</c>, scripted (STUDIO-41): it writes at <c>--to</c> the team folder
/// the CLI writes — <c>crew/config.yaml</c>, <c>data/</c>, <c>output/</c> and a
/// <c>studio-team.json</c> in the CLI's own shape — and answers with the <c>usecases.exported</c>
/// line; or, given a <see cref="Refusal"/>, answers with an <c>error</c> line and writes nothing.
/// Nothing is ever spawned.
/// </summary>
public sealed class FakeUseCaseExportLauncher : IProcessLauncher
{
    private static readonly JsonSerializerOptions SidecarOptions = new() { WriteIndented = true };

    /// <summary>Every request received, in call order.</summary>
    public List<ProcessLaunchRequest> Requests { get; } = [];

    /// <summary>The <c>--to</c> of every export asked for.</summary>
    public List<string> Destinations { get; } = [];

    /// <summary>The team's name the sidecar records.</summary>
    public string Name { get; set; } = "Daily email digest";

    /// <summary>The team's description the sidecar records.</summary>
    public string Description { get; set; } = "Every morning, read the emails that came in and write a short summary.";

    /// <summary>The manifest's mounts, team-relative.</summary>
    public IReadOnlyList<string> Mounts { get; set; } = ["./data:/data:ro", "./output:/output:rw"];

    /// <summary>When set, the export is refused with this code and message, and nothing is written.</summary>
    public (string Code, string Message)? Refusal { get; set; }

    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(
        ProcessLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);

        if (request.Arguments is not ["usecases", "export", var id, "--to", var destination, ..])
            return ProcessRunResult.FromExitCode(1, TimeSpan.Zero);

        Destinations.Add(destination);
        if (Refusal is { } refusal)
        {
            onOutput?.Invoke(Out(Line("error", new JsonObject
            {
                ["code"] = refusal.Code,
                ["message"] = refusal.Message,
                ["recoverable"] = false,
            })));
            return ProcessRunResult.FromExitCode(1, TimeSpan.Zero);
        }

        Directory.CreateDirectory(Path.Combine(destination, "crew"));
        await File.WriteAllTextAsync(
            Path.Combine(destination, "crew", "config.yaml"),
            "name: \"daily-mail-digest\"\nprocess: \"sequential\"\nagents:\n  reader:\n    role: \"Reader\"\ntasks:\n  digest:\n    agent: \"reader\"\n",
            cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(destination, "data"));
        await File.WriteAllTextAsync(Path.Combine(destination, "data", "inbox.eml"), "Subject: hello\n", cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(destination, "output"));
        await File.WriteAllTextAsync(
            Path.Combine(destination, "studio-team.json"),
            JsonSerializer.Serialize(
                new JsonObject
                {
                    ["name"] = Name,
                    ["description"] = Description,
                    ["mounts"] = new JsonArray([.. Mounts.Select(mount => (JsonNode?)mount)]),
                },
                SidecarOptions),
            cancellationToken).ConfigureAwait(false);

        onOutput?.Invoke(Out(Line("usecases.exported", new JsonObject
        {
            ["id"] = id,
            ["path"] = destination,
            ["name"] = Name,
            ["lang"] = "en",
            ["files"] = new JsonArray("crew/config.yaml", "data/inbox.eml", "studio-team.json"),
            ["mounts"] = new JsonArray([.. Mounts.Select(mount => (JsonNode?)mount)]),
        })));
        return ProcessRunResult.FromExitCode(0, TimeSpan.Zero);
    }

    private static string Line(string kind, JsonObject payload)
    {
        var line = new JsonObject
        {
            ["v"] = 2,
            ["seq"] = 1,
            ["ts"] = "2026-09-24T10:00:00Z",
            ["kind"] = kind,
        };
        foreach (var property in payload.ToList())
        {
            payload.Remove(property.Key);
            line[property.Key] = property.Value;
        }

        return line.ToJsonString();
    }

    private static ProcessOutputLine Out(string text) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, text);
}
