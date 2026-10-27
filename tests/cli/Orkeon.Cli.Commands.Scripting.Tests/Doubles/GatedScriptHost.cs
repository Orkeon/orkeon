using System.Collections.Concurrent;
using Orkeon.Cli.Commands.Scripting.Dispatch;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

#pragma warning disable IDE1006 // camelCase methods: exposed to scripts as ctx.services.get("script-host")
/// <summary>
/// Stand-in for <c>script-host</c>: each <see cref="runCrewAsync"/> parks until the test opens the gate of its
/// name, and <see cref="park"/> holds the engine thread until the <c>park</c> gate opens.
/// </summary>
internal sealed class GatedScriptHost
{
    private readonly CommandDispatchService _service;
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new();

    public GatedScriptHost(CommandDispatchService service) => _service = service;

    /// <summary>The gate of <paramref name="label"/>, created closed on first use.</summary>
    public TaskCompletionSource Gate(string label)
        => _gates.GetOrAdd(label, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    /// <summary>Opens every gate, so nothing outlives the test.</summary>
    public void OpenAll()
    {
        foreach (var gate in _gates.Values) gate.TrySetResult();
    }

    public string park()
    {
        Gate("park-entered").TrySetResult();
        Gate("park").Task.Wait();
        return "ok";
    }

    public string runCrewAsync(string name)
        => _service.postWork(name, "crew:" + name, async ct =>
        {
            await Gate(name).Task.WaitAsync(ct).ConfigureAwait(false);
            return new CommandResponse(name, name, true, name + "-out", null);
        });
}
#pragma warning restore IDE1006
