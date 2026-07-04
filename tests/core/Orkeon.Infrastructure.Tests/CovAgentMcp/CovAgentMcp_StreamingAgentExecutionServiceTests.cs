using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Tests.Doubles;
using System.Runtime.CompilerServices;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.Tests.CovAgentMcp;

/// <summary>
/// Coverage tests for <see cref="StreamingAgentExecutionService"/> branches not
/// exercised elsewhere: VFS mount prompt, previous-output context, max iterations,
/// and the tool-call path.
/// </summary>
public sealed class CovAgentMcp_StreamingAgentExecutionServiceTests
{
    private sealed class NoopLogger : ILogger<StreamingAgentExecutionService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel l, EventId e, TState s, Exception? ex, Func<TState, Exception?, string> f) { }
    }

    private sealed class EchoTool : ITool
    {
        private readonly bool _succeed;
        public EchoTool(string name, bool succeed = true)
        {
            Name = name;
            _succeed = succeed;
            Schema = new ToolSchema(name, "echo", new Dictionary<string, ParameterSchema>());
        }
        public string Name { get; }
        public string Description => "echo";
        public ToolSchema Schema { get; }
        public override string ToString() => Name;
        public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken ct = default)
            => Task.FromResult(_succeed
                ? new ToolCallResponse(true, "tool-output", null)
                : new ToolCallResponse(false, null, "tool-error"));
        public Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
            => Task.FromResult(ToolResult.CreateSuccess(string.Empty));
        public bool ValidateInput(string input) => true;
    }

    private sealed class StubFileSystem : IFileSystemService
    {
        private readonly IReadOnlyList<MountInfo> _mounts;
        public StubFileSystem(IReadOnlyList<MountInfo> mounts) => _mounts = mounts;
        public IReadOnlyList<MountInfo> GetAvailableMounts() => _mounts;

        public PathValidationResult ResolveAndValidate(string v, FileAccessRights r) => throw new NotImplementedException();
        public string? ToVirtualPath(string p) => throw new NotImplementedException();
        public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string r, VirtualEnumerationOptions? o, CancellationToken ct) => throw new NotImplementedException();
        public Task<Stream> OpenReadStreamAsync(string v, CancellationToken ct) => throw new NotImplementedException();
        public Task<byte[]?> TryReadAllBytesAsync(string v, CancellationToken ct) => throw new NotImplementedException();
        public Task<string?> TryReadAllTextAsync(string v, CancellationToken ct) => throw new NotImplementedException();
        public Task<VirtualEntryKind> GetEntryKindAsync(string v, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> WriteAllTextAsync(string v, string c, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string v, CancellationToken ct) => throw new NotImplementedException();
        public Task CreateDirectoryAsync(string v, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string v, bool recursive, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> WriteAllBytesAsync(string v, byte[] c, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> AppendAllTextAsync(string v, string c, CancellationToken ct) => throw new NotImplementedException();
        public Task<VirtualFileEntry?> TryGetEntryAsync(string v, CancellationToken ct) => throw new NotImplementedException();
        public Task<Stream> OpenWriteStreamAsync(string v, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Stream> OpenAppendStreamAsync(string v, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CopyAsync(string s, string d, bool overwrite = false, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static DomainAgent CreateAgent() => new AgentBuilder()
        .Role("Researcher").Goal("Research").Backstory("Expert").Build();

    private static DomainTask CreateTask() => new CrewTaskBuilder()
        .Description("Do work").ExpectedOutput("Result").Build();

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "MockMemoryScope (no-op Dispose) is owned by the returned execution context, which lives for the duration of the test.")]
    private static SimpleExecutionContext Context(params TaskOutput[] previous) =>
        new(new Orkeon.Domain.Common.CrewId(), [], new MockMemoryScope(), previous, CancellationToken.None);

    private static async IAsyncEnumerable<ChatResponseUpdate> Stream(
        IEnumerable<ChatResponseUpdate> updates,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var u in updates)
        {
            ct.ThrowIfCancellationRequested();
            yield return u;
            await Task.Yield();
        }
    }

    private static async Task<List<AgentThought>> Collect(
        StreamingAgentExecutionService service, DomainAgent agent, DomainTask task, SimpleExecutionContext ctx)
    {
        var list = new List<AgentThought>();
        await foreach (var t in service.StreamExecutionAsync(agent, task, ctx))
            list.Add(t);
        return list;
    }

    [Fact]
    public async Task StreamExecution_WithFileSystemMounts_BuildsSystemPromptWithMountTable()
    {
        using var chat = new MockChatClient();
        chat.SetStreamingFunc((_, _, ct) => Stream(
            [new ChatResponseUpdate(ChatRole.Assistant, "answer")], ct));

        var mounts = new List<MountInfo>
        {
            new("/workspace", FileAccessRights.ReadOnly,
                new List<SubPathOverride> { new("secrets", FileAccessRights.ReadWriteNoDelete) }),
            new("/output", FileAccessRights.ReadWrite, [])
        };
        var fs = new StubFileSystem(mounts);
        var service = new StreamingAgentExecutionService(chat, [], new NoopLogger(), fs);

        var thoughts = await Collect(service, CreateAgent(), CreateTask(), Context());

        Assert.Contains(thoughts, t => t.Type == AgentThought.ThoughtType.Conclusion);
        // System prompt is the first message; assert mounts were rendered.
        var systemMsg = chat.LastGetStreamingResponseMessages!.First();
        Assert.Contains("Available File Mounts", systemMsg.Text);
        Assert.Contains("/workspace", systemMsg.Text);
        Assert.Contains("/workspace/secrets", systemMsg.Text);
        Assert.Contains("ro", systemMsg.Text);
        Assert.Contains("rw", systemMsg.Text);
    }

    [Fact]
    public async Task StreamExecution_WithEmptyMounts_DoesNotRenderTable()
    {
        using var chat = new MockChatClient();
        chat.SetStreamingFunc((_, _, ct) => Stream(
            [new ChatResponseUpdate(ChatRole.Assistant, "answer")], ct));
        var service = new StreamingAgentExecutionService(chat, [], new NoopLogger(), new StubFileSystem([]));

        await Collect(service, CreateAgent(), CreateTask(), Context());

        var systemMsg = chat.LastGetStreamingResponseMessages!.First();
        Assert.DoesNotContain("Available File Mounts", systemMsg.Text);
    }

    [Fact]
    public async Task StreamExecution_WithPreviousOutputs_IncludesThemInUserPrompt()
    {
        using var chat = new MockChatClient();
        chat.SetStreamingFunc((_, _, ct) => Stream(
            [new ChatResponseUpdate(ChatRole.Assistant, "done")], ct));
        var service = new StreamingAgentExecutionService(chat, [], new NoopLogger(), new StubFileSystem([]));

        var previous = new TaskOutput("t1", "a1", "earlier finding", DateTime.UtcNow, true, TimeSpan.Zero);
        await Collect(service, CreateAgent(), CreateTask(), Context(previous));

        var userMsg = chat.LastGetStreamingResponseMessages!.Last();
        Assert.Contains("Context from previous tasks", userMsg.Text);
        Assert.Contains("earlier finding", userMsg.Text);
    }

    [Fact]
    public async Task StreamExecution_WhenToolCalled_ExecutesToolAndContinues()
    {
        var tool = new EchoTool("calc");
        var agent = new AgentBuilder()
            .Role("Math").Goal("Compute").Backstory("Mathy").WithTool(tool).Build();

        using var chat = new MockChatClient();
        var iteration = 0;
        chat.SetStreamingFunc((_, _, ct) =>
        {
            iteration++;
            // First iteration emits a tool call (forces a continue);
            // second iteration concludes with text.
            return iteration == 1
                ? Stream([new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new FunctionCallContent("call-1", "calc",
                        new Dictionary<string, object?> { ["x"] = 2 })]
                }], ct)
                : Stream([new ChatResponseUpdate(ChatRole.Assistant, "final")], ct);
        });

        var service = new StreamingAgentExecutionService(chat, [tool], new NoopLogger(), new StubFileSystem([]));

        var thoughts = await Collect(service, agent, CreateTask(), Context());

        Assert.Contains(thoughts, t => t.Type == AgentThought.ThoughtType.ToolSelection && t.Content.Contains("calc"));
        Assert.Contains(thoughts, t => t.Type == AgentThought.ThoughtType.ToolExecution && t.Content.Contains("tool-output"));
        Assert.Contains(thoughts, t => t.Type == AgentThought.ThoughtType.Conclusion && t.Content.Contains("final"));
    }

    [Fact]
    public async Task StreamExecution_WhenToolReturnsError_EmitsErrorResult()
    {
        var tool = new EchoTool("failing", succeed: false);
        var agent = new AgentBuilder()
            .Role("Math").Goal("Compute").Backstory("Mathy").WithTool(tool).Build();

        using var chat = new MockChatClient();
        var iteration = 0;
        chat.SetStreamingFunc((_, _, ct) =>
        {
            iteration++;
            return iteration == 1
                ? Stream([new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new FunctionCallContent("c1", "failing", new Dictionary<string, object?>())]
                }], ct)
                : Stream([new ChatResponseUpdate(ChatRole.Assistant, "fin")], ct);
        });
        var service = new StreamingAgentExecutionService(chat, [tool], new NoopLogger(), new StubFileSystem([]));

        var thoughts = await Collect(service, agent, CreateTask(), Context());

        Assert.Contains(thoughts, t =>
            t.Type == AgentThought.ThoughtType.ToolExecution && t.Content.Contains("Error: tool-error"));
    }

    [Fact]
    public async Task StreamExecution_WhenToolNotFound_SkipsExecution()
    {
        // Agent references a tool name that is not present in the service's tool set.
        var referenced = new EchoTool("present");
        var agent = new AgentBuilder()
            .Role("Math").Goal("Compute").Backstory("Mathy").WithTool(referenced).Build();

        using var chat = new MockChatClient();
        var iteration = 0;
        chat.SetStreamingFunc((_, _, ct) =>
        {
            iteration++;
            return iteration == 1
                ? Stream([new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    // Calls a tool name the available set does not contain.
                    Contents = [new FunctionCallContent("c1", "missing", new Dictionary<string, object?>())]
                }], ct)
                : Stream([new ChatResponseUpdate(ChatRole.Assistant, "end")], ct);
        });
        var service = new StreamingAgentExecutionService(chat, [referenced], new NoopLogger(), new StubFileSystem([]));

        var thoughts = await Collect(service, agent, CreateTask(), Context());

        // ToolSelection thought is emitted, but no ToolExecution for the missing tool.
        Assert.Contains(thoughts, t => t.Type == AgentThought.ThoughtType.ToolSelection && t.Content.Contains("missing"));
        Assert.DoesNotContain(thoughts, t => t.Type == AgentThought.ThoughtType.ToolExecution);
    }

    [Fact]
    public async Task StreamExecution_WhenMaxIterationsReached_EmitsErrorThought()
    {
        var tool = new EchoTool("loop");
        var agent = new AgentBuilder()
            .Role("Looper").Goal("Loop").Backstory("Loops").WithTool(tool)
            .MaxIterations(2).Build();

        using var chat = new MockChatClient();
        // Always emit a tool call so the loop never concludes.
        chat.SetStreamingFunc((_, _, ct) => Stream([new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new FunctionCallContent("c", "loop", new Dictionary<string, object?>())]
        }], ct));
        var service = new StreamingAgentExecutionService(chat, [tool], new NoopLogger(), new StubFileSystem([]));

        var thoughts = await Collect(service, agent, CreateTask(), Context());

        Assert.Contains(thoughts, t =>
            t.Type == AgentThought.ThoughtType.Error && t.Content.Contains("Max iterations"));
    }

    [Fact]
    public async Task StreamExecution_WithNullTools_TreatedAsEmpty()
    {
        using var chat = new MockChatClient();
        chat.SetStreamingFunc((_, _, ct) => Stream(
            [new ChatResponseUpdate(ChatRole.Assistant, "ok")], ct));
        // Pass null for tools — constructor coalesces to empty.
        var service = new StreamingAgentExecutionService(chat, null!, new NoopLogger(), new StubFileSystem([]));

        var thoughts = await Collect(service, CreateAgent(), CreateTask(), Context());

        Assert.Contains(thoughts, t => t.Type == AgentThought.ThoughtType.Conclusion);
    }
}
