using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Crew.Skills;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Tests.Crew.Skills;

/// <summary>The skills block of a task's prompt, a fixed list or the decision model's pick (#434).</summary>
public sealed class AgentSkillsResolverTests
{
    private const string Library = AgentSkillsOptions.DefaultLibrary;
    private const string Request = "Review the pull request for security issues.";

    private readonly FakeFileSystemService _files = new FakeFileSystemService().AddMount("/workspace");
    private readonly FakeDecisionModel _model = new();
    private readonly RecordingLogger _logger = new();

    private AgentSkillsResolver Resolver(DecisionCircuit? circuit = null, bool withModel = true, AgentSkillsOptions? options = null)
    {
        var services = new ServiceCollection();
        if (withModel)
            services.AddSingleton<IDecisionModel>(_model);
        if (circuit is not null)
            services.AddSingleton(circuit);
        return new AgentSkillsResolver(_files, services.BuildServiceProvider(), options ?? new AgentSkillsOptions(), _logger);
    }

    private static DomainAgent Agent(AgentSkills? skills) =>
        new AgentBuilder().Role("Reviewer").Goal("Review code").Skills(skills).Build();

    private static AgentSkills Auto(params string[] fallback) => new() { Auto = true, Fallback = fallback };

    private void AddLibrary(int count)
    {
        for (var i = 1; i <= count; i++)
            _files.AddFile($"{Library}/skill-{i}.md", $"---\nname: skill {i}\ndescription: what skill {i} is for\n---\nBODY OF SKILL {i}");
    }

    private void Answer(string choice, params (string Option, double Share)[] shares) =>
        _model.Respond = _ => new DecisionResponse
        {
            Model = "fake",
            Answers = ImmutableDictionary<string, DecisionAnswer>.Empty.Add(AgentSkillsResolver.QuestionName, new ChoiceAnswer
            {
                Choice = choice,
                Confidence = 0.9,
                Probabilities = shares.ToImmutableDictionary(s => s.Option, s => s.Share),
            }),
        };

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldCarryEveryListedSkillInOrder_WhenTheAgentListsSkills()
    {
        // Arrange
        _files.AddFile("/workspace/skills/review.md", "Check the inputs.").AddFile("/workspace/skills/tests.md", "Run the tests.");
        var agent = Agent(new AgentSkills { Paths = ["/workspace/skills/review.md", "/workspace/skills/tests.md"] });

        // Act
        var resolution = await Resolver().ResolveAsync(agent, "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolution);
        Assert.StartsWith("## Skills", resolution.Block, StringComparison.Ordinal);
        Assert.True(resolution.Block.IndexOf("Check the inputs.", StringComparison.Ordinal) < resolution.Block.IndexOf("Run the tests.", StringComparison.Ordinal));
        Assert.Equal(AgentSkillsModes.List, resolution.Notice.Mode);
        Assert.Equal(["/workspace/skills/review.md", "/workspace/skills/tests.md"], resolution.Notice.Skills);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldReturnNothing_WhenTheAgentDeclaresNoSkill()
    {
        // Arrange
        var agent = Agent(null);

        // Act
        var resolution = await Resolver().ResolveAsync(agent, "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(resolution);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldCutASkillAtItsCap_WhenItIsLongerThanMaxSkillChars()
    {
        // Arrange
        _files.AddFile("/workspace/skills/long.md", new string('a', 50) + new string('b', 50));
        var agent = Agent(new AgentSkills { Paths = ["/workspace/skills/long.md"] });

        // Act
        var resolution = await Resolver(options: new AgentSkillsOptions { MaxSkillChars = 50 })
            .ResolveAsync(agent, "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolution);
        Assert.Contains(new string('a', 50), resolution.Block, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 50) + "b", resolution.Block, StringComparison.Ordinal);
        Assert.Contains("[skill cut: the first 50 characters shown]", resolution.Block, StringComparison.Ordinal);
        Assert.Equal(["/workspace/skills/long.md"], resolution.Notice.Cut);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldLeaveOutASkill_WhenTheBlockCapIsSpent()
    {
        // Arrange: the heading, the intro, the first skill's title line and its text spend 300 characters but some
        // twenty, too few for the second skill's title line.
        _files.AddFile("/workspace/skills/one.md", new string('x', 100)).AddFile("/workspace/skills/two.md", "second skill");
        var agent = Agent(new AgentSkills { Paths = ["/workspace/skills/one.md", "/workspace/skills/two.md"] });

        // Act
        var resolution = await Resolver(options: new AgentSkillsOptions { MaxBlockChars = 300 })
            .ResolveAsync(agent, "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolution);
        Assert.DoesNotContain("second skill", resolution.Block, StringComparison.Ordinal);
        Assert.Equal(["/workspace/skills/one.md"], resolution.Notice.Skills);
        Assert.Equal(["/workspace/skills/two.md"], resolution.Notice.Cut);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldLeaveOutAMissingSkillWithAWarning_WhenItsFileCannotBeRead()
    {
        // Arrange
        _files.AddFile("/workspace/skills/review.md", "Check the inputs.");
        var agent = Agent(new AgentSkills { Paths = ["/workspace/skills/gone.md", "/workspace/skills/review.md"] });

        // Act
        var resolution = await Resolver().ResolveAsync(agent, "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolution);
        Assert.Equal(["/workspace/skills/review.md"], resolution.Notice.Skills);
        Assert.Equal(["/workspace/skills/gone.md"], resolution.Notice.Cut);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("/workspace/skills/gone.md", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldOfferOneLinePerSkillAndNeverABody_WhenAnAutoAgentAsks()
    {
        // Arrange
        AddLibrary(2);
        Answer(AgentSkillsResolver.NoSkillOption, ("none", 1.0));

        // Act
        await Resolver().ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(_model.Requests);
        var question = Assert.IsType<ChoiceQuestion>(request.Questions[AgentSkillsResolver.QuestionName]);
        Assert.Equal("skill 1: what skill 1 is for", question.Criteria["s1"]!.GetValue<string>());
        Assert.Equal("skill 2: what skill 2 is for", question.Criteria["s2"]!.GetValue<string>());
        Assert.True(question.Criteria.ContainsKey(AgentSkillsResolver.NoSkillOption));
        Assert.DoesNotContain("BODY OF SKILL", request.State.ToJsonString() + question.Criteria.Values.Select(v => v!.ToJsonString()).Aggregate(string.Concat), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldPickAtMostThreeSkillsByShare_WhenTheModelAnswers()
    {
        // Arrange: shares as a model gives them, unnormalised: four reach the floor, the three largest are kept.
        AddLibrary(5);
        Answer("s4", ("s1", 0.3), ("s2", 0.5), ("s3", 0.7), ("s4", 0.9), ("s5", 0.1));

        // Act
        var resolution = await Resolver().ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolution);
        Assert.Equal(AgentSkillsModes.Auto, resolution.Notice.Mode);
        Assert.Equal([$"{Library}/skill-4.md", $"{Library}/skill-3.md", $"{Library}/skill-2.md"], resolution.Notice.Skills);
        Assert.DoesNotContain($"{Library}/skill-1.md", resolution.Notice.Skills);
        Assert.Equal(5, resolution.Notice.Offered);
        Assert.Contains("BODY OF SKILL 4", resolution.Block, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldCarryNoSkill_WhenTheModelAnswersThatNoneFits()
    {
        // Arrange
        AddLibrary(2);
        Answer(AgentSkillsResolver.NoSkillOption, ("s1", 0.4), ("none", 0.6));

        // Act
        var resolution = await Resolver().ResolveAsync(Agent(Auto("/workspace/skills/fallback.md")), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolution);
        Assert.Equal(AgentSkillsModes.Auto, resolution.Notice.Mode);
        Assert.Empty(resolution.Notice.Skills);
        Assert.Equal(string.Empty, resolution.Block);
    }

    public static TheoryData<string> GarbageAnswers => ["no answer", "another kind", "an unknown option", "no share"];

    [Theory]
    [Trait("BR", "CH-434")]
    [MemberData(nameof(GarbageAnswers))]
    public async System.Threading.Tasks.Task ShouldUseTheFallback_WhenTheModelGivesNoUsableChoice(string garbage)
    {
        // Arrange
        AddLibrary(2);
        _files.AddFile("/workspace/skills/review.md", "Check the inputs.");
        switch (garbage)
        {
            case "another kind":
                _model.Respond = _ => new DecisionResponse
                {
                    Model = "fake",
                    Answers = ImmutableDictionary<string, DecisionAnswer>.Empty.Add(AgentSkillsResolver.QuestionName, new NoulAnswer { Noul = 0.9 }),
                };
                break;
            case "an unknown option":
                Answer("s42", ("s42", 1.0));
                break;
            case "no share":
                Answer("s1");
                break;
        }

        // Act
        var resolution = await Resolver().ResolveAsync(
            Agent(Auto("/workspace/skills/review.md")), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_model.Requests);
        Assert.NotNull(resolution);
        Assert.Equal(AgentSkillsModes.Fallback, resolution.Notice.Mode);
        Assert.Equal(AgentSkillsReasons.PickFailed, resolution.Notice.Reason);
        Assert.Equal(["/workspace/skills/review.md"], resolution.Notice.Skills);
        Assert.Contains("Check the inputs.", resolution.Block, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldReadASkillNoFurtherThanItsCap_WhenTheFileIsLonger()
    {
        // Arrange: a skill file far longer than the cap.
        var reads = new FakeCountingReadFileSystemService(new FakeFileSystemService().AddMount("/workspace")
            .AddFile("/workspace/skills/long.md", "  " + new string('a', 50) + new string('b', 100_000)));
        var resolver = new AgentSkillsResolver(reads, new ServiceCollection().BuildServiceProvider(),
            new AgentSkillsOptions { MaxSkillChars = 50 }, _logger);
        var agent = Agent(new AgentSkills { Paths = ["/workspace/skills/long.md"] });

        // Act
        var resolution = await resolver.ResolveAsync(agent, "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolution);
        Assert.Contains(new string('a', 50), resolution.Block, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 50) + "b", resolution.Block, StringComparison.Ordinal);
        Assert.Contains("[skill cut: the first 50 characters shown]", resolution.Block, StringComparison.Ordinal);
        Assert.Equal(["/workspace/skills/long.md"], resolution.Notice.Cut);
        Assert.Equal(0, reads.WholeReads);
        Assert.True(reads.BytesRead < 10_000, $"read {reads.BytesRead} bytes of a 100,052-byte skill");
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldNotSayASkillIsCut_WhenOnlyWhiteSpaceFollowsTheCap()
    {
        // Arrange
        _files.AddFile("/workspace/skills/exact.md", new string('a', 50) + "\n\n  \n");
        var agent = Agent(new AgentSkills { Paths = ["/workspace/skills/exact.md"] });

        // Act
        var resolution = await Resolver(options: new AgentSkillsOptions { MaxSkillChars = 50 })
            .ResolveAsync(agent, "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolution);
        Assert.DoesNotContain("[skill cut", resolution.Block, StringComparison.Ordinal);
        Assert.Empty(resolution.Notice.Cut);
    }

    [Theory]
    [Trait("BR", "CH-434")]
    [InlineData(DecisionCircuitCause.Switch)]
    [InlineData(DecisionCircuitCause.Settings)]
    public async System.Threading.Tasks.Task ShouldMakeNoDecisionModelCallAndUseTheFallback_WhenTheCircuitIsOff(DecisionCircuitCause cause)
    {
        // Arrange
        AddLibrary(2);
        _files.AddFile("/workspace/skills/review.md", "Check the inputs.");
        var circuit = DecisionCircuit.OffBy(cause);

        // Act
        var resolution = await Resolver(circuit).ResolveAsync(
            Agent(Auto("/workspace/skills/review.md")), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_model.Requests);
        Assert.NotNull(resolution);
        Assert.Equal(AgentSkillsModes.Fallback, resolution.Notice.Mode);
        Assert.Equal(["/workspace/skills/review.md"], resolution.Notice.Skills);
        Assert.Equal(circuit.OffReason, resolution.Notice.Detail);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldCarryNoSkillWithoutError_WhenTheCircuitIsOffAndTheAgentHasNoFallback()
    {
        // Arrange
        AddLibrary(2);

        // Act
        var resolution = await Resolver(DecisionCircuit.Off).ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_model.Requests);
        Assert.NotNull(resolution);
        Assert.Equal(AgentSkillsModes.Fallback, resolution.Notice.Mode);
        Assert.Empty(resolution.Notice.Skills);
        Assert.Equal(string.Empty, resolution.Block);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldUseTheFallback_WhenNoDecisionProviderIsRegistered()
    {
        // Arrange
        AddLibrary(1);
        _files.AddFile("/workspace/skills/review.md", "Check the inputs.");

        // Act
        var resolution = await Resolver(withModel: false).ResolveAsync(
            Agent(Auto("/workspace/skills/review.md")), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolution);
        Assert.Equal(AgentSkillsModes.Fallback, resolution.Notice.Mode);
        Assert.Equal(DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider).OffReason, resolution.Notice.Detail);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldWarnOnce_WhenTheCircuitIsOffForSeveralTasks()
    {
        // Arrange
        var resolver = Resolver(DecisionCircuit.Off);
        await resolver.ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Act
        await resolver.ResolveAsync(Agent(Auto()), "t2", Request, TestContext.Current.CancellationToken);

        // Assert
        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(DecisionCircuit.Off.OffReason!, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldUseTheFallback_WhenTheDecisionModelFails()
    {
        // Arrange
        AddLibrary(2);
        _files.AddFile("/workspace/skills/review.md", "Check the inputs.");
        _model.Failure = new DecisionModelException("provider down");

        // Act
        var resolution = await Resolver().ResolveAsync(
            Agent(Auto("/workspace/skills/review.md")), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolution);
        Assert.Equal(AgentSkillsModes.Fallback, resolution.Notice.Mode);
        Assert.Equal(["/workspace/skills/review.md"], resolution.Notice.Skills);
        Assert.Contains("provider down", resolution.Notice.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldMakeNoCallAndUseTheFallback_WhenTheLibraryHoldsNoSkill()
    {
        // Arrange
        _files.AddFile("/workspace/skills/review.md", "Check the inputs.");

        // Act
        var resolution = await Resolver().ResolveAsync(
            Agent(Auto("/workspace/skills/review.md")), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_model.Requests);
        Assert.NotNull(resolution);
        Assert.Equal(AgentSkillsModes.Fallback, resolution.Notice.Mode);
        Assert.Contains(Library, resolution.Notice.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async System.Threading.Tasks.Task ShouldMakeNoDecisionModelCall_WhenTheRunScopesCircuitIsOffAndTheContainersIsOn()
    {
        // Arrange
        AddLibrary(2);
        var resolver = Resolver(DecisionCircuit.On);

        // Act
        var resolution = await resolver.ResolveAsync(
            Agent(Auto()), "t1", Request, DecisionCircuit.Off, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_model.Requests);
        Assert.Equal(DecisionCircuit.Off.OffReason, resolution!.Notice.Detail);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async System.Threading.Tasks.Task ShouldMakeNoDecisionModelCall_WhenTheRunInProgressIsOffAndTheContainersIsOn()
    {
        // Arrange
        AddLibrary(2);
        var resolver = Resolver(DecisionCircuit.On);
        using var run = DecisionCircuitScope.Begin(DecisionCircuit.Off);

        // Act
        await resolver.ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldKeepTheWholeBlockWithinItsCap_WhenTitleLinesAndCutNotesAreCounted()
    {
        // Arrange
        _files.AddFile("/workspace/skills/a.md", new string('a', 200)).AddFile("/workspace/skills/b.md", new string('b', 200))
            .AddFile("/workspace/skills/c.md", new string('c', 200));
        var agent = Agent(new AgentSkills { Paths = ["/workspace/skills/a.md", "/workspace/skills/b.md", "/workspace/skills/c.md"] });

        // Act
        var resolution = await Resolver(options: new AgentSkillsOptions { MaxBlockChars = 500 })
            .ResolveAsync(agent, "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolution);
        Assert.InRange(resolution.Block.Length, 1, 500);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldNeverSplitASurrogatePair_WhenASkillIsCutAtItsCap()
    {
        // Arrange: the 50th character is the first half of an emoji.
        _files.AddFile("/workspace/skills/long.md", new string('a', 49) + "\U0001F600" + new string('b', 50));
        var agent = Agent(new AgentSkills { Paths = ["/workspace/skills/long.md"] });

        // Act
        var resolution = await Resolver(options: new AgentSkillsOptions { MaxSkillChars = 50 })
            .ResolveAsync(agent, "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Record.Exception(() => StrictUtf8.GetByteCount(resolution!.Block)));
    }

    private static readonly System.Text.UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldWarnOnce_WhenTheLibraryHoldsNoSkillForSeveralTasks()
    {
        // Arrange
        var resolver = Resolver();
        await resolver.ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Act
        await resolver.ResolveAsync(Agent(Auto()), "t2", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(Library, Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning).Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldWarnWithTheFault_WhenTheLibraryCannotBeRead()
    {
        // Arrange: a library outside every mount.
        var resolver = Resolver(options: new AgentSkillsOptions { Library = "/elsewhere/skills" });

        // Act
        await resolver.ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("No mount for virtual path '/elsewhere/skills'",
            Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning).Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldGiveTheReasonCircuitOff_WhenTheCircuitIsOff()
    {
        // Arrange
        AddLibrary(2);

        // Act
        var resolution = await Resolver(DecisionCircuit.Off).ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentSkillsReasons.CircuitOff, resolution!.Notice.Reason);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldGiveTheReasonEmptyLibrary_WhenTheLibraryHoldsNoSkill()
    {
        // Arrange
        var resolver = Resolver();

        // Act
        var resolution = await resolver.ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentSkillsReasons.EmptyLibrary, resolution!.Notice.Reason);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldGiveTheReasonPickFailed_WhenTheDecisionModelFails()
    {
        // Arrange
        AddLibrary(2);
        _model.Failure = new DecisionModelException("provider down");

        // Act
        var resolution = await Resolver().ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentSkillsReasons.PickFailed, resolution!.Notice.Reason);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldOfferTwentyFourSkillsAtMost_WhenTheLibraryHoldsMore()
    {
        // Arrange
        AddLibrary(30);
        Answer(AgentSkillsResolver.NoSkillOption, ("none", 1.0));

        // Act
        var resolution = await Resolver().ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(24, resolution!.Notice.Offered);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldOfferTheSkillsInNameOrder_WhenTheyWereWrittenInAnotherOrder()
    {
        // Arrange
        _files.AddFile($"{Library}/zeta.md", "Zeta body.").AddFile($"{Library}/alpha.md", "Alpha body.");
        Answer(AgentSkillsResolver.NoSkillOption, ("none", 1.0));

        // Act
        await Resolver().ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        var question = Assert.IsType<ChoiceQuestion>(Assert.Single(_model.Requests).Questions[AgentSkillsResolver.QuestionName]);
        Assert.StartsWith("alpha", question.Criteria["s1"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldStopWithTheCancellationAndNoFallback_WhenTheTaskIsCancelled()
    {
        // Arrange: the task is cancelled while the decision model is asked, as a provider call that honours the token.
        AddLibrary(2);
        _files.AddFile("/workspace/skills/review.md", "Check the inputs.");
        using var cancellation = new CancellationTokenSource();
        _model.OnDecide = _ => cancellation.Cancel();
        _model.Failure = new OperationCanceledException(cancellation.Token);
        var resolver = Resolver();

        // Act
        var exception = await Record.ExceptionAsync(() => resolver.ResolveAsync(Agent(Auto("/workspace/skills/review.md")), "t1", Request, cancellation.Token));

        // Assert
        Assert.Single(_model.Requests);
        Assert.IsType<OperationCanceledException>(exception, exactMatch: false);
        Assert.DoesNotContain(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldWarnAgainInTheNextRun_WhenTheCircuitIsOff()
    {
        // Arrange
        var resolver = Resolver(DecisionCircuit.Off);
        using (SystemOneRunScope.Begin(Orkeon.Domain.Crew.CrewDecisionMode.Unspecified))
            await resolver.ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);
        using var next = SystemOneRunScope.Begin(Orkeon.Domain.Crew.CrewDecisionMode.Unspecified);

        // Act
        await resolver.ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, _logger.Entries.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldWarnAgainInTheNextRun_WhenTheLibraryHoldsNoSkill()
    {
        // Arrange
        var resolver = Resolver();
        using (SystemOneRunScope.Begin(Orkeon.Domain.Crew.CrewDecisionMode.Unspecified))
            await resolver.ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);
        using var next = SystemOneRunScope.Begin(Orkeon.Domain.Crew.CrewDecisionMode.Unspecified);

        // Act
        await resolver.ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, _logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains(Library, StringComparison.Ordinal)));
    }

    public static TheoryData<string> FailedPicks => ["a failed call", "no usable choice"];

    [Theory]
    [Trait("BR", "CH-434")]
    [MemberData(nameof(FailedPicks))]
    public async System.Threading.Tasks.Task ShouldWarnOncePerRunAndTellEachTask_WhenThePickFailsForSeveralTasks(string failure)
    {
        // Arrange
        AddLibrary(2);
        if (failure == "a failed call")
            _model.Failure = new DecisionModelException("provider down");
        else
            Answer("s42", ("s42", 1.0));
        var resolver = Resolver();
        var notices = new List<AgentSkillsNotice>();
        using (SystemOneRunScope.Begin(Orkeon.Domain.Crew.CrewDecisionMode.Unspecified))
        {
            notices.Add((await resolver.ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken))!.Notice);
            notices.Add((await resolver.ResolveAsync(Agent(Auto()), "t2", Request, TestContext.Current.CancellationToken))!.Notice);
        }
        using var next = SystemOneRunScope.Begin(Orkeon.Domain.Crew.CrewDecisionMode.Unspecified);

        // Act
        notices.Add((await resolver.ResolveAsync(Agent(Auto()), "t1", Request, TestContext.Current.CancellationToken))!.Notice);

        // Assert: one warning in each run, and every task's notice says why.
        Assert.Equal(3, _model.Requests.Count);
        Assert.Equal(2, _logger.Entries.Count(e => e.Level == LogLevel.Warning));
        Assert.All(notices, n =>
        {
            Assert.Equal(AgentSkillsReasons.PickFailed, n.Reason);
            Assert.False(string.IsNullOrEmpty(n.Detail));
        });
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldUseTheFallbackWithoutACallOrAWarning_WhenTheCrewAsksSystemOneNothing()
    {
        // Arrange
        AddLibrary(2);
        _files.AddFile("/workspace/skills/review.md", "Check the inputs.");
        Answer("s1", ("s1", 1.0));
        using var run = SystemOneRunScope.Begin(Orkeon.Domain.Crew.CrewDecisionMode.None);

        // Act
        var resolution = await Resolver(DecisionCircuit.On).ResolveAsync(
            Agent(Auto("/workspace/skills/review.md")), "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_model.Requests);
        Assert.NotNull(resolution);
        Assert.Equal(AgentSkillsModes.Fallback, resolution.Notice.Mode);
        Assert.Equal(AgentSkillsReasons.DecisionNone, resolution.Notice.Reason);
        Assert.Equal(["/workspace/skills/review.md"], resolution.Notice.Skills);
        Assert.DoesNotContain(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldReadNoFurtherThanTheCapAndItsTrail_WhenWhiteSpaceRunsOnPastTheCap()
    {
        // Arrange: a skill of 50 characters at its cap, then 100,000 spaces and more text.
        var reads = new FakeCountingReadFileSystemService(new FakeFileSystemService().AddMount("/workspace")
            .AddFile("/workspace/skills/spaced.md", new string('a', 50) + new string(' ', 100_000) + "tail"));
        var resolver = new AgentSkillsResolver(reads, new ServiceCollection().BuildServiceProvider(),
            new AgentSkillsOptions { MaxSkillChars = 50 }, _logger);
        var agent = Agent(new AgentSkills { Paths = ["/workspace/skills/spaced.md"] });

        // Act
        var resolution = await resolver.ResolveAsync(agent, "t1", Request, TestContext.Current.CancellationToken);

        // Assert: white space that runs on past the trail is taken for more text, and the skill is said cut.
        Assert.NotNull(resolution);
        Assert.Contains(new string('a', 50), resolution.Block, StringComparison.Ordinal);
        Assert.Equal(["/workspace/skills/spaced.md"], resolution.Notice.Cut);
        Assert.True(reads.BytesRead < 10_000, $"read {reads.BytesRead} bytes of a 100,054-byte skill");
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldCarryASkill_WhenTheBlockCapIsTheLeastAccepted()
    {
        // Arrange
        _files.AddFile($"{Library}/review.md", new string('r', 1_000));
        var agent = Agent(new AgentSkills { Paths = [$"{Library}/review.md"] });

        // Act
        var resolution = await Resolver(options: new AgentSkillsOptions { MaxBlockChars = AgentSkillsOptions.MinBlockChars })
            .ResolveAsync(agent, "t1", Request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(resolution);
        Assert.Equal([$"{Library}/review.md"], resolution.Notice.Skills);
        Assert.True(resolution.Block.Length <= AgentSkillsOptions.MinBlockChars);
    }
}
