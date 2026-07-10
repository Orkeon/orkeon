using CommandLine;
using Orkeon.Examples.Shared;
using Orkeon.Hosting;

namespace Orkeon.Examples.Runner;

public class Options : RunnerOptionsBase
{
}

static class Program
{
    static Task<int> Main(string[] args)
    {
        return Parser.Default.ParseArguments<Options>(args)
            .MapResult(
                opts => RunnerExecution.RunOneShotAsync(
                    opts,
                    "Orkeon.Examples.Runner",
                    // Register the semantic_search agent tool, same as the interview-spec-forge
                    // runner. Several bundled crews (fraud-detection, insider-trading-detection,
                    // pharmacovigilance, crew-of-crews) list it in their YAML; without this it is
                    // dropped at crew load with "Tool 'semantic_search' not found in registry".
                    // Embeddings default to the infrastructure hash-based provider (no API key).
                    configureServices: (_, services) => services.AddSemanticSearchTool()),
                _ => Task.FromResult(1));
    }
}
