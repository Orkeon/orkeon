using Jint;
using Microsoft.Extensions.Logging;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// The procedural engine reads agents and nothing else. These tests pin the warning it
/// emits for the halves of the DSL it drops — the mirror of
/// <c>JsCrewConfigurationAdapter.CollectIgnoredFeatures</c> on the declarative side.
/// </summary>
public sealed class JsCrewShapeWarningTests
{
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Sink(Entries);
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        private sealed class Sink : ILogger
        {
            private readonly List<(LogLevel, string)> _entries;
            public Sink(List<(LogLevel, string)> entries) => _entries = entries;

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => Scope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => _entries.Add((logLevel, formatter(state, exception)));

            private sealed class Scope : IDisposable
            {
                public static readonly Scope Instance = new();
                public void Dispose() { }
            }
        }
    }

    private static async Task<List<(LogLevel Level, string Message)>> RunAsync(string script)
    {
        using var factory = new CapturingLoggerFactory();
        var engine = new JsEngineFactory(loggerFactory: factory).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync(script).ConfigureAwait(false)).ToObject()!;
        await crew.RunAsync(null, CancellationToken.None).ConfigureAwait(false);
        return factory.Entries;
    }

    private const string Agent = """
        const worker = agentBuilder()
            .name("worker")
            .role("Worker")
            .goal("Work")
            .body(async () => "done")
            .build();
        """;

    [Fact]
    public async Task Declaring_tasks_on_the_procedural_shape_warns_that_they_will_not_be_read()
    {
        var entries = await RunAsync(Agent + """
            const work = taskBuilder()
                .name("work")
                .agent(worker)
                .description("Do the work")
                .expectedOutput("A result")
                .build();

            crewBuilder()
                .name("mixed")
                .goal("Declares tasks and runs procedurally")
                .withAgent(worker)
                .withTask(work)
                .build();
            """);

        // The count matters: "some tasks were ignored" sends the reader looking for which.
        Assert.Contains(entries, e => e.Level == LogLevel.Warning
            && e.Message.Contains("1 task(s)", StringComparison.Ordinal)
            && e.Message.Contains("'mixed'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_process_other_than_sequential_warns_that_it_reaches_only_a_telemetry_tag()
    {
        var entries = await RunAsync(Agent + """
            crewBuilder()
                .name("parallel-in-name-only")
                .goal("Declares a process the procedural engine cannot honour")
                .process("parallel")
                .withAgent(worker)
                .build();
            """);

        Assert.Contains(entries, e => e.Level == LogLevel.Warning
            && e.Message.Contains("process(\"parallel\")", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_procedural_crew_that_declares_nothing_it_cannot_honour_stays_quiet()
    {
        var entries = await RunAsync(Agent + """
            crewBuilder()
                .name("clean")
                .goal("Only what this engine reads")
                .process("sequential")
                .withAgent(worker)
                .build();
            """);

        Assert.DoesNotContain(entries, e => e.Level == LogLevel.Warning
            && e.Message.Contains("Procedural crew script", StringComparison.Ordinal));
    }
}
