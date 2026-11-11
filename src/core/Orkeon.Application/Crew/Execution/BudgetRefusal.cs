using Orkeon.Domain.Autonomous;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Finds a spent budget in what a model call threw: the host's meter refuses a call with a
/// <see cref="BudgetExhaustedException"/>, which a chat-client adapter may wrap.
/// </summary>
internal static class BudgetRefusal
{
    /// <summary>The budget refusal in <paramref name="exception"/> or its inner exceptions; null when there is none.</summary>
    public static BudgetExhaustedException? Find(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is BudgetExhaustedException spent)
                return spent;
        }

        return null;
    }
}
