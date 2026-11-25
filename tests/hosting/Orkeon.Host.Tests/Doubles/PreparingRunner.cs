namespace Orkeon.Host.Tests.Doubles;

/// <summary>The real runner, keeping what each conversation was prepared into.</summary>
internal sealed class PreparingRunner(ICrewRunner inner) : ICrewRunner
{
    public List<PreparedRun> Prepared { get; } = [];

    public Task<HostedRunResult> RunAsync(
        string crewName,
        string prompt,
        string origin,
        Action<string>? onProgress = null,
        Func<string, Task>? onStarted = null,
        IReadOnlyDictionary<string, string>? variables = null,
        RunIdempotency? idempotency = null,
        HostedConversation? conversation = null) =>
        inner.RunAsync(crewName, prompt, origin, onProgress, onStarted, variables, idempotency, conversation is null ? null : conversation with
        {
            PrepareAsync = async ct =>
            {
                var prepared = await conversation.PrepareAsync(ct);
                lock (Prepared)
                    Prepared.Add(prepared);
                return prepared;
            },
        });
}
