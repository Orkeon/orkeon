using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Hosting;

namespace Orkeon.Examples.Interactive;

public class Options : RunnerOptionsBase
{
    [Option("stop", Required = false, Separator = ',', Default = new[] { "stop", "quit", "exit" },
        HelpText = "Comma-separated stop words to end the session.")]
    public IEnumerable<string> StopWords { get; set; } = ["stop", "quit", "exit"];
}

static class Program
{
    static Task<int> Main(string[] args)
    {
        return Parser.Default.ParseArguments<Options>(args)
            .MapResult(RunAsync, _ => Task.FromResult(1));
    }

    static Task<int> RunAsync(Options opts)
    {
        var stopWords = opts.StopWords
            .Select(w => w.Trim().ToLowerInvariant())
            .ToHashSet();

        var llmLogPath = opts.ResolvedLlmLogPath;
        var verbosity = Math.Clamp(opts.Verbose, 0, 2);

        return RunnerExecution.RunInteractiveLoopAsync(
            opts,
            loggerCategory: "Orkeon.Examples.Interactive",
            stopWords: stopWords,
            kickoffPerInputAsync: async (sp, input, ct) =>
            {
                var factory = sp.GetRequiredService<ICrewFactory>();
                var crew = await factory.CreateFromFileAsync(Path.GetFullPath(opts.ConfigPath), ct);

                var orchestrator = sp.GetRequiredService<ICrewOrchestrationService>();

                // Per-question variables: merge --var entries with the implicit "question" key
                // so YAML templates can reference {question} alongside any caller-supplied vars.
                var vars = new Dictionary<string, string>(opts.ParseVariables(), StringComparer.Ordinal)
                {
                    ["question"] = input
                };
                var crewInput = CrewInput.WithStringVariables(input, vars);

                return await orchestrator.KickoffAsync(crew.Id, crewInput, ct);
            },
            onSessionStart: _ =>
            {
                PrintBanner();
                Console.WriteLine($"  Stop words: {string.Join(", ", stopWords)}");
                if (llmLogPath != null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"  LLM logging → {llmLogPath}/llm-exchanges-*.jsonl");
                    Console.ResetColor();
                }
                if (verbosity > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine(verbosity == 1
                        ? "  Verbose mode ON — LLM and tool exchanges will be logged."
                        : "  Debug mode ON — full debug output with all details.");
                    Console.ResetColor();
                }
                Console.WriteLine();
            });
    }

    static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("╔══════════════════════════════════════════╗");
        Console.WriteLine("║     Orkeon — Interactive Q&A Session     ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("  Ask any question. The crew will collaborate to answer.");
        Console.WriteLine("  ^C during a run cancels the question; type 'stop', 'quit', or 'exit' to end.");
    }
}
