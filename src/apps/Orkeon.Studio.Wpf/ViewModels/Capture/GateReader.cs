using System.Collections;
using System.Globalization;
using System.Reflection;

namespace Orkeon.Studio.Wpf.ViewModels.Capture;

/// <summary>
/// Reads a dotted path off the shell — <c>CreateTeam.IsStep3</c>, <c>Teams.Teams[0].IsIdle</c>.
/// <para>
/// This is what turns a stop's <see cref="CaptureStop.Covers"/> from a comment into an assertion.
/// A path that no longer resolves is itself a failure, not a false: renaming a gate has to break
/// the build, not quietly make every claim about it vacuously true.
/// </para>
/// </summary>
internal static class GateReader
{
    /// <summary>Whether the gate at <paramref name="path"/> is currently true.</summary>
    public static bool IsTrue(object root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Read(root, path) switch
        {
            bool value => value,
            null => false,
            // A non-boolean gate reads as "has something to show": that is what a string banner or
            // a nullable object means on screen, and naming it in Covers should say exactly that.
            string text => text.Length > 0,
            var other => other is not null,
        };
    }

    /// <summary>The value at <paramref name="path"/>; throws when the path does not resolve.</summary>
    public static object? Read(object root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        object? current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is null)
                throw new InvalidOperationException($"'{path}': '{segment}' was reached through a null.");

            current = Step(current, segment, path);
        }

        return current;
    }

    /// <summary>True when the path resolves at all, whatever it holds.</summary>
    public static bool Resolves(object root, string path)
    {
        try
        {
            Read(root, path);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static object? Step(object current, string segment, string path)
    {
        var name = segment;
        int? index = null;

        var bracket = segment.IndexOf('[', StringComparison.Ordinal);
        if (bracket > 0 && segment.EndsWith(']'))
        {
            name = segment[..bracket];
            index = int.Parse(
                segment[(bracket + 1)..^1], CultureInfo.InvariantCulture);
        }

        var property = current.GetType().GetProperty(
            name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"'{path}': '{current.GetType().Name}' has no public property '{name}'.");

        var value = property.GetValue(current);
        if (index is not { } position)
            return value;

        if (value is not IEnumerable sequence)
            throw new InvalidOperationException($"'{path}': '{name}' is not indexable.");

        var items = sequence.Cast<object?>().ToList();
        if (position >= items.Count)
        {
            throw new InvalidOperationException(
                $"'{path}': '{name}' holds {items.Count} item(s), so [{position}] does not exist.");
        }

        return items[position];
    }
}
