using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Configuration;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// #449, #450, #454, #455, #456 — what the start validation of a shipped host makes of a value the
/// binder takes without a word: a computed property it lists as a key, a switch written without a
/// value it binds to the type's default, an entry of a list or a dictionary it drops when it cannot
/// convert it, a single value where a list is expected, and the culture a value is echoed in.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerHostSettingsValueTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-settings-value-" + Guid.NewGuid().ToString("N"));

    public RunnerHostSettingsValueTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private IReadOnlyList<string> RefusalsOfFile(string json)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, json);
        return RunnerHost.InspectSettings(path, configureServices: null).Refusals;
    }

    private IReadOnlyList<string> Refusals(string orkeon)
    {
        var path = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(path, $$"""{ "RaggableTree": { "Enabled": false }, "Orkeon": { {{orkeon}} } }""");
        return RunnerHost.InspectSettings(path, configureServices: null).Refusals;
    }

    [Fact]
    [Trait("BR", "CH-449")]
    public void ShouldRefuseAComputedPropertyAsAnUnknownKey_AndListOnlyTheSettings()
    {
        var refusals = Refusals("""
            "Tools": { "Bridge": { "IsEnabled": true } }
            """);

        var refusal = Assert.Single(refusals);
        Assert.StartsWith("Orkeon:Tools:Bridge:IsEnabled is not a setting: Orkeon:Tools:Bridge carries", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDockerHostOverHttp", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(", IsEnabled", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// #449 — no declared section of the runner host lists as a key a value the binder cannot write:
    /// a computed property, one behind a private setter. A collection or an object behind a getter
    /// stays a key: the binder fills it.
    /// </summary>
    [Fact]
    [Trait("BR", "CH-449")]
    public void ShouldListNoValueTheBinderCannotWrite_InAnyDeclaredSection()
    {
        var declarations = new List<SettingsDeclaration>();
        using (RunnerHost.Build(SettingsFile(), new RunnerMountPlan(),
                   configureServices: (_, services) => declarations.AddRange(services
                       .Where(descriptor => descriptor.ServiceType == typeof(SettingsDeclaration))
                       .Select(descriptor => (SettingsDeclaration)descriptor.ImplementationInstance!))))
        {
        }

        Assert.NotEmpty(declarations);
        Assert.Empty(SettingsShapeSweep.Unwritable(declarations.Select(declaration => declaration.Shape)));
        Assert.Equal(["Filled", "Initialised", "Settable"], SettingsShape.Of(typeof(Witness)).Named.Keys.Order(StringComparer.Ordinal));
    }

    private sealed class Witness
    {
        public bool Settable { get; set; }

        public bool Initialised { get; init; }

        public bool Private { get; private set; }

        public bool Computed => Settable;

        public List<string> Filled { get; } = [];
    }

    private string SettingsFile()
    {
        var path = Path.Combine(_root, "sweep.json");
        File.WriteAllText(path, """{ "RaggableTree": { "Enabled": false } }""");
        return path;
    }

    public static TheoryData<string, string> WrittenWithoutValue() => new()
    {
        { """ "Decision": { "Enabled": null } """, "Orkeon:Decision:Enabled" },
        { """ "Decision": { "Enabled": {} } """, "Orkeon:Decision:Enabled" },
        { """ "Guardian": { "Enabled": null } """, "Orkeon:Guardian:Enabled" },
        { """ "Compaction": { "Enabled": null } """, "Orkeon:Compaction:Enabled" },
        { """ "Compaction": { "TriggerShare": null } """, "Orkeon:Compaction:TriggerShare" },
    };

    /// <summary>#450 — null and {} read alike, and the binder wrote false: the decision circuit or the guardian went off.</summary>
    [Theory]
    [Trait("BR", "CH-450")]
    [MemberData(nameof(WrittenWithoutValue))]
    public void ShouldRefuseASettingWrittenWithoutAValue_WhenItIsNoString(string orkeon, string key)
    {
        var refusal = Assert.Single(Refusals(orkeon));

        Assert.StartsWith($"{key} is written without a value, which would bind it to ", refusal, StringComparison.Ordinal);
        Assert.Contains("leave the key out and its default applies", refusal, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-450")]
    public void ShouldAcceptAStringWrittenWithoutAValue()
    {
        Assert.Empty(Refusals("""
            "Decision": { "Provider": null }
            """));
    }

    /// <summary>
    /// #450 — the settings the <c>orkeon-host</c> template hands out write <c>"SettingsPath": null</c>, a
    /// string: none of their values is refused as written without one.
    /// </summary>
    [Fact]
    [Trait("BR", "CH-450")]
    public void ShouldNotRefuseTheTemplatesSettingsForAValueWrittenNull()
    {
        var template = Path.Combine(RepositoryRoot(), "src", "packaging", "Orkeon.Templates", "templates", "orkeon-host", "appsettings.json");
        Assert.Contains("null", File.ReadAllText(template), StringComparison.Ordinal);

        var verdict = RunnerHost.InspectSettings(template, configureServices: null);

        Assert.DoesNotContain(verdict.Refusals, refusal => refusal.Contains("without a value", StringComparison.Ordinal));
    }

    /// <summary>#454 — the binder dropped a stage or a profile it could not convert, or one written as a single value.</summary>
    [Theory]
    [Trait("BR", "CH-454")]
    [InlineData(
        """ "Decision": { "Profiles": { "p": { "Target": "default", "Stages": [ { "Kind": "guard", "Risks": { "x": "y" }, "BlockThreshold": "x" }, { "Kind": "guard", "Risks": { "x": "y" } } ] } } } """,
        "Failed to convert the configuration value at 'Orkeon:Decision:Profiles:p:Stages:0:BlockThreshold' to type 'System.Double'.")]
    [InlineData(
        """ "Decision": { "Profiles": { "p": { "Target": "default", "FailClosed": "x", "Stages": [ { "Kind": "guard", "Risks": { "x": "y" } } ] } } } """,
        "Failed to convert the configuration value at 'Orkeon:Decision:Profiles:p:FailClosed' to type 'System.Boolean'.")]
    [InlineData(
        """ "Decision": { "Profiles": { "p": "x" } } """,
        "Orkeon:Decision:Profiles:p is a single value where a decision profile is expected")]
    public void ShouldRefuseADecisionProfileTheBinderWouldDrop(string orkeon, string expected)
    {
        var refusal = Assert.Single(Refusals(orkeon));

        Assert.StartsWith(expected, refusal, StringComparison.Ordinal);
    }

    /// <summary>#454 — a stage written null in a list bound its type's default: a guard threshold of 0 blocks every request.</summary>
    [Fact]
    [Trait("BR", "CH-450")]
    public void ShouldRefuseAStageThresholdWrittenWithoutAValue()
    {
        var refusal = Assert.Single(Refusals("""
            "Decision": { "Profiles": { "p": { "Target": "default", "Stages": [ { "Kind": "guard", "Risks": { "x": "y" }, "BlockThreshold": null } ] } } }
            """));

        Assert.StartsWith("Orkeon:Decision:Profiles:p:Stages:0:BlockThreshold is written without a value, which would bind it to 0", refusal, StringComparison.Ordinal);
    }

    /// <summary>#455 — a single phase where the list is expected judged both phases; a number read as the member holding it.</summary>
    [Theory]
    [Trait("BR", "CH-455")]
    [InlineData(""" "Guardian": { "Llm": { "Phases": "Input" } } """, "Orkeon:Guardian:Llm:Phases is a single value where a list is expected")]
    [InlineData(""" "Guardian": { "Llm": { "Phases": ["1"] } } """, "Orkeon:Guardian:Llm:Phases: '1' is not a phase a model judges.")]
    [InlineData(""" "Guardian": { "Llm": { "Phases": ["7"] } } """, "Orkeon:Guardian:Llm:Phases: '7' is not a phase a model judges.")]
    public void ShouldRefuseTheGuardianPhasesOnce_WhenTheyAreNoListOfNames(string orkeon, string expected)
    {
        var refusal = Assert.Single(Refusals(orkeon));

        Assert.StartsWith(expected, refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// #454 — a list whose reader splits one comma-separated value takes that form, the one an
    /// environment variable writes (<c>Llm__AvailableModels=gpt-4o,claude</c>), as well as a list.
    /// </summary>
    [Theory]
    [Trait("BR", "CH-454")]
    [InlineData(""" "Llm": { "AvailableModels": "gpt-4o,claude" } """)]
    [InlineData(""" "Llm": { "AvailableModels": ["gpt-4o", "claude"] } """)]
    [InlineData(""" "Llm": { "ModelCapabilities": { "gpt-4o": { "ReasoningEfforts": "low,high" } } } """)]
    [InlineData(""" "Llm": { "ModelCapabilities": { "gpt-4o": { "ReasoningEfforts": ["low", "high"] } } } """)]
    [InlineData(""" "Llm": { "Profiles": { "fast": { "Model": "gpt-4o", "Fallback": "default,cheap" }, "cheap": { "Model": "gpt-4o-mini" } } } """)]
    [InlineData(""" "Llm": { "Profiles": { "fast": { "Model": "gpt-4o", "Fallback": ["default", "cheap"] }, "cheap": { "Model": "gpt-4o-mini" } } } """)]
    public void ShouldAcceptACommaSeparatedValue_WhereTheListReaderSplitsOne(string llm)
    {
        var refusals = RefusalsOfFile($$"""{ "RaggableTree": { "Enabled": false }, {{llm}} }""");

        Assert.DoesNotContain(refusals, refusal => refusal.Contains("a single value", StringComparison.Ordinal));
        Assert.DoesNotContain(refusals, refusal => refusal.Contains("AvailableModels", StringComparison.Ordinal)
                                                  || refusal.Contains("ReasoningEfforts", StringComparison.Ordinal)
                                                  || refusal.Contains("Fallback", StringComparison.Ordinal));
    }

    /// <summary>
    /// #454 — a list whose reader takes no single value still refuses one, and the refusal does not
    /// echo it: a value written where a list or a section is expected may be a secret.
    /// </summary>
    [Fact]
    [Trait("BR", "CH-454")]
    public void ShouldRefuseASingleValue_WhereAListReaderSplitsNone_WithoutEchoingIt()
    {
        var refusals = RefusalsOfFile("""{ "RaggableTree": { "Enabled": false }, "Security": { "Url": { "AllowedDomains": "sk-secret-example" } } }""");

        var refusal = Assert.Single(refusals);
        Assert.StartsWith("Security:Url:AllowedDomains is a single value where a list is expected", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-secret-example", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// #465 — no refusal of the start-time check names the value written under the key: a value the key
    /// cannot read may be anything written there by mistake, an API key included, and the refusal reaches
    /// the start-up output and the logs.
    /// </summary>
    [Theory]
    [Trait("BR", "CH-465")]
    [InlineData(""" "Orkeon": { "Decision": { "Enabled": "sk-secret-example" } } """, "Orkeon:Decision:Enabled")]
    [InlineData(""" "Orkeon": { "Compaction": { "TriggerShare": "sk-secret-example" } } """, "Orkeon:Compaction:TriggerShare")]
    [InlineData(""" "Llm": { "TimeoutSeconds": "sk-secret-example" } """, "Llm:TimeoutSeconds")]
    [InlineData(""" "Orkeon": { "Decision": { "Profiles": { "p": { "Target": "default", "FailClosed": "sk-secret-example", "Stages": [ { "Kind": "guard", "Risks": { "x": "y" } } ] } } } } """, "Orkeon:Decision:Profiles:p:FailClosed")]
    [InlineData(""" "Orkeon": { "Decision": { "Profiles": { "p": { "Target": "default", "Stages": [ { "Kind": "guard", "Risks": { "x": "y" }, "BlockThreshold": "sk-secret-example" } ] } } } } """, "Orkeon:Decision:Profiles:p:Stages:0:BlockThreshold")]
    [InlineData(""" "Orkeon": { "Guardian": { "Llm": { "Phases": "sk-secret-example" } } } """, "Orkeon:Guardian:Llm:Phases")]
    [InlineData(""" "Orkeon": { "Guardian": { "Llm": { "BlockThreshold": "sk-secret-example" } } } """, "Orkeon:Guardian:Llm:BlockThreshold")]
    public void ShouldRefuseAValueTheKeyCannotRead_WithoutEchoingIt(string sections, string key)
    {
        var refusals = RefusalsOfFile($$"""{ "RaggableTree": { "Enabled": false }, {{sections}} }""");

        Assert.Contains(refusals, refusal => refusal.Contains(key, StringComparison.Ordinal));
        Assert.DoesNotContain(refusals, refusal => refusal.Contains("sk-secret-example", StringComparison.Ordinal));
    }

    /// <summary>#465 — a number that is not finite is refused by its key, without the value.</summary>
    [Fact]
    [Trait("BR", "CH-465")]
    public void ShouldRefuseANumberThatIsNotFinite_WithoutEchoingIt()
    {
        var refusal = Assert.Single(RefusalsOfFile("""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Rag": { "Generation": { "Temperature": "NaN" } } } }"""));

        Assert.StartsWith("Orkeon:Rag:Generation:Temperature is not a finite number", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("'NaN'", refusal, StringComparison.Ordinal);
    }

    /// <summary>#464 — an enumeration written as a number is refused, naming the members, never the value.</summary>
    [Theory]
    [Trait("BR", "CH-464")]
    [InlineData("\"Orkeon\": { \"Guardian\": { \"Llm\": { \"FailMode\": \"1\" } } }", "Orkeon:Guardian:Llm:FailMode", "Closed, Open")]
    [InlineData("\"Orkeon\": { \"Guardian\": { \"Llm\": { \"FailMode\": \"-1\" } } }", "Orkeon:Guardian:Llm:FailMode", "Closed, Open")]
    [InlineData("\"Security\": { \"Audit\": { \"EnabledCategories\": [ \"LlmCall\", \"2\" ] } }", "Security:Audit:EnabledCategories:1", "LlmCall")]
    public void ShouldRefuseAnEnumerationWrittenAsANumber_NamingTheMembers(string sections, string key, string member)
    {
        var refusals = RefusalsOfFile($$"""{ "RaggableTree": { "Enabled": false }, {{sections}} }""");

        var refusal = Assert.Single(refusals);
        Assert.StartsWith($"{key} is a number where a name is expected: write one of ", refusal, StringComparison.Ordinal);
        Assert.Contains(member, refusal, StringComparison.Ordinal);
    }

    /// <summary>#464 — a phase written as a number is refused once, by the walk or by the phases' own check.</summary>
    [Fact]
    [Trait("BR", "CH-464")]
    public void ShouldRefuseAPhaseWrittenAsANumber_Once()
    {
        var refusal = Assert.Single(Refusals("""
            "Guardian": { "Llm": { "Phases": [ "1" ] } }
            """));

        Assert.Contains("Orkeon:Guardian:Llm:Phases", refusal, StringComparison.Ordinal);
    }

    /// <summary>#464 — a comma list binds a combination of members: refused for an enumeration that is not [Flags].</summary>
    [Fact]
    [Trait("BR", "CH-464")]
    public void ShouldRefuseACommaListForAnEnumerationThatIsNotFlags()
    {
        var refusal = Assert.Single(Refusals("""
            "Guardian": { "Llm": { "FailMode": "Closed, Open" } }
            """));

        Assert.Equal("Orkeon:Guardian:Llm:FailMode is a list where a single name is expected: write one of Closed, Open.", refusal);
    }

    /// <summary>#464 — a member's name is accepted in any case, in a value, a list and a dictionary.</summary>
    [Fact]
    [Trait("BR", "CH-464")]
    public void ShouldAcceptAMembersName_InAnyCase()
    {
        var refusals = RefusalsOfFile("""
            { "RaggableTree": { "Enabled": false },
              "Orkeon": { "Guardian": { "Llm": { "FailMode": "open", "Phases": [ "Input" ] } } },
              "Security": { "Audit": { "EnabledCategories": [ "LlmCall", "fileaccess" ] } },
              "Logging": { "LogLevel": { "Default": "Warning" } } }
            """);

        Assert.Empty(refusals);
    }

    /// <summary>#454 — a profile written as a string, a key in the wrong place, is refused without its value.</summary>
    [Fact]
    [Trait("BR", "CH-454")]
    public void ShouldRefuseAProfileWrittenAsAString_WithoutEchoingIt()
    {
        var refusals = RefusalsOfFile("""{ "RaggableTree": { "Enabled": false }, "Llm": { "Profiles": { "openai": "sk-secret-example" } } }""");

        var refusal = Assert.Single(refusals, refusal => refusal.StartsWith("Llm:Profiles:openai ", StringComparison.Ordinal));
        Assert.Contains("a single value where a section of keys is expected", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(refusals, refusal => refusal.Contains("sk-secret-example", StringComparison.Ordinal));
    }

    /// <summary>An empty JSON array reads as an empty value, and stays an empty list.</summary>
    [Fact]
    [Trait("BR", "CH-455")]
    public void ShouldAcceptAnEmptyListOfPhases()
    {
        Assert.Empty(Refusals("""
            "Guardian": { "Llm": { "Phases": [] } }
            """));
    }

    /// <summary>#456 — a value is echoed as the file wrote it, whatever the culture; each threshold has its own sentence.</summary>
    [Fact]
    [Trait("BR", "CH-456")]
    public void ShouldEchoTheValuesInTheInvariantCulture_OneSentencePerKey()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var stage = Assert.Single(Refusals("""
                "Decision": { "Profiles": { "p": { "Target": "default", "Stages": [ { "Kind": "guard", "Risks": { "x": "y" }, "BlockThreshold": 1.5 } ] } } }
                """));
            var guardian = Refusals("""
                "Guardian": { "Llm": { "BlockThreshold": 2, "WarnThreshold": 1.25 } }
                """);

            Assert.Equal("Orkeon:Decision:Profiles:p:Stages:0:BlockThreshold: 1.5 is not a probability between 0 and 1.", stage);
            Assert.Equal(
                ["Orkeon:Guardian:Llm:BlockThreshold: 2 is not a probability between 0 and 1.",
                 "Orkeon:Guardian:Llm:WarnThreshold: 1.25 is not a probability between 0 and 1."],
                guardian);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static string RepositoryRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
}

/// <summary>
/// #449 — the properties of the declared shapes, and of every Orkeon type below them, that the
/// binder cannot write — a value behind no public setter — yet <see cref="SettingsShape"/> lists as
/// keys.
/// </summary>
internal static class SettingsShapeSweep
{
    public static List<string> Unwritable(IEnumerable<Type> roots)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>(roots);
        while (pending.Count > 0)
        {
            var type = Nullable.GetUnderlyingType(pending.Peek()) ?? pending.Peek();
            pending.Pop();
            if (!seen.Add(type))
                continue;

            foreach (var argument in type.IsArray ? [type.GetElementType()!] : type.GetGenericArguments())
                pending.Push(argument);
            if (!IsOrkeon(type) || type.IsEnum)
                continue;

            var keys = SettingsShape.Of(type).Named;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0)
                    continue;

                pending.Push(property.PropertyType);
                if (property.GetSetMethod() is null && IsScalar(property.PropertyType) && keys.ContainsKey(property.Name)
                    && !type.GetProperties().Any(other => other != property && other.Name == property.Name))
                {
                    found.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        return [.. found];
    }

    private static bool IsOrkeon(Type type) =>
        type.Namespace?.StartsWith("Orkeon.", StringComparison.Ordinal) == true || type.DeclaringType is not null && IsOrkeon(type.DeclaringType);

    private static bool IsScalar(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        return target.IsPrimitive || target.IsEnum || target == typeof(string) || target == typeof(decimal)
               || target == typeof(TimeSpan) || target == typeof(Uri) || target == typeof(DateTimeOffset);
    }
}
