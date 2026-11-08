using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.HumanInput;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.HumanInput;
using Orkeon.Infrastructure.Orchestration;

namespace Orkeon.Infrastructure.Tests.HumanInput;

/// <summary>
/// FWK-23, through the registration the hosts use (<c>AddOrkeonHumanInput</c>): with checkpointing on, the provider a tool
/// is handed and the channel the permission gate asks are the durable ones; with it off, nothing changes.
/// </summary>
public sealed class DurableHumanInputRegistrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Build(bool checkpointing, string? pause = "Suspend", Action<IServiceCollection>? more = null)
    {
        var settings = new Dictionary<string, string?>();
        if (pause is not null)
            settings["Orkeon:HumanInput:Pause"] = pause;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddOptions<CheckpointingOptions>().Configure(o => o.Enabled = checkpointing);
        services.AddOrkeonCheckpointing();
        services.AddSingleton<IRunScope, AsyncLocalRunScope>();
        services.AddOrkeonHumanInput();
        more?.Invoke(services);
        services.DecorateHumanInputWithDeadline();
        return services.BuildServiceProvider();
    }

    // Entered by the caller itself, never inside an awaited helper: an ambient scope set there is gone when it returns.
    private static IDisposable[] EnterTask(ServiceProvider sp, ExecutionId id) =>
        [sp.GetRequiredService<IRunScope>().Enter(new RunScope(id, "crew")), TaskInputScope.Enter("ask#1")];

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldSuspendTheRun_WhenCheckpointingIsOnAndNobodyCanBeAsked()
    {
        using var sp = Build(checkpointing: true);
        var id = ExecutionId.New();
        await sp.GetRequiredService<ICheckpointManager>().StartSessionAsync(id, "crew", Ct);
        var scopes = EnterTask(sp, id);
        try
        {
            var provider = sp.GetRequiredService<IHumanInputProvider>();

            await Assert.ThrowsAsync<RunSuspendedException>(() => provider.GetInputAsync(HumanInputContext.CreateTextInput(Orkeon.Domain.Common.AgentId.Create(), Orkeon.Domain.Common.TaskId.Create(), "Which?"), Ct));

            Assert.Equal(SessionPhase.WaitingForInput, (await sp.GetRequiredService<IStateStore>().GetAsync(id.ToString(), Ct))!.Phase);
        }
        finally
        {
            foreach (var scope in scopes.Reverse())
                scope.Dispose();
        }
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldAnswerOnItsOwn_WhenCheckpointingIsOff()
    {
        using var sp = Build(checkpointing: false);
        var id = ExecutionId.New();
        await sp.GetRequiredService<ICheckpointManager>().StartSessionAsync(id, "crew", Ct);
        var scopes = EnterTask(sp, id);
        try
        {
            var provider = sp.GetRequiredService<IHumanInputProvider>();

            var answer = await provider.GetInputAsync(
                new HumanInputContext { Prompt = "Which?", DefaultValue = "fallback" }, Ct);

            Assert.Equal("fallback", answer);
        }
        finally
        {
            foreach (var scope in scopes.Reverse())
                scope.Dispose();
        }
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public async System.Threading.Tasks.Task ShouldSuspendTheRunOnAnApproval_WhenCheckpointingIsOnAndTheHostHasNoChannel()
    {
        using var sp = Build(checkpointing: true);
        var id = ExecutionId.New();
        await sp.GetRequiredService<ICheckpointManager>().StartSessionAsync(id, "crew", Ct);
        var scopes = EnterTask(sp, id);
        try
        {
            var channel = sp.GetService<IToolApprovalChannel>();

            Assert.NotNull(channel);
            await Assert.ThrowsAsync<RunSuspendedException>(() => channel.RequestAsync(
                new ToolApprovalRequest
                {
                    ToolName = "shell_command",
                    Arguments = ImmutableDictionary<string, object?>.Empty,
                    Mode = "default",
                    Reason = "it runs a command",
                },
                Ct));
        }
        finally
        {
            foreach (var scope in scopes.Reverse())
                scope.Dispose();
        }
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public void ShouldRegisterNoApprovalChannel_WhenCheckpointingIsOffAndTheHostHasNone()
    {
        using var sp = Build(checkpointing: false);

        Assert.Null(sp.GetService<IToolApprovalChannel>());
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public void ShouldHoldByDefault_WhenTheHostAsksForIt()
    {
        using var sp = Build(checkpointing: true, pause: null, more: s => s.UseHoldByDefault(TimeSpan.FromMinutes(30)));

        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HumanInputPauseOptions>>().Value;

        Assert.Equal(HumanInputPause.Hold, options.Pause);
        Assert.Equal(TimeSpan.FromMinutes(30), options.HoldTimeout);
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public void ShouldKeepTheSettingsValue_WhenTheHostDefaultsToHold()
    {
        using var sp = Build(checkpointing: true, pause: "Suspend", more: s => s.UseHoldByDefault(TimeSpan.FromMinutes(30)));

        Assert.Equal(HumanInputPause.Suspend, sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HumanInputPauseOptions>>().Value.Pause);
    }
}
