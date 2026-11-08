using System.Text.Json;
using Orkeon.Application.Interfaces.AgentCommunication;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>FWK-23: an A2A task whose run waits for a human reads <c>input-required</c>; every other status keeps its wire name.</summary>
public sealed class A2AInputRequiredTests
{
    [Fact]
    [Trait("BR", "FWK-23")]
    public void ShouldSerializeInputRequiredAsInputRequired_WhenTheTaskWaitsForAHuman()
    {
        var json = JsonSerializer.Serialize(new A2ATaskResponse { TaskId = "t-1", Status = A2ATaskStatus.InputRequired, Output = "Which colour?" });

        using var document = JsonDocument.Parse(json);
        Assert.Equal("input-required", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    [Trait("BR", "FWK-23")]
    public void ShouldReadInputRequiredBack_WhenTheWireNameIsUsed()
    {
        var response = JsonSerializer.Deserialize<A2ATaskResponse>("""{"taskId":"t-1","status":"input-required"}""");

        Assert.Equal(A2ATaskStatus.InputRequired, response!.Status);
    }

    [Theory]
    [InlineData(A2ATaskStatus.Pending, "Pending")]
    [InlineData(A2ATaskStatus.Working, "Working")]
    [InlineData(A2ATaskStatus.Completed, "Completed")]
    [InlineData(A2ATaskStatus.Failed, "Failed")]
    [InlineData(A2ATaskStatus.Cancelled, "Cancelled")]
    [Trait("BR", "FWK-23")]
    public void ShouldKeepTheWireNameOfTheOtherStatuses(A2ATaskStatus status, string wire)
    {
        var json = JsonSerializer.Serialize(new A2ATaskResponse { TaskId = "t-1", Status = status });

        Assert.Contains($"\"{wire}\"", json, StringComparison.Ordinal);
    }
}
