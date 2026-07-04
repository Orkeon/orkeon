using CommandLine;
using Orkeon.Hosting;
using Orkeon.Trading.Tools.Infrastructure.DependencyInjection;

namespace Orkeon.Examples.Trading.Runner;

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
                    "Orkeon.Examples.Trading.Runner",
                    configureServices: (_, services) =>
                    {
                        services.AddTradingTools();
                    }),
                _ => Task.FromResult(1));
    }
}
