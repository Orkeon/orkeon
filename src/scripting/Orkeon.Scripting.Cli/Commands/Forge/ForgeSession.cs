using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>Lifecycle of a session, orthogonal to the stage the cycle is in.</summary>
internal enum ForgeSessionStatus
{
    /// <summary>The cycle is running or waiting for the user.</summary>
    Active,

    /// <summary>A budget dimension ran out; the session is resumable with a raised budget.</summary>
    BudgetExhausted,

    /// <summary>A conforming crew is waiting to be promoted.</summary>
    Ready,

    /// <summary>Promoted out of the session directory.</summary>
    Promoted,

    /// <summary>The user stopped the cycle.</summary>
    Abandoned,

    /// <summary>An unrecoverable engine error; <see cref="ForgeSessionDocument.Error"/> says why.</summary>
    Failed,
}

/// <summary>The persisted shape of <c>session.json</c>. Versioned like the event protocol.</summary>
internal sealed record ForgeSessionDocument
{
    /// <summary>Contract version of the file.</summary>
    [JsonPropertyName("v")]
    public int Version { get; init; } = 1;

    /// <summary>
    /// The session's stable id (STUDIO-25): created with it, never changed — a rebuilt session
    /// is a new session and gets a new one. The promotion copies it into the team's
    /// <c>forge.json</c>, and rule R (<see cref="TeamSessionLink"/>) links a team folder to the
    /// session carrying its id. Null for a file written before the id existed: such a session is
    /// linked to no folder (D-06).
    /// </summary>
    [JsonPropertyName("id")]
    public Guid? Id { get; init; }

    /// <summary>Directory name of the session under <c>.orkeon/forge/</c>.</summary>
    [JsonPropertyName("slug")]
    public required string Slug { get; init; }

    /// <summary>Display name, derived from the brief's goal once there is one.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Requested output: <c>yaml</c> (default) or <c>script</c>.</summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "yaml";

    /// <summary>The stage the cycle is in, as <see cref="ForgeState"/> spells it.</summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = nameof(ForgeState.Brief);

    /// <summary>The session lifecycle, as <see cref="ForgeSessionStatus"/> spells it.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = nameof(ForgeSessionStatus.Active);

    /// <summary>Refine cycle currently underway, 1-based.</summary>
    [JsonPropertyName("iteration")]
    public int Iteration { get; set; } = 1;

    /// <summary>Repair attempts consumed on the current render (SPEC §8.4: two, then surface).</summary>
    [JsonPropertyName("repairAttempts")]
    public int RepairAttempts { get; set; }

    /// <summary>
    /// Bounds and consumption, carried across resumes. Settable because raising the budget
    /// is precisely the gesture a resume of a budget-exhausted session exists for — the
    /// replacement must carry the consumed counters over (see <see cref="ForgeBudget"/>).
    /// </summary>
    [JsonPropertyName("budget")]
    public ForgeBudget Budget { get; set; } = new();

    /// <summary>Why the session failed, when it did.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Absolute destination of the promotion, once promoted — what a client's
    /// "relaunch this solution" points at (the promoted folder is the ordinary artifact;
    /// the session only remembers where it went).
    /// </summary>
    [JsonPropertyName("promotedTo")]
    public string? PromotedTo { get; set; }

    /// <summary>Creation instant (UTC, ISO-8601).</summary>
    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    /// <summary>Last save instant (UTC, ISO-8601).</summary>
    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; set; } = "";
}

/// <summary>One turn of the persisted conversation.</summary>
internal sealed record ForgeTranscriptTurn
{
    /// <summary>UTC instant, ISO-8601.</summary>
    [JsonPropertyName("ts")]
    public string Timestamp { get; init; } = "";

    /// <summary><c>user</c> or <c>assistant</c>.</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = "";

    /// <summary>The turn's text.</summary>
    [JsonPropertyName("text")]
    public string Text { get; init; } = "";
}

/// <summary>One line of <c>forge list</c>.</summary>
internal sealed record ForgeSessionSummary(
    string Slug,
    string? Title,
    string State,
    string Status,
    string Format,
    string UpdatedAt);

/// <summary>
/// A forge session on disk: <c>&lt;workspace&gt;/.orkeon/forge/&lt;slug&gt;/</c> holding
/// <c>session.json</c> (state, budget, format) and <c>history.jsonl</c> (append-only, one
/// line per transition). Resumable, diffable between iterations, auditable — and the very
/// object promotion takes as input (SPEC-ORKEON-FORGE §4.1).
/// </summary>
internal sealed class ForgeSession
{
    /// <summary>Session root, relative to the workspace.</summary>
    public const string RootDirectoryName = ".orkeon/forge";

    /// <summary>The YAML render, the default format.</summary>
    public const string FormatYaml = "yaml";

    /// <summary>The <c>.ork.ts</c> render (SPEC §8.2), opt-in via <c>--format script</c>.</summary>
    public const string FormatScript = "script";

    /// <summary>Whether <paramref name="format"/> is the script render.</summary>
    public static bool IsScriptFormat(string? format) =>
        string.Equals(format, FormatScript, StringComparison.OrdinalIgnoreCase);

    /// <summary>File holding <see cref="ForgeSessionDocument"/>.</summary>
    public const string SessionFileName = "session.json";

    /// <summary>Append-only transition log.</summary>
    public const string HistoryFileName = "history.jsonl";

    /// <summary>The structured brief, once the interview produced one.</summary>
    public const string BriefFileName = "brief.json";

    /// <summary>The latest blueprint.</summary>
    public const string BlueprintFileName = "blueprint.json";

    /// <summary>The pending validation errors a repair must address.</summary>
    public const string RepairFileName = "repair.json";

    /// <summary>Append-only conversation transcript — what makes a resumed interview remember.</summary>
    public const string TranscriptFileName = "transcript.jsonl";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private ForgeSession(string directory, ForgeSessionDocument document)
    {
        Directory = directory;
        Document = document;
    }

    /// <summary>Absolute directory of the session.</summary>
    public string Directory { get; }

    /// <summary>The persisted state.</summary>
    public ForgeSessionDocument Document { get; }

    /// <summary>The stage the cycle is in, parsed back from the document.</summary>
    public ForgeState State =>
        Enum.TryParse<ForgeState>(Document.State, out var state) ? state : ForgeState.Failed;

    /// <summary>The session lifecycle, parsed back from the document.</summary>
    public ForgeSessionStatus Status =>
        Enum.TryParse<ForgeSessionStatus>(Document.Status, out var status) ? status : ForgeSessionStatus.Failed;

    /// <summary>What rule R reads of this session: its id and its <c>promotedTo</c>; null without an id (D-06).</summary>
    public SessionPromotion? Promotion =>
        Document.Id is { } id ? new SessionPromotion(id, Document.PromotedTo) : null;

    /// <summary>The session root for <paramref name="workspaceDirectory"/>.</summary>
    public static string RootFor(string workspaceDirectory) =>
        Path.Combine(workspaceDirectory, ConventionalNames.StateDirectory, "forge");

    /// <summary>
    /// Creates a fresh session directory. A slug collision gets a numeric suffix rather
    /// than an error: two sessions about the same problem are an ordinary situation.
    /// </summary>
    public static ForgeSession Create(
        string workspaceDirectory,
        string? requestedSlug = null,
        string format = "yaml",
        ForgeBudget? budget = null,
        DateTimeOffset? now = null)
    {
        var root = RootFor(workspaceDirectory);
        var stamp = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
        // The folder rule Studio's teams follow too, with the session's own fallback: a need
        // that keeps no usable character names its session after the instant it was opened.
        var baseSlug = FolderSlug.From(requestedSlug) ?? stamp.ToString("'forge-'yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        var slug = baseSlug;
        var suffix = 2;
        while (System.IO.Directory.Exists(Path.Combine(root, slug)))
            slug = string.Create(CultureInfo.InvariantCulture, $"{baseSlug}-{suffix++}");

        var directory = Path.Combine(root, slug);
        System.IO.Directory.CreateDirectory(directory);

        var createdAt = FormatInstant(stamp);
        var document = new ForgeSessionDocument
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Format = format,
            Budget = budget ?? new ForgeBudget(),
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

        var session = new ForgeSession(directory, document);
        session.Save(now);
        return session;
    }

    /// <summary>
    /// Loads a session directory. A missing or corrupt <c>session.json</c> comes back as an
    /// error message, never an exception: a broken session must not block <c>forge list</c>.
    /// </summary>
    public static bool TryLoad(string directory, out ForgeSession? session, out string? error)
    {
        session = null;
        var path = Path.Combine(directory, SessionFileName);
        if (!File.Exists(path))
        {
            error = $"'{directory}' holds no {SessionFileName}.";
            return false;
        }

        try
        {
            var document = JsonSerializer.Deserialize<ForgeSessionDocument>(File.ReadAllText(path), SerializerOptions);
            if (document is null || string.IsNullOrWhiteSpace(document.Slug))
            {
                error = $"'{path}' is not a forge session file.";
                return false;
            }

            session = new ForgeSession(directory, document);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"'{path}' is unreadable: {ex.Message}";
            return false;
        }
    }

    /// <summary>Loads a session by slug under <paramref name="workspaceDirectory"/>.</summary>
    public static bool TryLoadBySlug(
        string workspaceDirectory, string slug, out ForgeSession? session, out string? error)
    {
        var directory = Path.Combine(RootFor(workspaceDirectory), slug);
        if (System.IO.Directory.Exists(directory))
            return TryLoad(directory, out session, out error);

        session = null;
        error = $"No forge session named '{slug}' under {RootFor(workspaceDirectory)}.";
        return false;
    }

    /// <summary>
    /// The session of <paramref name="workspaceDirectory"/> whose id is <paramref name="id"/>, or
    /// null. Two sessions only share an id when a session directory was copied by hand; the most
    /// recently touched one answers then — the order Studio's catalog lists them in — so both
    /// sides name the same one. Corrupt sessions are skipped, like <see cref="List"/>.
    /// </summary>
    public static ForgeSession? FindById(string workspaceDirectory, Guid id)
    {
        var root = RootFor(workspaceDirectory);
        if (!System.IO.Directory.Exists(root))
            return null;

        var matches = new List<ForgeSession>();
        foreach (var directory in System.IO.Directory.EnumerateDirectories(root))
        {
            if (TryLoad(directory, out var session, out _) && session!.Document.Id == id)
                matches.Add(session);
        }

        return matches
            .OrderByDescending(s => s.Document.UpdatedAt, StringComparer.Ordinal)
            .ThenBy(s => s.Document.Slug, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>The sessions of a workspace, most recently touched first. Corrupt ones are skipped.</summary>
    public static IReadOnlyList<ForgeSessionSummary> List(string workspaceDirectory)
    {
        var root = RootFor(workspaceDirectory);
        if (!System.IO.Directory.Exists(root))
            return [];

        var summaries = new List<ForgeSessionSummary>();
        foreach (var directory in System.IO.Directory.EnumerateDirectories(root))
        {
            if (!TryLoad(directory, out var session, out _) || session is null)
                continue;

            var document = session.Document;
            summaries.Add(new ForgeSessionSummary(
                document.Slug, document.Title, document.State, document.Status, document.Format, document.UpdatedAt));
        }

        return [.. summaries.OrderByDescending(s => s.UpdatedAt, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The folder name this session takes to follow a team folder named
    /// <paramref name="teamFolderName"/> (STUDIO-26): that name as it is, or suffixed <c>-2</c>,
    /// <c>-3</c>… past any other entry of the session root — never over one (D-04). Null when the
    /// session already stands there: its own folder is no collision, so a re-adoption finds
    /// nothing to move. Compared the way the platform compares paths, so a name differing only in
    /// case is this very folder on Windows.
    /// </summary>
    public string? FolderNameAfter(string teamFolderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamFolderName);

        var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Directory))!;
        var self = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Directory));
        var candidate = teamFolderName;
        for (var suffix = 2; ; suffix++)
        {
            var path = Path.GetFullPath(Path.Combine(root, candidate));
            if (string.Equals(path, self, PhysicalPathContainment.Comparison))
                return null;
            if (!Path.Exists(path))
                return candidate;

            candidate = string.Create(CultureInfo.InvariantCulture, $"{teamFolderName}-{suffix}");
        }
    }

    /// <summary>
    /// Moves the session folder to <paramref name="slug"/> under the same root and returns the
    /// session as it stands there: reloaded from its new folder, <c>session.json</c> rewritten
    /// under the new slug. <see cref="Directory"/> is fixed for the life of an instance, so the
    /// moved session is a new one — this one keeps naming the old folder and is not to be
    /// written again. Throws what the disk throws; a rewrite refused after the move puts the
    /// folder back first, so a failure never leaves a folder and its <c>session.json</c>
    /// disagreeing on the session's name.
    /// </summary>
    public ForgeSession MoveTo(string slug, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var target = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Directory))!, slug);
        System.IO.Directory.Move(Directory, target);
        try
        {
            if (!TryLoad(target, out var reloaded, out var error))
                throw new IOException(error);

            var moved = new ForgeSession(target, reloaded!.Document with { Slug = slug });
            moved.Save(now);
            return moved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MoveBackQuietly(target);
            throw;
        }
    }

    private void MoveBackQuietly(string target)
    {
        try
        {
            System.IO.Directory.Move(target, Directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the error being reported is the rewrite the new folder refused.
        }
    }

    /// <summary>Writes <c>session.json</c> and stamps <see cref="ForgeSessionDocument.UpdatedAt"/>.</summary>
    public void Save(DateTimeOffset? now = null)
    {
        Document.UpdatedAt = FormatInstant((now ?? DateTimeOffset.UtcNow).UtcDateTime);
        File.WriteAllText(
            Path.Combine(Directory, SessionFileName),
            JsonSerializer.Serialize(Document, SerializerOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Appends one transition to <c>history.jsonl</c>.</summary>
    public void AppendHistory(ForgeState from, ForgeTrigger trigger, ForgeState to, DateTimeOffset? now = null)
    {
        var line = JsonSerializer.Serialize(new
        {
            ts = FormatInstant((now ?? DateTimeOffset.UtcNow).UtcDateTime),
            from = from.ToString(),
            trigger = trigger.ToString(),
            to = to.ToString(),
        });

        File.AppendAllText(
            Path.Combine(Directory, HistoryFileName),
            line + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Appends one conversation turn to the transcript.</summary>
    public void AppendTranscript(string role, string text, DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(text);

        var line = JsonSerializer.Serialize(new ForgeTranscriptTurn
        {
            Timestamp = FormatInstant((now ?? DateTimeOffset.UtcNow).UtcDateTime),
            Role = role,
            Text = text,
        });

        File.AppendAllText(
            Path.Combine(Directory, TranscriptFileName),
            line + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Reads the conversation so far; unreadable lines are skipped, never fatal.</summary>
    public IReadOnlyList<ForgeTranscriptTurn> LoadTranscript()
    {
        var path = Path.Combine(Directory, TranscriptFileName);
        if (!File.Exists(path))
            return [];

        var turns = new List<ForgeTranscriptTurn>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                if (JsonSerializer.Deserialize<ForgeTranscriptTurn>(line) is { } turn)
                    turns.Add(turn);
            }
            catch (JsonException)
            {
                // A truncated tail (kill mid-append) must not lose the whole conversation.
            }
        }

        return turns;
    }

    /// <summary>Writes one of the session's JSON artifacts (brief, blueprint, repair state).</summary>
    public void SaveArtifact<T>(string fileName, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        File.WriteAllText(
            Path.Combine(Directory, fileName),
            JsonSerializer.Serialize(value, SerializerOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Reads one of the session's JSON artifacts; null when absent or unreadable.</summary>
    public T? TryLoadArtifact<T>(string fileName)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var path = Path.Combine(Directory, fileName);
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Removes one of the session's JSON artifacts, if present.</summary>
    public void DeleteArtifact(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var path = Path.Combine(Directory, fileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>The stored diagnosis of the last trial, at the session root.</summary>
    public const string VerdictFileName = "verdict.json";

    /// <summary>Whether a trial of this session has been judged and the verdict kept.</summary>
    public bool HasVerdict => File.Exists(Path.Combine(Directory, VerdictFileName));

    /// <summary>Adopts a state transition into the document (the caller saves when ready).</summary>
    public void SetState(ForgeState state) => Document.State = state.ToString();

    /// <summary>
    /// The reopen (W-09): anything that reached a verdict can be re-arbitrated — a
    /// promoted session for the modify / re-try / re-adopt cycle, and an abandoned one so
    /// a single abort after a reopen does not strand the team forever. Coerces the session
    /// back and saves; false when this session has nothing to reopen. The coercion lives
    /// here, at the command level's disposal — the state machine keeps its terminal states
    /// terminal, exactly like promote's own transition.
    /// <para>
    /// Where it lands depends on whether a verdict exists. One does → the arbitration, which
    /// re-announces it and offers the same choices. None does — a team adopted without a
    /// trial — → the dry pause, which is the boundary that team actually came from and
    /// where the same two answers are on offer again: try it, or keep it as it is. Sending
    /// it to the arbitration instead would have re-entered a stage whose first act is to
    /// read a <c>verdict.json</c> that was never written, and stranded the team on
    /// <c>FORGE-SESSION-CORRUPT</c>.
    /// </para>
    /// </summary>
    public bool TryReopen(DateTimeOffset now)
    {
        var hasVerdict = HasVerdict;
        var eligible = Status == ForgeSessionStatus.Promoted
            || (Status == ForgeSessionStatus.Abandoned && hasVerdict);
        if (!eligible)
            return false;

        var target = hasVerdict ? ForgeState.Verdict : ForgeState.Test;
        AppendHistory(State, ForgeTrigger.Reopen, target, now);
        SetState(target);
        SetStatus(ForgeSessionStatus.Active);
        Save(now);
        return true;
    }

    /// <summary>
    /// Adoption without a trial: a session paused at the dry boundary goes straight to
    /// Ready. False when this session is not at that boundary — the only place where a
    /// rendered, validated crew exists and nothing has been executed yet.
    /// <para>
    /// The move is checked against the machine rather than asserted here, so the map of
    /// legal moves stays the single authority on what may follow what; this method only
    /// applies what the machine allowed and records it under its own trigger.
    /// </para>
    /// </summary>
    /// <param name="now">The instant to stamp on the history line.</param>
    public bool TryAdoptWithoutTrial(DateTimeOffset now)
    {
        // BudgetExhausted adopts too, and it is the case that matters most: a user whose
        // budget ran out at the pause is precisely the one who cannot pay for a trial, and
        // adopting costs nothing at all. Refusing there would have left the team stranded
        // behind a toll it could no longer pay.
        if (Status is not (ForgeSessionStatus.Active or ForgeSessionStatus.BudgetExhausted))
            return false;

        var machine = ForgeStateMachineFactory.Create(State);
        if (!machine.CanFire(ForgeTrigger.TrialSkipped))
            return false;

        machine.Fire(ForgeTrigger.TrialSkipped);
        AppendHistory(State, ForgeTrigger.TrialSkipped, machine.CurrentState, now);
        SetState(machine.CurrentState);
        SetStatus(ForgeSessionStatus.Ready);

        // The pause is reachable AFTER a verdict — an amended blueprint and a refine both
        // re-enter Test — and nothing on the loop back clears the previous cycle's
        // diagnosis. Adopting from there with the file still on disk would hand the
        // promoted card a score, a pass/fail and findings earned by a crew that no longer
        // exists. This team has no verdict; the card knows how to say exactly that.
        DeleteArtifact(VerdictFileName);
        DeleteArtifact(TestStage.LastRunFileName);

        Save(now);
        return true;
    }

    /// <summary>Adopts a lifecycle change into the document (the caller saves when ready).</summary>
    public void SetStatus(ForgeSessionStatus status) => Document.Status = status.ToString();

    private static string FormatInstant(DateTime utc) =>
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
