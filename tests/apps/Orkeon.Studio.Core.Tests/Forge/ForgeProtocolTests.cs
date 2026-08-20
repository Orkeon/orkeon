using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Forge;

namespace Orkeon.Studio.Core.Tests.Forge;

/// <summary>
/// The client side of the forge wire contract (SPEC-ORKEON-FORGE §6). The pinned lines are
/// copied **verbatim** from the CLI's own golden test (<c>ForgeEventWriterTests</c>):
/// Studio.Core never references the CLI, so these strings are the drift pin between the
/// two halves of the protocol.
/// </summary>
public class ForgeEventParserTests
{
    [Fact]
    public void The_cli_golden_lines_parse_with_their_envelope()
    {
        string[] goldenLines =
        [
            """{"v":2,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"stage.entered","stage":"brief","iteration":1}""",
            """{"v":2,"seq":2,"ts":"2026-08-19T12:00:01Z","kind":"question.asked","id":"q1","text":"Quel est l'objectif ?","answerKind":"free"}""",
            """{"v":2,"seq":3,"ts":"2026-08-19T12:00:02Z","kind":"error","code":"FORGE-BUDGET-EXHAUSTED","message":"The session's token budget is exhausted.","recoverable":true}""",
            """{"v":2,"seq":4,"ts":"2026-08-19T12:00:03Z","kind":"session.finished","status":"abandoned","exitCode":0}""",
        ];

        var events = goldenLines.Select(line =>
        {
            Assert.True(OrkeonEventParser.TryParse(line, out var parsed), line);
            return parsed!;
        }).ToList();

        Assert.All(events, e => Assert.Equal(OrkeonEventParser.KnownProtocolVersion, e.Version));
        Assert.Equal([1L, 2L, 3L, 4L], events.Select(e => e.Seq));
        Assert.Equal(
            [ForgeEventKinds.StageEntered, ForgeEventKinds.QuestionAsked, ForgeEventKinds.Error, ForgeEventKinds.SessionFinished],
            events.Select(e => e.Kind));

        // Payloads read flat, and the relaxed escaping means French stays French.
        Assert.Equal("brief", events[0].GetString("stage"));
        Assert.Equal("Quel est l'objectif ?", events[1].GetString("text"));
        Assert.True(events[2].GetBool("recoverable"));
        Assert.Equal(0, events[3].GetInt64("exitCode"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain crew output, not an event")]
    [InlineData("{\"no\":\"kind\"}")]
    [InlineData("{\"kind\":\"x\"}")]           // no envelope
    [InlineData("[1,2,3]")]
    [InlineData("{ broken json")]
    public void Anything_that_is_not_an_event_says_so_instead_of_throwing(string? line)
    {
        Assert.False(OrkeonEventParser.TryParse(line, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void An_unknown_kind_still_parses_so_the_client_can_show_it_raw()
    {
        Assert.True(OrkeonEventParser.TryParse(
            """{"v":3,"seq":9,"ts":"2026-08-19T12:00:00Z","kind":"something.new","x":1}""", out var parsed));
        Assert.Equal("something.new", parsed!.Kind);
        Assert.Equal(3, parsed.Version);   // a future version is surfaced, not enforced
    }
}

/// <summary>The stage → milestone table of the UX study §3, pinned line by line.</summary>
public class ForgeMilestoneTests
{
    [Theory]
    [InlineData("brief", ForgeMilestone.Describe)]
    [InlineData("blueprint", ForgeMilestone.Propose)]
    [InlineData("render", ForgeMilestone.Propose)]
    [InlineData("validate", ForgeMilestone.Propose)]
    [InlineData("test", ForgeMilestone.Try)]
    [InlineData("diagnose", ForgeMilestone.Try)]
    [InlineData("verdict", ForgeMilestone.Try)]
    [InlineData("ready", ForgeMilestone.Adopt)]
    [InlineData("promoted", ForgeMilestone.Adopt)]
    public void Every_engine_stage_lands_on_its_milestone(string stage, ForgeMilestone expected) =>
        Assert.Equal(expected, ForgeMilestones.FromStage(stage));

    [Theory]
    [InlineData("failed")]
    [InlineData("abandoned")]
    [InlineData(null)]
    [InlineData("later.addition")]
    public void Failure_states_and_strangers_are_not_milestones(string? stage) =>
        Assert.Null(ForgeMilestones.FromStage(stage));
}

/// <summary>
/// The projection every front-end reads (UX study §3-§4): feed the stream, read the
/// screen — conversation, success card, narrative proposal, live try, checklist, decision.
/// </summary>
public class ForgeSessionModelTests
{
    private static OrkeonEvent Event(string json)
    {
        Assert.True(OrkeonEventParser.TryParse(json, out var parsed), json);
        return parsed!;
    }

    private static ForgeSessionModel FullCycle()
    {
        var model = new ForgeSessionModel();
        string[] stream =
        [
            """{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}""",
            """{"v":2,"seq":2,"ts":"t","kind":"stage.entered","stage":"brief","iteration":1}""",
            """{"v":2,"seq":3,"ts":"t","kind":"assistant.message","text":"Quel est le fournisseur ?"}""",
            """{"v":2,"seq":4,"ts":"t","kind":"brief.ready","brief":{"goal":"Résumer chaque matin les offres","acceptance":[{"id":"A1","statement":"Le résumé cite ses sources","kind":"must"},{"id":"A2","statement":"Moins d'une page","kind":"should"}]}}""",
            """{"v":2,"seq":5,"ts":"t","kind":"stage.entered","stage":"blueprint","iteration":1}""",
            """{"v":2,"seq":6,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille","goal":"Résumer"},"agents":[{"key":"collecteur","role":"Web Researcher","tools":["web_scrape"]},{"key":"redacteur","role":"Writer","tools":["file_write"]}],"tasks":[{"key":"collecte","description":"Collecter les offres","agent":"collecteur"},{"key":"resume","description":"Rédiger le résumé","agent":"redacteur"}],"rationale":"Deux rôles séparés."},"iteration":1}""",
            """{"v":2,"seq":7,"ts":"t","kind":"stage.entered","stage":"render","iteration":1}""",
            """{"v":2,"seq":8,"ts":"t","kind":"file.written","path":"crew/config.yaml","bytes":120}""",
            """{"v":2,"seq":9,"ts":"t","kind":"stage.entered","stage":"validate","iteration":1}""",
            """{"v":2,"seq":10,"ts":"t","kind":"validation.result","ok":true,"errors":[],"warnings":[],"attempt":0}""",
            """{"v":2,"seq":11,"ts":"t","kind":"stage.entered","stage":"test","iteration":1}""",
            """{"v":2,"seq":12,"ts":"t","kind":"run.started","run":1,"target":"crew"}""",
            """{"v":2,"seq":13,"ts":"t","kind":"task.completed","taskId":"collecte","agentRole":"Web Researcher","success":true,"durationMs":1200}""",
            """{"v":2,"seq":14,"ts":"t","kind":"run.finished","run":1,"success":true,"outputPath":"runs/1/output.md"}""",
            """{"v":2,"seq":15,"ts":"t","kind":"cost.updated","tokens":500,"budgetRemaining":null}""",
            """{"v":2,"seq":16,"ts":"t","kind":"stage.entered","stage":"diagnose","iteration":1}""",
            """{"v":2,"seq":17,"ts":"t","kind":"verdict.ready","score":0.4,"passing":false,"findings":[{"id":"F1","severity":"major","acceptance":"A1","statement":"Les sources ne sont pas citées"}],"suggestions":[{"target":"agent:redacteur","change":"citer chaque source","reason":"critère A1"}],"judge":"llm"}""",
            """{"v":2,"seq":18,"ts":"t","kind":"stage.entered","stage":"verdict","iteration":1}""",
            """{"v":2,"seq":19,"ts":"t","kind":"decision.needed","options":["accept","refine","abort"]}""",
        ];

        foreach (var line in stream)
            model.Feed(Event(line));
        return model;
    }

    [Fact]
    public void The_full_stream_projects_the_whole_screen()
    {
        var model = FullCycle();

        Assert.Equal("veille", model.Slug);
        Assert.Equal("Résumer chaque matin les offres", model.Title);
        Assert.Equal(ForgeMilestone.Try, model.Milestone);

        // The conversation, the success card, the narrative proposal.
        Assert.Equal("Quel est le fournisseur ?", Assert.Single(model.Messages).Text);
        Assert.Equal(["A1", "A2"], model.Criteria.Select(c => c.Id));
        Assert.True(model.Criteria[0].Must);
        Assert.NotNull(model.Proposal);
        Assert.Equal(["Collecter les offres", "Rédiger le résumé"], model.Proposal!.Steps.Select(s => s.Description));
        Assert.Equal("Web Researcher", model.Proposal.Steps[0].AgentRole);   // roles, never keys
        Assert.Equal(["web_scrape", "file_write"], model.Proposal.Tools);
        Assert.Equal("Deux rôles séparés.", model.Proposal.Rationale);

        // The try, the meter, the verdict, the pending decision.
        Assert.False(model.RunInProgress);
        Assert.Equal("collecte", Assert.Single(model.Activity).TaskId);
        Assert.Equal(500, model.TokensSpent);
        Assert.Null(model.TokensRemaining);
        Assert.False(model.Verdict!.Passing);
        Assert.True(model.DecisionPending);
        Assert.Equal(["accept", "refine", "abort"], model.DecisionOptions);
    }

    [Fact]
    public void The_checklist_reuses_the_success_cards_words_and_never_invents_a_check()
    {
        var model = FullCycle();

        var checklist = model.BuildChecklist();

        // A1 failed with the finding's wording; A2 passed because a real judge looked.
        Assert.Equal(2, checklist.Count);
        Assert.Equal("Le résumé cite ses sources", checklist[0].Statement);
        Assert.False(checklist[0].Passed);
        Assert.Equal("Les sources ne sont pas citées", checklist[0].Detail);
        Assert.Equal("Moins d'une page", checklist[1].Statement);
        Assert.True(checklist[1].Passed);
    }

    [Fact]
    public void A_deterministic_verdict_says_judge_for_yourself_instead_of_a_fake_check()
    {
        var model = FullCycle();
        model.Feed(Event(
            """{"v":2,"seq":20,"ts":"t","kind":"verdict.ready","score":0.7,"passing":true,"findings":[{"id":"F-RUN","severity":"blocking","statement":"The crew did not complete its run."}],"suggestions":[],"judge":"deterministic"}"""));

        var checklist = model.BuildChecklist();

        // The mechanical finding leads, unverified criteria are null — never an invented ✔.
        Assert.Equal(3, checklist.Count);
        Assert.False(checklist[0].Passed);
        Assert.Equal("The crew did not complete its run.", checklist[0].Statement);
        Assert.Null(checklist[1].Passed);
        Assert.Null(checklist[2].Passed);
    }

    [Fact]
    public void Refine_returns_to_propose_and_a_new_stage_clears_the_pending_decision()
    {
        var model = FullCycle();
        model.AddUserMessage("corrige les sources");
        model.Feed(Event("""{"v":2,"seq":20,"ts":"t","kind":"stage.entered","stage":"blueprint","iteration":2}"""));

        Assert.False(model.DecisionPending);
        Assert.Equal(ForgeMilestone.Propose, model.Milestone);
        Assert.Equal(2, model.Iteration);
        Assert.Equal(ForgeChatMessage.User, model.Messages[^1].Role);
    }

    [Fact]
    public void Promotion_and_errors_land_where_the_adopt_card_reads_them()
    {
        var model = new ForgeSessionModel();
        model.Feed(Event(
            """{"v":2,"seq":1,"ts":"t","kind":"promoted","path":"/solutions/veille","launcher":"run.sh","schedule":"schedule","install":"systemctl --user enable --now orkeon-veille.timer"}"""));
        model.Feed(Event(
            """{"v":2,"seq":2,"ts":"t","kind":"error","code":"FORGE-LLM-UNAVAILABLE","message":"no LLM","recoverable":true}"""));
        model.Feed(Event("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""));

        Assert.Equal(ForgeMilestone.Adopt, model.Milestone);
        Assert.Equal("/solutions/veille", model.Promotion!.Path);
        Assert.Contains("systemctl", model.Promotion.Install, StringComparison.Ordinal);
        Assert.Equal("FORGE-LLM-UNAVAILABLE", model.LastError!.Code);
        Assert.True(model.LastError.Recoverable);
        Assert.Equal("ready", model.FinishedStatus);
    }
}
