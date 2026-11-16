namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// The read a <c>DeliverableBaselineScope</c> shares between the attempts of a task (#392): each call
/// records the token it was given and waits on its own gate, which the test completes or cancels.
/// </summary>
internal sealed class FakeDeliverableBaselineRead
{
    /// <summary>One gate per call, in call order.</summary>
    public List<TaskCompletionSource<string?>> Gates { get; } = [];

    /// <summary>The token each call was given, in call order.</summary>
    public List<CancellationToken> Tokens { get; } = [];

    /// <summary>The number of reads made.</summary>
    public int Calls => Gates.Count;

    public System.Threading.Tasks.Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Gates.Add(gate);
        Tokens.Add(cancellationToken);
        return gate.Task.WaitAsync(cancellationToken);
    }
}
