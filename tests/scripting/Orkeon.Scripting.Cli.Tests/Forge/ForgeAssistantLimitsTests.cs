using Microsoft.Extensions.Configuration;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Configuration;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The engine limits of one assistant turn.
/// <para>
/// These were not resolved at all: <c>ForgeCrewAssistant</c> built its
/// <c>JsEngineFactory</c> without <c>limits:</c>, so the all-optional overload applied the
/// 30 s untrusted-script default and <c>Orkeon:Scripting:Limits</c> was discarded in
/// silence. Jint's TimeoutInterval is WALL CLOCK — the stopwatch keeps running while the
/// host awaits the LLM — so one slow reply, or a single round trip wasted on a refused tool
/// call, ended the interview with a bare "The operation has timed out." and no
/// <c>session.finished</c>. The pack's own <c>wallTime: 600</c> was unreachable.
/// </para>
/// </summary>
public sealed class ForgeAssistantLimitsTests
{
    private static IConfiguration ConfigWith(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();

    [Fact]
    public void An_unconfigured_interview_gets_the_packs_own_budget_not_the_strict_default()
    {
        var limits = ForgeCrewAssistant.ResolveLimits(ConfigWith());

        // 600 s is what forge-assistant.ork.js declares in its own budget().
        Assert.Equal(TimeSpan.FromSeconds(600), limits.ExecutionTimeout);
        Assert.NotEqual(new ScriptingLimitsOptions().ExecutionTimeout, limits.ExecutionTimeout);
    }

    [Fact]
    public void A_configured_timeout_wins()
    {
        var limits = ForgeCrewAssistant.ResolveLimits(
            ConfigWith(("Orkeon:Scripting:Limits:ExecutionTimeout", "00:45:00")));

        Assert.Equal(TimeSpan.FromMinutes(45), limits.ExecutionTimeout);
    }

    /// <summary>
    /// Absence is read from the key, never by comparing against the default value — asking
    /// for 30 s deliberately must not be mistaken for having asked for nothing.
    /// </summary>
    [Fact]
    public void Asking_for_the_strict_default_explicitly_is_honoured()
    {
        var limits = ForgeCrewAssistant.ResolveLimits(
            ConfigWith(("Orkeon:Scripting:Limits:ExecutionTimeout", "00:00:30")));

        Assert.Equal(TimeSpan.FromSeconds(30), limits.ExecutionTimeout);
    }

    [Fact]
    public void The_rest_of_the_section_is_carried_through()
    {
        var limits = ForgeCrewAssistant.ResolveLimits(
            ConfigWith(("Orkeon:Scripting:Limits:MemoryLimitBytes", "209715200")));

        Assert.Equal(209715200, limits.MemoryLimitBytes);
        Assert.Equal(TimeSpan.FromSeconds(600), limits.ExecutionTimeout);
    }
}
