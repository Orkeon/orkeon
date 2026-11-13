using System.Text.Json.Nodes;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>
/// A wire probe whose answers are scripted per step: each <see cref="Reset"/> loads the next
/// wire string and the next warning list, as a real capture would between two requests.
/// </summary>
internal sealed class FakeReasoningWireProbe : IReasoningWireProbe
{
    /// <summary>The wire strings handed out, one per step (null means nothing was captured).</summary>
    public Queue<string?> Wires { get; } = new();

    /// <summary>The warning lists handed out, one per step; steps beyond them have none.</summary>
    public Queue<string[]> WarningLists { get; } = new();

    /// <summary>How many times the runner reset the probe.</summary>
    public int Resets { get; private set; }

    /// <summary>Every call to <see cref="Override"/>, in order; null marks a clearing call.</summary>
    public List<JsonObject?> Overrides { get; } = [];

    /// <summary>The override currently set.</summary>
    public JsonObject? CurrentOverride { get; private set; }

    /// <inheritdoc />
    public string? LastFields { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    /// <inheritdoc />
    public void Reset()
    {
        Resets++;
        LastFields = Wires.Count > 0 ? Wires.Dequeue() : null;
        Warnings = WarningLists.Count > 0 ? WarningLists.Dequeue() : [];
    }

    /// <inheritdoc />
    public void Override(JsonObject? fragment)
    {
        Overrides.Add(fragment);
        CurrentOverride = fragment;
    }
}
