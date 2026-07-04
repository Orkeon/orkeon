using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Services.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Security.Guards;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Infrastructure.Tests.Security.Guards;

public class GuardianTestsFixture
{
    // --- Pipeline creation ---

    public static GuardianPipeline CreatePipeline(
        GuardianPolicyEngine? policyEngine = null,
        IAuditLogger? auditLogger = null)
    {
        var defaultPolicy = new GuardianPolicy();
        var engine = policyEngine ?? new GuardianPolicyEngine(defaultPolicy);
        var logger = NullLogger<GuardianPipeline>.Instance;
        return new GuardianPipeline(engine, logger, auditLogger);
    }

    // --- Guard creation ---

    public static ToolGuard CreateToolGuard(GuardianPolicy? policy = null)
    {
        var globalPolicy = policy ?? new GuardianPolicy();
        var engine = new GuardianPolicyEngine(globalPolicy);
        return new ToolGuard(engine, NullLogger<ToolGuard>.Instance);
    }

    public static DelegationGuard CreateDelegationGuard(int maxDepth = 5)
    {
        var policy = new GuardianPolicy { MaxDelegationDepth = maxDepth };
        var engine = new GuardianPolicyEngine(policy);
        return new DelegationGuard(engine, NullLogger<DelegationGuard>.Instance);
    }

    public static InputGuard CreateInputGuard(MockPromptSanitizer? sanitizer = null)
    {
        return new InputGuard(sanitizer ?? new MockPromptSanitizer(), NullLogger<InputGuard>.Instance);
    }

    public static OutputGuard CreateOutputGuard(MockOutputValidationPipeline? pipeline = null)
    {
        return new OutputGuard(pipeline ?? new MockOutputValidationPipeline(), NullLogger<OutputGuard>.Instance);
    }

    // --- Mock factories ---

    public static MockGuardian CreateMockGuardianThatAllows()
    {
        var guard = new MockGuardian();
        guard.SetAllow();
        return guard;
    }

    public static MockGuardian CreateMockGuardianThatBlocks(string reason, GuardViolation[]? violations = null)
    {
        var guard = new MockGuardian();
        guard.SetBlock(reason, violations ?? []);
        return guard;
    }

    public static MockGuardian CreateMockGuardianThatWarns(string reason, GuardViolation[]? violations = null)
    {
        var guard = new MockGuardian();
        guard.SetWarn(reason, violations ?? []);
        return guard;
    }

    // --- Context factories ---

    public static GuardContext CreateContext(
        GuardPhase phase = GuardPhase.Input,
        string agentId = "agent1",
        string crewId = CrewIdAlt1,
        string? toolName = null,
        string? content = null,
        int delegationDepth = 0,
        string? targetAgentId = null,
        Dictionary<string, object>? toolArgs = null)
        => new()
        {
            Phase = phase,
            AgentId = agentId,
            CrewId = crewId,
            ToolName = toolName,
            Content = content,
            DelegationDepth = delegationDepth,
            TargetAgentId = targetAgentId,
            ToolArgs = toolArgs
        };

    // --- Violation factory ---

    public static GuardViolation CreateViolation(
        string guardName = "TestGuard",
        GuardPhase phase = GuardPhase.Input,
        string description = "Test violation",
        GuardThreatSeverity severity = GuardThreatSeverity.High)
        => new(guardName, phase, description, severity, DateTime.UtcNow);

    // --- DI Resolution helper ---

    public static ServiceProvider BuildDIProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([])
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddSingleton<IPromptSanitizer>(new MockPromptSanitizer());
        services.AddSingleton<IOutputValidationPipeline>(new MockOutputValidationPipeline());

        services.AddOrkeonGuardian();

        return services.BuildServiceProvider();
    }

    // --- Inner helper class ---

    public class ThrowingGuardian : IGuardian
    {
        private readonly Exception _exception;
        public ThrowingGuardian(Exception exception) => _exception = exception;

        public Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default)
        {
            throw _exception;
        }
    }
}
