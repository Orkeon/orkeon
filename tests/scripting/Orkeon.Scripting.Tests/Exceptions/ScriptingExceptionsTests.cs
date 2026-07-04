using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Versioning;

namespace Orkeon.Scripting.Tests.Exceptions;

public sealed class ScriptingExceptionsTests
{
    [Fact]
    public void AgentNotInCrewException_exposes_agent_name_and_message()
    {
        var ex = new AgentNotInCrewException("Alice");

        Assert.Equal("Alice", ex.AgentName);
        Assert.Contains("Alice", ex.Message);
        Assert.Contains("not attached", ex.Message);
        Assert.IsType<Exception>(ex, exactMatch: false);
    }

    [Fact]
    public void RecursiveAgentInvocationException_exposes_agent_name_and_message()
    {
        var ex = new RecursiveAgentInvocationException("Bob");

        Assert.Equal("Bob", ex.AgentName);
        Assert.Contains("Bob", ex.Message);
        Assert.Contains("Recursive", ex.Message);
    }

    [Fact]
    public void StateMutationOutsideWithException_exposes_property_name_and_message()
    {
        var ex = new StateMutationOutsideWithException("counter");

        Assert.Equal("counter", ex.PropertyName);
        Assert.Contains("counter", ex.Message);
        Assert.Contains("ctx.state.with", ex.Message);
    }

    [Fact]
    public void AgentAlreadyInCrewException_exposes_agent_and_crew_names()
    {
        var ex = new AgentAlreadyInCrewException("Alice", "Crew1");

        Assert.Equal("Alice", ex.AgentName);
        Assert.Equal("Crew1", ex.CrewName);
        Assert.Contains("Alice", ex.Message);
        Assert.Contains("Crew1", ex.Message);
        Assert.Contains("mono-crew", ex.Message);
    }

    [Fact]
    public void AgentNotInThisCrewException_exposes_agent_and_crew_names()
    {
        var ex = new AgentNotInThisCrewException("Alice", "Crew1");

        Assert.Equal("Alice", ex.AgentName);
        Assert.Equal("Crew1", ex.CrewName);
        Assert.Contains("Alice", ex.Message);
        Assert.Contains("Crew1", ex.Message);
        Assert.Contains("not a member", ex.Message);
    }

    [Fact]
    public void DuplicateAgentNameException_exposes_agent_and_crew_names()
    {
        var ex = new DuplicateAgentNameException("Alice", "Crew1");

        Assert.Equal("Alice", ex.AgentName);
        Assert.Equal("Crew1", ex.CrewName);
        Assert.Contains("Alice", ex.Message);
        Assert.Contains("Crew1", ex.Message);
        Assert.Contains("already contains", ex.Message);
    }

    [Fact]
    public void WaiterKickedException_exposes_queue_name_and_message()
    {
        var ex = new WaiterKickedException("jobs");

        Assert.Equal("jobs", ex.QueueName);
        Assert.Contains("jobs", ex.Message);
        Assert.Contains("kicked", ex.Message);
    }

    [Fact]
    public void ReceiveTimeoutException_exposes_timeout_and_message()
    {
        var timeout = TimeSpan.FromSeconds(2.5);
        var ex = new ReceiveTimeoutException(timeout);

        Assert.Equal(timeout, ex.Timeout);
        Assert.Contains("timed out", ex.Message);
        Assert.Contains("2.5", ex.Message);
    }

    [Fact]
    public void ScriptVersionMismatchError_exposes_declared_and_supported_versions()
    {
        var ex = new ScriptVersionMismatchError("2.0", "1.0");

        Assert.Equal("2.0", ex.DeclaredVersion);
        Assert.Equal("1.0", ex.SupportedVersion);
        Assert.Contains("2.0", ex.Message);
        Assert.Contains("1.0", ex.Message);
        Assert.IsType<Exception>(ex, exactMatch: false);
    }
}
