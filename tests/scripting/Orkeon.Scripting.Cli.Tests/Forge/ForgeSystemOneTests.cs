using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The forge's own System One (#435): advice on the brief, the process, the trial and each criterion, which never
/// moves the cycle; off by <c>--no-decision</c>, the settings or no provider, and orthogonal to the crew's circuit.
/// </summary>
public sealed class ForgeSystemOneTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-system-one-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private const string Need = "Résumer chaque matin les offres du fournisseur";

    /// <summary>One full cycle, in a workspace of its own so two cycles can be compared file by file.</summary>
    private sealed class Cycle
    {
        public required ForgeSession Session { get; init; }
        public required ScriptedAssistant Assistant { get; init; }
        public required StringWriter Output { get; init; }
        public required ForgeEngineResult Result { get; init; }

        public IReadOnlyList<JsonElement> Events() =>
        [
            .. Output.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonElement.Parse(line)),
        ];

        public IReadOnlyList<JsonElement> Events(string kind) =>
            [.. Events().Where(e => e.GetProperty("kind").GetString() == kind)];

        public string File(string relative) =>
            System.IO.File.ReadAllText(Path.Combine(Session.Directory, relative));

        /// <summary>Every file of the rendered crew, by its path inside the crew folder.</summary>
        public IReadOnlyList<(string Path, string Text)> Crew()
        {
            var root = Path.Combine(Session.Directory, ForgeYamlRenderer.CrewDirectoryName);
            return
            [
                .. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
                    .Select(f => (Path.GetRelativePath(root, f), System.IO.File.ReadAllText(f))),
            ];
        }

        /// <summary>The assistant's requests as the assistant reads them, one JSON line each.</summary>
        public IReadOnlyList<string> Requests() =>
            [.. Assistant.Requests.Select(r => JsonSerializer.Serialize(r))];
    }

    private async Task<Cycle> RunAsync(
        string name,
        ForgeSystemOne? systemOne,
        bool auto = false,
        IForgeTestBench? bench = null,
        FakeJudge? judge = null,
        ForgeBudget? budget = null,
        string crewDecision = ForgeCrewDecision.None,
        string blueprint = ForgeDocuments.ValidBlueprint,
        DecisionCircuit? host = null,
        ForgeSession? session = null)
    {
        session ??= ForgeSession.Create(Path.Combine(_workspace, name), "veille", budget: budget, crewDecision: crewDecision);
        var assistant = new ScriptedAssistant().SubmitsBrief(ForgeDocuments.ValidBrief).SubmitsBlueprint(blueprint);
        var channel = auto ? new ScriptedUserChannel() : new ScriptedUserChannel().Decides("accept");
        var output = new StringWriter();
        var events = new ForgeEventWriter(output, new FakeOrkeonClock()) { SystemOne = systemOne };
        var engine = new ForgeEngine(
            session,
            events,
            [
                new BriefStage(assistant, channel, Need, autoConfirmFolders: auto, systemOne: systemOne),
                new BlueprintStage(assistant, systemOne),
                new RenderStage(),
                new ValidateStage(ForgeDocuments.KnownTools),
                new TestStage(bench ?? new FakeTestBench().Succeeds(), new ForgeRunObserver(), host, systemOne),
                new DiagnoseStage(judge ?? new FakeJudge().Approves(), systemOne),
                new VerdictStage(auto, channel, ForgeDocuments.KnownTools),
            ],
            new FakeOrkeonClock());
        var result = await engine.RunAsync(cancellationToken: TestContext.Current.CancellationToken);
        return new Cycle { Session = session, Assistant = assistant, Output = output, Result = result };
    }

    private static StubDecisionModel StubChoosing(string process, double p) => new()
    {
        Choice = new ChoiceAnswer
        {
            Choice = process,
            Confidence = p,
            Probabilities = ImmutableDictionary.CreateRange([KeyValuePair.Create(process, p), KeyValuePair.Create("other", 1 - p)]),
        },
    };

    private static ForgeProcessHint? HintOf(Cycle cycle) =>
        cycle.Assistant.Requests.Single(r => r.Phase == ForgeAssistantPhase.Blueprint).ProcessHint;

    // --- the circuit -------------------------------------------------------------------------

    public static TheoryData<string, string?, bool, string> Causes => new()
    {
        { "switch", null, true, "switch" },
        { "settings", "settings", false, "settings" },
        { "no-provider", "no-provider", false, "no-provider" },
    };

    [Theory]
    [MemberData(nameof(Causes))]
    public void The_forge_circuit_is_off_by_each_cause(string name, string? hostCause, bool noDecision, string cause)
    {
        var services = new ServiceCollection();
        if (hostCause != "no-provider")
            services.AddSingleton<IDecisionModel>(new StubDecisionModel());
        if (hostCause == "settings")
            services.AddSingleton(DecisionCircuit.OffBy(DecisionCircuitCause.Settings));

        var circuit = ForgeSystemOne.Resolve(noDecision, services.BuildServiceProvider());

        Assert.False(circuit.Enabled, name);
        Assert.Equal(cause, circuit.Cause);
    }

    [Fact]
    public void The_forge_circuit_is_on_when_a_decision_model_is_registered()
    {
        var services = new ServiceCollection().AddSingleton<IDecisionModel>(new StubDecisionModel());

        var circuit = ForgeSystemOne.Resolve(noDecision: false, services.BuildServiceProvider());

        Assert.True(circuit.Enabled);
        Assert.Null(circuit.Cause);
    }

    [Fact]
    public void The_switch_turns_the_forge_off_without_touching_the_hosts_circuit()
    {
        var services = new ServiceCollection().AddSingleton<IDecisionModel>(new StubDecisionModel()).BuildServiceProvider();

        ForgeSystemOne.Resolve(noDecision: true, services);

        Assert.True(DecisionCircuit.Of(services).Enabled);
    }

    [Theory]
    [InlineData("on", null)]
    [InlineData("off", "switch")]
    [InlineData("off", "settings")]
    [InlineData("off", "no-provider")]
    public async Task Session_started_names_the_forge_circuit_and_its_cause(string circuit, string? cause)
    {
        var systemOne = cause is null ? ForgeSystemOne.On(new StubDecisionModel()) : ForgeSystemOne.Off(cause);

        var cycle = await RunAsync("started", systemOne);

        var started = Assert.Single(cycle.Events("session.started"));
        Assert.Equal(circuit, started.GetProperty("forgeCircuit").GetString());
        Assert.Equal(cause, started.TryGetProperty("forgeCircuitCause", out var c) ? c.GetString() : null);
    }

    // --- off: nothing changes ------------------------------------------------------------------

    [Fact]
    public async Task An_off_forge_asks_the_decision_model_nothing()
    {
        var model = new StubDecisionModel();
        var services = new ServiceCollection().AddSingleton<IDecisionModel>(model).BuildServiceProvider();

        await RunAsync("off", ForgeSystemOne.Resolve(noDecision: true, services));

        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task An_off_forge_writes_no_system_one_line()
    {
        var cycle = await RunAsync("off", ForgeSystemOne.Off("switch"));

        Assert.Empty(cycle.Events(ForgeSystemOne.AnsweredKind));
    }

    [Fact]
    [Trait("BR", "CH-435")]
    public async Task An_off_forge_feeds_the_assistant_and_writes_the_files_of_a_forge_before_the_circuit()
    {
        // Arrange: the switch, the cause an off forge is most often off by.
        var off = await RunAsync("off", ForgeSystemOne.Off("switch"));

        // Assert: the assistant's inputs, built as the real assistant builds them, hold no processHint key and no
        // other field beyond those of a forge before #435.
        Assert.Equal([ForgeAssistantPhase.Brief, ForgeAssistantPhase.Blueprint], off.Assistant.Requests.Select(r => r.Phase));
        foreach (var request in off.Assistant.Requests)
        {
            Assert.Null(request.ProcessHint);
            var inputs = JsonElement.Parse(ForgeCrewAssistant.BuildInputsJson(request, off.Session, [], [], "fr"));
            Assert.False(inputs.TryGetProperty("processHint", out _));
            Assert.Equal(OffCapture.InputKeys[request.Phase], inputs.EnumerateObject().Select(p => p.Name));
        }

        // Assert: the files, against the fixed capture of an off forge.
        Assert.Equal(OffCapture.Brief.ReplaceLineEndings(), off.File(ForgeSession.BriefFileName).ReplaceLineEndings());
        Assert.Equal(OffCapture.Blueprint.ReplaceLineEndings(), off.File(ForgeSession.BlueprintFileName).ReplaceLineEndings());
        Assert.Equal(
            OffCapture.Crew.Select(f => (f.Path, f.Text.ReplaceLineEndings())),
            off.Crew().Select(f => (f.Path.Replace('\\', '/'), f.Text.ReplaceLineEndings())));
        Assert.Equal(OffCapture.Verdict.ReplaceLineEndings(), off.File(ForgeSession.VerdictFileName).ReplaceLineEndings());
    }

    /// <summary>
    /// The files an off forge writes, captured once from the cycle <see cref="RunAsync"/> drives (FakeJudge approving,
    /// FakeTestBench succeeding): none carries a System One field, and an off circuit must keep writing exactly these,
    /// so any change on the off path shows here. The input keys are those the assistant read before #435, whose only
    /// addition to them is <c>processHint</c>.
    /// </summary>
    private static class OffCapture
    {
        public const string Brief = """
            {
              "goal": "R\u00E9sumer chaque matin les nouvelles offres du fournisseur",
              "context": "Achat de composants \u00E9lectroniques",
              "inputs": [
                {
                  "name": "supplier_url",
                  "description": "La page des offres",
                  "example": "https://exemple.fr/offres"
                }
              ],
              "expectedOutput": {
                "format": "markdown",
                "description": "Un r\u00E9sum\u00E9 d\u0027une page, sources cit\u00E9es"
              },
              "folders": [
                {
                  "path": "/workspace",
                  "role": "input",
                  "purpose": "Ce que l\u0027\u00E9quipe lit."
                },
                {
                  "path": "/output",
                  "role": "output",
                  "purpose": "O\u00F9 l\u0027\u00E9quipe \u00E9crit ses r\u00E9sultats."
                }
              ],
              "constraints": [
                "moins d\u0027une page",
                "en fran\u00E7ais"
              ],
              "toolHints": [
                "lire une page web"
              ],
              "acceptance": [
                {
                  "id": "A1",
                  "statement": "Le r\u00E9sum\u00E9 cite ses sources",
                  "kind": "must"
                },
                {
                  "id": "A2",
                  "statement": "Moins d\u0027une page",
                  "kind": "should"
                }
              ],
              "sample": {
                "variables": {
                  "supplier_url": "https://exemple.fr/offres"
                },
                "initialContext": "Premier essai"
              },
              "language": "fr"
            }
            """;

        public const string Blueprint = """
            {
              "crew": {
                "name": "veille-fournisseur",
                "goal": "R\u00E9sumer les nouvelles offres",
                "process": "sequential"
              },
              "agents": [
                {
                  "key": "collecteur",
                  "role": "Web Researcher",
                  "goal": "Collecter les offres",
                  "tools": [
                    "web_scrape"
                  ]
                },
                {
                  "key": "redacteur",
                  "role": "Writer",
                  "goal": "R\u00E9diger le r\u00E9sum\u00E9",
                  "tools": [
                    "file_write"
                  ]
                }
              ],
              "tasks": [
                {
                  "key": "collecte",
                  "description": "Collecter les offres du jour",
                  "expectedOutput": "La liste brute",
                  "agent": "collecteur"
                },
                {
                  "key": "resume",
                  "description": "R\u00E9diger le r\u00E9sum\u00E9",
                  "expectedOutput": "Le r\u00E9sum\u00E9 cit\u00E9",
                  "agent": "redacteur",
                  "dependencies": [
                    "collecte"
                  ],
                  "deliverable": "/output/resume.md"
                }
              ],
              "rationale": "Deux r\u00F4les s\u00E9par\u00E9s: collecter puis r\u00E9diger, s\u00E9quentiel."
            }
            """;

        public const string Verdict = """
            {
              "score": 0.9,
              "passing": true,
              "findings": [
                {
                  "id": "F-DELIVERABLE-resume",
                  "severity": "major",
                  "statement": "Task \u0027resume\u0027 promised \u0027/output/resume.md\u0027 and the run did not produce it."
                }
              ],
              "suggestions": [],
              "judge": "llm",
              "passes": [
                {
                  "passing": true,
                  "score": 0.9
                }
              ]
            }
            """;

        /// <summary>Every file of the rendered crew, by its path inside the crew folder; each ends with a line feed.</summary>
        public static readonly IReadOnlyList<(string Path, string Text)> Crew =
        [
            ("agents/collecteur.yaml", """
                role: Web Researcher
                goal: Collecter les offres
                tools:
                - web_scrape
                """ + "\n"),
            ("agents/redacteur.yaml", """
                role: Writer
                goal: Rédiger le résumé
                tools:
                - file_write
                """ + "\n"),
            ("config.yaml", """
                name: veille-fournisseur
                goal: Résumer les nouvelles offres
                process: sequential
                mounts:
                - /workspace
                - /output
                """ + "\n"),
            ("tasks/collecte.yaml", """
                description: Collecter les offres du jour
                expectedOutput: La liste brute
                agent: collecteur
                """ + "\n"),
            ("tasks/resume.yaml", """
                description: Rédiger le résumé
                expectedOutput: Le résumé cité
                agent: redacteur
                dependencies:
                - collecte
                deliverable:
                  path: /output/resume.md
                """ + "\n"),
        ];

        /// <summary>The keys of the assistant's inputs per phase, as an off forge filled them before #435.</summary>
        public static readonly IReadOnlyDictionary<ForgeAssistantPhase, string[]> InputKeys = new Dictionary<ForgeAssistantPhase, string[]>
        {
            [ForgeAssistantPhase.Brief] = ["phase", "language", "message", "history", "readTools", "submitTool"],
            [ForgeAssistantPhase.Blueprint] = ["phase", "language", "history", "brief", "readTools", "crewTools", "submitTool", "crewDecision"],
        };
    }

    [Fact]
    public async Task An_off_forge_passes_the_assistant_no_process_hint()
    {
        var cycle = await RunAsync("off", ForgeSystemOne.Off("settings"));

        Assert.Null(HintOf(cycle));
    }

    // --- on: advice only -------------------------------------------------------------------------

    [Fact]
    public async Task An_on_forge_writes_the_same_verdict_as_an_off_forge()
    {
        var off = await RunAsync("off", ForgeSystemOne.Off("switch"));

        var on = await RunAsync("on", ForgeSystemOne.On(StubChoosing("parallel", 0.9)));

        Assert.Equal(off.File(ForgeSession.VerdictFileName), on.File(ForgeSession.VerdictFileName));
    }

    [Fact]
    public async Task An_on_forge_keeps_the_passing_of_an_off_forge_when_the_judge_rejects()
    {
        var off = await RunAsync("off", ForgeSystemOne.Off("switch"), auto: true, judge: new FakeJudge().Rejects().Rejects().Rejects());
        var model = new StubDecisionModel { Noul = new NoulAnswer { Noul = 0.99 } };

        var on = await RunAsync("on", ForgeSystemOne.On(model), auto: true, judge: new FakeJudge().Rejects().Rejects().Rejects());

        Assert.Equal(
            off.Events("verdict.ready").Select(v => v.GetProperty("passing").GetBoolean()),
            on.Events("verdict.ready").Select(v => v.GetProperty("passing").GetBoolean()));
    }

    [Fact]
    public async Task An_on_forge_reaches_the_same_auto_outcome_as_an_off_forge()
    {
        var off = await RunAsync("off", ForgeSystemOne.Off("switch"), auto: true);

        var on = await RunAsync("on", ForgeSystemOne.On(new StubDecisionModel()), auto: true);

        Assert.Equal(off.Result.Outcome, on.Result.Outcome);
        Assert.Equal(off.Session.Document.State, on.Session.Document.State);
        Assert.Equal(off.Session.Document.Status, on.Session.Document.Status);
    }

    [Fact]
    public async Task An_on_forge_leaves_the_brief_as_the_assistant_submitted_it()
    {
        var off = await RunAsync("off", ForgeSystemOne.Off("switch"));
        var model = new StubDecisionModel { Score = new ScoreAnswer { Score = 0, Confidence = 0.95, Probabilities = ImmutableDictionary<string, double>.Empty.Add("0", 0.95) } };

        var on = await RunAsync("on", ForgeSystemOne.On(model));

        Assert.Equal(off.File(ForgeSession.BriefFileName), on.File(ForgeSession.BriefFileName));
    }

    [Fact]
    public async Task Each_use_asks_once_in_the_order_of_the_stages()
    {
        var cycle = await RunAsync("on", ForgeSystemOne.On(new StubDecisionModel()));

        Assert.Equal(
            ["coverage", "process", "estimate", "criteria", "criteria"],
            cycle.Events(ForgeSystemOne.AnsweredKind).Select(e => e.GetProperty("use").GetString()));
    }

    [Fact]
    public async Task Each_call_writes_exactly_one_system_one_line()
    {
        var model = new StubDecisionModel();

        var cycle = await RunAsync("on", ForgeSystemOne.On(model));

        Assert.Equal(model.Requests.Count, cycle.Events(ForgeSystemOne.AnsweredKind).Count);
    }

    [Fact]
    public async Task A_system_one_line_carries_every_field()
    {
        var cycle = await RunAsync("on", ForgeSystemOne.On(new StubDecisionModel()));

        var line = cycle.Events(ForgeSystemOne.AnsweredKind)[0];
        Assert.Equal("brief", line.GetProperty("stage").GetString());
        Assert.Equal("coverage", line.GetProperty("use").GetString());
        Assert.Equal(JsonValueKind.Object, line.GetProperty("questions").GetProperty("coverage").ValueKind);
        Assert.Equal(2, line.GetProperty("answers").GetProperty("coverage").GetProperty("score").GetDouble());
        Assert.Equal("stub-1", line.GetProperty("model").GetString());
        Assert.Equal(300, line.GetProperty("inputTokens").GetInt64());
        Assert.Equal(0, line.GetProperty("outputTokens").GetInt64());
        Assert.True(line.GetProperty("durationMs").GetInt64() >= 0);
    }

    [Fact]
    public async Task A_criterion_line_names_the_criterion_and_answers_in_the_compact_shape()
    {
        var cycle = await RunAsync("on", ForgeSystemOne.On(new StubDecisionModel { Noul = new NoulAnswer { Noul = 0.2 } }));

        var first = cycle.Events(ForgeSystemOne.AnsweredKind).First(e => e.GetProperty("use").GetString() == "criteria");
        var answer = first.GetProperty("answers").GetProperty("A1");
        Assert.Equal("no", answer.GetProperty("chosen").GetString());
        Assert.Equal(0.2, answer.GetProperty("probability").GetDouble(), 3);
        Assert.Equal(0.8, answer.GetProperty("confidence").GetDouble(), 3);
    }

    [Fact]
    public async Task The_criteria_advice_stays_out_of_the_verdict()
    {
        var cycle = await RunAsync("on", ForgeSystemOne.On(new StubDecisionModel()));

        Assert.DoesNotContain("systemOne", cycle.File(ForgeSession.VerdictFileName), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_estimate_is_asked_before_the_trial_starts()
    {
        var cycle = await RunAsync("on", ForgeSystemOne.On(new StubDecisionModel()));

        var kinds = cycle.Events().Select(e => e.GetProperty("kind").GetString() == ForgeSystemOne.AnsweredKind
            ? e.GetProperty("use").GetString()
            : e.GetProperty("kind").GetString()).ToList();
        Assert.True(kinds.IndexOf("estimate") < kinds.IndexOf("run.started"));
    }

    [Fact]
    public async Task The_coverage_question_reads_the_need_as_the_user_stated_it()
    {
        var model = new StubDecisionModel();

        await RunAsync("on", ForgeSystemOne.On(model));

        var coverage = model.Requests.Single(r => r.Questions.ContainsKey("coverage"));
        Assert.Equal(Need, coverage.State["request"]![0]!.GetValue<string>());
    }

    // --- the process hint ------------------------------------------------------------------------

    [Fact]
    public async Task A_process_suggested_at_p_0_9_reaches_the_assistant()
    {
        var cycle = await RunAsync("on", ForgeSystemOne.On(StubChoosing("parallel", 0.9)));

        Assert.Equal(new ForgeProcessHint("parallel", 0.9), HintOf(cycle));
    }

    [Fact]
    public async Task A_process_suggested_below_0_6_does_not_reach_the_assistant()
    {
        var cycle = await RunAsync("on", ForgeSystemOne.On(StubChoosing("parallel", 0.5)));

        Assert.Null(HintOf(cycle));
    }

    [Fact]
    public async Task A_process_outside_the_offered_ones_does_not_reach_the_assistant()
    {
        var cycle = await RunAsync("on", ForgeSystemOne.On(StubChoosing("delivery", 0.95)));

        Assert.Null(HintOf(cycle));
    }

    [Fact]
    public async Task A_crew_that_decides_is_offered_only_the_processes_that_run_decision_tasks()
    {
        var model = new StubDecisionModel();

        await RunAsync("optional", ForgeSystemOne.On(model), crewDecision: ForgeCrewDecision.Optional,
            blueprint: ForgeCrewDecisionPlanTests.TriageBlueprint, bench: new FakeCircuitRecordingTestBench(),
            judge: new FakeJudge().Approves().Approves(), host: DecisionCircuit.On);

        var process = (ChoiceQuestion)model.Requests.Single(r => r.Questions.ContainsKey("process")).Questions["process"];
        Assert.Equal(["graph", "parallel", "sequential"], process.Criteria.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_crew_that_never_decides_is_offered_every_process()
    {
        var model = new StubDecisionModel();

        await RunAsync("none", ForgeSystemOne.On(model));

        var process = (ChoiceQuestion)model.Requests.Single(r => r.Questions.ContainsKey("process")).Questions["process"];
        Assert.Equal(6, process.Criteria.Count);
    }

    [Fact]
    public async Task A_suggested_process_never_replaces_the_one_the_assistant_chose()
    {
        var cycle = await RunAsync("on", ForgeSystemOne.On(StubChoosing("parallel", 0.95)));

        Assert.Contains("\"process\": \"sequential\"", cycle.File(ForgeSession.BlueprintFileName), StringComparison.Ordinal);
    }

    // --- the budget ------------------------------------------------------------------------------

    [Fact]
    public async Task System_one_tokens_are_charged_to_the_budget()
    {
        var cycle = await RunAsync("on", ForgeSystemOne.On(new StubDecisionModel()));

        Assert.Equal(5 * 300, cycle.Session.Document.Budget.ConsumedSystemOneTokens);
    }

    [Fact]
    public async Task Cost_updated_shows_the_system_one_tokens()
    {
        var cycle = await RunAsync("on", ForgeSystemOne.On(new StubDecisionModel()));

        Assert.Equal(5 * 300, cycle.Events("cost.updated")[^1].GetProperty("systemOneTokens").GetInt64());
    }

    [Fact]
    public async Task System_one_tokens_count_against_max_tokens()
    {
        var budget = new ForgeBudget { MaxTokens = 250 };

        var cycle = await RunAsync("on", ForgeSystemOne.On(new StubDecisionModel()), budget: budget);

        Assert.Equal(ForgeEngineOutcome.BudgetExhausted, cycle.Result.Outcome);
    }

    // --- failures --------------------------------------------------------------------------------

    [Fact]
    public async Task A_failing_decision_model_gives_one_warning_per_call()
    {
        var model = new StubDecisionModel { Failure = new DecisionModelException("unreachable") };

        var cycle = await RunAsync("failing", ForgeSystemOne.On(model));

        Assert.Equal(
            model.Requests.Count,
            cycle.Events("warning").Count(w => w.GetProperty("code").GetString() == ForgeSystemOne.FailedWarning));
    }

    [Fact]
    public async Task A_failing_decision_model_lets_the_cycle_complete()
    {
        var model = new StubDecisionModel { Failure = new DecisionModelException("unreachable") };

        var cycle = await RunAsync("failing", ForgeSystemOne.On(model), auto: true);

        Assert.Equal(nameof(ForgeState.Ready), cycle.Session.Document.State);
    }

    // --- the crew's own circuit (#431 AC11, #433) ------------------------------------------------

    [Fact]
    public async Task An_off_forge_still_produces_a_required_crew()
    {
        var cycle = await RunAsync("required", ForgeSystemOne.Off("switch"), crewDecision: ForgeCrewDecision.Required,
            blueprint: ForgeCrewDecisionPlanTests.TriageBlueprint, bench: new FakeCircuitRecordingTestBench(), host: DecisionCircuit.On);

        Assert.True(File.Exists(Path.Combine(cycle.Session.Directory, ForgeSession.VerdictFileName)));
    }

    [Fact]
    public async Task An_off_forge_leaves_the_trial_of_a_required_crew_unchanged()
    {
        var before = new FakeCircuitRecordingTestBench();
        await RunAsync("before", systemOne: null, crewDecision: ForgeCrewDecision.Required,
            blueprint: ForgeCrewDecisionPlanTests.TriageBlueprint, bench: before, host: DecisionCircuit.On);
        var off = new FakeCircuitRecordingTestBench();

        await RunAsync("off", ForgeSystemOne.Off("switch"), crewDecision: ForgeCrewDecision.Required,
            blueprint: ForgeCrewDecisionPlanTests.TriageBlueprint, bench: off, host: DecisionCircuit.On);

        Assert.Equal(before.Circuits, off.Circuits);
    }

    [Fact]
    public async Task An_off_forge_leaves_the_crew_of_a_required_session_unchanged()
    {
        var before = await RunAsync("before", systemOne: null, crewDecision: ForgeCrewDecision.Required,
            blueprint: ForgeCrewDecisionPlanTests.TriageBlueprint, bench: new FakeCircuitRecordingTestBench(), host: DecisionCircuit.On);

        var off = await RunAsync("off", ForgeSystemOne.Off("switch"), crewDecision: ForgeCrewDecision.Required,
            blueprint: ForgeCrewDecisionPlanTests.TriageBlueprint, bench: new FakeCircuitRecordingTestBench(), host: DecisionCircuit.On);

        Assert.Equal(before.Crew(), off.Crew());
    }

    [Fact]
    public async Task The_trial_of_a_required_crew_still_asks_the_decision_model_when_the_forge_is_off()
    {
        var model = new StubDecisionModel();
        var services = new ServiceCollection().AddSingleton<IDecisionModel>(model).BuildServiceProvider();

        await RunAsync("required", ForgeSystemOne.Resolve(noDecision: true, services), crewDecision: ForgeCrewDecision.Required,
            blueprint: ForgeCrewDecisionPlanTests.TriageBlueprint, bench: new FakeDecidingTestBench(model), host: DecisionCircuit.Of(services));

        Assert.Equal(["urgent"], model.Requests.SelectMany(r => r.Questions.Keys));
    }

    [Fact]
    public async Task An_off_forge_still_tries_an_optional_crew_twice()
    {
        var model = new StubDecisionModel();
        var services = new ServiceCollection().AddSingleton<IDecisionModel>(model).BuildServiceProvider();
        var bench = new FakeCircuitRecordingTestBench();

        await RunAsync("optional", ForgeSystemOne.Resolve(noDecision: true, services), crewDecision: ForgeCrewDecision.Optional,
            blueprint: ForgeCrewDecisionPlanTests.TriageBlueprint, bench: bench, judge: new FakeJudge().Approves().Approves(),
            host: DecisionCircuit.Of(services));

        Assert.Equal(["on", "off"], bench.Circuits.Select(c => ForgeCrewDecision.CircuitName(c!)));
    }

    [Fact]
    public async Task A_resume_after_a_budget_stop_between_passes_asks_no_second_estimate()
    {
        var model = new StubDecisionModel();
        var stopped = await RunAsync("budget", ForgeSystemOne.On(model), crewDecision: ForgeCrewDecision.Optional,
            blueprint: ForgeCrewDecisionPlanTests.TriageBlueprint, bench: new FakeCircuitRecordingTestBench { Tokens = 50_000 },
            budget: new ForgeBudget { MaxTokens = 40_000 }, host: DecisionCircuit.On);
        stopped.Session.Document.Budget = new ForgeBudget { MaxTokens = 1_000_000, ConsumedTokens = stopped.Session.Document.Budget.ConsumedTokens };
        stopped.Session.SetStatus(ForgeSessionStatus.Active);

        var resumed = await RunAsync("budget", ForgeSystemOne.On(model), crewDecision: ForgeCrewDecision.Optional,
            bench: new FakeCircuitRecordingTestBench(), judge: new FakeJudge().Approves().Approves(), host: DecisionCircuit.On,
            session: stopped.Session);

        Assert.DoesNotContain(resumed.Events(ForgeSystemOne.AnsweredKind), e => e.GetProperty("use").GetString() == "estimate");
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task A_resume_after_a_budget_stop_between_passes_asks_no_second_estimate_when_the_host_has_lost_its_model()
    {
        // Arrange
        var model = new StubDecisionModel();
        var stopped = await RunAsync("budget", ForgeSystemOne.On(model), crewDecision: ForgeCrewDecision.Optional,
            blueprint: ForgeCrewDecisionPlanTests.TriageBlueprint, bench: new FakeCircuitRecordingTestBench { Tokens = 50_000 },
            budget: new ForgeBudget { MaxTokens = 40_000 }, host: DecisionCircuit.On);
        stopped.Session.Document.Budget = new ForgeBudget { MaxTokens = 1_000_000, ConsumedTokens = stopped.Session.Document.Budget.ConsumedTokens };
        stopped.Session.SetStatus(ForgeSessionStatus.Active);

        // Act
        var resumed = await RunAsync("budget", ForgeSystemOne.On(model), crewDecision: ForgeCrewDecision.Optional,
            bench: new FakeCircuitRecordingTestBench(), judge: new FakeJudge().Approves().Approves(),
            host: DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider), session: stopped.Session);

        // Assert
        Assert.DoesNotContain(resumed.Events(ForgeSystemOne.AnsweredKind), e => e.GetProperty("use").GetString() == "estimate");
    }

    // --- the criteria of each trial pass (#435) ---------------------------------------------------

    private Task<Cycle> TryOptionalOnAsync(StubDecisionModel model, FakeCircuitRecordingTestBench bench) =>
        RunAsync("optional", ForgeSystemOne.On(model), crewDecision: ForgeCrewDecision.Optional,
            blueprint: ForgeCrewDecisionPlanTests.TriageBlueprint, bench: bench,
            judge: new FakeJudge().Approves().Approves(), host: DecisionCircuit.On);

    private static IReadOnlyList<string?> CriteriaCircuits(Cycle cycle) =>
    [
        .. cycle.Events(ForgeSystemOne.AnsweredKind)
            .Where(e => e.GetProperty("use").GetString() == ForgeSystemOne.UseCriteria)
            .Select(e => e.TryGetProperty("circuit", out var circuit) ? circuit.GetString() : null),
    ];

    [Fact]
    [Trait("BR", "CH-435")]
    public async Task Each_pass_of_an_optional_trial_gets_its_own_criteria_advice_named_by_its_circuit()
    {
        // Arrange
        var bench = new FakeCircuitRecordingTestBench();

        // Act
        var cycle = await TryOptionalOnAsync(new StubDecisionModel(), bench);

        // Assert
        Assert.Equal(["on", "on", "off", "off"], CriteriaCircuits(cycle));
    }

    [Fact]
    [Trait("BR", "CH-435")]
    public async Task The_criteria_advice_of_each_pass_reads_that_passes_output()
    {
        // Arrange
        var model = new StubDecisionModel();

        // Act
        await TryOptionalOnAsync(model, new FakeCircuitRecordingTestBench());

        // Assert
        var outputs = model.Requests
            .Where(r => r.State["output"] is not null)
            .Select(r => r.State["output"]!.GetValue<string>())
            .Distinct();
        Assert.Equal(["Tri par System One.", "Tri par le relais."], outputs);
    }

    [Fact]
    [Trait("BR", "CH-435")]
    public async Task A_failed_pass_of_an_optional_trial_gets_no_criteria_advice()
    {
        // Arrange
        var bench = new FakeCircuitRecordingTestBench { FailsTheOffPass = true };

        // Act
        var cycle = await TryOptionalOnAsync(new StubDecisionModel(), bench);

        // Assert
        Assert.Equal(["on", "on"], CriteriaCircuits(cycle));
    }

    [Fact]
    [Trait("BR", "CH-435")]
    public async Task The_criteria_advice_of_a_one_pass_trial_names_no_circuit()
    {
        // Arrange
        var model = new StubDecisionModel();

        // Act
        var cycle = await RunAsync("one", ForgeSystemOne.On(model), host: DecisionCircuit.On);

        // Assert
        Assert.Equal([null, null], CriteriaCircuits(cycle));
    }

    // --- the forge's own calls name no decision profile ------------------------------------------

    [Fact]
    public async Task The_judge_names_no_profile()
    {
        var provider = new ScriptedLlmProvider().Answers("not json").Answers("not json");
        Assert.True(ForgeBrief.TryParse(ForgeDocuments.ValidBrief, out var brief, out _));

        await new LlmForgeJudge(provider).JudgeAsync(brief!, "output", [], TestContext.Current.CancellationToken);

        Assert.All(provider.ChatConfigs, config => Assert.Null(config?.Profile));
    }

    [Fact]
    public void The_assistant_pack_names_no_profile()
    {
        var pack = ForgePack.Ensure(Directory.CreateDirectory(_workspace).FullName);

        var source = File.ReadAllText(pack);

        Assert.DoesNotContain("llm.profile", source, StringComparison.Ordinal);
    }

    // --- the terminal ----------------------------------------------------------------------------

    [Fact]
    public void The_terminal_shows_the_advice_on_one_line()
    {
        var console = new StringWriter();
        using var renderer = new ForgeTerminalRenderer(console);

        renderer.WriteLine(
            """{"v":2,"seq":4,"ts":"2026-08-19T12:00:00Z","kind":"systemone.answered","stage":"test","use":"estimate","answers":{"estimate":{"chosen":"yes","probability":0.82,"confidence":0.82}},"model":"stub-1","inputTokens":300,"outputTokens":0,"durationMs":12}""");

        Assert.Equal("◇ System One advises (test, estimate): estimate: yes (p 0.82)", console.ToString().TrimEnd());
    }

    [Fact]
    [Trait("BR", "CH-435")]
    public void The_terminal_names_the_pass_a_criteria_line_read()
    {
        // Arrange
        var console = new StringWriter();
        using var renderer = new ForgeTerminalRenderer(console);

        // Act
        renderer.WriteLine(
            """{"v":2,"seq":9,"ts":"2026-08-19T12:00:00Z","kind":"systemone.answered","stage":"diagnose","use":"criteria","circuit":"off","answers":{"A1":{"chosen":"yes","probability":0.9,"confidence":0.9}},"model":"stub-1","inputTokens":300,"outputTokens":0,"durationMs":12}""");

        // Assert
        Assert.Equal("◇ System One advises (diagnose, criteria, off pass): A1: yes (p 0.90)", console.ToString().TrimEnd());
    }
}
