using System.Text.Json;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.ValueObjects;

public class DomainValueObjectsTests
{
    private static readonly string[] ProgrammingDebuggingSkills = ["programming", "debugging"];
    private static readonly string[] VisualStudioGitTools = ["Visual Studio", "Git"];
    private static readonly string[] CSharpPythonLanguages = ["C#", "Python"];
    private static readonly string[] ProgrammingSkill = ["Programming"];
    private static readonly string[] GitTool = ["GIT"];
    private static readonly string[] CSharpLanguage = ["C#"];
    private static readonly string[] TwoSkills = ["skill1", "skill2"];
    private static readonly string[] TwoTools = ["tool1", "tool2"];
    private static readonly string[] S1S2Skills = ["s1", "s2"];
    private static readonly string[] T1Tool = ["t1"];
    private static readonly string[] L1L2L3Languages = ["l1", "l2", "l3"];
    private static readonly string[] AnalysisDesignSkills = ["analysis", "design"];
    private static readonly string[] Dep1Dep2Dependencies = ["dep1", "dep2"];
    private static readonly string[] AdminUserRoles = ["admin", "user"];
    private static readonly string[] ReadWritePermissions = ["read", "write"];
    private static readonly string[] SingleRole = ["role"];
    private static readonly string[] SinglePerm = ["perm"];
    private static readonly string[] AdminRole = ["Admin"];
    private static readonly string[] ReadPermission = ["Read"];
    private static readonly string[] D1D2D3Dependencies = ["d1", "d2", "d3"];
    private static readonly ProtocolType[] AsyncProtocolTypes = [ProtocolType.MessageQueue, ProtocolType.EventStream];
    private static readonly ProtocolType[] SyncProtocolTypes = [ProtocolType.Direct, ProtocolType.Broadcast];

    #region AgentCapabilities Tests

    [Fact]
    public void ShouldCreateEmpty_WhenUsingAgentCapabilitiesConstructorWithNullCollections()
    {
        // Act
        var capabilities = AgentCapabilities.Create(
            skills: null,
            tools: null,
            languages: null);

        // Assert
        Assert.Empty(capabilities.Skills);
        Assert.Empty(capabilities.Tools);
        Assert.Empty(capabilities.Languages);
        Assert.Equal(ConfidenceLevel.Medium, capabilities.OverallConfidence);
    }

    [Fact]
    public void ShouldCreateCorrectly_WhenUsingAgentCapabilitiesConstructorWithValues()
    {
        // Arrange
        // Act
        var capabilities = AgentCapabilities.Create(ProgrammingDebuggingSkills, VisualStudioGitTools, CSharpPythonLanguages, ConfidenceLevel.High);

        // Assert
        Assert.Equal(2, capabilities.Skills.Count);
        Assert.Equal(2, capabilities.Tools.Count);
        Assert.Equal(2, capabilities.Languages.Count);
        Assert.Equal(ConfidenceLevel.High, capabilities.OverallConfidence);
    }

    [Fact]
    public void ShouldWork_WhenUsingAgentCapabilitiesCaseInsensitive()
    {
        // Arrange
        var capabilities = AgentCapabilities.Create(
            skills: ProgrammingSkill,
            tools: GitTool,
            languages: CSharpLanguage);

        // Act & Assert
        Assert.True(capabilities.HasSkill("programming"));
        Assert.True(capabilities.HasSkill("PROGRAMMING"));
        Assert.True(capabilities.HasTool("git"));
        Assert.True(capabilities.HasLanguage("c#"));
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingAgentCapabilitiesAddMethods()
    {
        // Arrange
        var original = AgentCapabilities.Empty;

        // Act
        var withSkill = original.AddSkill("coding");
        var withTool = withSkill.AddTool("IDE");
        var withLanguage = withTool.AddLanguage("Java");

        // Assert
        Assert.Empty(original.Skills);
        Assert.Single(withSkill.Skills);
        Assert.Single(withTool.Tools);
        Assert.Single(withLanguage.Languages);
        Assert.True(withLanguage.HasSkill("coding"));
        Assert.True(withLanguage.HasTool("IDE"));
        Assert.True(withLanguage.HasLanguage("Java"));
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingAgentCapabilitiesRemoveMethods()
    {
        // Arrange
        var capabilities = AgentCapabilities.Create(
            skills: TwoSkills,
            tools: TwoTools);

        // Act
        var removed = capabilities
            .RemoveSkill("skill1")
            .RemoveTool("tool2");

        // Assert
        Assert.Single(removed.Skills);
        Assert.Single(removed.Tools);
        Assert.False(removed.HasSkill("skill1"));
        Assert.False(removed.HasTool("tool2"));
    }

    [Fact]
    public void ShouldFormatCorrectly_WhenUsingAgentCapabilitiesToString()
    {
        // Arrange
        var capabilities = AgentCapabilities.Create(
            skills: S1S2Skills,
            tools: T1Tool,
            languages: L1L2L3Languages,
            ConfidenceLevel.Expert);

        // Act
        var result = capabilities.ToString();

        // Assert
        Assert.Contains("Skills: 2", result);
        Assert.Contains("Tools: 1", result);
        Assert.Contains("Languages: 3", result);
        Assert.Contains("Confidence: Expert", result);
    }

    #endregion

    #region TaskComplexity Tests

    [Fact]
    public void ShouldCreate_WhenUsingTaskComplexityConstructorWithValidValues()
    {
        // Arrange
        var skills = AnalysisDesignSkills;
        var dependencies = Dep1Dep2Dependencies;

        // Act
        var complexity = TaskComplexity.Create(
            ComplexityLevel.Complex,
            5,
            TimeSpan.FromHours(2),
            skills,
            dependencies);

        // Assert
        Assert.Equal(ComplexityLevel.Complex, complexity.Level);
        Assert.Equal(5, complexity.EstimatedEffort);
        Assert.Equal(TimeSpan.FromHours(2), complexity.EstimatedDuration);
        Assert.Equal(2, complexity.RequiredSkills.Count);
        Assert.Equal(2, complexity.Dependencies.Count);
    }

    [Fact]
    public void ShouldThrow_WhenUsingTaskComplexityConstructorWithInvalidEffort()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => TaskComplexity.Create(ComplexityLevel.Simple, 0, TimeSpan.FromHours(1)));
        Assert.Contains("Effort must be positive", ex.Message);
    }

    [Fact]
    public void ShouldThrow_WhenUsingTaskComplexityConstructorWithInvalidDuration()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => TaskComplexity.Create(ComplexityLevel.Simple, 1, TimeSpan.Zero));
        Assert.Contains("Duration must be positive", ex.Message);
    }

    [Fact]
    public void ShouldCaseInsensitive_WhenUsingTaskComplexityRequiresSkill()
    {
        // Arrange
        var complexity = TaskComplexity.Create(
            ComplexityLevel.Medium,
            3,
            TimeSpan.FromHours(1),
            ProgrammingSkill);

        // Act & Assert
        Assert.True(complexity.RequiresSkill("programming"));
        Assert.True(complexity.RequiresSkill("PROGRAMMING"));
        Assert.False(complexity.RequiresSkill("design"));
    }

    [Fact]
    public void ShouldCreateCorrectly_WhenUsingTaskComplexityFactoryMethods()
    {
        // Act
        var simple = TaskComplexity.Simple(TimeSpan.FromMinutes(30));
        var medium = TaskComplexity.Medium(TimeSpan.FromHours(1), "skill1", "skill2");
        var complex = TaskComplexity.Complex(
            TimeSpan.FromHours(4),
            S1S2Skills,
            D1D2D3Dependencies);

        // Assert
        Assert.Equal(ComplexityLevel.Simple, simple.Level);
        Assert.Equal(1, simple.EstimatedEffort);
        Assert.Empty(simple.RequiredSkills);

        Assert.Equal(ComplexityLevel.Medium, medium.Level);
        Assert.Equal(3, medium.EstimatedEffort);
        Assert.Equal(2, medium.RequiredSkills.Count);

        Assert.Equal(ComplexityLevel.Complex, complex.Level);
        Assert.Equal(8, complex.EstimatedEffort);
        Assert.Equal(2, complex.RequiredSkills.Count);
        Assert.Equal(3, complex.Dependencies.Count);
    }

    #endregion

    #region CollaborationContext Tests

    [Fact]
    public void ShouldCreate_WhenUsingCollaborationContextUsingConstructorWithValidParticipants()
    {
        // Arrange
        var participants = new[] { AgentId.Create(), AgentId.Create(), AgentId.Create() };
        var leader = participants[0];
        var sharedState = JsonDocument.Parse("{}");

        // Act
        var context = CollaborationContext.Create(
            CollaborationType.Hierarchical,
            participants,
            leader,
            sharedState,
            TimeSpan.FromMinutes(30));

        // Assert
        Assert.Equal(CollaborationType.Hierarchical, context.Type);
        Assert.Equal(3, context.ParticipantCount);
        Assert.Equal(leader, context.Leader);
        Assert.NotNull(context.SharedState);
        Assert.Equal(TimeSpan.FromMinutes(30), context.MaxDuration);
    }

    [Fact]
    public void ShouldThrow_WhenUsingCollaborationContextUsingConstructorWithTooFewParticipants()
    {
        // Arrange
        var participants = new[] { AgentId.Create() };

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => CollaborationContext.Create(CollaborationType.PeerToPeer, participants));
        Assert.Contains("at least 2 participants", ex.Message);
    }

    [Fact]
    public void ShouldThrow_WhenUsingCollaborationContextUsingConstructorWithInvalidLeader()
    {
        // Arrange
        var participants = new[] { AgentId.Create(), AgentId.Create() };
        var nonParticipant = AgentId.Create();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => CollaborationContext.Create(
                CollaborationType.Hierarchical,
                participants,
                nonParticipant));
        Assert.Contains("Leader must be one of the participants", ex.Message);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingCollaborationContextUsingIsExpiredWithExpiredDuration()
    {
        // Arrange
        var participants = new[] { AgentId.Create(), AgentId.Create() };
        var context = CollaborationContext.Create(
            CollaborationType.PeerToPeer,
            participants,
            maxDuration: TimeSpan.FromMilliseconds(1));

        // Act — poll the observable condition instead of sleeping (R5.6)
        ClockAdvance.Until(() => context.IsExpired);

        // Assert
        Assert.True(context.IsExpired);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingCollaborationContextAddingRemoveParticipant()
    {
        // Arrange
        var initial = new[] { AgentId.Create(), AgentId.Create() };
        var context = CollaborationContext.Create(CollaborationType.Broadcast, initial);
        var newAgent = AgentId.Create();

        // Act
        var added = context.AddParticipant(newAgent);
        var removed = added.RemoveParticipant(initial[0]);

        // Assert
        Assert.Equal(2, context.ParticipantCount);
        Assert.Equal(3, added.ParticipantCount);
        Assert.Equal(2, removed.ParticipantCount);
        Assert.True(added.HasParticipant(newAgent));
        Assert.False(removed.HasParticipant(initial[0]));
    }

    #endregion

    #region PerformanceMetrics Tests

    [Fact]
    public void ShouldCreate_WhenUsingPerformanceMetricsUsingConstructorWithValidValues()
    {
        // Act
        var metrics = PerformanceMetrics.Create(
            throughput: 50.5,
            averageResponseTime: 2.3,
            errorRate: 1.2,
            resourceUtilization: 65.5,
            concurrentTasks: 10);

        // Assert
        Assert.Equal(50.5, metrics.Throughput);
        Assert.Equal(2.3, metrics.AverageResponseTime);
        Assert.Equal(1.2, metrics.ErrorRate);
        Assert.Equal(65.5, metrics.ResourceUtilization);
        Assert.Equal(10, metrics.ConcurrentTasks);
        Assert.True(metrics.MeasuredAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldThrow_WhenUsingPerformanceMetricsUsingConstructorWithInvalidValues()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => PerformanceMetrics.Create(-1, 1, 1, 1, 1));
        Assert.Throws<ArgumentException>(() => PerformanceMetrics.Create(1, -1, 1, 1, 1));
        Assert.Throws<ArgumentException>(() => PerformanceMetrics.Create(1, 1, -1, 1, 1));
        Assert.Throws<ArgumentException>(() => PerformanceMetrics.Create(1, 1, 101, 1, 1));
        Assert.Throws<ArgumentException>(() => PerformanceMetrics.Create(1, 1, 1, -1, 1));
        Assert.Throws<ArgumentException>(() => PerformanceMetrics.Create(1, 1, 1, 101, 1));
        Assert.Throws<ArgumentException>(() => PerformanceMetrics.Create(1, 1, 1, 1, -1));
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingPerformanceMetricsUsingIsHealthyWithGoodMetrics()
    {
        // Arrange
        var metrics = PerformanceMetrics.Create(100, 1, 1, 50, 10);

        // Act & Assert
        Assert.True(metrics.IsHealthy);
        Assert.False(metrics.NeedsAttention);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingPerformanceMetricsUsingNeedsAttentionWithBadMetrics()
    {
        // Arrange
        var metrics = PerformanceMetrics.Create(10, 65, 15, 96, 50);

        // Act & Assert
        Assert.False(metrics.IsHealthy);
        Assert.True(metrics.NeedsAttention);
    }

    [Fact]
    public void ShouldCreateCorrectly_WhenUsingPerformanceMetricsUsingStaticFactories()
    {
        // Act
        var excellent = PerformanceMetrics.Excellent;
        var poor = PerformanceMetrics.Poor;

        // Assert
        Assert.True(excellent.IsHealthy);
        Assert.False(excellent.NeedsAttention);

        Assert.False(poor.IsHealthy);
        Assert.True(poor.NeedsAttention);
    }

    #endregion

    #region SecurityContext Tests

    [Fact]
    public void ShouldCreate_WhenUsingSecurityContextUsingConstructorWithValidValues()
    {
        // Arrange
        var roles = AdminUserRoles;
        var permissions = ReadWritePermissions;

        // Act
        var context = SecurityContext.Create(
            "user123",
            roles,
            permissions,
            SecurityLevel.High,
            TimeSpan.FromHours(4));

        // Assert
        Assert.Equal("user123", context.UserId);
        Assert.Equal(2, context.Roles.Count);
        Assert.Equal(2, context.Permissions.Count);
        Assert.Equal(SecurityLevel.High, context.Level);
        Assert.True(context.IsValid);
    }

    [Fact]
    public void ShouldThrow_WhenUsingSecurityContextUsingConstructorWithEmptyUserId()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => SecurityContext.Create("", SingleRole, SinglePerm));
        Assert.Throws<ArgumentException>(
            () => SecurityContext.Create("   ", SingleRole, SinglePerm));
    }

    [Fact]
    public void ShouldWork_WhenUsingSecurityContextUsingCaseInsensitive()
    {
        // Arrange
        var context = SecurityContext.Create(
            "user",
            AdminRole,
            ReadPermission);

        // Act & Assert
        Assert.True(context.HasRole("admin"));
        Assert.True(context.HasRole("ADMIN"));
        Assert.True(context.HasPermission("read"));
        Assert.True(context.HasPermission("READ"));
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingSecurityContextUsingIsExpiredAfterExpiration()
    {
        // Arrange
        var context = SecurityContext.Create(
            "user",
            SingleRole,
            SinglePerm,
            validity: TimeSpan.FromMilliseconds(1));

        // Act — poll the observable condition instead of sleeping (R5.6)
        ClockAdvance.Until(() => context.IsExpired);

        // Assert
        Assert.True(context.IsExpired);
        Assert.False(context.IsValid);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingSecurityContextAddingMethods()
    {
        // Arrange
        var context = SecurityContext.ReadOnly("user");

        // Act
        var withRole = context.AddRole("editor");
        var withPermission = withRole.AddPermission("write");

        // Assert
        Assert.False(context.HasRole("editor"));
        Assert.True(withRole.HasRole("editor"));
        Assert.True(withPermission.HasPermission("write"));
    }

    [Fact]
    public void ShouldCreateCorrectly_WhenUsingSecurityContextUsingStaticFactories()
    {
        // Act
        var admin = SecurityContext.Admin("admin123");
        var readOnly = SecurityContext.ReadOnly("viewer123");

        // Assert
        Assert.True(admin.HasRole("admin"));
        Assert.True(admin.HasPermission("create"));
        Assert.True(admin.HasPermission("delete"));
        Assert.Equal(SecurityLevel.High, admin.Level);

        Assert.True(readOnly.HasRole("viewer"));
        Assert.True(readOnly.HasPermission("read"));
        Assert.False(readOnly.HasPermission("write"));
        Assert.Equal(SecurityLevel.Low, readOnly.Level);
    }

    #endregion

    #region CommunicationProtocol Tests

    [Fact]
    public void ShouldCreate_WhenUsingCommunicationProtocolUsingConstructorWithValidValues()
    {
        // Arrange
        var parameters = CommunicationParameters.CreateBuilder()
            .AddTimeout(TimeSpan.FromSeconds(60))
            .Build();

        // Act
        var protocol = CommunicationProtocol.Create(
            ProtocolType.MessageQueue,
            "xml",
            parameters,
            TimeSpan.FromSeconds(45),
            5);

        // Assert
        Assert.Equal(ProtocolType.MessageQueue, protocol.Type);
        Assert.Equal("xml", protocol.Format);
        Assert.Equal(parameters, protocol.Parameters);
        Assert.Equal(TimeSpan.FromSeconds(45), protocol.Timeout);
        Assert.Equal(5, protocol.MaxRetries);
    }

    [Fact]
    public void ShouldUseDefaults_WhenUsingCommunicationProtocolUsingConstructorWithDefaults()
    {
        // Act
        var protocol = CommunicationProtocol.Create(ProtocolType.Direct);

        // Assert
        Assert.Equal("json", protocol.Format);
        Assert.NotNull(protocol.Parameters);
        Assert.Equal(TimeoutQuick, protocol.Timeout);
        Assert.Equal(3, protocol.MaxRetries);
    }

    [Fact]
    public void ShouldThrow_WhenUsingCommunicationProtocolUsingConstructorWithNegativeRetries()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => CommunicationProtocol.Create(ProtocolType.Direct, maxRetries: -1));
        Assert.Contains("Max retries cannot be negative", ex.Message);
    }

    [Fact]
    public void ShouldReturnCorrectly_WhenUsingCommunicationProtocolUsingIsAsync()
    {
        // Arrange
        var asyncTypes = AsyncProtocolTypes;
        var syncTypes = SyncProtocolTypes;

        // Act & Assert
        foreach (var type in asyncTypes)
        {
            var protocol = CommunicationProtocol.Create(type);
            Assert.True(protocol.IsAsync);
            Assert.False(protocol.IsSync);
        }

        foreach (var type in syncTypes)
        {
            var protocol = CommunicationProtocol.Create(type);
            Assert.False(protocol.IsAsync);
            Assert.True(protocol.IsSync);
        }
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingCommunicationProtocolWithParameter()
    {
        // Arrange
        var protocol = CommunicationProtocol.Direct;

        // Act
        var withParam = protocol.WithParameter("key", "value");

        // Assert
        Assert.NotSame(protocol, withParam);
        Assert.False(protocol.Parameters.Contains("key"));
        Assert.True(withParam.Parameters.Contains("key"));
    }

    [Fact]
    public void ShouldCreateCorrectly_WhenUsingCommunicationProtocolUsingStaticFactories()
    {
        // Act
        var direct = CommunicationProtocol.Direct;
        var broadcast = CommunicationProtocol.Broadcast;
        var messageQueue = CommunicationProtocol.MessageQueue;

        // Assert
        Assert.Equal(ProtocolType.Direct, direct.Type);
        Assert.Equal(ProtocolType.Broadcast, broadcast.Type);
        Assert.Equal(ProtocolType.MessageQueue, messageQueue.Type);
    }

    #endregion

    #region ExecutionPlan Tests

    [Fact]
    public void ShouldCreate_WhenUsingExecutionPlanUsingConstructorWithValidValues()
    {
        // Arrange
        var steps = new[]
        {
            ExecutionStep.Create("Step1", "First step", TimeoutExtended),
            ExecutionStep.Create("Step2", "Second step", TimeSpan.FromMinutes(20))
        };
        var context = ExecutionContextParameters.CreateBuilder()
            .AddWorkingDirectory("/workspace")
            .Build();

        // Act
        var plan = ExecutionPlan.Create("TestPlan", steps, ExecutionStrategy.Sequential, context);

        // Assert
        Assert.Equal("TestPlan", plan.Name);
        Assert.Equal(2, plan.StepCount);
        Assert.Equal(ExecutionStrategy.Sequential, plan.Strategy);
        Assert.Equal(context, plan.Context);
        Assert.Equal(TimeSpan.FromMinutes(30), plan.EstimatedDuration);
    }

    [Fact]
    public void ShouldThrow_WhenUsingExecutionPlanUsingConstructorWithEmptyName()
    {
        // Arrange
        var steps = new[] { ExecutionStep.Create("Step", "Desc", TimeSpan.FromMinutes(1)) };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => ExecutionPlan.Create("", steps));
        Assert.Throws<ArgumentException>(() => ExecutionPlan.Create("   ", steps));
    }

    [Fact]
    public void ShouldThrow_WhenUsingExecutionPlanUsingConstructorWithNoSteps()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(
            () => ExecutionPlan.Create("Plan", Array.Empty<ExecutionStep>()));
        Assert.Contains("at least one step", ex.Message);
    }

    [Fact]
    public void ShouldReturnCorrectly_WhenUsingExecutionPlanUsingCanExecuteInParallel()
    {
        // Arrange
        var step = ExecutionStep.Create("Step", "Desc", TimeSpan.FromMinutes(1));

        // Act & Assert
        var sequential = ExecutionPlan.Create("Plan", [step], ExecutionStrategy.Sequential);
        Assert.False(sequential.CanExecuteInParallel);

        var parallel = ExecutionPlan.Create("Plan", [step], ExecutionStrategy.Parallel);
        Assert.True(parallel.CanExecuteInParallel);

        var mixed = ExecutionPlan.Create("Plan", [step], ExecutionStrategy.Mixed);
        Assert.True(mixed.CanExecuteInParallel);

        var adaptive = ExecutionPlan.Create("Plan", [step], ExecutionStrategy.Adaptive);
        Assert.False(adaptive.CanExecuteInParallel);
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingExecutionPlanAddingStep()
    {
        // Arrange
        var plan = ExecutionPlan.Create("Plan",
        [
            ExecutionStep.Create("Step1", "First", TimeoutStandard)
        ]);
        var newStep = ExecutionStep.Create("Step2", "Second", TimeoutExtended);

        // Act
        var updated = plan.AddStep(newStep);

        // Assert
        Assert.Equal(1, plan.StepCount);
        Assert.Equal(2, updated.StepCount);
        Assert.Equal(TimeoutLong, updated.EstimatedDuration);
    }

    #endregion

    #region ExecutionStep Tests

    [Fact]
    public void ShouldCreate_WhenUsingExecutionStepUsingConstructorWithValidValues()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var dependencies = Dep1Dep2Dependencies;
        var parameters = ExecutionStepParameters.CreateBuilder()
            .AddInput("data", "value")
            .Build();

        // Act
        var step = ExecutionStep.Create(
            "TestStep",
            "Test description",
            TimeoutLong,
            taskId,
            agentId,
            dependencies,
            parameters);

        // Assert
        Assert.Equal("TestStep", step.Name);
        Assert.Equal("Test description", step.Description);
        Assert.Equal(TimeoutLong, step.EstimatedDuration);
        Assert.Equal(taskId, step.TaskId);
        Assert.Equal(agentId, step.AssignedAgent);
        Assert.Equal(2, step.Dependencies.Count);
        Assert.Equal(parameters, step.Parameters);
    }

    [Fact]
    public void ShouldThrow_WhenUsingExecutionStepUsingConstructorWithInvalidValues()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => ExecutionStep.Create("", "Desc", TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentException>(
            () => ExecutionStep.Create("Name", "", TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentException>(
            () => ExecutionStep.Create("Name", "Desc", TimeSpan.Zero));
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingExecutionStepAssigningTo()
    {
        // Arrange
        var step = ExecutionStep.Create("Step", "Desc", TimeoutStandard);
        var agentId = AgentId.Create();

        // Act
        var assigned = step.AssignTo(agentId);

        // Assert
        Assert.Null(step.AssignedAgent);
        Assert.False(step.IsAssigned);
        Assert.Equal(agentId, assigned.AssignedAgent);
        Assert.True(assigned.IsAssigned);
    }

    #endregion

    #region Sealed Record Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingSealedRecordEnums()
    {
        // ConfidenceLevel
        Assert.Equal(4, ConfidenceLevel.All.Count);
        Assert.Equal("Low", ConfidenceLevel.Low.Value);
        Assert.Equal("Medium", ConfidenceLevel.Medium.Value);
        Assert.Equal("High", ConfidenceLevel.High.Value);
        Assert.Equal("Expert", ConfidenceLevel.Expert.Value);

        // ComplexityLevel
        Assert.Equal(4, ComplexityLevel.All.Count);
        Assert.Equal("Simple", ComplexityLevel.Simple.Value);
        Assert.Equal("Medium", ComplexityLevel.Medium.Value);
        Assert.Equal("Complex", ComplexityLevel.Complex.Value);
        Assert.Equal("VeryComplex", ComplexityLevel.VeryComplex.Value);

        // CollaborationType
        Assert.Equal(4, CollaborationType.All.Count);
        Assert.Equal("PeerToPeer", CollaborationType.PeerToPeer.Value);
        Assert.Equal("Hierarchical", CollaborationType.Hierarchical.Value);
        Assert.Equal("Consensus", CollaborationType.Consensus.Value);
        Assert.Equal("Broadcast", CollaborationType.Broadcast.Value);

        // SecurityLevel
        Assert.Equal(4, SecurityLevel.All.Count);
        Assert.Equal("Low", SecurityLevel.Low.Value);
        Assert.Equal("Standard", SecurityLevel.Standard.Value);
        Assert.Equal("High", SecurityLevel.High.Value);
        Assert.Equal("Maximum", SecurityLevel.Maximum.Value);

        // ProtocolType
        Assert.Equal(4, ProtocolType.All.Count);
        Assert.Equal("Direct", ProtocolType.Direct.Value);
        Assert.Equal("Broadcast", ProtocolType.Broadcast.Value);
        Assert.Equal("MessageQueue", ProtocolType.MessageQueue.Value);
        Assert.Equal("EventStream", ProtocolType.EventStream.Value);

        // ExecutionStrategy
        Assert.Equal(4, ExecutionStrategy.All.Count);
        Assert.Equal("Sequential", ExecutionStrategy.Sequential.Value);
        Assert.Equal("Parallel", ExecutionStrategy.Parallel.Value);
        Assert.Equal("Mixed", ExecutionStrategy.Mixed.Value);
        Assert.Equal("Adaptive", ExecutionStrategy.Adaptive.Value);
    }

    #endregion
}
