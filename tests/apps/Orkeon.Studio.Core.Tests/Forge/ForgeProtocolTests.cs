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

    /// <summary>
    /// STUDIO-26's two lines, verbatim from the CLI's golden test
    /// (<c>ForgeEventWriterTests.The_rename_and_warning_lines_are_the_pinned_golden_form</c>).
    /// </summary>
    [Fact]
    public void The_cli_golden_rename_and_warning_lines_parse_with_their_payload()
    {
        Assert.True(OrkeonEventParser.TryParse(
            """{"v":2,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"session.renamed","from":"veille","to":"ma-veille-2","dir":"/ws/.orkeon/forge/ma-veille-2","suffixed":true}""",
            out var renamed));
        Assert.True(OrkeonEventParser.TryParse(
            """{"v":2,"seq":2,"ts":"2026-08-19T12:00:01Z","kind":"warning","code":"FORGE-SESSION-NOT-RENAMED","message":"The session folder 'veille' keeps its name: access denied."}""",
            out var warning));

        Assert.Equal(ForgeEventKinds.SessionRenamed, renamed!.Kind);
        Assert.Equal("veille", renamed.GetString("from"));
        Assert.Equal("ma-veille-2", renamed.GetString("to"));
        Assert.Equal("/ws/.orkeon/forge/ma-veille-2", renamed.GetString("dir"));
        Assert.True(renamed.GetBool("suffixed"));
        Assert.Equal(ForgeEventKinds.Warning, warning!.Kind);
        Assert.Equal("FORGE-SESSION-NOT-RENAMED", warning.GetString("code"));
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
            """{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false,"engine":"1.0.0-rc.2"}""",
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
            """{"v":2,"seq":17,"ts":"t","kind":"verdict.ready","score":0.4,"passing":false,"findings":[{"id":"F1","severity":"major","acceptance":"A1","statement":"Les sources ne sont pas citées"}],"suggestions":[{"target":"agent:redacteur","change":"citer chaque source","reason":"critère A1"}],"judge":"llm","durationMs":59000,"tokens":12840,"cacheHitTokens":7980,"cacheMissTokens":4020}""",
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
        // The session's stable id (STUDIO-25): what the team's forge.json will carry.
        Assert.Equal(Guid.Parse("6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f"), model.SessionId);
        // Which build answered. It is the first thing to read when a screen shows nothing:
        // an engine that reports nothing and one too old to report look the same otherwise.
        Assert.Equal("1.0.0-rc.2", model.EngineVersion);
        // The blueprint's short crew name supersedes the brief's goal sentence as the
        // title: the adoption slug derives from it (a goal-length slug made 200-char folders).
        Assert.Equal("veille", model.Title);
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

        // W-08: the verdict carries the trial's own cost, distinct from the session meter.
        Assert.Equal(59_000, model.Verdict.DurationMs);
        Assert.Equal(12_840, model.Verdict.Tokens);
        Assert.Equal(7_980, model.Verdict.CacheHitTokens);
        Assert.Equal(4_020, model.Verdict.CacheMissTokens);
    }

    /// <summary>
    /// STUDIO-40: <c>session.started</c> names the use case the session is composed from; a
    /// session started from nothing — or by an engine older than the field — names none.
    /// </summary>
    [Fact]
    public void The_session_started_names_the_use_case_the_session_is_composed_from()
    {
        var model = new ForgeSessionModel();

        model.Feed(Event("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"trier","dir":"/ws/.orkeon/forge/trier","format":"yaml","reference":{"id":"03-email-pipeline","title":"Email triage and replies"},"resumed":false}"""));
        Assert.Equal("03-email-pipeline", model.ReferenceUseCaseId);

        model.Feed(Event("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":true}"""));
        Assert.Null(model.ReferenceUseCaseId);
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
    public void A_closed_question_takes_the_assistants_turn_in_the_conversation()
    {
        var model = new ForgeSessionModel();
        model.Feed(Event(
            """{"v":2,"seq":1,"ts":"t","kind":"question.asked","id":"q1","text":"Quel est l'objectif ?","answerKind":"free"}"""));

        var message = Assert.Single(model.Messages);
        Assert.Equal(ForgeChatMessage.Assistant, message.Role);
        Assert.Equal("Quel est l'objectif ?", message.Text);
    }

    [Fact]
    public void Hydrated_artifacts_carry_the_milestone_without_any_stage_event()
    {
        // A resume seeds from files: blueprint.json and verdict.json arrive as their events
        // with no stage.entered around them — the artifact itself proves the stage was reached.
        var model = new ForgeSessionModel();
        model.Feed(Event(
            """{"v":2,"seq":1,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"v","goal":"g"},"agents":[],"tasks":[],"rationale":""},"iteration":1}"""));
        Assert.Equal(ForgeMilestone.Propose, model.Milestone);

        model.Feed(Event(
            """{"v":2,"seq":2,"ts":"t","kind":"verdict.ready","score":0.9,"passing":true,"findings":[],"suggestions":[],"judge":"llm"}"""));
        Assert.Equal(ForgeMilestone.Try, model.Milestone);
    }

    [Fact]
    public void Acknowledging_a_decision_retires_the_options_until_the_engine_asks_again()
    {
        var model = FullCycle();
        Assert.True(model.DecisionPending);

        model.AcknowledgeDecision();

        Assert.False(model.DecisionPending);
        Assert.Empty(model.DecisionOptions);
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

    /// <summary>
    /// FORGE-09: <c>team.reopened</c> tells the wizard where the session the engine found or
    /// rebuilt stands — <c>test</c> is the dry pause, opened without an engine — and whether
    /// it was rebuilt. Until it arrives, the model says nothing about it.
    /// </summary>
    [Fact]
    public void A_team_reopened_event_records_the_state_and_whether_it_was_rebuilt()
    {
        var model = new ForgeSessionModel();
        Assert.Null(model.ReopenedState);
        Assert.Null(model.ReopenedRebuilt);

        model.Feed(Event("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}"""));
        model.Feed(Event("""{"v":2,"seq":2,"ts":"t","kind":"team.reopened","slug":"veille","dir":"/ws/.orkeon/forge/veille","path":"/teams/veille","state":"test","rebuilt":true,"brief":"derived"}"""));
        model.Feed(Event("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""));

        Assert.Equal("veille", model.Slug);
        Assert.Equal("/ws/.orkeon/forge/veille", model.Directory);
        // An engine that sends no id — or one that is not an id — leaves the model without one.
        Assert.Null(model.SessionId);
        Assert.Equal("test", model.ReopenedState);
        Assert.True(model.ReopenedRebuilt);
        Assert.Equal("paused", model.FinishedStatus);
    }

    /// <summary>
    /// STUDIO-26, D-03: once the promotion is written the engine renames the session folder after
    /// the team, and <c>session.renamed</c> moves the model with it — the slug the next resume
    /// names, and the folder the generated definition is read from. The promotion it follows
    /// stays exactly as the stream said it.
    /// </summary>
    [Fact]
    public void A_session_renamed_event_moves_the_slug_and_the_directory()
    {
        var model = new ForgeSessionModel();
        model.Feed(Event("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"resumer-les-offres","id":"6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f","dir":"/ws/.orkeon/forge/resumer-les-offres","format":"yaml","resumed":true}"""));
        model.Feed(Event("""{"v":2,"seq":2,"ts":"t","kind":"promoted","path":"/teams/ma-veille","launcher":"run.sh","updated":false}"""));
        model.Feed(Event("""{"v":2,"seq":3,"ts":"t","kind":"session.renamed","from":"resumer-les-offres","to":"ma-veille","dir":"/ws/.orkeon/forge/ma-veille","suffixed":false}"""));
        model.Feed(Event("""{"v":2,"seq":4,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""));

        Assert.Equal("ma-veille", model.Slug);
        Assert.Equal("/ws/.orkeon/forge/ma-veille", model.Directory);
        Assert.Equal(Guid.Parse("6f1c2a0e-4b7d-4e9a-9f53-1d2c3b4a5e6f"), model.SessionId);
        Assert.Equal("/teams/ma-veille", model.Promotion!.Path);
        Assert.Null(model.LastWarning);
        Assert.Null(model.LastError);
    }

    /// <summary>
    /// An engine that names the new slug but not the folder still moves the model: the folder is
    /// the old one's sibling, under the same session root.
    /// </summary>
    [Fact]
    public void A_session_renamed_event_without_a_dir_puts_the_folder_beside_the_old_one()
    {
        var model = new ForgeSessionModel();
        var root = Path.Combine(Path.GetTempPath(), "ws", ".orkeon", "forge");
        model.Feed(Event($$"""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":{{System.Text.Json.JsonSerializer.Serialize(Path.Combine(root, "veille"))}},"format":"yaml","resumed":true}"""));
        model.Feed(Event("""{"v":2,"seq":2,"ts":"t","kind":"session.renamed","from":"veille","to":"ma-veille"}"""));

        Assert.Equal("ma-veille", model.Slug);
        Assert.Equal(Path.Combine(root, "ma-veille"), model.Directory);
    }

    /// <summary>
    /// D-05: a rename the disk refused arrives as a <c>warning</c> — kept apart from the errors, so
    /// the screen can say it without reading the adoption as failed.
    /// </summary>
    [Fact]
    public void A_warning_is_kept_apart_from_the_errors()
    {
        var model = new ForgeSessionModel();
        model.Feed(Event("""{"v":2,"seq":1,"ts":"t","kind":"promoted","path":"/teams/ma-veille","launcher":"run.sh"}"""));
        model.Feed(Event("""{"v":2,"seq":2,"ts":"t","kind":"warning","code":"FORGE-SESSION-NOT-RENAMED","message":"The session folder 'veille' keeps its name."}"""));
        model.Feed(Event("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""));

        Assert.Equal("FORGE-SESSION-NOT-RENAMED", model.LastWarning!.Code);
        Assert.Equal("The session folder 'veille' keeps its name.", model.LastWarning.Message);
        Assert.Null(model.LastError);
        Assert.Equal("ready", model.FinishedStatus);
    }
}
