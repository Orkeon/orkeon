using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orkeon.ConsoleApp.DependencyInjection;
using Orkeon.ConsoleApp.Tests.Fakes;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>CH-09: the REPL's bootstrapper warns about a <c>custom</c> address with no path.</summary>
public sealed class ReplLlmEndpointWarningTests
{
    [Fact]
    [Trait("BR", "CH-09")]
    public void ShouldWarn_WhenTheReplReadsAnAddressWithoutPath()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:BaseUrl"] = "http://localhost:8080",
                ["Llm:Model"] = "m",
            })
            .Build();
        var logger = new CapturingLogger();

        ConfiguredLlmProviderBootstrapper.ReportApiKeys(configuration, logger);

        var warning = Assert.Single(
            logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("Llm:BaseUrl", StringComparison.Ordinal));
        Assert.Contains("http://localhost:8080", warning.Message, StringComparison.Ordinal);
    }
}
