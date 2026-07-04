// Marker attributes consumed by the Orkeon.Generators incremental source generator
// (chantier R4.5 / SML-004). They are declared once here, in the innermost assembly,
// instead of being injected per-compilation via RegisterPostInitializationOutput:
// Orkeon.Domain exposes InternalsVisibleTo to Orkeon.Application and
// Orkeon.Infrastructure, so per-compilation internal copies would collide (CS0436).
// The namespace intentionally matches the generator contract
// ("Orkeon.Generators.*" metadata names looked up by ForAttributeWithMetadataName).

namespace Orkeon.Generators;

/// <summary>
/// Marks a partial class as a typed-dictionary wrapper. The Orkeon.Generators source
/// generator emits the duplicated plumbing for the class: the private constructor, the
/// <c>Empty</c> instance, <c>CreateBuilder()</c> and the shell of the nested
/// <c>Builder</c> class (backing dictionary, generic <c>Add</c>, <c>Build()</c>), plus
/// the implementations of the strongly typed partial <c>AddXxx</c> methods annotated
/// with <see cref="DictionaryEntryAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
internal sealed class TypedDictionaryAttribute : Attribute
{
    /// <summary>Initializes the marker.</summary>
    /// <param name="valueType">The dictionary value type (e.g. a *Value wrapper, or <see cref="string"/>).</param>
    public TypedDictionaryAttribute(Type valueType)
    {
        ValueType = valueType;
    }

    /// <summary>Gets the dictionary value type.</summary>
    public Type ValueType { get; }

    /// <summary>Caches the <c>Empty</c> instance in a static field (iso with the historic hand-written variant).</summary>
    public bool CacheEmpty { get; set; }

    /// <summary>Emits the static <c>Empty</c> property (default <see langword="true"/>).</summary>
    public bool EmitEmpty { get; set; } = true;

    /// <summary>Emits <c>CreateBuilderFrom(existing)</c> and the matching internal Builder constructor.</summary>
    public bool EmitBuilderFrom { get; set; }

    /// <summary>Emits the untyped <c>Builder.Add(string key, object value)</c> method (default <see langword="true"/>).</summary>
    public bool EmitGenericAdd { get; set; } = true;
}

/// <summary>
/// Marks a strongly typed partial <c>AddXxx</c> method of a typed-dictionary
/// <c>Builder</c> so its implementation is generated. The generated body is
/// <c>_items[KEY] = VALUE; return this;</c> where VALUE is the last parameter
/// (wrapped by the <see cref="Factory"/> method of the value type, when set).
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
internal sealed class DictionaryEntryAttribute : Attribute
{
    /// <summary>Pass-through entry: the first parameter is the key, the last parameter is the value.</summary>
    public DictionaryEntryAttribute()
    {
    }

    /// <summary>Keyed entry.</summary>
    /// <param name="key">
    /// The dictionary key: a literal (<c>"timeout"</c>) or a format whose <c>{0}</c>, <c>{1}</c>…
    /// placeholders are interpolated from the method parameters (<c>"input.{0}"</c>).
    /// </param>
    public DictionaryEntryAttribute(string key)
    {
        Key = key;
    }

    /// <summary>Gets the key literal or format, or <see langword="null"/> for pass-through.</summary>
    public string? Key { get; }

    /// <summary>
    /// Static factory method of the value type used to wrap the value
    /// (default <c>"From"</c>; use <c>""</c> to assign the value directly).
    /// </summary>
    public string Factory { get; set; } = "From";
}
