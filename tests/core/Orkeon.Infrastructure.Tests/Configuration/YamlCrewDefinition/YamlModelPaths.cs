using System.Collections;
using System.Reflection;
using Orkeon.Infrastructure.Configuration;
using YamlDotNet.Serialization.NamingConventions;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// The keys the crew YAML models read, by path from <see cref="CrewYamlConfig"/> (GAP-39), as a crew file
/// writes them: <c>agents.*.llm.thinking.budgetTokens</c>, <c>tasks.*.llmOverride.responseSchema.strict</c>.
/// A dictionary entry or a list item is <c>*</c>. A property that holds no model ends a path: a scalar, a
/// list of names, a table without a type (<c>knowledge</c>, <c>context</c>, <c>toolRules</c>). A model is a
/// class of the Infrastructure assembly whose name ends in <c>YamlConfig</c>.
/// </summary>
internal static class YamlModelPaths
{
    /// <summary>Every path the models under <paramref name="root"/> read.</summary>
    public static SortedSet<string> Of(Type root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var paths = new SortedSet<string>(StringComparer.Ordinal);
        Collect(root, string.Empty, paths, []);
        return paths;
    }

    /// <summary>The paths a deserialized model sets: each property that is not null, down to its leaves.</summary>
    public static SortedSet<string> SetIn(object model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var paths = new SortedSet<string>(StringComparer.Ordinal);
        Walk(model, string.Empty, paths);
        return paths;
    }

    /// <summary>The model a property type holds — itself, or a dictionary's values, or a list's items — if any.</summary>
    public static (Type Model, bool Container)? ModelHeldBy(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (IsModel(type))
            return (type, false);

        if (GenericInterface(type, typeof(IDictionary<,>)) is { } dictionary)
        {
            var value = dictionary.GetGenericArguments()[1];
            return IsModel(value) ? (value, true) : null;
        }

        if (type != typeof(string) && GenericInterface(type, typeof(IEnumerable<>)) is { } sequence)
        {
            var item = sequence.GetGenericArguments()[0];
            return IsModel(item) ? (item, true) : null;
        }

        return null;
    }

    /// <summary>The public properties a model reads, each with the key a crew file writes it under.</summary>
    public static IEnumerable<(PropertyInfo Property, string Key)> KeysOf(Type model) =>
        model.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
            .Select(property => (property, CamelCaseNamingConvention.Instance.Apply(property.Name)));

    private static bool IsModel(Type type) =>
        type.IsClass
        && type.Assembly == typeof(CrewYamlConfig).Assembly
        && type.Name.EndsWith("YamlConfig", StringComparison.Ordinal);

    private static Type? GenericInterface(Type type, Type generic) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == generic
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == generic);

    private static void Collect(Type model, string prefix, SortedSet<string> paths, HashSet<Type> onPath)
    {
        if (!onPath.Add(model))
            throw new InvalidOperationException($"{model.Name} holds itself under '{prefix}': the paths would never end.");

        foreach (var (property, key) in KeysOf(model))
        {
            if (ModelHeldBy(property.PropertyType) is { } held)
                Collect(held.Model, prefix + key + (held.Container ? ".*." : "."), paths, onPath);
            else
                paths.Add(prefix + key);
        }

        onPath.Remove(model);
    }

    private static void Walk(object model, string prefix, SortedSet<string> paths)
    {
        foreach (var (property, key) in KeysOf(model.GetType()))
        {
            if (property.GetValue(model) is not { } value)
                continue;

            switch (ModelHeldBy(property.PropertyType))
            {
                case null:
                    paths.Add(prefix + key);
                    break;
                case { Container: false }:
                    Walk(value, prefix + key + ".", paths);
                    break;
                default:
                    var items = value is IDictionary dictionary ? dictionary.Values : (IEnumerable)value;
                    foreach (var item in items)
                    {
                        if (item is not null)
                            Walk(item, prefix + key + ".*.", paths);
                    }
                    break;
            }
        }
    }
}
