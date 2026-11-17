using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="ITokenBudgetTracker"/> that implements the provider-less members only, as a tracker written
/// before the provider overload does; <see cref="Calls"/> counts the usages it received.
/// </summary>
public sealed class StubTokenBudgetTracker : ITokenBudgetTracker
{
    public int Calls;

    public BudgetCheckResult RecordUsage(string crewId, string agentRole, int promptTokens, int completionTokens, string model)
    {
        Calls++;
        return BudgetCheckResult.WithinBudget(TokenBudgetUsage.None, TokenBudgetUsage.None);
    }

    public TokenBudgetReport GetReport(string crewId)
        => new(crewId, TokenBudgetUsage.None, new Dictionary<string, TokenBudgetUsage>());
}
