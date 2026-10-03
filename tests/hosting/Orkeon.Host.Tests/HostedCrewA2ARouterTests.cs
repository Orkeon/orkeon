using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests;

/// <summary>
/// GAP-23 — the host's A2A router on its own, over <see cref="ScriptedRunner"/>: which skills it
/// publishes, what it hands the runner for a task (crew, input, metadata, origin), and how each
/// way a run ends reads in A2A terms. The run itself — mounts, bound, deadline, stop — is
/// <see cref="CrewRunner"/>'s, held end to end by <see cref="HostA2ATests"/>.
/// </summary>
public sealed class HostedCrewA2ARouterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HostedCrewA2ARouter Router(ICrewRunner runner)
    {
        var options = Options.Create(new OrkeonHostOptions
        {
            Crews =
            [
                new HostedCrewOptions { Name = "support", Path = "/crews/support.yaml" },
                new HostedCrewOptions { Name = "veille", Path = "/crews/veille.yaml", Description = "Weekly technology watch." },
                new HostedCrewOptions { Name = "billing", Path = "/crews/billing.yaml" },
            ],
            A2A = new HostA2AOptions { Enabled = true, Crews = ["veille", "billing"] },
        });

        return new HostedCrewA2ARouter(runner, new CrewHostRegistry(options), options, NullLogger<HostedCrewA2ARouter>.Instance);
    }

    [Fact]
    public async Task The_skills_are_the_exposed_crews_in_the_sections_order()
    {
        var skills = await Router(new ScriptedRunner()).GetSkillsAsync(Ct);

        Assert.Collection(
            skills,
            veille =>
            {
                Assert.Equal("veille", veille.Id);
                Assert.Equal("veille", veille.Name);
                Assert.Equal("Weekly technology watch.", veille.Description);
            },
            billing =>
            {
                // No Description in its configuration: the card still says what the skill does.
                Assert.Equal("billing", billing.Id);
                Assert.Contains("'billing'", billing.Description, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task A_task_runs_its_crew_on_its_input_with_its_metadata_attributed_to_its_id()
    {
        var runner = new ScriptedRunner { Result = new(HostedRunOutcome.Completed, "run-9", "the watch report") };

        var response = await Router(runner).RouteTaskAsync(new A2ATaskRequest
        {
            Id = "task-1",
            SkillId = "veille",
            Input = "What changed this week?",
            Metadata = new Dictionary<string, string> { ["topic"] = "agents" },
        }, Ct);

        Assert.Equal("task-1", response.TaskId);
        Assert.Equal(A2ATaskStatus.Completed, response.Status);
        Assert.Equal("the watch report", response.Output);
        Assert.Equal("veille:What changed this week?", Assert.Single(runner.Ran));
        Assert.Equal("a2a:task-1", Assert.Single(runner.Origins));
        Assert.Equal("agents", Assert.Single(runner.Variables)!["topic"]);
    }

    [Theory]
    [InlineData(nameof(HostedRunOutcome.Failed), A2ATaskStatus.Failed, "The run failed (run run-9). Details are in the host log.")]
    [InlineData(nameof(HostedRunOutcome.Busy), A2ATaskStatus.Failed, "'veille' is already running 4 run(s); try again shortly.")]
    [InlineData(nameof(HostedRunOutcome.Cancelled), A2ATaskStatus.Cancelled, "The run was stopped.")]
    [InlineData(nameof(HostedRunOutcome.Cancelled), A2ATaskStatus.Cancelled, "The run timed out after 00:30:00.")]
    public async Task A_run_that_did_not_complete_answers_why_and_nothing_else(
        string outcome, A2ATaskStatus status, string message)
    {
        // By name: the outcome type is the host's own (internal), a test method's is public.
        var runner = new ScriptedRunner { Result = new(Enum.Parse<HostedRunOutcome>(outcome), "run-9", message) };

        var response = await Router(runner).RouteTaskAsync(
            new A2ATaskRequest { Id = "task-2", SkillId = "veille", Input = "What changed?" }, Ct);

        Assert.Equal(status, response.Status);
        Assert.Equal(message, response.Error);
        Assert.Null(response.Output);
    }

    [Fact]
    public async Task An_empty_input_runs_nothing()
    {
        var runner = new ScriptedRunner();

        var response = await Router(runner).RouteTaskAsync(
            new A2ATaskRequest { Id = "task-3", SkillId = "veille", Input = "  " }, Ct);

        Assert.Equal(A2ATaskStatus.Failed, response.Status);
        Assert.Empty(runner.Ran);
    }
}
