namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Result returned by <c>crew.run(...)</c>. Mirrors the shape declared in <c>crew.d.ts</c>.
/// </summary>
#pragma warning disable IDE1006 // Property names match the JS surface
#pragma warning disable CS1591
public sealed class JsCrewResult
{
    public string output { get; }
    public IReadOnlyDictionary<string, object?> artifacts { get; }
    public IReadOnlyList<JsTaskResult> tasks { get; }

    internal JsCrewResult(string output,
        IReadOnlyDictionary<string, object?> artifacts,
        IReadOnlyList<JsTaskResult> tasks)
    {
        this.output = output;
        this.artifacts = artifacts;
        this.tasks = tasks;
    }
}

public sealed class JsTaskResult
{
    public string name { get; }
    public object? output { get; }
    public double durationMs { get; }

    internal JsTaskResult(string name, object? output, double durationMs)
    {
        this.name = name;
        this.output = output;
        this.durationMs = durationMs;
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
