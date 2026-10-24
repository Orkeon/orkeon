using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.HumanInput;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IHumanInputProvider with call tracking and configurable results.
/// </summary>
public class MockHumanInputProvider : IHumanInputProvider
{
    private string _inputResult = "mock user input";
    private bool _confirmationResult = true;
    private string _choiceResult = "option1";
    private bool _isAvailable = true;

    // --- Tracking ---
    public int GetInputCallCount { get; private set; }
    public HumanInputContext? LastGetInputContext { get; private set; }

    public int GetConfirmationCallCount { get; private set; }
    public HumanInputContext? LastGetConfirmationContext { get; private set; }

    public int GetChoiceCallCount { get; private set; }
    public HumanInputContext? LastGetChoiceContext { get; private set; }

    public int GetMultiChoiceCallCount { get; private set; }
    public HumanInputContext? LastGetMultiChoiceContext { get; private set; }
    private IReadOnlyList<string> _multiChoiceResult = ["option1"];
    public void SetMultiChoiceResult(IReadOnlyList<string> result) => _multiChoiceResult = result;

    /// <summary>When set, GetMultiChoiceAsync answers with this pending result: the test releases it.</summary>
    public TaskCompletionSource<IReadOnlyList<string>>? PendingMultiChoice { get; set; }

    /// <summary>Completed when GetMultiChoiceAsync was entered.</summary>
    public TaskCompletionSource MultiChoiceEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int IsAvailableCallCount { get; private set; }

    /// <summary>When set, GetInputAsync answers with this pending result: the test releases it.</summary>
    public TaskCompletionSource<string>? PendingInput { get; set; }

    /// <summary>Completed when GetInputAsync was entered.</summary>
    public TaskCompletionSource InputEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // --- Configuration ---
    public void SetInputResult(string result) => _inputResult = result;
    public void SetConfirmationResult(bool result) => _confirmationResult = result;
    public void SetChoiceResult(string result) => _choiceResult = result;
    public void SetAvailable(bool available) => _isAvailable = available;

    // --- IHumanInputProvider ---
    public Task<string> GetInputAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        GetInputCallCount++;
        LastGetInputContext = context;
        InputEntered.TrySetResult();
        if (PendingInput is { } pending)
            return pending.Task;
        return Task.FromResult(_inputResult);
    }

    public Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        GetConfirmationCallCount++;
        LastGetConfirmationContext = context;
        return Task.FromResult(_confirmationResult);
    }

    public Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        GetChoiceCallCount++;
        LastGetChoiceContext = context;
        return Task.FromResult(_choiceResult);
    }

    public Task<IReadOnlyList<string>> GetMultiChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        GetMultiChoiceCallCount++;
        LastGetMultiChoiceContext = context;
        MultiChoiceEntered.TrySetResult();
        if (PendingMultiChoice is { } pending)
            return pending.Task;
        return Task.FromResult(_multiChoiceResult);
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        IsAvailableCallCount++;
        return Task.FromResult(_isAvailable);
    }
}
