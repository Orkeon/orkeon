using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IAgentChannel"/>: answers every request with <see cref="Answer"/> when set,
/// else forwards to <see cref="Inner"/>; records each answer it returned in <see cref="Answers"/>.
/// </summary>
public sealed class FakeAgentChannel : IAgentChannel
{
    public IAgentChannel? Inner { get; init; }

    public Func<AgentChannelRequest, AgentChannelResponse>? Answer { get; init; }

    public List<AgentChannelResponse> Answers { get; } = [];

    public async System.Threading.Tasks.Task<AgentChannelResponse> RequestAsync(
        AgentChannelRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var response = Answer is not null
            ? Answer(request)
            : await Inner!.RequestAsync(request, timeout, cancellationToken).ConfigureAwait(false);
        lock (Answers) Answers.Add(response);
        return response;
    }

    public IDisposable RegisterHandler(
        AgentId agentId,
        Func<AgentChannelRequest, CancellationToken, System.Threading.Tasks.Task<AgentChannelResponse>> handler)
        => Inner?.RegisterHandler(agentId, handler) ?? new NoopRegistration();

    public System.Threading.Tasks.Task BroadcastAsync(
        AgentId fromAgentId, CrewId crewId, string content, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.CompletedTask;

    private sealed class NoopRegistration : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
