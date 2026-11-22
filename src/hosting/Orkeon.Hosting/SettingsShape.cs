using System.Collections;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Orkeon.Hosting;

/// <summary>
/// The keys a declared section may carry (GAP-40), read off the type its reader declares the way the
/// configuration binder reads it: a public property is a key (its <see cref="ConfigurationKeyNameAttribute"/>
/// name when it has one) when the binder writes it — a public <c>set</c> or <c>init</c> — or fills
/// it — a dictionary, a list or an object behind a getter —, a computed value or one behind a private
/// setter none (#449); a dictionary takes any key, a list any index, and a value — anything the
/// binder converts from text — none. A value carries the types it is bound to (<see cref="Values"/>),
/// which the start validation converts it to. The shapes declared at one path are merged: the readers
/// of one section together.
/// </summary>
internal sealed class SettingsShape
{
    private SettingsShape()
    {
    }

    /// <summary>The keys the node names, each with the shape below it.</summary>
    public Dictionary<string, SettingsShape> Named { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The shape of any other key — a dictionary's entries, a list's items —, or null when the node names its keys.</summary>
    public SettingsShape? Entries { get; private set; }

    /// <summary>Whether anything goes below the node (an <see cref="object"/>, a raw section).</summary>
    public bool Open { get; private set; }

    /// <summary>
    /// The types the binder converts the node's value to, as declared — a nullable one included —, one
    /// per reader that declares a different one; empty when the node is no value.
    /// </summary>
    public HashSet<Type> Values { get; } = [];

    /// <summary>Whether the binder reads the node's items as a list (an array, a collection), not a dictionary's entries.</summary>
    public bool IsList { get; private set; }

    /// <summary>
    /// Whether the list's reader also takes it as one comma-separated value
    /// (<see cref="Orkeon.Application.Configuration.CommaSeparatedSettingAttribute"/>): a single value
    /// written there is the list, not a mistake. True when one reader of the key takes it so.
    /// </summary>
    public bool TakesCommaSeparated { get; private set; }

    /// <summary>
    /// The number type the binder reads the node's value as — <see cref="float"/> or
    /// <see cref="double"/> —, or null: a value it takes as such a number must be finite. A float when
    /// one reader reads it so: what overflows a <see cref="float"/> is still a finite <see cref="double"/>.
    /// </summary>
    public Type? Number
    {
        get
        {
            var types = Values.Select(type => Nullable.GetUnderlyingType(type) ?? type).ToList();
            return types.Contains(typeof(float)) ? typeof(float) : types.Contains(typeof(double)) ? typeof(double) : null;
        }
    }

    /// <summary>A node with no key of its own — a value —, which declarations below it extend.</summary>
    public static SettingsShape Empty() => new();

    /// <summary>The shape of <paramref name="type"/>.</summary>
    public static SettingsShape Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Of(type, []);
    }

    /// <summary>The shape below <paramref name="key"/>, or null when the node does not take it.</summary>
    public SettingsShape? Child(string key)
    {
        if (Open)
            return this;

        var named = Named.GetValueOrDefault(key);
        return (named, Entries) switch
        {
            (null, null) => null,
            (null, var entries) => entries,
            (var only, null) => only,
            var (both, entries) => Merged(both, entries),
        };
    }

    /// <summary>Whether the node takes keys of its own, beyond a value.</summary>
    public bool TakesKeys => Open || Entries is not null || Named.Count > 0;

    /// <summary>Adds <paramref name="other"/>'s keys to this node.</summary>
    public void Merge(SettingsShape other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other))
            return;

        Open |= other.Open;
        IsList |= other.IsList;
        TakesCommaSeparated |= other.TakesCommaSeparated;
        Values.UnionWith(other.Values);
        if (other.Entries is not null)
            Entries = Entries is null ? other.Entries : Merged(Entries, other.Entries);

        foreach (var (key, shape) in other.Named)
        {
            if (Named.TryGetValue(key, out var existing))
                existing.Merge(shape);
            else
                Named[key] = shape;
        }
    }

    private static SettingsShape Merged(SettingsShape first, SettingsShape second)
    {
        var merged = new SettingsShape();
        merged.Merge(first);
        merged.Merge(second);
        return merged;
    }

    private static SettingsShape Of(Type type, HashSet<Type> visiting)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (Leaf(type, target, visiting) is { } leaf)
            return leaf;

        // A type that contains itself takes anything below its second appearance.
        if (!visiting.Add(target))
            return new SettingsShape { Open = true };

        var shape = new SettingsShape();
        foreach (var property in target.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetGetMethod() is null || !IsBound(property))
                continue;

            var below = Of(property.PropertyType, visiting);
            if (below.IsList && property.IsDefined(typeof(Orkeon.Application.Configuration.CommaSeparatedSettingAttribute), inherit: true))
                below.TakesCommaSeparated = true;

            shape.AddNamed(property.GetCustomAttribute<ConfigurationKeyNameAttribute>()?.Name ?? property.Name, below);
        }

        visiting.Remove(target);
        return shape;
    }

    /// <summary>
    /// Whether the binder sets or fills <paramref name="property"/>: a public setter — <c>init</c>
    /// included — writes any of them; behind a getter alone or a private setter, the binder fills a
    /// dictionary, a list or an object it reads, and cannot write a value.
    /// </summary>
    private static bool IsBound(PropertyInfo property)
    {
        if (property.GetSetMethod() is not null)
            return true;

        var target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        return !IsValue(target) || target == typeof(object) || IsJsonValue(target);
    }

    /// <summary>The shape of a type the binder does not walk property by property — open, a value, a dictionary, a list —, or null for an object with properties.</summary>
    private static SettingsShape? Leaf(Type declared, Type target, HashSet<Type> visiting)
    {
        if (target == typeof(object) || typeof(IConfiguration).IsAssignableFrom(target) || IsJsonValue(target))
            return new SettingsShape { Open = true };
        if (IsValue(target))
        {
            var value = new SettingsShape();
            value.Values.Add(declared);
            return value;
        }

        if (DictionaryValueType(target) is { } valueType)
            return new SettingsShape { Entries = Of(valueType, visiting) };
        if (typeof(IDictionary).IsAssignableFrom(target))
            return new SettingsShape { Open = true };
        if (ItemType(target) is { } itemType)
            return new SettingsShape { Entries = Of(itemType, visiting), IsList = true };

        return null;
    }

    /// <summary>Adds the shape below <paramref name="key"/>, merged into the one already there when two properties share a key.</summary>
    private void AddNamed(string key, SettingsShape below)
    {
        if (Named.TryGetValue(key, out var existing))
            existing.Merge(below);
        else
            Named[key] = below;
    }

    /// <summary>What the binder converts from text: a primitive, an enum, a string, and every type whose converter reads text.</summary>
    private static bool IsValue(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || TypeDescriptor.GetConverter(type).CanConvertFrom(typeof(string));

    private static bool IsJsonValue(Type type) =>
        type.Namespace is "System.Text.Json" or "System.Text.Json.Nodes";

    private static Type? DictionaryValueType(Type type)
    {
        foreach (var candidate in SelfAndInterfaces(type))
        {
            if (!candidate.IsGenericType)
                continue;

            var definition = candidate.GetGenericTypeDefinition();
            if ((definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
                && candidate.GetGenericArguments()[0] == typeof(string))
            {
                return candidate.GetGenericArguments()[1];
            }
        }

        return null;
    }

    private static Type? ItemType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        return SelfAndInterfaces(type)
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static Type[] SelfAndInterfaces(Type type) => type.IsInterface ? [type, .. type.GetInterfaces()] : type.GetInterfaces();
}
