using System.Net;
using System.Text.Json;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.A2A;

public sealed class A2AClientTests : IDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly FakeHttpMessageHandler _handler;
    private readonly A2AClient _client;

    public A2AClientTests()
    {
        _handler = new FakeHttpMessageHandler();
        var factory = new FakeHttpClientFactory(_handler);
        var options = new A2AOptions { TimeoutSeconds = 30 };
        _client = new A2AClient(factory, options, new FakeFileSystemService());
    }

    [Fact]
    public async Task SendTaskAsync_ShouldReturnResponse_WhenSuccessful()
    {
        // Arrange
        var expectedResponse = new A2ATaskResponse
        {
            TaskId = "task-123",
            Status = A2ATaskStatus.Completed,
            Output = "Task completed successfully"
        };

        _handler.SetResponse(
            "http://remote:5002/a2a/tasks/send",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(expectedResponse, s_jsonOptions));

        var request = new A2ATaskRequest
        {
            Id = "task-123",
            SkillId = "researcher",
            Input = "Research AI trends"
        };

        // Act
        var result = await _client.SendTaskAsync(new Uri("http://remote:5002"), request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("task-123", result.TaskId);
        Assert.Equal(A2ATaskStatus.Completed, result.Status);
        Assert.Equal("Task completed successfully", result.Output);
    }

    [Fact]
    public async Task GetTaskStatusAsync_ShouldReturnResponse()
    {
        // Arrange
        var expectedResponse = new A2ATaskResponse
        {
            TaskId = "task-456",
            Status = A2ATaskStatus.Working
        };

        _handler.SetResponse(
            "http://remote:5002/a2a/tasks/task-456",
            HttpStatusCode.OK,
            JsonSerializer.Serialize(expectedResponse, s_jsonOptions));

        // Act
        var result = await _client.GetTaskStatusAsync(new Uri("http://remote:5002"), "task-456", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("task-456", result.TaskId);
        Assert.Equal(A2ATaskStatus.Working, result.Status);
    }

    [Fact]
    public async Task CancelTaskAsync_ShouldNotThrow_WhenSuccessful()
    {
        // Arrange
        _handler.SetResponse(
            "http://remote:5002/a2a/tasks/task-789",
            HttpStatusCode.OK,
            "{}");

        // Act & Assert — should not throw
        var exception = await Record.ExceptionAsync(() => _client.CancelTaskAsync(new Uri("http://remote:5002"), "task-789", TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

    [Fact]
    public async Task SendTaskAsync_ShouldThrow_WhenServerReturnsError()
    {
        // Arrange
        _handler.SetResponse(
            "http://remote:5002/a2a/tasks/send",
            HttpStatusCode.InternalServerError,
            "Server Error");

        var request = new A2ATaskRequest
        {
            Id = "task-fail",
            SkillId = "unknown",
            Input = "test"
        };

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => _client.SendTaskAsync(new Uri("http://remote:5002"), request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A2ATaskRequest_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var request = new A2ATaskRequest();

        // Assert
        Assert.NotEmpty(request.Id); // Auto-generated GUID
        Assert.Equal("", request.SkillId);
        Assert.Equal("", request.Input);
        Assert.Equal("text/plain", request.InputMode);
        Assert.Null(request.Metadata);
    }

    [Fact]
    public void A2ATaskResponse_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var response = new A2ATaskResponse();

        // Assert
        Assert.Equal("", response.TaskId);
        Assert.Equal(A2ATaskStatus.Pending, response.Status);
        Assert.Null(response.Output);
        Assert.Null(response.Error);
    }

    [Fact]
    public void A2ATaskUpdate_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var update = new A2ATaskUpdate();

        // Assert
        Assert.Equal("", update.TaskId);
        Assert.Equal(A2ATaskStatus.Pending, update.Status);
        Assert.Null(update.PartialOutput);
    }

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }
}
