using System.Text;
using System.Text.Json;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// PUB-08 T2: A2A task persistence over the opt-in checkpointing state store —
/// GET /a2a/tasks/{id} answers 200/404 (instead of 501) once a store is
/// registered, and DELETE records the cancellation.
/// </summary>
public class A2ATaskPersistenceTests
{
    // ---- Adapter round-trip (no HTTP) ----

    [Fact]
    public async Task StateStoreAdapter_RoundTripsRecords()
    {
        var store = new StateStoreA2ATaskStore(new InMemoryStateStore());
        var record = new A2ATaskRecord
        {
            TaskId = "t-42",
            SkillId = "researcher",
            Status = A2ATaskStatus.Completed,
            Output = "done"
        };

        await store.SaveAsync(record, TestContext.Current.CancellationToken);
        var loaded = await store.GetAsync("t-42", TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal("t-42", loaded!.TaskId);
        Assert.Equal(A2ATaskStatus.Completed, loaded.Status);
        Assert.Equal("done", loaded.Output);
        Assert.Equal("researcher", loaded.SkillId);
    }

    [Fact]
    public async Task StateStoreAdapter_ReturnsNull_ForUnknownTask()
    {
        var store = new StateStoreA2ATaskStore(new InMemoryStateStore());

        var loaded = await store.GetAsync("nope", TestContext.Current.CancellationToken);

        Assert.Null(loaded);
    }

    // ---- End-to-end over HTTP ----

    [Fact]
    public async Task GetTask_Returns200_AfterSend_WhenStoreRegistered()
    {
        var store = new StateStoreA2ATaskStore(new InMemoryStateStore());
        var (server, port) = await A2ALoopback.StartAsync(
            options => new A2AServer(options, new StubA2ATaskRouter(), taskStore: store), TestContext.Current.CancellationToken);
        try
        {
            using var httpClient = new HttpClient();
            var taskId = Guid.NewGuid().ToString();
            var body = JsonSerializer.Serialize(new { id = taskId, skillId = "researcher", input = "hello" });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");

            var sendResponse = await httpClient.PostAsync(
                $"{LoopbackPorts.Host}:{port}/a2a/tasks/send", content, TestContext.Current.CancellationToken);
            Assert.Equal(System.Net.HttpStatusCode.OK, sendResponse.StatusCode);

            var getResponse = await httpClient.GetAsync(
                $"{LoopbackPorts.Host}:{port}/a2a/tasks/{taskId}", TestContext.Current.CancellationToken);

            Assert.Equal(System.Net.HttpStatusCode.OK, getResponse.StatusCode);
            var payload = JsonSerializer.Deserialize<A2ATaskResponse>(
                await getResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.NotNull(payload);
            Assert.Equal(taskId, payload!.TaskId);
            Assert.Equal(A2ATaskStatus.Completed, payload.Status);
            Assert.Equal("Stub output", payload.Output);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetTask_Returns404_ForUnknownId_WhenStoreRegistered()
    {
        var store = new StateStoreA2ATaskStore(new InMemoryStateStore());
        var (server, port) = await A2ALoopback.StartAsync(
            options => new A2AServer(options, new StubA2ATaskRouter(), taskStore: store), TestContext.Current.CancellationToken);
        try
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(
                $"{LoopbackPorts.Host}:{port}/a2a/tasks/unknown-task", TestContext.Current.CancellationToken);

            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetTask_Returns501_WithoutStore()
    {
        var (server, port) = await A2ALoopback.StartAsync(
            options => new A2AServer(options, new StubA2ATaskRouter()), TestContext.Current.CancellationToken);
        try
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(
                $"{LoopbackPorts.Host}:{port}/a2a/tasks/whatever", TestContext.Current.CancellationToken);

            Assert.Equal(System.Net.HttpStatusCode.NotImplemented, response.StatusCode);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task CancelTask_Returns404_ForUnknownId_And409_ForAFinishedTask_WhenStoreRegistered()
    {
        var store = new StateStoreA2ATaskStore(new InMemoryStateStore());
        var (server, port) = await A2ALoopback.StartAsync(
            options => new A2AServer(options, new StubA2ATaskRouter(), taskStore: store), TestContext.Current.CancellationToken);
        try
        {
            using var httpClient = new HttpClient();

            // Unknown id → 404 (no fabricated acknowledgement anymore).
            var unknown = await httpClient.DeleteAsync(
                $"{LoopbackPorts.Host}:{port}/a2a/tasks/ghost", TestContext.Current.CancellationToken);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, unknown.StatusCode);

            // A task that already finished cannot be cancelled (GAP-10): 409, and its record
            // keeps the state the execution left — it is never rewritten to Cancelled.
            var taskId = Guid.NewGuid().ToString();
            var body = JsonSerializer.Serialize(new { id = taskId, skillId = "researcher", input = "hello" });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            await httpClient.PostAsync(
                $"{LoopbackPorts.Host}:{port}/a2a/tasks/send", content, TestContext.Current.CancellationToken);

            var cancel = await httpClient.DeleteAsync(
                $"{LoopbackPorts.Host}:{port}/a2a/tasks/{taskId}", TestContext.Current.CancellationToken);
            Assert.Equal(System.Net.HttpStatusCode.Conflict, cancel.StatusCode);

            var record = await store.GetAsync(taskId, TestContext.Current.CancellationToken);
            Assert.NotNull(record);
            Assert.Equal(A2ATaskStatus.Completed, record!.Status);
        }
        finally
        {
            await server.DisposeAsync();
        }
    }
}
