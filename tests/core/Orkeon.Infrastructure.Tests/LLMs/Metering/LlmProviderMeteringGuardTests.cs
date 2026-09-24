using System.Text.RegularExpressions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Infrastructure.LLMs;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Architecture guard (STUDIO-42 D-04): an LLM provider reaches the runtime on the metered
/// path, or its calls escape the token meter. The path has two entrances — the provider
/// factory, which meters every vendor provider it builds, and <c>AddOrkeonLlmProvider</c>,
/// which meters a provider registered by hand — so a provider may be built only in the
/// factory or in the very statement that hands it to <c>AddOrkeonLlmProvider</c>, and the
/// meter itself is applied nowhere else (a second meter would count calls twice).
/// </summary>
/// <remarks>
/// Pragmatic, like the other source guards of this suite: the provider types are discovered
/// from the class declarations under <c>src/</c> (and cross-checked against the Infrastructure
/// assembly), then every <c>src/**/*.cs</c> file — comments stripped — is scanned for a
/// construction or a DI activation of one of them. The scan itself is a function of the
/// sources it is given, which is what lets a counter-example prove it bites.
/// </remarks>
public sealed partial class LlmProviderMeteringGuardTests
{
    /// <summary>The factory: every vendor provider is built there, and metered there.</summary>
    private const string FactoryFile = "src/core/Orkeon.Infrastructure/LLMs/LlmProviderFactory.cs";

    /// <summary>The DI entrance of the metered path.</summary>
    private const string RegistrationFile = "src/core/Orkeon.Infrastructure/DependencyInjection/LlmProviderRegistrationExtensions.cs";

    /// <summary>
    /// Decorators: each wraps a provider it was handed, already obtained on the path. They
    /// build no new source of calls.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Decorators = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(MeteredLlmProvider)] = "The meter itself.",
        [nameof(RateLimitedLlmProvider)] = "Throttles the provider the scripted host resolved from DI, metered already.",
    };

    /// <summary>
    /// Providers built off the path on purpose, each with its reason. Keep this list as short
    /// as possible; an entry whose construction is gone fails the stale-entry test.
    /// </summary>
    private static readonly IReadOnlyDictionary<(string Type, string File), string> Exemptions =
        new Dictionary<(string, string), string>
        {
            [("MockLlmProvider", "src/scripting/Orkeon.Scripting/Testing/JsTestNamespace.cs")] =
                "The double `orkeon test` hands a script under test: it answers from expectations and calls no model.",
            [("AIAgentLlmProvider", "src/interop/Orkeon.Interop.AgentFramework/AgentBuilderExtensions.cs")] =
                "Set as Agent.FunctionCallingLlm, a slot no runtime path calls; a host that wants the MAF agent to "
                + "answer registers it with AddOrkeonLlmProvider.",
        };

    [Fact]
    public void Every_llm_provider_of_the_source_is_built_on_the_metered_path()
    {
        var sources = ReadSources();
        var providers = DiscoverProviderTypes(sources);

        // Sanity: the discovery must see the whole family, or the guard passes vacuously.
        Assert.Contains("OpenAIProvider", providers);
        Assert.Contains("AnthropicLlmProvider", providers);
        Assert.Contains("OllamaLlmProvider", providers);
        Assert.Contains("UndefinedLlmProvider", providers);
        foreach (var type in ConcreteProviderTypesOfTheInfrastructure())
            Assert.True(providers.Contains(type.Name) || Decorators.ContainsKey(type.Name), $"{type.Name} escaped the source discovery.");

        var violations = FindOffPathConstructions(sources, providers);

        if (violations.Count > 0)
        {
            Assert.Fail(
                "An LLM provider is built off the metered path, so its calls escape the token meter "
                + "(STUDIO-42 D-04). Build it in LlmProviderFactory, or hand it to AddOrkeonLlmProvider "
                + "in the statement that builds it:\n - " + string.Join("\n - ", violations));
        }
    }

    [Fact]
    public void The_meter_is_applied_only_at_the_two_entrances_of_the_path()
    {
        var violations = FindOffPathMetering(ReadSources());

        if (violations.Count > 0)
        {
            Assert.Fail(
                "MeteredLlmProvider.Wrap is called off the metered path — a second meter counts the same "
                + "calls twice (STUDIO-42 D-03):\n - " + string.Join("\n - ", violations));
        }
    }

    [Fact]
    public void A_provider_built_off_the_path_turns_the_guard_red()
    {
        // The counter-example: a service that builds its own vendor provider, and one that
        // meters a provider on its own. Both are exactly what the guard exists to catch.
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FactoryFile] = "public sealed class LlmProviderFactory { OpenAIProvider Build() => new OpenAIProvider(c, h, s, l); }",
            ["src/core/Orkeon.Infrastructure/LLMs/OpenAIProvider.cs"] =
                "public class OpenAIProvider : OpenAICompatibleProviderBase { }",
            ["src/core/Orkeon.Infrastructure/LLMs/Base/OpenAICompatibleProviderBase.cs"] =
                "public abstract partial class OpenAICompatibleProviderBase : HttpLlmProviderBase { }",
            ["src/core/Orkeon.Infrastructure/LLMs/Base/HttpLlmProviderBase.cs"] =
                "public abstract partial class HttpLlmProviderBase : ILlmProvider, IStreamingLlmProvider, IDisposable { }",
            ["src/core/Orkeon.Infrastructure/Rogue/SummaryService.cs"] =
                """
                public sealed class SummaryService
                {
                    // new OpenAIProvider(...) in a comment is not a construction.
                    private readonly ILlmProvider _llm = new OpenAIProvider(config, http, strategy, logger);
                    private ILlmProvider Metered(ILlmProvider p, ILlmUsageSink s) => MeteredLlmProvider.Wrap(p, s);
                }
                """,
        };

        var providers = DiscoverProviderTypes(sources);
        var constructions = FindOffPathConstructions(sources, providers);
        var metering = FindOffPathMetering(sources);

        var construction = Assert.Single(constructions);
        Assert.Contains("SummaryService.cs:4", construction, StringComparison.Ordinal);
        Assert.Contains("OpenAIProvider", construction, StringComparison.Ordinal);
        Assert.Contains("SummaryService.cs:5", Assert.Single(metering), StringComparison.Ordinal);
    }

    [Fact]
    public void A_provider_handed_to_the_registration_in_its_own_statement_is_on_the_path()
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/scripting/Orkeon.Scripting/Runtime/UndefinedLlmProvider.cs"] =
                "public sealed class UndefinedLlmProvider : ILlmProvider { }",
            ["src/hosting/Orkeon.Hosting/Host.cs"] =
                """
                services.AddOrkeonLlmProvider(_ => new UndefinedLlmProvider());
                services.AddSingleton<UndefinedLlmProvider>();
                """,
        };

        var violation = Assert.Single(FindOffPathConstructions(sources, DiscoverProviderTypes(sources)));

        Assert.Contains("Host.cs:2", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_exemption_still_names_a_construction_that_exists()
    {
        var sources = ReadSources();

        var stale = Exemptions.Keys
            .Where(key => !sources.TryGetValue(key.File, out var source)
                || !ConstructionPattern(key.Type).IsMatch(StripComments(source)))
            .Select(key => $"{key.Type} in {key.File}")
            .ToList();

        Assert.True(stale.Count == 0, "Stale exemption(s) — remove them: " + string.Join(", ", stale));
    }

    // --- The scan: a function of the sources it is given ---

    /// <summary>
    /// The concrete provider types declared in <paramref name="sources"/>: every class whose
    /// bases name <see cref="ILlmProvider"/>, a provider base, or another provider — the
    /// decorators and the abstract bases excluded.
    /// </summary>
    private static HashSet<string> DiscoverProviderTypes(IReadOnlyDictionary<string, string> sources)
    {
        var declarations = sources.Values
            .SelectMany(source => ClassDeclarationRegex().Matches(StripComments(source)))
            .Select(match => (
                Name: match.Groups["name"].Value,
                Abstract: match.Groups["modifiers"].Value.Contains("abstract", StringComparison.Ordinal),
                Bases: match.Groups["bases"].Value))
            .ToList();

        var family = new HashSet<string>(StringComparer.Ordinal) { nameof(ILlmProvider) };
        bool grew;
        do
        {
            grew = false;
            foreach (var declaration in declarations)
            {
                if (!family.Contains(declaration.Name)
                    && family.Any(member => Regex.IsMatch(declaration.Bases, $@"\b{member}\b")))
                {
                    family.Add(declaration.Name);
                    grew = true;
                }
            }
        }
        while (grew);

        var abstracts = declarations.Where(d => d.Abstract).Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        family.Remove(nameof(ILlmProvider));
        family.RemoveWhere(name => abstracts.Contains(name) || Decorators.ContainsKey(name));
        return family;
    }

    private static List<string> FindOffPathConstructions(
        IReadOnlyDictionary<string, string> sources, IReadOnlySet<string> providers)
    {
        var violations = new List<string>();
        foreach (var (file, raw) in sources)
        {
            if (file == FactoryFile)
                continue;

            var source = StripComments(raw);
            foreach (var type in providers)
            {
                if (Exemptions.ContainsKey((type, file)))
                    continue;

                foreach (Match match in ConstructionPattern(type).Matches(source))
                {
                    if (IsHandedToTheRegistration(source, match.Index))
                        continue;

                    violations.Add($"{file}:{LineOf(source, match.Index)} -> {Collapse(match.Value)}");
                }
            }
        }

        return violations;
    }

    private static List<string> FindOffPathMetering(IReadOnlyDictionary<string, string> sources) =>
        sources
            .Where(entry => entry.Key != FactoryFile && entry.Key != RegistrationFile)
            .SelectMany(entry =>
            {
                var source = StripComments(entry.Value);
                return WrapCallRegex().Matches(source)
                    .Select(match => $"{entry.Key}:{LineOf(source, match.Index)} -> {Collapse(match.Value)}");
            })
            .ToList();

    /// <summary>
    /// <c>new T(</c> (qualified or not), <c>T x = new(</c>, and a DI activation of <c>T</c>
    /// (<c>AddSingleton&lt;T&gt;</c>, <c>TryAddScoped&lt;ILlmProvider, T&gt;</c>, …).
    /// </summary>
    private static Regex ConstructionPattern(string type) => new(
        $@"\bnew\s+(?:[\w.]+\.)?{type}\s*\(|\b{type}\s+\w+\s*=\s*new\s*\(|\b(?:Try)?Add(?:Singleton|Scoped|Transient)\s*<[^>;]*\b{type}\b[^>;]*>");

    /// <summary>
    /// Whether the construction at <paramref name="index"/> sits in a statement that hands
    /// the provider to <c>AddOrkeonLlmProvider</c> — which meters it on the spot.
    /// </summary>
    private static bool IsHandedToTheRegistration(string source, int index)
    {
        var start = source.LastIndexOfAny([';', '{', '}'], index) + 1;
        var end = source.IndexOf(';', index);
        var statement = source[start..(end < 0 ? source.Length : end)];
        return statement.Contains("AddOrkeonLlmProvider(", StringComparison.Ordinal);
    }

    // --- Repository access and text helpers ---

    private static IEnumerable<Type> ConcreteProviderTypesOfTheInfrastructure() =>
        typeof(LlmProviderFactory).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(ILlmProvider).IsAssignableFrom(type));

    /// <summary>Every <c>src/**/*.cs</c> file, keyed by its repository-relative path.</summary>
    private static Dictionary<string, string> ReadSources()
    {
        var root = FindRepositoryRoot();
        var srcRoot = Path.Combine(root, "src");
        return Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj" or "obj-linux"))
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllText,
                StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Orkeon.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root (Orkeon.sln marker) above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>Removes comments while keeping line numbers (tripwire-grade, like the other guards).</summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = BlockCommentRegex().Replace(source, m => new string('\n', m.Value.Count(c => c == '\n')));
        return LineCommentRegex().Replace(withoutBlocks, string.Empty);
    }

    private static int LineOf(string text, int index) => 1 + text.AsSpan(0, index).Count('\n');

    private static string Collapse(string value) => WhitespaceRegex().Replace(value, " ").Trim();

    [GeneratedRegex(@"(?<modifiers>(?:\b(?:public|internal|private|protected|sealed|partial|abstract|static|file)\s+)*)class\s+(?<name>\w+)(?:<[^>]*>)?\s*(?:\([^)]*\))?\s*:\s*(?<bases>[^{;]+)[{;]")]
    private static partial Regex ClassDeclarationRegex();

    [GeneratedRegex(@"\bMeteredLlmProvider\s*\.\s*Wrap\s*\(")]
    private static partial Regex WrapCallRegex();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex LineCommentRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
