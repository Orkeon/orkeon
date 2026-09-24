using System.Text;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Cli.Commands.UseCases;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// STUDIO-40: the use case a composition starts from — its sheet, and the structure of its crew
/// read from the file the tool embeds (STUDIO-38), reduced to what the composer is to take from
/// it: agents, tasks, process, tools. Offline throughout, over the real catalogue.
/// </summary>
public sealed class ForgeReferenceTests
{
    private const string EmailPipeline = "03-email-pipeline";

    /// <summary>Every tool some use case names: a catalogue that removes nothing.</summary>
    private static IReadOnlyCollection<string> EveryTool =>
        [.. UseCaseCatalog.Embedded.UseCases.SelectMany(useCase => useCase.Tools).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The reader is pinned on the whole catalogue, both formats: each crew reads back into the
    /// agents, tasks, process and tools its sheet declares — a crew file reshaped so the reader no
    /// longer understands it fails here, not in front of a user.
    /// </summary>
    [Fact]
    public void Every_use_case_reads_back_into_the_structure_its_sheet_declares()
    {
        foreach (var useCase in UseCaseCatalog.Embedded.UseCases)
        {
            var reference = ForgeReference.Load(useCase.Id);

            Assert.True(useCase.Agents == reference.Agents.Count, $"{useCase.Id}: {reference.Agents.Count} agents, the sheet says {useCase.Agents}.");
            Assert.True(useCase.Tasks == reference.Tasks.Count, $"{useCase.Id}: {reference.Tasks.Count} tasks, the sheet says {useCase.Tasks}.");
            Assert.True(useCase.Process == reference.Process, $"{useCase.Id}: process '{reference.Process}', the sheet says '{useCase.Process}'.");
            Assert.Equal(
                useCase.Tools.Order(StringComparer.Ordinal),
                reference.Agents.SelectMany(agent => agent.Tools).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

            var agents = reference.Agents.Select(agent => agent.Key).ToHashSet(StringComparer.Ordinal);
            var tasks = reference.Tasks.Select(task => task.Key).ToHashSet(StringComparer.Ordinal);
            Assert.All(reference.Tasks, task => Assert.True(
                task.Agent is null || agents.Contains(task.Agent), $"{useCase.Id}: task '{task.Key}' names agent '{task.Agent}'."));
            Assert.All(reference.Tasks.SelectMany(task => task.Dependencies), key => Assert.Contains(key, tasks));
        }
    }

    [Fact]
    public void The_outline_keeps_the_structure_and_leaves_the_backstories_out()
    {
        var outline = ForgeReference.Load(EmailPipeline).Outline(["email_parser", "file_write", "json_tool"]);

        Assert.Contains("Process: sequential", outline, StringComparison.Ordinal);
        Assert.Contains(
            "- trieur (Trieur d'Emails): Classify incoming emails by urgency and category [tools: email_parser, json_tool]",
            outline, StringComparison.Ordinal);
        Assert.Contains(
            "- validateur (Validateur Humain): Review and approve drafted email responses before sending [no tools]",
            outline, StringComparison.Ordinal);
        Assert.Contains("- triage_emails (agent: trieur): Parse incoming emails", outline, StringComparison.Ordinal);
        Assert.Contains("- extract_actions (agent: extracteur; after: triage_emails): ", outline, StringComparison.Ordinal);
        Assert.Contains(" Produces: JSON array of classified emails", outline, StringComparison.Ordinal);

        // A backstory is the example's content, never its structure.
        Assert.DoesNotContain("executive assistant", outline, StringComparison.Ordinal);
    }

    /// <summary>D-02: a tool the forge's catalogue does not offer is removed from the reference.</summary>
    [Fact]
    public void The_tools_missing_from_the_catalogue_are_removed()
    {
        var outline = ForgeReference.Load(EmailPipeline).Outline(["json_tool"]);

        Assert.DoesNotContain("email_parser", outline, StringComparison.Ordinal);
        Assert.DoesNotContain("file_write", outline, StringComparison.Ordinal);
        Assert.Contains(
            "- trieur (Trieur d'Emails): Classify incoming emails by urgency and category [tools: json_tool]",
            outline, StringComparison.Ordinal);
        Assert.Contains("- redacteur (Redacteur de Reponses): ", outline, StringComparison.Ordinal);
    }

    /// <summary>
    /// The finance examples are reference-only (DC-3) and still usable as references: their
    /// TypeScript crews are read through the builders, and the shared <c>_tools/</c> they pick,
    /// being no built-in, go the way of every tool the catalogue lacks.
    /// </summary>
    [Fact]
    public void A_script_crew_is_read_through_its_builders()
    {
        var reference = ForgeReference.Load("40-invoice-processing");

        Assert.Equal("sequential", reference.Process);
        Assert.Equal(["extractor", "structurer", "reconciler"], reference.Agents.Select(agent => agent.Key));
        Assert.Equal("Accounting Reconciler", reference.Agents[2].Role);
        Assert.Equal(
            ["relational_database_query", "csv_reader", "json_tool", "file_write", "audit_trail", "dashboard_metrics"],
            reference.Agents[2].Tools);
        Assert.Equal(["extract_invoices", "structure_data", "reconcile_accounts"], reference.Tasks.Select(task => task.Key));
        Assert.Equal("structurer", reference.Tasks[1].Agent);
        Assert.Equal(["extract_invoices"], reference.Tasks[1].Dependencies);

        var outline = reference.Outline(["csv_reader", "file_write", "json_tool", "pdf_reader"]);

        Assert.Contains("- reconciler (Accounting Reconciler): ", outline, StringComparison.Ordinal);
        Assert.Contains("[tools: csv_reader, json_tool, file_write]", outline, StringComparison.Ordinal);
        Assert.DoesNotContain("audit_trail", outline, StringComparison.Ordinal);
        Assert.DoesNotContain("relational_database_query", outline, StringComparison.Ordinal);
    }

    /// <summary>
    /// D-03: the reference has a bounded size. Each text is cut at a word past
    /// <see cref="ForgeReference.MaxTextLength"/>, and the outline stops at a whole line before
    /// <see cref="ForgeReference.MaxOutlineLength"/>, saying it was cut.
    /// </summary>
    [Fact]
    public void A_reference_longer_than_the_bound_is_cut_and_says_so()
    {
        var reference = ForgeReference.Read(Sheet("99-huge-crew"), HugeCrew(agents: 60, taskWords: 400));

        var outline = reference.Outline(["file_read"]);

        Assert.True(outline.Length <= ForgeReference.MaxOutlineLength, $"{outline.Length} characters");
        Assert.EndsWith(ForgeReference.CutNotice, outline, StringComparison.Ordinal);
        // Whole lines only: the cut never lands inside an agent or a task.
        var lines = outline.Split('\n');
        Assert.All(lines[..^1], line => Assert.True(
            line.StartsWith("- ", StringComparison.Ordinal) || line.EndsWith(':') || line.StartsWith("Process:", StringComparison.Ordinal)
                || line.StartsWith("Goal:", StringComparison.Ordinal),
            line));
        Assert.Contains("- agent-1 (Role 1): Goal of agent 1 [tools: file_read]", outline, StringComparison.Ordinal);
        Assert.DoesNotContain("- task-60 ", outline, StringComparison.Ordinal);
    }

    [Fact]
    public void A_text_longer_than_its_bound_is_cut_at_a_word()
    {
        var reference = ForgeReference.Read(Sheet("99-wordy-task"), HugeCrew(agents: 1, taskWords: 400));

        var task = reference.Outline(["file_read"]).Split('\n').Single(line => line.StartsWith("- task-1 ", StringComparison.Ordinal));
        var description = task[(task.IndexOf("): ", StringComparison.Ordinal) + 3)..task.IndexOf(" Produces: ", StringComparison.Ordinal)];

        // At most the bound, plus the ellipsis that says so — and cut after a whole word.
        Assert.True(description.Length <= ForgeReference.MaxTextLength + 1, $"{description.Length} characters");
        Assert.StartsWith("word1 word2 ", description, StringComparison.Ordinal);
        Assert.Matches(@" word\d+…$", description);
    }

    /// <summary>The bound is chosen for the catalogue as it stands: every use case fits, whole or cut.</summary>
    [Fact]
    public void Every_use_case_fits_the_bound()
    {
        foreach (var useCase in UseCaseCatalog.Embedded.UseCases)
        {
            var outline = ForgeReference.Load(useCase.Id).Outline(EveryTool);
            Assert.True(outline.Length <= ForgeReference.MaxOutlineLength, $"{useCase.Id}: {outline.Length} characters");
        }
    }

    /// <summary>D-04: the title is written in the brief's language, else in English.</summary>
    [Fact]
    public void The_title_is_in_the_briefs_language_else_in_english()
    {
        var reference = ForgeReference.Load(EmailPipeline);

        Assert.Equal("Tri et réponse aux e-mails", reference.TitleIn("fr"));
        Assert.Equal("Email triage and replies", reference.TitleIn("en"));
        Assert.Equal("Email triage and replies", reference.TitleIn(null));
        Assert.Equal(
            new ForgeReferenceRecord { Id = EmailPipeline, Title = "Email triage and replies" },
            reference.RecordIn(null));
    }

    /// <summary>D-05: an unknown id is the catalogue's own typed error, <c>USECASES-UNKNOWN-ID</c>.</summary>
    [Fact]
    public void An_unknown_id_is_the_catalogues_typed_error()
    {
        var error = Assert.Throws<UnknownUseCaseException>(() => ForgeReference.Load("99-nowhere"));

        Assert.Equal("99-nowhere", error.UseCaseId);
        Assert.Contains(UnknownUseCaseException.Code, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// D-01: a resumed session composes with the reference it recorded. One the catalogue no
    /// longer carries — a tool upgraded since — is none: the session goes on without it.
    /// </summary>
    [Fact]
    public void A_recorded_reference_is_found_again_and_a_vanished_one_is_none()
    {
        Assert.Equal(EmailPipeline, ForgeReference.Recorded(new ForgeReferenceRecord { Id = EmailPipeline })!.Id);
        Assert.Null(ForgeReference.Recorded(new ForgeReferenceRecord { Id = "99-gone", Title = "Gone" }));
        Assert.Null(ForgeReference.Recorded(null));
    }

    /// <summary>
    /// D-04 at promotion: the recorded title takes the brief's language from the catalogue, and a
    /// record whose case the catalogue lost keeps the title it had — provenance is history.
    /// </summary>
    [Fact]
    public void A_record_takes_the_title_of_the_briefs_language_unless_the_catalogue_lost_its_case()
    {
        var english = new ForgeReferenceRecord { Id = EmailPipeline, Title = "Email triage and replies" };
        Assert.Equal("Tri et réponse aux e-mails", ForgeReference.Retitle(english, "fr").Title);
        Assert.Equal("Email triage and replies", ForgeReference.Retitle(english, null).Title);

        var gone = new ForgeReferenceRecord { Id = "99-gone", Title = "Gone" };
        Assert.Equal(gone, ForgeReference.Retitle(gone, "fr"));
    }

    private static UseCase Sheet(string id) => new()
    {
        Id = id,
        Category = "09-experimental",
        Format = "yaml",
        Process = "sequential",
    };

    /// <summary>A YAML crew of <paramref name="agents"/> agents, one task each, every description <paramref name="taskWords"/> words long.</summary>
    private static string HugeCrew(int agents, int taskWords)
    {
        var description = string.Join(' ', Enumerable.Range(1, taskWords).Select(word => $"word{word}"));
        var yaml = new StringBuilder("name: huge\ngoal: \"A crew far too big for a prompt\"\nprocess: sequential\nagents:\n");
        for (var i = 1; i <= agents; i++)
            yaml.Append($"  agent-{i}:\n    role: \"Role {i}\"\n    goal: \"Goal of agent {i}\"\n    tools: [\"file_read\", \"ghost_tool\"]\n");

        yaml.Append("tasks:\n");
        for (var i = 1; i <= agents; i++)
            yaml.Append($"  task-{i}:\n    description: \"{description}\"\n    expectedOutput: \"Output {i}\"\n    agent: \"agent-{i}\"\n");

        return yaml.ToString();
    }
}
