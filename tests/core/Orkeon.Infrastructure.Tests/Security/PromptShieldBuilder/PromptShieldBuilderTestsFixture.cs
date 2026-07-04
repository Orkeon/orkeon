using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Agent;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Security;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.Tests.Security;

public class PromptShieldBuilderTestsFixture
{
    private readonly PromptShieldBuilder _builder;
    private readonly IPromptSanitizer _sanitizer;

    public PromptShieldBuilderTestsFixture()
    {
        var options = Options.Create(new PromptSecurityOptions
        {
            Policy = SanitizationPolicy.Strip,
            EnableExfiltrationDetection = true
        });
        _sanitizer = new PromptSanitizer(options, NullLogger<PromptSanitizer>.Instance);
        _builder = new PromptShieldBuilder(_sanitizer);
    }

    // --- Execution ---

    public static string BuildSecureSystemPrompt(DomainAgent agent)
        => PromptShieldBuilder.BuildSecureSystemPrompt(agent);

    public string BuildSecureUserPrompt(Domain.Task.CrewTask task, SimpleExecutionContext context)
        => _builder.BuildSecureUserPrompt(task, context);

    // --- Agent factory ---

    public static DomainAgent CreateTestAgent(
        string role = "Researcher",
        string goal = "Find information",
        string? backstory = "A skilled researcher",
        IEnumerable<ITool>? tools = null)
    {
        var builder = new AgentBuilder()
            .Role(role)
            .Goal(goal);
        if (backstory != null) builder.Backstory(backstory);
        if (tools != null) builder.WithTools(tools);
        return builder.Build();
    }

    // --- Context factory ---

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "MockMemoryScope (no-op Dispose) is owned by the returned execution context, which lives for the duration of the test.")]
    public static SimpleExecutionContext CreateTestContext(
        Dictionary<string, string>? variables = null)
        => new(
            CrewId.Create(),
            variables ?? [],
            new MockMemoryScope(),
            []);

    // --- Inspection ---

    public PromptShieldBuilder GetBuilder() => _builder;
    public IPromptSanitizer GetSanitizer() => _sanitizer;
}
