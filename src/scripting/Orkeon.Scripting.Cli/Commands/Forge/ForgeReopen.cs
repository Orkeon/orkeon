using Orkeon.Constants.FileSystem;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// <c>forge.json</c> — the machine-readable twin of <c>FORGE.md</c>, written by
/// <c>forge promote</c> next to the crew (FORGE-09). It carries what the crew files cannot:
/// the brief the team was built against, and the session's identity — its id above all, which
/// rule R reads to link the folder to its session (STUDIO-25). <c>forge reopen</c> reads it to
/// rebuild a faithful session; without it, the brief is derived from the plan. Nothing secret
/// goes in it — the folder is made to be shared.
/// </summary>
internal sealed record ForgeTeamRecord
{
    /// <summary>File name, next to <c>FORGE.md</c>.</summary>
    public const string FileName = "forge.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Contract version of the file.</summary>
    [JsonPropertyName("v")]
    public int Version { get; init; } = 1;

    /// <summary>
    /// The id of the session the folder is linked to (STUDIO-25): copied by the promotion,
    /// rewritten by a rebuild. Kept as the text the file holds — a record hand-edited into an
    /// id that does not parse stays readable, brief and all, and simply links to nothing.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary><see cref="Id"/>, parsed; null when absent or not an id.</summary>
    [JsonIgnore]
    public Guid? SessionId => Guid.TryParse(Id, out var id) ? id : null;

    /// <summary>Slug of the session the folder is linked to — a rebuild asks for the same one.</summary>
    [JsonPropertyName("slug")]
    public string? Slug { get; init; }

    /// <summary>Display name of the team, as the session titled it.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Render format of the promoted crew (<c>yaml</c> or <c>script</c>).</summary>
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    /// <summary>When the folder was promoted (UTC, ISO-8601).</summary>
    [JsonPropertyName("promotedAt")]
    public string? PromotedAt { get; init; }

    /// <summary>The brief the team was built against, when the session held one.</summary>
    [JsonPropertyName("brief")]
    public ForgeBrief? Brief { get; init; }

    /// <summary>
    /// The use case the team was composed from, titled in the brief's language (STUDIO-40, D-04);
    /// a rebuilt session takes it back. Null for a team composed from nothing.
    /// </summary>
    [JsonPropertyName("reference")]
    public ForgeReferenceRecord? Reference { get; init; }

    /// <summary>Writes the record into <paramref name="destination"/> (overwriting a previous promotion's).</summary>
    public static void Write(string destination, ForgeSession session, ForgeBrief? brief, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(session);

        Save(destination, new ForgeTeamRecord
        {
            Id = session.Document.Id?.ToString(),
            Slug = session.Document.Slug,
            Title = session.Document.Title,
            Format = session.Document.Format,
            PromotedAt = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            Brief = brief,
            Reference = session.Document.Reference,
        });
    }

    /// <summary>
    /// Links <paramref name="teamDirectory"/> to the session a rebuild just gave it (STUDIO-25):
    /// the record takes that session's id and slug and keeps everything else the promotion
    /// recorded. A folder without a readable record gets one holding the session's identity
    /// alone — no brief, so a later rebuild still says its brief was derived, never recorded.
    /// </summary>
    public static void Relink(string teamDirectory, ForgeSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);
        ArgumentNullException.ThrowIfNull(session);

        var id = session.Document.Id?.ToString();
        Save(teamDirectory, TryRead(teamDirectory) is { } recorded
            ? recorded with { Id = id, Slug = session.Document.Slug }
            : new ForgeTeamRecord
            {
                Id = id,
                Slug = session.Document.Slug,
                Title = session.Document.Title,
                Format = session.Document.Format,
            });
    }

    /// <summary>
    /// The id of the session <paramref name="teamDirectory"/>'s record names — rule R's reader.
    /// Null when the folder, its record or the id is absent or unreadable; never a throw.
    /// </summary>
    public static Guid? ReadSessionId(string teamDirectory) => TryRead(teamDirectory)?.SessionId;

    private static void Save(string teamDirectory, ForgeTeamRecord record) =>
        File.WriteAllText(
            Path.Combine(teamDirectory, FileName),
            JsonSerializer.Serialize(record, SerializerOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>Reads the record of <paramref name="teamDirectory"/>; null when absent or unreadable — never fatal.</summary>
    public static ForgeTeamRecord? TryRead(string teamDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        var path = Path.Combine(teamDirectory, FileName);
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ForgeTeamRecord>(File.ReadAllText(path), SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Reads a team's <c>crew/</c> back into the blueprint it describes — the inverse of
/// <see cref="ForgeBlueprintCompiler.Compile"/> + <see cref="ForgeYamlRenderer.Render"/>,
/// over the very DTOs the loader deserializes. Both layouts the loader accepts are read: the
/// per-entity one the forge renders (<c>config.yaml</c> + <c>agents/</c> + <c>tasks/</c>) and
/// the single-file one (<c>agents:</c>/<c>tasks:</c> inline). What the blueprint has no
/// field for — guardrails, per-agent LLM overrides, a hand-written <c>mounts:</c> — is not
/// carried, exactly as a re-render would not carry it; the caller says so.
/// </summary>
internal static class ForgeTeamReader
{
    /// <summary>The extensions a crew definition file may carry.</summary>
    private static readonly string[] YamlExtensions = [".yaml", ".yml"];

    /// <summary>
    /// Reads the plan under <paramref name="crewDirectory"/>. False with the reasons when
    /// the folder holds no readable YAML crew, or when what it holds is not a structurally
    /// valid plan (<see cref="ForgeBlueprint.Validate"/>) — the caller lists them, the user
    /// fixes the files. Never an exception for a malformed file.
    /// </summary>
    public static bool TryRead(string crewDirectory, out ForgeBlueprint? blueprint, out IReadOnlyList<string> errors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(crewDirectory);
        blueprint = null;

        if (!Directory.Exists(crewDirectory))
        {
            errors = [$"'{crewDirectory}' does not exist."];
            return false;
        }

        if (File.Exists(Path.Combine(crewDirectory, ForgeScriptRenderer.ScriptFileName)))
        {
            errors =
            [
                $"'{crewDirectory}' holds a script crew ({ForgeScriptRenderer.ScriptFileName}), which cannot be read back into a plan"
                + " — only a YAML crew can be reopened.",
            ];
            return false;
        }

        var settingsPath = new[] { ConventionalNames.CrewSettingsFile, ConventionalNames.CrewSettingsFallbackFile }
            .Select(name => Path.Combine(crewDirectory, name))
            .FirstOrDefault(File.Exists);
        if (settingsPath is null)
        {
            errors =
            [
                $"'{crewDirectory}' holds no {ConventionalNames.CrewSettingsFile}"
                + $" (nor {ConventionalNames.CrewSettingsFallbackFile}).",
            ];
            return false;
        }

        var serializer = new YamlDotNetSerializer();
        var problems = new List<string>();
        var crew = Read<CrewYamlConfig>(serializer, settingsPath, problems) ?? new CrewYamlConfig();

        // Inline entries first (single-file layout), then one file per entity; an entity
        // spelled in both places keeps the inline one — the loader reads them the same way.
        var agents = new Dictionary<string, AgentYamlConfig>(StringComparer.Ordinal);
        foreach (var (key, agent) in crew.Agents ?? [])
            agents[key] = agent;
        foreach (var (key, path) in EntityFiles(Path.Combine(crewDirectory, "agents")))
        {
            if (!agents.ContainsKey(key) && Read<AgentYamlConfig>(serializer, path, problems) is { } agent)
                agents[key] = agent;
        }

        var tasks = new Dictionary<string, TaskYamlConfig>(StringComparer.Ordinal);
        foreach (var (key, task) in crew.Tasks ?? [])
            tasks[key] = task;
        foreach (var (key, path) in EntityFiles(Path.Combine(crewDirectory, "tasks")))
        {
            if (!tasks.ContainsKey(key) && Read<TaskYamlConfig>(serializer, path, problems) is { } task)
                tasks[key] = task;
        }

        if (problems.Count > 0)
        {
            errors = problems;
            return false;
        }

        var candidate = new ForgeBlueprint
        {
            Crew = new ForgeBlueprintCrew
            {
                Name = crew.Name ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(crewDirectory) ?? crewDirectory)),
                Goal = crew.Goal,
                Process = crew.Process,
                Verbose = crew.Verbose,
                Memory = crew.Memory,
            },
            Agents = agents.Select(pair => new ForgeBlueprintAgent
            {
                Key = pair.Key,
                Role = pair.Value.Role,
                Goal = pair.Value.Goal,
                Backstory = pair.Value.Backstory,
                Tools = pair.Value.Tools is { Count: > 0 } tools ? [.. tools] : null,
                AllowDelegation = pair.Value.AllowDelegation,
                MaxIterations = pair.Value.MaxIter,
            }).ToList(),
            Tasks = tasks.Select(pair => new ForgeBlueprintTask
            {
                Key = pair.Key,
                Description = pair.Value.Description,
                ExpectedOutput = pair.Value.ExpectedOutput,
                Agent = pair.Value.Agent,
                Dependencies = pair.Value.Dependencies is { Count: > 0 } deps ? [.. deps] : null,
                Deliverable = pair.Value.Deliverable?.Path,
            }).ToList(),
            Manager = crew.ManagerAgent,
            Rationale = "Plan read back from the team's crew files: what they describe, not what an assistant proposed.",
        };

        var structural = candidate.Validate();
        if (structural.Count > 0)
        {
            errors = [.. structural.Select(error => $"{Path.GetFileName(settingsPath)}: {error}")];
            return false;
        }

        blueprint = candidate;
        errors = [];
        return true;
    }

    /// <summary>The entity files of <paramref name="directory"/>, key = file stem, in a stable order.</summary>
    private static IEnumerable<(string Key, string Path)> EntityFiles(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;

        var files = Directory.EnumerateFiles(directory)
            .Where(path => YamlExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal)
            .ThenBy(path => path, StringComparer.Ordinal);

        foreach (var path in files)
            yield return (Path.GetFileNameWithoutExtension(path), path);
    }

    /// <summary>One YAML file as <typeparamref name="T"/>; a malformed one becomes a problem line, never a throw.</summary>
    private static T? Read<T>(YamlDotNetSerializer serializer, string path, List<string> problems)
        where T : class
    {
        try
        {
            var text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text) ? null : serializer.Deserialize<T>(text);
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            problems.Add($"'{path}' could not be read as a crew file: {ex.Message}");
            return null;
        }
    }
}

/// <summary>Where the rebuilt session's brief came from — said on the wire and in the terminal.</summary>
internal enum ForgeBriefSource
{
    /// <summary>The brief recorded by the promotion (<c>forge.json</c>), verbatim.</summary>
    Recorded,

    /// <summary>No record: a minimal brief derived from the plan itself.</summary>
    Derived,
}

/// <summary>What a rebuild produced.</summary>
internal sealed record ForgeRebuildResult(ForgeSession Session, ForgeBriefSource BriefSource);

/// <summary>
/// Rebuilds a forge session from a promoted team folder (FORGE-09): the plan read back from
/// <c>crew/</c>, the brief from <c>forge.json</c> when the promotion left one, derived from
/// the plan otherwise; the crew copied as it is, so what the session tries is what the folder
/// runs. The session lands at the dry pause — Test, Active, nothing run — with
/// <c>promotedTo</c> pointing back at the folder, and the folder's <c>forge.json</c> carrying
/// the new session's id (STUDIO-25), so the next reopen finds it and a later promotion
/// updates the folder in place.
/// </summary>
internal static class ForgeSessionRebuilder
{
    /// <summary>
    /// Rebuilds a session for <paramref name="teamDirectory"/> under <paramref name="workspace"/>.
    /// Throws <see cref="InvalidOperationException"/> with the reasons when the folder holds no
    /// plan the forge can read — the command maps it to exit 1.
    /// </summary>
    /// <param name="workspace">The workspace the session is created under.</param>
    /// <param name="teamDirectory">The team folder to rebuild a session for.</param>
    /// <param name="now">The instant stamped on the session.</param>
    /// <param name="isCopy">
    /// Whether rule R found the folder to be a copy: its record names its original's session,
    /// which still holds that slug, so the new session is named after the copy's own folder.
    /// </param>
    public static ForgeRebuildResult Rebuild(string workspace, string teamDirectory, DateTimeOffset now, bool isCopy = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamDirectory);

        var crewDirectory = Path.Combine(teamDirectory, ForgeYamlRenderer.CrewDirectoryName);
        if (!ForgeTeamReader.TryRead(crewDirectory, out var blueprint, out var errors))
        {
            throw new InvalidOperationException(
                $"'{teamDirectory}' holds no crew the forge can read back into a plan:"
                + string.Concat(errors.Select(error => $"\n  - {error}")));
        }

        var record = ForgeTeamRecord.TryRead(teamDirectory);
        var briefSource = record?.Brief is { } recorded && recorded.Validate().Count == 0
            ? ForgeBriefSource.Recorded
            : ForgeBriefSource.Derived;
        var brief = briefSource == ForgeBriefSource.Recorded ? record!.Brief! : DeriveBrief(blueprint!);

        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(teamDirectory));
        var session = ForgeSession.Create(
            workspace,
            requestedSlug: !isCopy && record?.Slug is { Length: > 0 } slug ? slug : folderName,
            format: ForgeSession.FormatYaml,
            now: now);

        // Verbatim, not re-rendered: a hand edit in the folder is what the team runs today,
        // and the trial must try that. The first amendment re-renders from the plan — and
        // loses what the plan has no field for, as every amendment always did.
        ForgePromoter.CopyDirectory(crewDirectory, Path.Combine(session.Directory, ForgeYamlRenderer.CrewDirectoryName));

        session.SaveArtifact(ForgeSession.BriefFileName, brief);
        session.SaveArtifact(ForgeSession.BlueprintFileName, blueprint!);
        session.Document.Title = record?.Title is { Length: > 0 } title ? title : blueprint!.Crew?.Name;
        // The use case the team was composed from (STUDIO-40): a reopen keeps it, like a resume.
        session.Document.Reference = record?.Reference;
        session.Document.PromotedTo = Path.GetFullPath(teamDirectory);
        session.AppendHistory(ForgeState.Brief, ForgeTrigger.Rebuilt, ForgeState.Test, now);
        session.SetState(ForgeState.Test);
        session.SetStatus(ForgeSessionStatus.Active);
        session.Save(now);

        // The folder takes the new session's id — a copy's rewritten (D-05), a gone session's
        // replaced, a record written where there was none — so the next reopen finds this
        // session rather than rebuilding another. Last, once the session is whole: a record
        // naming a half-built session would be found, and resumed.
        try
        {
            ForgeTeamRecord.Relink(teamDirectory, session);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A session no folder names would be rebuilt again at every reopen; none is left
            // behind instead, and the refusal is the command's to report.
            DeleteQuietly(session.Directory);
            throw;
        }

        return new ForgeRebuildResult(session, briefSource);
    }

    private static void DeleteQuietly(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the error being reported is the record the folder refused.
        }
    }

    /// <summary>
    /// The smallest brief the cycle accepts, from the plan alone: the crew's goal, one
    /// blocking criterion on it, one advisory criterion per task with an expected output.
    /// The diagnosis judges against these — coarse, but honest about where they come from.
    /// </summary>
    private static ForgeBrief DeriveBrief(ForgeBlueprint blueprint)
    {
        var acceptance = new List<ForgeAcceptanceCriterion>
        {
            new()
            {
                Id = "A1",
                Statement = $"The crew accomplishes its goal: {blueprint.Crew?.Goal}",
                Kind = "must",
            },
        };

        var index = 2;
        foreach (var task in blueprint.Tasks ?? [])
        {
            if (string.IsNullOrWhiteSpace(task.ExpectedOutput))
                continue;

            acceptance.Add(new ForgeAcceptanceCriterion
            {
                Id = string.Create(CultureInfo.InvariantCulture, $"A{index++}"),
                Statement = $"Task '{task.Key}' delivers: {task.ExpectedOutput}",
                Kind = "should",
            });
        }

        var lastOutput = (blueprint.Tasks ?? []).LastOrDefault(task => !string.IsNullOrWhiteSpace(task.ExpectedOutput));
        return new ForgeBrief
        {
            Goal = blueprint.Crew?.Goal,
            Context = "Brief derived from the team's crew files by `forge reopen`: no interview recorded this team's need.",
            ExpectedOutput = lastOutput is null ? null : new ForgeExpectedOutput { Description = lastOutput.ExpectedOutput },
            Acceptance = acceptance,
        };
    }
}
