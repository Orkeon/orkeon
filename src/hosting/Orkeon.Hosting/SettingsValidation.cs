using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Constants.Configuration;

namespace Orkeon.Hosting;

/// <summary>
/// The start validation of a shipped host (GAP-40): every setting it reads is judged before the host
/// is handed out, whether the run uses it or not, and every refusal names its key. In this order:
/// <list type="number">
/// <item>the values — each declared options section is created, so the binder converts it and its
/// rules, the names it holds among them, run; then <see cref="IStartupValidator"/> catches whatever a
/// registration validated without declaring;</item>
/// <item>the section names under the containers of <see cref="SettingsSections"/>;</item>
/// <item>the keys of every declared section, against the shapes its readers declared, the retired keys
/// with their migration — and, under those keys, a number the binder reads as a float or a double that
/// is not finite, which it takes without a word.</item>
/// </list>
/// Nothing is built but the options and the named factories, which hold functions: no store, provider,
/// model or connection. Only what the binder, a rule, the names or the keys refuse becomes a refusal —
/// a defect of the code keeps its exception and its stack.
/// </summary>
internal static class SettingsValidation
{
    /// <summary>
    /// The keys GAP-08 removed, with what replaces them: still read as absent, they are refused with
    /// the migration the changelog gave rather than as an unknown key.
    /// </summary>
    private static readonly Dictionary<string, string> s_retiredKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Memory:ConnectionString"] =
            "Memory:ConnectionString is not a setting any more: a memory provider is connected from its own section. " +
            "Move it to the section of the provider it was for — Orkeon:Redis:ConnectionString, " +
            "Orkeon:Sqlite:ConnectionString, Orkeon:ChromaDb:BaseUrl or Orkeon:LanceDb:Endpoint.",
        ["Orkeon:Rag:ConnectionString"] =
            "Orkeon:Rag:ConnectionString is not a setting any more: the RAG store is connected from the section of " +
            "the provider Orkeon:Rag:Provider names. Move it there — Orkeon:Sqlite:ConnectionString for sqlite, " +
            "Orkeon:Redis:ConnectionString for redis, Orkeon:ChromaDb:BaseUrl, Orkeon:LanceDb:Endpoint.",
        ["Orkeon:Rag:ProviderOptions"] =
            "Orkeon:Rag:ProviderOptions is not a setting any more: the RAG store is connected from the section of " +
            "the provider Orkeon:Rag:Provider names. Move its keys there — Orkeon:Redis, Orkeon:Sqlite, " +
            "Orkeon:ChromaDb, Orkeon:Pinecone or Orkeon:LanceDb.",
        ["Orkeon:Pinecone:Environment"] =
            "Orkeon:Pinecone:Environment is not a setting any more: write Orkeon:Pinecone:Host, the host the Pinecone " +
            "console shows, or remove it and let the provider look the host up.",
    };

    /// <summary>The seven levels a <c>LogLevel</c> key takes.</summary>
    private static readonly string[] s_logLevels = Enum.GetNames<LogLevel>();

    /// <summary>
    /// Every refusal of the start validation, in the order above, each one sentence naming its key;
    /// empty when the host may start.
    /// </summary>
    /// <param name="services">The built container.</param>
    public static IReadOnlyList<string> Refusals(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var declarations = services.GetServices<SettingsDeclaration>().ToList();
        var refusals = new List<string>();
        EvaluateDeclaredOptions(services, declarations, refusals);
        if (refusals.Count == 0)
            RunStartupValidator(services, refusals);

        var configuration = services.GetRequiredService<IConfiguration>();
        refusals.AddRange(UnknownSectionNames(configuration));
        refusals.AddRange(UnknownKeys(configuration, declarations));
        return [.. refusals.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// <c>Logging</c> is read while the host builds its logger, before any barrier, and a
    /// level the logging configuration does not know made the build throw a sentence without the key.
    /// Checked on the configuration the host is built from: every value under a <c>LogLevel</c> key, for
    /// every provider, is one of the seven levels, any case; the console provider's own options are
    /// bound here, so the binder names a key it cannot convert.
    /// </summary>
    /// <exception cref="InvalidOperationException">A level is none, or the console options cannot be bound.</exception>
    public static void CheckLogging(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var logging = configuration.GetSection("Logging");
        foreach (var levels in new[] { logging.GetSection("LogLevel") }
                     .Concat(logging.GetChildren().Select(provider => provider.GetSection("LogLevel"))))
        {
            foreach (var (path, value) in levels.AsEnumerable())
            {
                if (string.IsNullOrWhiteSpace(value)
                    || s_logLevels.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"{path} is '{value}', which is not a log level: write one of {string.Join(", ", s_logLevels)}.");
            }
        }

        var console = logging.GetSection("Console");
        console.Bind(new ConsoleLoggerOptions());
        console.GetSection("FormatterOptions").Bind(new SimpleConsoleFormatterOptions());
    }

    private static void EvaluateDeclaredOptions(
        IServiceProvider services, List<SettingsDeclaration> declarations, List<string> refusals)
    {
        foreach (var declaration in declarations)
        {
            if (declaration.Evaluate is not { } evaluate)
                continue;

            try
            {
                evaluate(services);
            }
            catch (OptionsValidationException ex)
            {
                refusals.AddRange(ex.Failures);
            }
            catch (InvalidOperationException ex) when (IsBinderRefusal(ex))
            {
                // The binder's refusal: "Failed to convert configuration value 'x' at 'Key' to type 'T'."
                refusals.Add(ex.Message);
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="ex"/> is the configuration binder refusing a value — thrown by the binder
    /// itself —, not a defect of the code that sets the options up, which keeps its type and its stack.
    /// </summary>
    private static bool IsBinderRefusal(InvalidOperationException ex) =>
        ex.TargetSite?.DeclaringType?.Assembly == typeof(ConfigurationBinder).Assembly;

    /// <summary>
    /// The options a registration validates at start without declaring its section — a C# opt-in, a
    /// third-party package —, judged as <c>StartAsync</c> would judge them.
    /// </summary>
    private static void RunStartupValidator(IServiceProvider services, List<string> refusals)
    {
        try
        {
            services.GetService<IStartupValidator>()?.Validate();
        }
        catch (OptionsValidationException ex)
        {
            refusals.AddRange(ex.Failures);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OptionsValidationException))
        {
            refusals.AddRange(ex.InnerExceptions.Cast<OptionsValidationException>().SelectMany(inner => inner.Failures));
        }
        catch (InvalidOperationException ex) when (IsBinderRefusal(ex))
        {
            refusals.Add(ex.Message);
        }
    }

    /// <summary>
    /// A child of a container of <see cref="SettingsSections"/> that is no section Orkeon reads —
    /// <c>Orkeon:Guardain</c> —, with the closest known name. The root stays open: it also holds the
    /// environment variables without a prefix.
    /// </summary>
    private static IEnumerable<string> UnknownSectionNames(IConfiguration configuration)
    {
        foreach (var container in SettingsSections.Containers)
        {
            var section = configuration.GetSection(container);
            if (!section.Exists())
                continue;

            var known = SettingsSections.Known
                .Concat(SettingsSections.Containers)
                .Where(path => IsChildOf(path, container))
                .Select(path => path[(container.Length + 1)..])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var child in section.GetChildren())
            {
                if (known.Contains(child.Key, StringComparer.OrdinalIgnoreCase))
                    continue;

                var closest = Closest(child.Key, known);
                yield return $"{child.Path} is not a section Orkeon reads: {container} holds {string.Join(", ", known)}."
                             + (closest is null ? string.Empty : $" Did you mean {container}:{closest}?");
            }
        }
    }

    private static bool IsChildOf(string path, string container) =>
        path.Length > container.Length + 1
        && path.StartsWith(container + ConfigurationPath.KeyDelimiter, StringComparison.OrdinalIgnoreCase)
        && !path.AsSpan(container.Length + 1).Contains(':');

    /// <summary>
    /// The keys of every declared section no declaration knows at their path, the readers of one
    /// section together, a sub-section another reader declares included. Only the declared sections
    /// are walked: the keys of a section this host does not read are another host's to judge.
    /// </summary>
    private static IEnumerable<string> UnknownKeys(IConfiguration configuration, List<SettingsDeclaration> declarations)
    {
        var tree = SettingsShape.Empty();
        foreach (var declaration in declarations)
            NodeAt(tree, declaration.Path).Merge(SettingsShape.Of(declaration.Shape));

        var roots = declarations
            .Select(declaration => declaration.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !declarations.Any(other => IsBelow(path, other.Path)))
            .ToList();
        foreach (var root in roots)
        {
            var section = configuration.GetSection(root);
            if (!section.Exists())
                continue;

            foreach (var refusal in Walk(section, NodeAt(tree, root)))
                yield return refusal;
        }
    }

    private static bool IsBelow(string path, string ancestor) =>
        path.Length > ancestor.Length
        && path.StartsWith(ancestor + ConfigurationPath.KeyDelimiter, StringComparison.OrdinalIgnoreCase);

    private static SettingsShape NodeAt(SettingsShape tree, string path)
    {
        var node = tree;
        foreach (var key in path.Split(ConfigurationPath.KeyDelimiter))
        {
            if (!node.Named.TryGetValue(key, out var next))
            {
                next = SettingsShape.Empty();
                node.Named[key] = next;
            }

            node = next;
        }

        return node;
    }

    private static IEnumerable<string> Walk(IConfigurationSection section, SettingsShape shape)
    {
        foreach (var child in section.GetChildren())
        {
            if (s_retiredKeys.TryGetValue(child.Path, out var migration))
            {
                yield return migration;
                continue;
            }

            if (shape.Child(child.Key) is { } below)
            {
                if (IsNotFinite(child.Value, below.Number))
                {
                    yield return $"{child.Path} is '{child.Value}', which is not a finite number.";
                    continue;
                }

                foreach (var refusal in Walk(child, below))
                    yield return refusal;
                continue;
            }

            yield return Unknown(section, child, shape);
        }
    }

    /// <summary>
    /// A value the binder reads as <paramref name="number"/> but no setting can mean: <c>NaN</c>, an
    /// infinity, or a number past the type's range, which it reads as one — <c>1e40</c> for a float. A
    /// value that is no number at all is the binder's to refuse.
    /// </summary>
    private static bool IsNotFinite(string? value, Type? number)
    {
        const NumberStyles Styles = NumberStyles.Float | NumberStyles.AllowThousands;
        if (value is null || number is null)
            return false;

        return number == typeof(float)
            ? float.TryParse(value, Styles, CultureInfo.InvariantCulture, out var single) && !float.IsFinite(single)
            : double.TryParse(value, Styles, CultureInfo.InvariantCulture, out var @double) && !double.IsFinite(@double);
    }

    private static string Unknown(IConfigurationSection section, IConfigurationSection child, SettingsShape shape)
    {
        if (!shape.TakesKeys)
            return $"{child.Path} is not a setting: {section.Path} is a single value.";

        var known = shape.Named.Keys.ToList();
        var closest = Closest(child.Key, known);
        return $"{child.Path} is not a setting: {section.Path} carries {string.Join(", ", known)}."
               + (closest is null ? string.Empty : $" Did you mean {section.Path}:{closest}?");
    }

    /// <summary>
    /// The known name <paramref name="name"/> is likely a misspelling of — two edits away at most, a
    /// third of its length for a long one, a swap of two neighbours counting as one —, or null.
    /// </summary>
    internal static string? Closest(string name, IEnumerable<string> known)
    {
        var budget = Math.Max(2, name.Length / 3);
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in known)
        {
            var distance = Distance(name.ToUpperInvariant(), candidate.ToUpperInvariant());
            if (distance <= budget && distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>The optimal string alignment distance: insertions, deletions, substitutions, adjacent swaps.</summary>
    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1][];
        for (var i = 0; i <= a.Length; i++)
        {
            d[i] = new int[b.Length + 1];
            d[i][0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
            d[0][j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i][j] = Math.Min(Math.Min(d[i - 1][j] + 1, d[i][j - 1] + 1), d[i - 1][j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i][j] = Math.Min(d[i][j], d[i - 2][j - 2] + 1);
            }
        }

        return d[a.Length][b.Length];
    }
}
