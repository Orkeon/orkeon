using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Abstractions.Tests.Doubles;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.ConsoleApp.Services;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Domain.HumanInput;

namespace Orkeon.ConsoleApp.Tests.Services;

/// <summary>A background task's human_input question reaches the TUI through the wiring (#352).</summary>
public sealed class TuiFidelityWiringQuestionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (TuiIntegration Integration, ReplHumanInputProvider Provider, FakeToolApprovalWatch Approvals, ServiceProvider Services) Build()
    {
        var console = new FakeThreadedConsole { SeparatesThreads = true };
        var provider = new ReplHumanInputProvider(() => console, () => ("t8", "dev-spec"));
        var approvals = new FakeToolApprovalWatch();
        var integration = new TuiIntegration();
        var services = new ServiceCollection()
            .AddSingleton(integration)
            .AddSingleton(provider)
            .AddSingleton<IToolApprovalWatch>(approvals)
            .BuildServiceProvider();
        TuiFidelityWiring.Wire(services);
        return (integration, provider, approvals, services);
    }

    private static HumanInputContext Text() => new() { Prompt = "Name?", InputType = "text" };

    [Fact]
    public async Task ShouldFollowProvider_WhenAnswerPromptRead()
    {
        var (integration, provider, _, services) = Build();
        using var _ = services;
        Assert.Null(integration.AnswerPrompt!());

        var answer = provider.GetInputAsync(Text(), Ct);

        Assert.Equal("answer [t8]> ", integration.AnswerPrompt!());
        Assert.True(provider.TryAnswer("t8", "Ada"));
        Assert.Equal("Ada", await answer);
        Assert.Null(integration.AnswerPrompt!());
    }

    [Fact]
    public async Task ShouldBeNull_WhenToolApprovalWaits()
    {
        var (integration, provider, approvals, services) = Build();
        using var _ = services;
        var answer = provider.GetInputAsync(Text(), Ct);
        var approval = approvals.Ask("write_file");

        Assert.Null(integration.AnswerPrompt!());

        approvals.Settle(approval);
        Assert.Equal("answer [t8]> ", integration.AnswerPrompt!());
        Assert.True(provider.TryAnswer("t8", "Ada"));
        await answer;
    }

    [Fact]
    public async Task ShouldAnswerTaskQuestion_WhenSendToTaskCalled()
    {
        var (integration, provider, _, services) = Build();
        using var _ = services;
        var answer = provider.GetInputAsync(Text(), Ct);

        Assert.False(integration.SendToTask!("t9", "Ada"));
        Assert.True(integration.SendToTask!("t8", "Ada"));

        Assert.Equal("Ada", await answer);
    }

    [Fact]
    public async Task ShouldRaiseQuestionChanged_WhenProviderPendingChanges()
    {
        var (integration, provider, _, services) = Build();
        using var _ = services;
        var raised = 0;
        integration.QuestionChanged += (_, _) => raised++;

        var answer = provider.GetInputAsync(Text(), Ct);
        Assert.Equal(1, raised);
        Assert.True(provider.TryAnswer("t8", "Ada"));
        await answer;

        Assert.Equal(2, raised);
    }
}
