using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Constants.Configuration;
using Orkeon.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Hosting;

/// <summary>
/// What a shipped host makes of its settings before it starts: what it refuses, each a sentence
/// naming its key, and what it only reports — a section it knows and no component of it reads.
/// </summary>
/// <param name="Refusals">What stops the start; empty when the host may start.</param>
/// <param name="Notices">What the host says and starts with (<see cref="SettingsValidation.Notices"/>).</param>
internal sealed record SettingsVerdict(IReadOnlyList<string> Refusals, IReadOnlyList<string> Notices);

/// <summary>
/// What a notice adds for a section its reader most likely wrote for something this host does read.
/// </summary>
/// <param name="Sentence">The sentence, naming the sections it points to.</param>
/// <param name="Sections">The sections it points to: each one a shipped host reads.</param>
internal sealed record SettingsNoticeHint(string Sentence, IReadOnlyList<string> Sections);

/// <summary>
/// The start validation of a shipped host (GAP-40): every setting it reads is judged before the host
/// is handed out, whether the run uses it or not, and every refusal names its key. In this order:
/// <list type="number">
/// <item>the values — each declared options section is created, so the binder converts it and its
/// rules, the names it holds among them, run; then <see cref="IStartupValidator"/> catches whatever a
/// registration validated without declaring;</item>
/// <item>the section names under the containers of <see cref="SettingsSections"/>;</item>
/// <item>the keys of every declared section, against the shapes its readers declared — and, under those keys, a number the binder reads as a float or a double that
/// is not finite, which it takes without a word; a non-string value written without one, which it
/// binds to its type's default (#450); a single value where a list or a section is expected; and,
/// inside a list or a dictionary, where it drops what it cannot convert, a value not of its type (#454).</item>
/// </list>
/// Nothing is built but the options and the named factories, which hold functions: no store, provider,
/// model or connection. Only what the binder, a rule, the names or the keys refuse becomes a refusal —
/// a defect of the code keeps its exception and its stack.
/// <para>
/// Beside the refusals, <see cref="Notices"/>: the sections the file writes that Orkeon knows and no
/// shipped host reads. They refuse nothing — a file shared with a host written in C# is legitimate —
/// and they are said, because a run without the limit or the screening its operator wrote is worse
/// than a line on stderr.
/// </para>
/// </summary>
internal static class SettingsValidation
{
    /// <summary>
    /// The section the plugins are loaded from. Its registration binds it while it registers, without
    /// a declaration, and lives in an assembly this one does not reference: the registry it leaves in
    /// the container is what says the section was read.
    /// </summary>
    private const string PluginsSection = "Plugins";

    private const string PluginsAssembly = "Orkeon.Plugins";

    private const string PluginRegistryType = "Orkeon.Plugins.IPluginRegistry";

    /// <summary>
    /// What comes close to a section no shipped host reads, where whoever wrote it most likely meant
    /// something this host does read. Written by hand, and held by a test to sections every shipped
    /// host reads — the list of the sections reported is not written: it is the catalogue's.
    /// </summary>
    internal static IReadOnlyDictionary<string, SettingsNoticeHint> NoticeHints { get; } =
        new Dictionary<string, SettingsNoticeHint>(StringComparer.OrdinalIgnoreCase)
        {
            ["ToolRateLimiting"] = new(
                "The calls to the model are limited by RateLimiting, which this host reads.",
                ["RateLimiting"]),
            ["Orkeon:Dlp"] = new(
                "Data-loss prevention is not active: what this host screens is set by Security:Prompt and Security:ToolResults.",
                ["Security:Prompt", "Security:ToolResults"]),
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

        var configuration = services.GetRequiredService<IConfiguration>();
        var references = ReferenceRefusals(configuration);
        if (references.Count > 0)
            return references;

        var declarations = services.GetServices<SettingsDeclaration>().ToList();
        var refusals = new List<string>();
        EvaluateDeclaredOptions(services, declarations, refusals);
        if (refusals.Count == 0)
            RunStartupValidator(services, refusals);

        refusals.AddRange(UnknownSectionNames(configuration));
        foreach (var (path, refusal, kind) in UnknownKeys(configuration, declarations).ToList())
        {
            // One refusal per key. A value written without one explains what a rule then says of the
            // default the binder wrote ('0' for a file that says null): it replaces that sentence.
            // Otherwise the sentence of a rule or of the binder that names the key wins over the walk's.
            if (kind == Found.Empty)
                refusals.RemoveAll(earlier => NamesExactly(earlier, path));
            else if (kind == Found.Value && refusals.Any(earlier => Names(earlier, path)))
                continue;

            refusals.Add(refusal);
        }

        refusals.AddRange(UnansweredProviderKeys(configuration, services));
        return [.. refusals.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The <c>Provider</c> of the <c>Llm</c> section and of each profile that no registered provider
    /// module answers: the factory would refuse it at the first call, long after the start. A vendor
    /// of the catalogue written there is refused where the section is read.
    /// </summary>
    private static IEnumerable<string> UnansweredProviderKeys(IConfiguration configuration, IServiceProvider services)
    {
        var modules = services.GetServices<Orkeon.Application.Interfaces.Ports.ILlmProviderModule>()
            .Select(module => module.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        var llm = configuration.GetSection(ConfigurationKeys.LlmSection);
        var sections = llm.GetSection(ConfigurationKeys.LlmProfiles).GetChildren().Prepend(llm);
        foreach (var section in sections)
        {
            var named = section["Provider"];
            if (string.IsNullOrWhiteSpace(named))
                continue;

#pragma warning disable CA1308 // the key is matched in its lowercase wire form
            var key = named.Trim().ToLowerInvariant();
#pragma warning restore CA1308
            if (key == LlmProviderKeys.Mock
                || LlmProviderKeys.All.Contains(key, StringComparer.Ordinal)
                || LlmProviderKeys.Aliases.ContainsKey(key)
                || modules.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;

            var known = modules.Count == 0 ? "this host registers none" : "registered: " + string.Join(", ", modules);
            yield return $"{section.Path}:Provider: '{named.Trim()}' names no provider of this host. "
                + $"Provider takes '{LlmProviderKeys.Mock}' or the key of a provider module ({known}); "
                + "leave it out and the provider follows from BaseUrl, the model's name and the key.";
        }
    }

    /// <summary>
    /// What the host reports and starts with: every section the configuration writes — the file or
    /// the environment — that the settings catalogue knows, that no shipped host reads, and that this
    /// composition does not read either. One sentence per section, naming it and the registration a
    /// host written in C# reads it through, then what comes close when something does
    /// (<see cref="NoticeHints"/>). Computed from the catalogue: a section a shipped host starts to
    /// read leaves the list by itself.
    /// <para>
    /// Not reported: a section another shipped host reads (<c>Orkeon:Host</c> in a file
    /// <c>orkeon run</c> shares with the daemon), a root section Orkeon does not know — the root
    /// stays open —, and a single value at the path of a section, which is an environment variable
    /// of that name rather than a section.
    /// </para>
    /// </summary>
    /// <param name="services">The built container.</param>
    public static IReadOnlyList<string> Notices(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var configuration = services.GetRequiredService<IConfiguration>();
        HashSet<string>? read = null;
        var notices = new List<string>();
        foreach (var section in SettingsCatalog.Complete.Sections)
        {
            if (section.Hosts.Count > 0 || !configuration.GetSection(section.Path).GetChildren().Any())
                continue;

            read ??= SectionsRead(services);
            if (read.Any(path => string.Equals(path, section.Path, StringComparison.OrdinalIgnoreCase) || IsBelow(section.Path, path)))
                continue;

            notices.Add(Notice(section));
        }

        return notices;
    }

    /// <summary>
    /// The sections this composition reads: those its registrations declared, those they registered
    /// as read without a declaration, and the plugins' when their registry is there.
    /// </summary>
    private static HashSet<string> SectionsRead(IServiceProvider services)
    {
        var read = services.GetServices<SettingsDeclaration>().Select(declaration => declaration.Path)
            .Concat(services.GetServices<SettingsSource>().Select(source => source.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // An assembly that is not loaded registered nothing.
        var registry = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, PluginsAssembly, StringComparison.Ordinal))
            ?.GetType(PluginRegistryType);
        if (registry is not null && services.GetService(registry) is not null)
            read.Add(PluginsSection);

        return read;
    }

    private static string Notice(SettingsCatalogSection section)
    {
        var reader = section.Registration is { } registration
            ? $"a C# host reads it through {registration}()."
            : "no shipped host reads it.";
        var close = NoticeHints.TryGetValue(section.Path, out var hint) ? " " + hint.Sentence : string.Empty;
        return $"{section.Path} is read by no component of this host: {reader}{close}";
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
                // The binder's refusal, which names the value: reworded without it (#465).
                refusals.Add(BinderRefusal.WithoutValue(ex.Message));
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
            refusals.Add(BinderRefusal.WithoutValue(ex.Message));
        }
    }

    /// <summary>
    /// The settings of <see cref="SettingPlaceholders.Settings"/> that hold a variable reference in the
    /// configuration as it is read: a value a higher layer covers is not the one read, so not judged.
    /// </summary>
    private static List<string> ReferenceRefusals(IConfiguration configuration)
    {
        var pending = new Stack<IConfigurationSection>(configuration.GetChildren());
        var refusals = new List<string>();
        while (pending.Count > 0)
        {
            var section = pending.Pop();
            var children = section.GetChildren().ToList();
            if (children.Count > 0)
            {
                foreach (var child in children)
                    pending.Push(child);
            }
            else if (SettingReference.Phrase(section.Path, section.Value) is { } refusal)
            {
                refusals.Add(refusal);
            }
        }

        refusals.Sort(StringComparer.Ordinal);
        return refusals;
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

                var closest = ClosestName.Of(child.Key, known);
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
    private static IEnumerable<(string Path, string Refusal, Found Kind)> UnknownKeys(IConfiguration configuration, List<SettingsDeclaration> declarations)
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

            foreach (var refusal in Walk(section, NodeAt(tree, root), inCollection: false))
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

    /// <summary>
    /// Whether <paramref name="refusal"/> names the key at <paramref name="path"/> — or, for a list
    /// item, the list —, a whole key and not the start of a longer one.
    /// </summary>
    private static bool Names(string refusal, string path)
    {
        var last = path.LastIndexOf(':');
        var parent = last > 0 && path[(last + 1)..].All(char.IsAsciiDigit) ? path[..last] : null;
        return NamesExactly(refusal, path) || (parent is not null && NamesExactly(refusal, parent));
    }

    private static bool NamesExactly(string refusal, string key)
    {
        for (var at = refusal.IndexOf(key, StringComparison.OrdinalIgnoreCase); at >= 0;
             at = refusal.IndexOf(key, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = at + key.Length;
            if (end == refusal.Length || !char.IsAsciiLetterOrDigit(refusal[end]))
                return true;
        }

        return false;
    }

    /// <summary>What the walk of the keys found at a key.</summary>
    private enum Found
    {
        /// <summary>A key no declaration knows.</summary>
        Key,

        /// <summary>A value the binder would take, drop or misread without a word.</summary>
        Value,

        /// <summary>A key written without a value, which the binder would bind to its type's default.</summary>
        Empty,
    }

    /// <summary>
    /// The refusals below <paramref name="section"/>: a key no declaration knows, and a value the
    /// binder would take without a word: written without a value on a setting it would then bind to
    /// its type's default (#450), a single value where a list or a section is expected (#454, #455),
    /// a number that is not finite, and, below a list or a dictionary, where the binder drops the item
    /// it cannot convert, a value that is not of its type (#454). An enumeration is left to its
    /// reader, which may take its own spellings.
    /// </summary>
    private static IEnumerable<(string Path, string Refusal, Found Kind)> Walk(
        IConfigurationSection section, SettingsShape shape, bool inCollection)
    {
        foreach (var child in section.GetChildren())
        {
            if (shape.Child(child.Key) is not { } below)
            {
                yield return (child.Path, Unknown(section, child, shape), Found.Key);
                continue;
            }

            var item = inCollection || (!shape.Open && shape.Entries is not null && !shape.Named.ContainsKey(child.Key));
            if (ValueRefusal(child, below, item) is { } refusal)
            {
                yield return (child.Path, refusal, child.Value is null ? Found.Empty : Found.Value);
                continue;
            }

            foreach (var found in Walk(child, below, item))
                yield return found;
        }
    }

    /// <summary>What is wrong with the value of <paramref name="child"/>, or null.</summary>
    private static string? ValueRefusal(IConfigurationSection child, SettingsShape below, bool inCollection)
    {
        if (below.Open || child.GetChildren().Any())
            return null;

        var value = child.Value;
        if (value is null)
        {
            // JSON null and {} read alike: no value, no child, and the binder writes the type's default.
            var bound = below.TakesKeys ? null : below.Values.FirstOrDefault(IsDefaultedWhenEmpty);
            return bound is null
                ? null
                : $"{child.Path} is written without a value, which would bind it to {Default(bound)}: "
                  + $"write {Expected(bound)}, or leave the key out and its default applies.";
        }

        if (below.Values.Count == 0)
        {
            // An empty JSON array reads as an empty value.
            // A list whose reader splits one comma-separated value takes that form (an environment variable's).
            if (!below.TakesKeys || value.Length == 0 || (below.IsList && below.TakesCommaSeparated))
                return null;

            // The value is not echoed: written where a section is expected, it may be a secret.
            return below.IsList
                ? $"{child.Path} is a single value where a list is expected: write its items as a list, or leave the key out."
                : $"{child.Path} is a single value where a section of keys is expected: write its keys, or leave the key out.";
        }

        if (IsNotFinite(value, below.Number))
            // The value is not echoed (#465): every refusal of the check leaves out what the key holds.
            return $"{child.Path} is not a finite number: NaN, an infinity and a number past the type's range are refused.";

        if (!inCollection)
            return null;

        foreach (var declared in below.Values)
        {
            if (Unconverted(child.Path, value, declared) is { } refusal)
                return refusal;
        }

        return null;
    }

    /// <summary>A value type the binder writes its default to when the key holds no value: <c>false</c>, <c>0</c>, the first member.</summary>
    private static bool IsDefaultedWhenEmpty(Type type) => type.IsValueType && Nullable.GetUnderlyingType(type) is null;

    private static string Default(Type type) =>
        type == typeof(bool) ? "false"
        : IsNumber(type) ? "0"
        : type.IsEnum ? Enum.ToObject(type, 0).ToString() ?? type.Name
        : $"the default of {type.Name}";

    private static string Expected(Type type) =>
        type == typeof(bool) ? "true or false"
        : type.IsEnum ? "one of " + string.Join(", ", Enum.GetNames(type))
        : IsNumber(type) ? "a number"
        : $"a {type.Name}";

    private static bool IsNumber(Type type) =>
        !type.IsEnum && Type.GetTypeCode(type) is >= TypeCode.SByte and <= TypeCode.Decimal;

    /// <summary>
    /// The refusal of <paramref name="value"/> as <paramref name="declared"/>, worded as the binder words
    /// it where it refuses it itself, or null when it converts. Neither a string nor an enumeration is
    /// judged: their readers may take what the binder does not.
    /// </summary>
    private static string? Unconverted(string path, string value, Type declared)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        if (type == typeof(string) || type.IsEnum || (value.Length == 0 && type != declared))
            return null;

        try
        {
            _ = TypeDescriptor.GetConverter(type).ConvertFromInvariantString(value);
            return null;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException or OverflowException
                                       || ex.InnerException is FormatException or OverflowException)
        {
            // The value is not echoed (#465): it is not of the key's type, so it may be anything, a secret included.
            return BinderRefusal.Unconvertible(path, type.ToString());
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
        var closest = ClosestName.Of(child.Key, known);
        return $"{child.Path} is not a setting: {section.Path} carries {string.Join(", ", known)}."
               + (closest is null ? string.Empty : $" Did you mean {section.Path}:{closest}?")
               + ListedBy(section.Path);
    }

    /// <summary>
    /// Where the keys of the section holding <paramref name="path"/> are listed with their type, their
    /// default and their meaning: the verb of the CLI, by the catalogue's name of that section. Empty
    /// for a section the catalogue does not know — a host's own, declared in C#.
    /// </summary>
    private static string ListedBy(string path) =>
        SettingsCatalog.Complete.SectionCovering(path) is { } section
            ? $" `orkeon settings {section.Path}` lists its keys."
            : string.Empty;
}
