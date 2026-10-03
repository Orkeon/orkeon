using Jint;
using Microsoft.Extensions.Logging;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Adapters;

/// <summary>
/// GAP-38 — YAML parity <c>maxRpm:</c>, at both levels: <c>agentBuilder().maxRpm(n)</c> bounds the
/// model requests of an agent per minute, <c>crewBuilder().maxRpm(n)</c> those of all its agents and
/// its manager. Absent, no limit of its own; zero or negative is refused at <c>build()</c>. The
/// procedural shape — its <c>ctx.llm</c> calls are no agent turns — applies neither, and says so once.
/// </summary>
public sealed class MaxRpmParityTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static JsCrew Crew(string agentCall, string crewCall) =>
        (JsCrew)NewEngine().Evaluate($$"""
            const analyst = agentBuilder().name("analyst").role("Analyst").goal("Analyze"){{agentCall}}.build();
            const work = taskBuilder().name("work").agent(analyst)
                .description("Do the work").expectedOutput("A result").build();

            crewBuilder()
                .name("paced").goal("Analyze at a pace")
                {{crewCall}}
                .withAgent(analyst)
                .withTask(work)
                .build();
            """).ToObject()!;

    [Fact]
    public void The_agent_and_crew_maxRpm_reach_the_configuration()
    {
        var config = JsCrewConfigurationAdapter.ToConfiguration(Crew(".maxRpm(3)", ".maxRpm(30)"));

        Assert.Equal(3, Assert.Single(config.Agents).MaxRpm);
        Assert.Equal(30, config.MaxRpm);
    }

    [Fact]
    public void Without_maxRpm_neither_level_has_a_limit_of_its_own()
    {
        var config = JsCrewConfigurationAdapter.ToConfiguration(Crew("", ""));

        Assert.Null(Assert.Single(config.Agents).MaxRpm);
        Assert.Null(config.MaxRpm);
        // maxIterations left unset is the one default of every surface (GAP-38, decision 6).
        Assert.Equal(20, Assert.Single(config.Agents).MaxIterations);
    }

    [Theory]
    [InlineData(".maxRpm(0)", "")]
    [InlineData(".maxRpm(-1)", "")]
    [InlineData("", ".maxRpm(0)")]
    [InlineData("", ".maxRpm(-5)")]
    public void A_maxRpm_of_zero_or_less_is_refused_at_build(string agentCall, string crewCall)
    {
        var error = ThrowsContaining<InvalidScriptException>(() => Crew(agentCall, crewCall), ".maxRpm");

        Assert.Contains(agentCall.Length > 0 ? "analyst" : "paced", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_procedural_shape_says_once_that_it_applies_no_maxRpm()
    {
        using var factory = new CapturingLoggerFactory();
        var engine = new JsEngineFactory(loggerFactory: factory).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync("""
            const worker = agentBuilder().name("worker").role("Worker").goal("Work")
                .maxRpm(2)
                .body(async () => "done")
                .build();

            crewBuilder()
                .name("paced")
                .goal("Runs its agents procedurally")
                .maxRpm(10)
                .withAgent(worker)
                .build();
            """, cancellationToken: TestContext.Current.CancellationToken)).ToObject()!;

        await crew.RunAsync(null, TestContext.Current.CancellationToken);
        await crew.RunAsync(null, TestContext.Current.CancellationToken);

        var warning = Assert.Single(factory.Entries, e => e.Level == LogLevel.Warning
            && e.Message.Contains("maxRpm", StringComparison.Ordinal));
        Assert.Contains("'paced'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("worker", warning.Message, StringComparison.Ordinal);
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Sink(Entries);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class Sink(List<(LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (entries)
                    entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
