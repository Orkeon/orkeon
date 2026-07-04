using CommandLine;
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
                opts => RunnerExecution.RunOneShotAsync(opts, "Orkeon.Examples.Runner"),
                _ => Task.FromResult(1));
    }
}
