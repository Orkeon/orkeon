namespace Orkeon.Application.Crew.Compaction;

/// <summary>
/// A stage that relieves a loop's conversation before compaction does (#432, the seam of #434's
/// pruning). Before every model call of a loop that compacts, each registered stage is handed the
/// measure and the conversation, in registration order; when one relieved it, the loop measures
/// again, and compacts only when the estimate still reaches <see cref="ContextPressure.TriggerTokens"/>.
/// A stage decides on its own threshold (<see cref="ContextPressure.FillShare"/>), below the trigger.
/// </summary>
public interface IContextPressureStage
{
    /// <summary>Relieves <paramref name="conversation"/> when it judges the pressure calls for it.</summary>
    /// <param name="pressure">The measure before this call.</param>
    /// <param name="conversation">The loop's conversation, which the stage may rewrite.</param>
    /// <param name="cancellationToken">The task's cancellation.</param>
    /// <returns>True when the stage changed the conversation, so the loop measures it again.</returns>
    ValueTask<bool> RelieveAsync(ContextPressure pressure, ConversationView conversation, CancellationToken cancellationToken);
}
