using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Domain.HumanInput;

namespace Orkeon.ConsoleApp.Services;

/// <summary>Asks the operator through the REPL read loop, never beside it (RT-08).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Process-lifetime singleton; the semaphore holds no handle until waited on asynchronously.")]
internal sealed class ReplHumanInputProvider : IHumanInputProvider, IReplLineInterceptor
{
    private const string AnswerPrompt = "answer> ";

    private readonly Lazy<Orkeon.Cli.Abstractions.Console.IConsoleAdapter> _console;
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly object _gate = new();
    private Pending? _pending;

    public ReplHumanInputProvider(Func<Orkeon.Cli.Abstractions.Console.IConsoleAdapter> console) => _console = new(console);

    public string? PromptOverride
    {
        get
        {
            lock (_gate)
                return _pending is null ? null : AnswerPrompt;
        }
    }

    public bool TryConsume(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var answer = line.Trim();
        if (answer.StartsWith('/'))
            return false;

        string? problem;
        lock (_gate)
        {
            if (_pending is not { } pending)
                return false;

            problem = pending.Accept(answer);
            if (problem is null)
                _pending = null;
        }

        if (problem is not null)
            _console.Value.WriteLine(problem);
        return true;
    }

    public Task<string> GetInputAsync(HumanInputContext context, CancellationToken cancellationToken = default)
        => AskAsync(
            context,
            static (_, a) => (a, null),
            static c => c.DefaultValue ?? string.Empty,
            options: false,
            cancellationToken);

    public Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken cancellationToken = default)
        => AskAsync(
            context,
            static (_, a) => a.ToUpperInvariant() switch
            {
                "Y" or "YES" or "O" or "OUI" => (true, null),
                "N" or "NO" or "NON" => (false, null),
                _ => (false, "Answer yes or no."),
            },
            static c => bool.TryParse(c.DefaultValue, out var d) && d,
            options: false,
            cancellationToken);

    public Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
        => AskAsync(
            context,
            static (c, a) => Resolve(c, a) is { } v ? (v, null) : (string.Empty, "Pick a number or a value from the list."),
            static c => Recommended(c) ?? c.DefaultValue ?? string.Empty,
            options: true,
            cancellationToken);

    public Task<IReadOnlyList<string>> GetMultiChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
        => AskAsync<IReadOnlyList<string>>(
            context,
            static (c, a) =>
            {
                var picked = new List<string>();
                foreach (var part in a.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Resolve(c, part) is not { } v)
                        return ([], $"'{part}' is not in the list: pick numbers or values, comma-separated.");
                    picked.Add(v);
                }

                return (picked, null);
            },
            static c => Recommended(c) is { } r ? [r] : [],
            options: true,
            cancellationToken);

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    private static string? Recommended(HumanInputContext c)
        => c.Recommended is { } i && i >= 0 && i < c.Options.Count ? c.Options[i] : null;

    private static string? Resolve(HumanInputContext c, string token)
    {
        if (int.TryParse(token, out var n))
            return n >= 1 && n <= c.Options.Count ? c.Options[n - 1] : null;
        return c.Options.FirstOrDefault(o => string.Equals(o, token, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<T> AskAsync<T>(
        HumanInputContext context,
        Func<HumanInputContext, string, (T Value, string? Problem)> parse,
        Func<HumanInputContext, T> whenEmpty,
        bool options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new Pending(a =>
            {
                if (a.Length == 0)
                {
                    completion.TrySetResult(whenEmpty(context));
                    return null;
                }

                var (value, problem) = parse(context, a);
                if (problem is null)
                    completion.TrySetResult(value);
                return problem;
            });

            Write(context, options);
            lock (_gate)
                _pending = pending;

            await using var registration = cancellationToken.Register(() =>
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_pending, pending))
                        _pending = null;
                }

                completion.TrySetCanceled(cancellationToken);
            });
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _one.Release();
        }
    }

    private void Write(HumanInputContext context, bool options)
    {
        var console = _console.Value;
        console.WriteLine(context.Prompt);
        if (!options)
            return;

        for (var i = 0; i < context.Options.Count; i++)
        {
            var marker = i == context.Recommended ? " (recommended)" : string.Empty;
            console.WriteLine($"  {i + 1}. {context.Options[i]}{marker}");
        }
    }

    private sealed class Pending(Func<string, string?> accept)
    {
        public string? Accept(string answer) => accept(answer);
    }
}
