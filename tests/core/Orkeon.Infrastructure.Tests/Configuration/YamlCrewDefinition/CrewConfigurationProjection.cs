using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// The canonical projection of a <see cref="CrewConfiguration"/> (GAP-39, decision 4): every public
/// property, recursively, one line per leaf keyed by its path — <c>Tasks[collect].Deliverable.SchemaInline</c>.
/// Lists keep their order, dictionaries are read by key. An agent or a task is identified by its key, by its
/// identifier when it has none, and every <see cref="AgentId"/> or <see cref="TaskId"/> met on the way becomes
/// the identity of the entry it names: the loader mints new identifiers at each load, the keys stay.
/// <see cref="RagCrewConfig.CrewDirectory"/> is left aside — where the crew was read from, not what it declares.
/// <para>
/// Record equality cannot stand in for it: identifiers differ at each load, and the records hold lists and
/// dictionaries their <c>Equals</c> compares by reference. Nor can the exported text alone: a key the export
/// never writes is absent on both sides.
/// </para>
/// </summary>
internal static class CrewConfigurationProjection
{
    private const int MaxDepth = 32;

    /// <summary>The properties the projection leaves aside: the entries' identity is in the path.</summary>
    private static readonly HashSet<(Type Type, string Property)> s_leftAside =
    [
        (typeof(RagCrewConfig), nameof(RagCrewConfig.CrewDirectory)),
        (typeof(AgentConfiguration), nameof(AgentConfiguration.Id)),
        (typeof(AgentConfiguration), nameof(AgentConfiguration.Key)),
        (typeof(TaskConfiguration), nameof(TaskConfiguration.Id)),
        (typeof(TaskConfiguration), nameof(TaskConfiguration.Key)),
    ];

    /// <summary>The projection of <paramref name="config"/>: each leaf's path and its canonical text.</summary>
    public static SortedDictionary<string, string> Of(CrewConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var walker = new Walker(config);
        foreach (var property in PublicPropertiesOf(typeof(CrewConfiguration)))
        {
            switch (property.Name)
            {
                case nameof(CrewConfiguration.Agents):
                    walker.Entries(property.Name, config.Agents, NameOf);
                    break;
                case nameof(CrewConfiguration.Tasks):
                    walker.Entries(property.Name, config.Tasks, NameOf);
                    break;
                default:
                    walker.Visit(property.Name, Read(property, config), depth: 1);
                    break;
            }
        }

        return walker.Lines;
    }

    /// <summary>Each path where the two projections differ, with both sides.</summary>
    public static IReadOnlyList<string> Differences(CrewConfiguration expected, CrewConfiguration actual)
    {
        var left = Of(expected);
        var right = Of(actual);

        var differences = new List<string>();
        foreach (var path in left.Keys.Union(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var hasLeft = left.TryGetValue(path, out var leftValue);
            var hasRight = right.TryGetValue(path, out var rightValue);
            if (hasLeft && hasRight && string.Equals(leftValue, rightValue, StringComparison.Ordinal))
                continue;

            differences.Add($"{path}: expected {(hasLeft ? leftValue : "(absent)")}, actual {(hasRight ? rightValue : "(absent)")}");
        }

        return differences;
    }

    /// <summary>Fails, naming every path that differs, unless the two configurations project alike.</summary>
    public static void AssertEqual(CrewConfiguration expected, CrewConfiguration actual)
    {
        var differences = Differences(expected, actual);
        Assert.True(
            differences.Count == 0,
            $"The configurations differ at {differences.Count} path(s):{Environment.NewLine}"
            + string.Join(Environment.NewLine, differences.Take(60)));
    }

    /// <summary>How the projection names an agent: its key, else its identifier — as the export writes it.</summary>
    public static string NameOf(AgentConfiguration agent) =>
        string.IsNullOrWhiteSpace(agent.Key) ? agent.Id.ToString() : agent.Key;

    /// <summary>How the projection names a task: its key, else its identifier — as the export writes it.</summary>
    public static string NameOf(TaskConfiguration task) =>
        string.IsNullOrWhiteSpace(task.Key) ? task.Id.ToString() : task.Key;

    private static IEnumerable<PropertyInfo> PublicPropertiesOf(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Where(property => !s_leftAside.Contains((property.DeclaringType!, property.Name)));

    private static object? Read(PropertyInfo property, object owner)
    {
        try
        {
            return property.GetValue(owner);
        }
        catch (TargetInvocationException ex)
        {
            return new ThrownByGetter(ex.InnerException?.GetType().Name ?? ex.GetType().Name);
        }
    }

    private static bool IsLeaf(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
        || type == typeof(Guid) || type == typeof(Uri) || typeof(TypedId).IsAssignableFrom(type);

    /// <summary>A getter that threw: projected as the exception's type, so both sides still compare.</summary>
    private sealed record ThrownByGetter(string ExceptionType);

    private sealed class Walker
    {
        private readonly Dictionary<AgentId, string> _agents = [];
        private readonly Dictionary<TaskId, string> _tasks = [];

        public Walker(CrewConfiguration config)
        {
            foreach (var agent in config.Agents)
                _agents.TryAdd(agent.Id, NameOf(agent));
            foreach (var task in config.Tasks)
                _tasks.TryAdd(task.Id, NameOf(task));
        }

        public SortedDictionary<string, string> Lines { get; } = new(StringComparer.Ordinal);

        /// <summary>Agents or tasks: their names in order, then each one under its name.</summary>
        public void Entries<T>(string path, IReadOnlyList<T> entries, Func<T, string> nameOf)
            where T : notnull
        {
            Set(path, "[" + string.Join(", ", entries.Select(nameOf)) + "]");
            foreach (var entry in entries)
                Visit($"{path}[{nameOf(entry)}]", entry, depth: 1);
        }

        public void Visit(string path, object? value, int depth)
        {
            if (depth > MaxDepth)
                throw new InvalidOperationException($"The projection went {MaxDepth} levels deep at {path}: a cycle?");

            switch (value)
            {
                case null:
                    Set(path, "null");
                    return;
                case string text:
                    Set(path, "\"" + text + "\"");
                    return;
                case bool flag:
                    Set(path, flag ? "true" : "false");
                    return;
                case AgentId agentId:
                    Set(path, "@" + (_agents.TryGetValue(agentId, out var agent) ? agent : "?" + agentId));
                    return;
                case TaskId taskId:
                    Set(path, "@" + (_tasks.TryGetValue(taskId, out var task) ? task : "?" + taskId));
                    return;
                case ThrownByGetter thrown:
                    Set(path, "!" + thrown.ExceptionType);
                    return;
            }

            var type = value.GetType();
            if (IsLeaf(type))
            {
                Set(path, value is IFormattable formattable
                    ? formattable.ToString(null, CultureInfo.InvariantCulture)
                    : value.ToString() ?? string.Empty);
                return;
            }

            if (TryReadDictionary(value, out var pairs))
            {
                Set(path, "{" + pairs.Count.ToString(CultureInfo.InvariantCulture) + "}");
                foreach (var (key, item) in pairs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    Visit($"{path}[{key}]", item, depth + 1);
                return;
            }

            if (value is IEnumerable sequence)
            {
                if (IsDefaultImmutableArray(value))
                {
                    Set(path, "default");
                    return;
                }

                var items = sequence.Cast<object?>().ToList();
                Set(path, "[" + items.Count.ToString(CultureInfo.InvariantCulture) + "]");
                for (var i = 0; i < items.Count; i++)
                    Visit($"{path}[{i.ToString(CultureInfo.InvariantCulture)}]", items[i], depth + 1);
                return;
            }

            var properties = PublicPropertiesOf(type).ToList();
            if (properties.Count == 0)
            {
                Set(path, value.ToString() ?? string.Empty);
                return;
            }

            Set(path, "{" + type.Name + "}");
            foreach (var property in properties)
                Visit($"{path}.{property.Name}", Read(property, value), depth + 1);
        }

        private void Set(string path, string text)
        {
            if (!Lines.TryAdd(path, text))
                throw new InvalidOperationException($"Two values project to {path}: two entries share a name?");
        }

        private static bool TryReadDictionary(object value, out List<(string Key, object? Value)> pairs)
        {
            pairs = [];
            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                    pairs.Add((KeyText(entry.Key), entry.Value));
                return true;
            }

            var readOnly = value.GetType().GetInterfaces().FirstOrDefault(
                i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
            if (readOnly is null)
                return false;

            foreach (var pair in (IEnumerable)value)
            {
                var pairType = pair!.GetType();
                pairs.Add((KeyText(pairType.GetProperty("Key")!.GetValue(pair)), pairType.GetProperty("Value")!.GetValue(pair)));
            }

            return true;
        }

        private static string KeyText(object? key) =>
            key is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : key?.ToString() ?? "null";

        private static bool IsDefaultImmutableArray(object value)
        {
            var type = value.GetType();
            return type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(ImmutableArray<>)
                && (bool)type.GetProperty(nameof(ImmutableArray<int>.IsDefault))!.GetValue(value)!;
        }
    }
}
