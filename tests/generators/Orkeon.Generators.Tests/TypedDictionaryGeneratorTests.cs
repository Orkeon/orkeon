namespace Orkeon.Generators.Tests;

public class TypedDictionaryGeneratorTests
{
    /// <summary>A value wrapper with the <c>From</c> factories the generated code calls.</summary>
    private const string ValueType = """
        namespace Sample
        {
            public sealed class SampleValue
            {
                private SampleValue(object value) { Value = value; }
                public object Value { get; }
                public static SampleValue From(object value) => new(value);
                public static SampleValue Wrap(object value) => new(value);
            }
        }
        """;

    [Fact]
    public void ShouldEmitTheWholeWrapper_WhenAClassIsMarked()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                [Orkeon.Generators.TypedDictionary(typeof(SampleValue))]
                public sealed partial class Settings
                {
                    public partial class Builder
                    {
                        [Orkeon.Generators.DictionaryEntry("timeout")]
                        public partial Builder AddTimeout(int seconds);
                    }
                }
            }
            """);

        run.AssertCompiles();
        Assert.Empty(run.GeneratorDiagnostics);
        var file = run.Single;
        Assert.Equal("Sample.Settings.TypedDictionary.g.cs", file.HintName);
        Assert.Contains("namespace Sample;", file.Text, StringComparison.Ordinal);
        Assert.Contains("partial class Settings", file.Text, StringComparison.Ordinal);
        Assert.Contains("private readonly global::System.Collections.Immutable.ImmutableDictionary<string, global::Sample.SampleValue> _items;", file.Text, StringComparison.Ordinal);
        Assert.Contains("public static Settings Empty => new(", file.Text, StringComparison.Ordinal);
        Assert.Contains("public static Builder CreateBuilder() => new Builder();", file.Text, StringComparison.Ordinal);
        Assert.Contains("_items[\"timeout\"] = global::Sample.SampleValue.From(seconds);", file.Text, StringComparison.Ordinal);
        Assert.Contains("public Builder Add(string key, object value)", file.Text, StringComparison.Ordinal);
        Assert.Contains("public Settings Build()", file.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBuilderFrom", file.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldNameTheFileAfterTheClassOnly_WhenTheClassIsInTheGlobalNamespace()
    {
        var run = GeneratorHarness.Run(ValueType + """
            [Orkeon.Generators.TypedDictionary(typeof(Sample.SampleValue))]
            public sealed partial class GlobalSettings
            {
            }
            """);

        run.AssertCompiles();
        var file = run.Single;
        Assert.Equal("GlobalSettings.TypedDictionary.g.cs", file.HintName);
        Assert.DoesNotContain("namespace ", file.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldCacheTheEmptyInstance_WhenCacheEmptyIsSet()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                [Orkeon.Generators.TypedDictionary(typeof(SampleValue), CacheEmpty = true)]
                public sealed partial class Cached
                {
                }
            }
            """);

        run.AssertCompiles();
        var text = run.Single.Text;
        Assert.Contains("private static readonly Cached _empty = new(", text, StringComparison.Ordinal);
        Assert.Contains("public static Cached Empty => _empty;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldLeaveEmptyAndTheGenericAddOut_WhenTheyAreTurnedOff()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                [Orkeon.Generators.TypedDictionary(typeof(SampleValue), EmitEmpty = false, EmitGenericAdd = false)]
                public sealed partial class Bare
                {
                }
            }
            """);

        run.AssertCompiles();
        var text = run.Single.Text;
        Assert.DoesNotContain(" Empty ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("public Builder Add(string key, object value)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldEmitCreateBuilderFrom_WhenEmitBuilderFromIsSet()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                [Orkeon.Generators.TypedDictionary(typeof(SampleValue), EmitBuilderFrom = true)]
                public sealed partial class Seeded
                {
                }
            }
            """);

        run.AssertCompiles();
        var text = run.Single.Text;
        Assert.Contains("public static Builder CreateBuilderFrom(Seeded existing) => new Builder(existing._items);", text, StringComparison.Ordinal);
        Assert.Contains("internal Builder(global::System.Collections.Immutable.ImmutableDictionary<string, global::Sample.SampleValue> existing)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldChainToTheBaseConstructor_WhenTheWrapperDerivesFromABase()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                public abstract class ParametersBase
                {
                    protected ParametersBase(System.Collections.Immutable.ImmutableDictionary<string, SampleValue> items) { Items = items; }
                    protected System.Collections.Immutable.ImmutableDictionary<string, SampleValue> Items { get; }
                }

                [Orkeon.Generators.TypedDictionary(typeof(SampleValue), EmitBuilderFrom = true)]
                public sealed partial class Derived : ParametersBase
                {
                }
            }
            """);

        run.AssertCompiles();
        var text = run.Single.Text;
        Assert.Contains(": base(items)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly global::System.Collections.Immutable.ImmutableDictionary", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBuilderFrom", text, StringComparison.Ordinal);
        Assert.DoesNotContain("internal Builder(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldEmitAPartialRecord_WhenTheWrapperIsARecord()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                [Orkeon.Generators.TypedDictionary(typeof(SampleValue))]
                public sealed partial record RecordSettings
                {
                }
            }
            """);

        run.AssertCompiles();
        Assert.Contains("partial record RecordSettings", run.Single.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldUseTheKeyParameter_WhenTheEntryIsPassThrough()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                [Orkeon.Generators.TypedDictionary(typeof(SampleValue))]
                public sealed partial class Passing
                {
                    public partial class Builder
                    {
                        [Orkeon.Generators.DictionaryEntry]
                        public partial Builder AddEntry(string name, object value);
                    }
                }
            }
            """);

        run.AssertCompiles();
        Assert.Contains("_items[name] = global::Sample.SampleValue.From(value);", run.Single.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldInterpolateTheKey_WhenTheKeyIsAFormat()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                [Orkeon.Generators.TypedDictionary(typeof(SampleValue))]
                public sealed partial class Formatted
                {
                    public partial class Builder
                    {
                        [Orkeon.Generators.DictionaryEntry("input.{0}.{1}")]
                        public partial Builder AddInput(string section, string name, object value);

                        [Orkeon.Generators.DictionaryEntry("raw.{x}.{9}.}")]
                        public partial Builder AddRaw(string part, object value);
                    }
                }
            }
            """);

        run.AssertCompiles();
        var text = run.Single.Text;
        Assert.Contains("_items[$\"input.{section}.{name}\"]", text, StringComparison.Ordinal);
        // A placeholder that names no parameter, and a lone brace, are kept as literal braces.
        Assert.Contains("_items[$\"raw.{{x}}.{{9}}.}}\"]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldEscapeQuotesAndBackslashes_WhenTheKeyHoldsThem()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                [Orkeon.Generators.TypedDictionary(typeof(SampleValue))]
                public sealed partial class Escaped
                {
                    public partial class Builder
                    {
                        [Orkeon.Generators.DictionaryEntry("a\"b\\c")]
                        public partial Builder AddLiteral(object value);

                        [Orkeon.Generators.DictionaryEntry("f\"{0}\\")]
                        public partial Builder AddFormat(string name, object value);
                    }
                }
            }
            """);

        run.AssertCompiles();
        var text = run.Single.Text;
        Assert.Contains("_items[\"a\\\"b\\\\c\"]", text, StringComparison.Ordinal);
        Assert.Contains("_items[$\"f\\\"{name}\\\\\"]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldAssignTheValueDirectlyOrThroughTheNamedFactory_WhenFactoryIsSet()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                [Orkeon.Generators.TypedDictionary(typeof(SampleValue))]
                public sealed partial class Factories
                {
                    public partial class Builder
                    {
                        [Orkeon.Generators.DictionaryEntry("direct", Factory = "")]
                        public partial Builder AddDirect(SampleValue value);

                        [Orkeon.Generators.DictionaryEntry("wrapped", Factory = "Wrap")]
                        public partial Builder AddWrapped(object value);
                    }
                }
            }
            """);

        run.AssertCompiles();
        var text = run.Single.Text;
        Assert.Contains("_items[\"direct\"] = value;", text, StringComparison.Ordinal);
        Assert.Contains("_items[\"wrapped\"] = global::Sample.SampleValue.Wrap(value);", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldCopyTypeParametersConstraintsParamsAndKeywordNames_WhenTheEntryIsGeneric()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                public interface IMarker { }

                [Orkeon.Generators.TypedDictionary(typeof(SampleValue))]
                public sealed partial class Generic
                {
                    public partial class Builder
                    {
                        [Orkeon.Generators.DictionaryEntry("class")]
                        public partial Builder AddClass<T>(T @class) where T : class, IMarker, new();

                        [Orkeon.Generators.DictionaryEntry("struct")]
                        public partial Builder AddStruct<T>(T value) where T : struct;

                        [Orkeon.Generators.DictionaryEntry("unmanaged")]
                        public partial Builder AddUnmanaged<T>(T value) where T : unmanaged;

                        [Orkeon.Generators.DictionaryEntry("notnull")]
                        public partial Builder AddNotNull<T>(T value) where T : notnull;

                        [Orkeon.Generators.DictionaryEntry("free")]
                        public partial Builder AddFree<T>(T value);

                        [Orkeon.Generators.DictionaryEntry("many")]
                        public partial Builder AddMany(params object[] values);
                    }
                }
            }
            """);

        run.AssertCompiles();
        var text = run.Single.Text;
        Assert.Contains("public partial Builder AddClass<T>(T @class) where T : class, global::Sample.IMarker, new()", text, StringComparison.Ordinal);
        Assert.Contains("_items[\"class\"] = global::Sample.SampleValue.From(@class);", text, StringComparison.Ordinal);
        Assert.Contains("where T : struct", text, StringComparison.Ordinal);
        Assert.Contains("where T : unmanaged", text, StringComparison.Ordinal);
        Assert.Contains("where T : notnull", text, StringComparison.Ordinal);
        Assert.Contains("public partial Builder AddFree<T>(T value)\n", text, StringComparison.Ordinal);
        Assert.Contains("public partial Builder AddMany(params object[] values)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldSkipMethodsItCannotImplement_WhenTheyLackTheAttributeOrAParameter()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                [Orkeon.Generators.TypedDictionary(typeof(SampleValue))]
                public sealed partial class Skipping
                {
                    public partial class Builder
                    {
                        [Orkeon.Generators.DictionaryEntry("kept")]
                        public partial Builder AddKept(object value);

                        public partial Builder AddUnmarked(object value);

                        [Orkeon.Generators.DictionaryEntry("none")]
                        public partial Builder AddNothing();

                        [Orkeon.Generators.DictionaryEntry]
                        public partial Builder AddKeyless(object value);
                    }
                }
            }
            """);

        var text = run.Single.Text;
        Assert.Contains("AddKept(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AddUnmarked", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AddNothing", text, StringComparison.Ordinal);
        Assert.DoesNotContain("AddKeyless", text, StringComparison.Ordinal);
        // The skipped partial definitions keep no implementation: the compiler reports exactly those.
        Assert.Equal(3, run.CompilationErrors.Count(d => d.Id == "CS8795"));
    }

    [Fact]
    public void ShouldEmitTheEntriesInDeclarationOrder_WhenTheBuilderHasSeveral()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                [Orkeon.Generators.TypedDictionary(typeof(SampleValue))]
                public sealed partial class Ordered
                {
                    public partial class Builder
                    {
                        [Orkeon.Generators.DictionaryEntry("second")]
                        public partial Builder AddB(object value);

                        [Orkeon.Generators.DictionaryEntry("first")]
                        public partial Builder AddA(object value);
                    }
                }
            }
            """);

        run.AssertCompiles();
        var text = run.Single.Text;
        Assert.True(text.IndexOf("AddB(", StringComparison.Ordinal) < text.IndexOf("AddA(", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldGenerateNothing_WhenNoClassIsMarked()
    {
        var run = GeneratorHarness.Run(ValueType);

        run.AssertCompiles();
        Assert.Empty(run.Sources);
    }

    [Fact]
    public void ShouldBuildAWorkingDictionary_WhenTheGeneratedCodeRuns()
    {
        var run = GeneratorHarness.Run(ValueType + """
            namespace Sample
            {
                [Orkeon.Generators.TypedDictionary(typeof(SampleValue), EmitBuilderFrom = true)]
                public sealed partial class Live
                {
                    public int Count => _items.Count;
                    public object Get(string key) => _items[key].Value;

                    public partial class Builder
                    {
                        [Orkeon.Generators.DictionaryEntry("timeout")]
                        public partial Builder AddTimeout(int seconds);
                    }
                }
            }
            """);

        run.AssertCompiles();
        var live = run.Load("Sample.Live");

        var builder = live.GetMethod("CreateBuilder")!.Invoke(null, null)!;
        builder.GetType().GetMethod("AddTimeout")!.Invoke(builder, [30]);
        builder.GetType().GetMethod("Add")!.Invoke(builder, ["name", "orkeon"]);
        var built = builder.GetType().GetMethod("Build")!.Invoke(builder, null)!;

        Assert.Equal(2, live.GetProperty("Count")!.GetValue(built));
        Assert.Equal(30, live.GetMethod("Get")!.Invoke(built, ["timeout"]));
        Assert.Equal(0, live.GetProperty("Count")!.GetValue(live.GetProperty("Empty")!.GetValue(null)));

        var copy = live.GetMethod("CreateBuilderFrom")!.Invoke(null, [built])!;
        var rebuilt = copy.GetType().GetMethod("Build")!.Invoke(copy, null)!;
        Assert.Equal("orkeon", live.GetMethod("Get")!.Invoke(rebuilt, ["name"]));
    }
}
