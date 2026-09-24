using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Scripting.Cli.Commands.UseCases;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The use case a session was composed from, as <c>session.json</c>, <c>forge.json</c> and
/// <c>FORGE.md</c> trace it (STUDIO-40, D-01/D-04): its id, and its title in the brief's
/// language — in English until there is a brief.
/// </summary>
internal sealed record ForgeReferenceRecord
{
    /// <summary>
    /// The use case's id in the catalogue (<c>03-email-pipeline</c>). Not <c>required</c>, on
    /// purpose: the serializer would then refuse a whole <c>session.json</c> or <c>forge.json</c>
    /// hand-edited into a <c>reference</c> without one — here it is empty, and names nothing.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>Its title, in the brief's language, else in English.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }
}

/// <summary>One agent of a reference crew, as far as its structure goes: no backstory.</summary>
/// <param name="Key">The agent's key in its crew.</param>
/// <param name="Role">Its role.</param>
/// <param name="Goal">Its goal.</param>
/// <param name="Tools">The tools the crew file gives it, in the file's order.</param>
internal sealed record ForgeReferenceAgent(string Key, string? Role, string? Goal, IReadOnlyList<string> Tools);

/// <summary>One task of a reference crew: who does it, after what, and what it produces.</summary>
/// <param name="Key">The task's key in its crew.</param>
/// <param name="Agent">The key of the agent doing it.</param>
/// <param name="Description">What the work is.</param>
/// <param name="ExpectedOutput">What it produces.</param>
/// <param name="Dependencies">The keys of the tasks it follows.</param>
internal sealed record ForgeReferenceTask(
    string Key, string? Agent, string? Description, string? ExpectedOutput, IReadOnlyList<string> Dependencies);

/// <summary>
/// The use case a composition starts from (STUDIO-40): its sheet, and the structure of its crew —
/// agents, tasks, process, tools — read from the crew file the tool embeds (STUDIO-38). The
/// composer gets that structure as a model, never the file: a backstory is the example's content,
/// and the file's tools are the example's, not the forge's.
/// <para>
/// Both crew formats of the catalogue are read: the YAML crews through the loader's own DTOs, the
/// TypeScript crews of the finance examples through the calls of their builders. A reader of those
/// calls, not a JavaScript engine: the finance crews import a shared <c>_tools/</c> module the tool
/// does not carry, so they cannot run here — and their structure is all that is wanted of them.
/// <c>ForgeReferenceTests</c> reads every use case of the catalogue back into what its sheet declares.
/// </para>
/// </summary>
internal sealed partial class ForgeReference
{
    /// <summary>
    /// The most characters one text of the outline carries — a role, a goal, a description, an
    /// expected output. Past it the text is cut after a word, with an ellipsis: the structure is in
    /// the list of agents and tasks, not in the length of their prose.
    /// </summary>
    public const int MaxTextLength = 200;

    /// <summary>
    /// The most characters the outline takes in the composer's prompt (D-03) — about 1,150 tokens
    /// at the forge's estimate. Measured on the catalogue on 2026-09-24, with every tool kept: the
    /// outlines take 1,500 to 3,900 characters, 2,200 for the median crew, and one crew in 105 —
    /// eight agents, nine tasks — is cut. A cut outline stops at the last whole line that fits,
    /// followed by <see cref="CutNotice"/>.
    /// </summary>
    public const int MaxOutlineLength = 4000;

    /// <summary>The last line of an outline that was cut, so the composer knows the team goes on.</summary>
    public const string CutNotice = "(… the rest of this team was cut to keep the prompt short)";

    private ForgeReference(
        UseCase useCase,
        string? goal,
        string? process,
        IReadOnlyList<ForgeReferenceAgent> agents,
        IReadOnlyList<ForgeReferenceTask> tasks)
    {
        UseCase = useCase;
        Goal = goal;
        Process = string.IsNullOrWhiteSpace(process) ? useCase.Process : process;
        Agents = agents;
        Tasks = tasks;
    }

    /// <summary>The use case's sheet.</summary>
    public UseCase UseCase { get; }

    /// <summary>The use case's id.</summary>
    public string Id => UseCase.Id;

    /// <summary>The crew's goal, as its file states it.</summary>
    public string? Goal { get; }

    /// <summary>The crew's orchestration mode; the sheet's when the file names none.</summary>
    public string Process { get; }

    /// <summary>The crew's agents, in the file's order.</summary>
    public IReadOnlyList<ForgeReferenceAgent> Agents { get; }

    /// <summary>The crew's tasks, in the file's order.</summary>
    public IReadOnlyList<ForgeReferenceTask> Tasks { get; }

    /// <summary>
    /// The use case <paramref name="id"/> names in the catalogue this build embeds. An unknown id
    /// throws <see cref="UnknownUseCaseException"/> — the typed error of D-05, which the command
    /// raises before it opens a session or boots a host, so no model is ever asked.
    /// </summary>
    public static ForgeReference Load(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var catalog = UseCaseCatalog.Embedded;
        return Read(catalog.Get(id), catalog.ReadCrew(id));
    }

    /// <summary>
    /// The reference a session recorded, when this build still carries it — what a resumed session
    /// composes with (D-01). Null without a record, and for an id the catalogue no longer carries:
    /// the session goes on without a model rather than not at all, its provenance kept as written.
    /// </summary>
    public static ForgeReference? Recorded(ForgeReferenceRecord? record) =>
        record is not null && UseCaseCatalog.Embedded.TryGet(record.Id, out _) ? Load(record.Id) : null;

    /// <summary>
    /// <paramref name="record"/>, titled in <paramref name="language"/> — the brief's, which the
    /// promotion knows (D-04). A record whose use case the catalogue no longer carries keeps the
    /// title it has: provenance is history.
    /// </summary>
    public static ForgeReferenceRecord Retitle(ForgeReferenceRecord record, string? language)
    {
        ArgumentNullException.ThrowIfNull(record);

        return UseCaseCatalog.Embedded.TryGet(record.Id, out var useCase)
            ? record with { Title = TitleOf(useCase, language) }
            : record;
    }

    /// <summary>Reads <paramref name="crewText"/>, the crew file of <paramref name="useCase"/>, in the sheet's format.</summary>
    public static ForgeReference Read(UseCase useCase, string crewText)
    {
        ArgumentNullException.ThrowIfNull(useCase);
        ArgumentException.ThrowIfNullOrWhiteSpace(crewText);

        return string.Equals(useCase.Format, UseCase.ScriptFormat, StringComparison.Ordinal)
            ? ReadScript(useCase, crewText)
            : ReadYaml(useCase, crewText);
    }

    /// <summary>The title in <paramref name="language"/> — the brief's — else in English; the id when the sheet has none.</summary>
    public string TitleIn(string? language) => TitleOf(UseCase, language);

    /// <summary>What <c>session.json</c> records of this reference, titled in <paramref name="language"/>.</summary>
    public ForgeReferenceRecord RecordIn(string? language) => new() { Id = Id, Title = TitleIn(language) };

    /// <summary>
    /// The structure the composer reads (D-02, D-03): the crew's goal and process, each agent with
    /// its role, goal and tools, each task with its agent, the tasks it follows, its description and
    /// what it produces. The tools <paramref name="catalogue"/> does not offer are removed — the
    /// catalogue stays the only list a plan may draw from — and the whole is bounded by
    /// <see cref="MaxOutlineLength"/>.
    /// </summary>
    public string Outline(IReadOnlyCollection<string> catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        var offered = catalogue.ToHashSet(StringComparer.Ordinal);
        var lines = new List<string>();
        if (Text(Goal) is { } goal)
            lines.Add($"Goal: {goal}");
        lines.Add($"Process: {Process}");

        lines.Add("Agents:");
        foreach (var agent in Agents)
        {
            var tools = agent.Tools.Where(offered.Contains).Distinct(StringComparer.Ordinal).ToList();
            lines.Add(
                $"- {agent.Key}"
                + (Text(agent.Role) is { } role ? $" ({role})" : "")
                + (Text(agent.Goal) is { } agentGoal ? $": {agentGoal}" : "")
                + (tools.Count > 0 ? $" [tools: {string.Join(", ", tools)}]" : " [no tools]"));
        }

        lines.Add("Tasks, in order:");
        foreach (var task in Tasks)
        {
            var who = new List<string>();
            if (!string.IsNullOrWhiteSpace(task.Agent))
                who.Add($"agent: {task.Agent}");
            if (task.Dependencies.Count > 0)
                who.Add($"after: {string.Join(", ", task.Dependencies)}");

            lines.Add(
                $"- {task.Key}"
                + (who.Count > 0 ? $" ({string.Join("; ", who)})" : "")
                + (Text(task.Description) is { } description ? $": {description}" : "")
                + (Text(task.ExpectedOutput) is { } output ? $" Produces: {output}" : ""));
        }

        return Bound(lines);
    }

    private static string TitleOf(UseCase useCase, string? language) =>
        useCase.TitleIn(language ?? UseCaseLanguages.English) is { Length: > 0 } title ? title : useCase.Id;

    /// <summary>The lines, whole, when they fit the bound; else the lines that fit and <see cref="CutNotice"/>.</summary>
    private static string Bound(List<string> lines)
    {
        var whole = string.Join('\n', lines);
        if (whole.Length <= MaxOutlineLength)
            return whole;

        var kept = new StringBuilder();
        foreach (var line in lines)
        {
            if (kept.Length + line.Length + 1 + CutNotice.Length > MaxOutlineLength)
                break;
            kept.Append(line).Append('\n');
        }

        return kept.Append(CutNotice).ToString();
    }

    /// <summary>One line of prose: whitespace collapsed, cut after a word past <see cref="MaxTextLength"/>.</summary>
    private static string? Text(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length <= MaxTextLength)
            return line;

        var cut = line.LastIndexOf(' ', MaxTextLength);
        return (cut > 0 ? line[..cut] : line[..MaxTextLength]) + "…";
    }

    /// <summary>A YAML crew, through the DTOs the crew loader itself deserializes.</summary>
    private static ForgeReference ReadYaml(UseCase useCase, string text)
    {
        var crew = new YamlDotNetSerializer().Deserialize<CrewYamlConfig>(text) ?? new CrewYamlConfig();

        var agents = (crew.Agents ?? [])
            .Select(pair => new ForgeReferenceAgent(
                pair.Key, pair.Value?.Role, pair.Value?.Goal, [.. pair.Value?.Tools ?? []]))
            .ToList();
        var tasks = (crew.Tasks ?? [])
            .Select(pair => new ForgeReferenceTask(
                pair.Key,
                pair.Value?.Agent,
                pair.Value?.Description,
                pair.Value?.ExpectedOutput,
                [.. pair.Value?.Dependencies ?? []]))
            .ToList();

        return new ForgeReference(useCase, crew.Goal, crew.Process, agents, tasks);
    }

    /// <summary>
    /// A TypeScript crew, through its builders: each <c>const x = agentBuilder()…build()</c> and
    /// <c>taskBuilder()</c> statement, the string literal of each call that names, describes or
    /// orders, the tools of <c>.tools([…])</c> and <c>pickTools(…)</c>, and the variables
    /// <c>.agent(x)</c> and <c>.withContext(x)</c> point at, resolved to the names they carry.
    /// </summary>
    private static ForgeReference ReadScript(UseCase useCase, string text)
    {
        var statements = BuilderStatement().Matches(text)
            .Select(match => (
                Variable: match.Groups["variable"].Value,
                IsAgent: match.Groups["builder"].Value == "agentBuilder",
                Chain: match.Groups["chain"].Value))
            .ToList();

        // Every builder is named by its variable in the code and by .name() in the crew.
        var names = statements.ToDictionary(
            statement => statement.Variable,
            statement => StringCalls(statement.Chain).GetValueOrDefault("name") ?? statement.Variable,
            StringComparer.Ordinal);

        var agents = new List<ForgeReferenceAgent>();
        var tasks = new List<ForgeReferenceTask>();
        foreach (var (variable, isAgent, chain) in statements)
        {
            var calls = StringCalls(chain);
            if (isAgent)
            {
                var tools = ToolList().Matches(chain)
                    .SelectMany(list => Literal().Matches(list.Groups["list"].Value))
                    .Select(literal => literal.Groups["value"].Value)
                    .ToList();
                agents.Add(new ForgeReferenceAgent(names[variable], calls.GetValueOrDefault("role"), calls.GetValueOrDefault("goal"), tools));
                continue;
            }

            var references = BuilderReference().Matches(chain)
                .Select(match => (
                    Call: match.Groups["call"].Value,
                    Names: match.Groups["variables"].Value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(name => names.GetValueOrDefault(name, name))))
                .ToList();

            tasks.Add(new ForgeReferenceTask(
                names[variable],
                references.Where(reference => reference.Call == "agent").SelectMany(reference => reference.Names).FirstOrDefault(),
                calls.GetValueOrDefault("description"),
                calls.GetValueOrDefault("expectedOutput"),
                [.. references.Where(reference => reference.Call == "withContext").SelectMany(reference => reference.Names)]));
        }

        var crew = CrewStatement().Match(text) is { Success: true } crewMatch
            ? StringCalls(crewMatch.Groups["chain"].Value)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        return new ForgeReference(useCase, crew.GetValueOrDefault("goal"), crew.GetValueOrDefault("process"), agents, tasks);
    }

    /// <summary>The single-string-literal calls of a builder chain, first one of each name kept, literals unescaped.</summary>
    private static Dictionary<string, string> StringCalls(string chain)
    {
        var calls = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match call in StringCall().Matches(chain))
            calls.TryAdd(call.Groups["call"].Value, Unescape(call.Groups["value"].Value));

        return calls;
    }

    /// <summary>A JavaScript string literal's body, its escapes resolved; a line break becomes a space.</summary>
    private static string Unescape(string literal)
    {
        var text = new StringBuilder(literal.Length);
        for (var i = 0; i < literal.Length; i++)
        {
            if (literal[i] != '\\' || i + 1 == literal.Length)
            {
                text.Append(literal[i]);
                continue;
            }

            var escaped = literal[++i];
            text.Append(escaped is 'n' or 'r' or 't' ? ' ' : escaped);
        }

        return text.ToString();
    }

    [GeneratedRegex(
        @"\b(?:const|let|var)\s+(?<variable>\w+)\s*=\s*(?<builder>agentBuilder|taskBuilder)\(\)(?<chain>.*?)\.build\(\)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BuilderStatement();

    [GeneratedRegex(
        @"\bcrewBuilder\(\)(?<chain>.*?)\.build\(\)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CrewStatement();

    /// <summary><c>.name("…")</c>, <c>.goal('…')</c>, <c>.backstory(`…`)</c>: one call, one string literal.</summary>
    [GeneratedRegex(
        @"\.(?<call>\w+)\(\s*(?<quote>[""'`])(?<value>(?:\\.|(?!\k<quote>).)*)\k<quote>\s*\)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StringCall();

    /// <summary>The arguments of <c>.tools([…])</c> and of <c>pickTools(…)</c>.</summary>
    [GeneratedRegex(
        @"(?:\.tools\(\s*\[(?<list>[^\]]*)\]|\bpickTools\((?<list>[^)]*))\)",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ToolList();

    [GeneratedRegex(@"[""'`](?<value>[^""'`]+)[""'`]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Literal();

    /// <summary><c>.agent(x)</c> and <c>.withContext(x)</c> / <c>.withContext([x, y])</c>: builders named by their variable.</summary>
    [GeneratedRegex(
        @"\.(?<call>agent|withContext)\(\s*\[?(?<variables>[\w\s,]*)\]?\s*\)",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BuilderReference();
}
