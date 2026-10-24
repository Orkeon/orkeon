using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Registry;
using Orkeon.ConsoleApp.Registries;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.Runners;

/// <summary>
/// Interactive Q&amp;A runner. Prompts for a YAML crew config (or uses a built-in
/// embedded crew), then routes every user line as a question via the
/// <see cref="QaCommandRegistry"/> fallback.
/// </summary>
internal sealed class QaRunner : InteractiveRunnerBase
{
    private readonly ConsoleInputService _input;
    private readonly IFileSystemService _fs;

    public QaRunner(
        DefaultCommandRegistry defaults,
        QaCommandRegistry specific,
        IConsoleAdapter console,
        ConsoleInputService input,
        IFileSystemService fs,
        ILogger<QaRunner> logger,
        IServiceProvider services)
        : base(defaults, specific, console, logger, services)
    {
        _input = input;
        _fs = fs;
    }

    /// <summary>Path to the crew YAML config the user named (set by <see cref="OnStartAsync"/>); null for the built-in crew.</summary>
    public string? CrewConfigPath { get; private set; }

    /// <summary>True when the user pressed Enter and questions go to <see cref="BuiltInCrewYaml"/>.</summary>
    public bool UsesBuiltInCrew { get; private set; }

    protected override string Banner => """

==================================
   Interactive Q&A Session
==================================

""";

    protected override string Prompt => "[Question] > ";

    protected override async Task OnStartAsync(CancellationToken ct)
    {
        var configPath = _input.GetOptionalString(
            "Path to crew config.yaml (or Enter for built-in Q&A crew): ");

        if (string.IsNullOrWhiteSpace(configPath))
        {
            UsesBuiltInCrew = true;
            Console.WriteLine("  Using built-in interactive Q&A crew.");
        }
        else if (!await _fs.ExistsAsync(configPath, ct).ConfigureAwait(false))
        {
            Console.WriteLine($"  ERROR: File not found: {configPath}");
            throw new FileNotFoundException("Crew config not found.", configPath);
        }
        else
        {
            CrewConfigPath = configPath;
        }

        Console.WriteLine("");
        Console.WriteLine("  Ask any question. Type 'exit' or press Ctrl+D to end.");
        Console.WriteLine("");
    }

    /// <summary>
    /// The crew a question goes to when no config path is given. It is loaded from this string, not
    /// written to a file: the REPL mounts no <c>/tmp</c> unless asked, and a file would only be read back.
    /// </summary>
    internal const string BuiltInCrewYaml = """
            name: "interactive-qa"
            goal: "Answer user questions accurately and helpfully"
            process: "sequential"
            verbose: true
            memory: false
            planning: false

            agents:
              analyst:
                role: "Question Analyst"
                goal: "Understand the user's question and identify key topics"
                backstory: |
                  You are an expert at understanding questions in any domain.
                  You break down complex questions into components and identify
                  the best approach to answer them.
                tools: []
                allowDelegation: false
                maxIter: 5
                verbose: true

              responder:
                role: "Answer Composer"
                goal: "Provide a clear, helpful, and well-structured answer"
                backstory: |
                  You are a skilled communicator who excels at explaining complex
                  topics in an accessible way. You structure your answers clearly
                  and always ensure the response directly addresses the question.
                tools: []
                allowDelegation: false
                maxIter: 5
                verbose: true

            tasks:
              analyze:
                description: "Analyze the following question and identify key topics and the best approach: {question}"
                expectedOutput: "A structured analysis with identified topics and suggested approach"
                agent: "analyst"
                dependencies: []
                asyncExecution: false
                humanInput: false

              answer:
                description: "Using the analysis, compose a clear and helpful answer to: {question}"
                expectedOutput: "A well-written answer that directly addresses the user's question"
                agent: "responder"
                dependencies:
                  - "analyze"
                asyncExecution: false
                humanInput: false
            """;
}
