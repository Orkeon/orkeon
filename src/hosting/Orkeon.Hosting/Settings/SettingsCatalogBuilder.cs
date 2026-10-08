using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Constants.Configuration;
using Orkeon.Domain.Constants.Rag;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Hosting;

/// <summary>
/// A section a composition reads and the type its keys are read into: a declared one
/// (<see cref="SettingsDeclaration"/>), or one a reader reads without declaring it — registered as
/// such by the code that reads it, so the catalogue lists it and the start validation, which walks
/// the declarations only, judges exactly what it judged.
/// </summary>
/// <param name="Path">The section's configuration path.</param>
/// <param name="Shape">The type whose public properties are the section's keys; a value type for a section that is one key.</param>
internal sealed record SettingsSource(string Path, Type Shape)
{
    /// <summary>What the section is, when its type carries no comment of its own (a dictionary, a string).</summary>
    public string? Description { get; init; }

    /// <summary>Whether every value of the section is a secret.</summary>
    public bool Secret { get; init; }
}

/// <summary>
/// Builds a <see cref="SettingsCatalog"/> from what a composition declares: each section's keys read
/// off its type the way <see cref="SettingsShape"/> reads them for the start validation — a public
/// property is a key, a dictionary takes a name, a list an index —, each key with the type the
/// binder converts it to, the value the property has on a new instance, and the summary of its
/// comment. Builds no container, opens no file and reads no setting.
/// </summary>
internal static class SettingsCatalogBuilder
{
    /// <summary>What stands for a name the operator chooses: a dictionary's key.</summary>
    public const string Name = "<name>";

    /// <summary>What stands for an index: an item of a list of sections.</summary>
    public const string Index = "<i>";

    /// <summary>The type of a value its reader takes as it is written.</summary>
    public const string Any = "any";

    /// <summary>The endings of a key whose value is a secret.</summary>
    private static readonly string[] s_secretEndings =
    [
        "ApiKey", "Password", "ConnectionString", "ClientSecret", "MasterKey", "EncryptionKey", "PrivateKey", "AccessKey",
        "AccessToken", "AuthToken", "BearerToken", "ApiToken",
    ];

    /// <summary>The names that are a secret by themselves.</summary>
    private static readonly string[] s_secretNames = ["Key", "Secret", "Token"];

    /// <summary>
    /// The switches that gate an optional registration of a shipped host, all on: composed over this
    /// configuration, a host declares every section it can read, not only those a default run reads.
    /// A registration that starts to depend on a switch adds it here, or its section leaves the
    /// catalogue — and the test that holds the host's file says so.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> EverySwitchOn { get; } = new Dictionary<string, string?>
    {
        ["BRAVE_API_KEY"] = "catalogue",
        ["MCP:Servers:catalogue:Command"] = "catalogue",
        ["Orkeon:Security:PermissionGate:Enabled"] = "true",
        ["Orkeon:Cli:ConsoleStreaming:Enabled"] = "true",
        ["Orkeon:Host:A2A:Enabled"] = "true",
    };

    /// <summary>
    /// The sections <paramref name="services"/> reads: those its registrations declared, those they
    /// registered as read without a declaration, and the three a registration that does not see this
    /// assembly reads — <c>Orkeon:Tools:Email</c>, bound account by account by its own binder,
    /// <c>Orkeon:Tools:Shell</c>, which the code tools bind and a shipped host declares for them, and
    /// <c>Secrets</c>, the configuration stop of the secret chain.
    /// </summary>
    public static IReadOnlyList<SettingsSource> SourcesOf(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var sources = new List<SettingsSource>();
        foreach (var descriptor in services)
        {
            if (descriptor.IsKeyedService)
                continue;

            switch (descriptor.ImplementationInstance)
            {
                case SettingsDeclaration declaration when descriptor.ServiceType == typeof(SettingsDeclaration):
                    sources.Add(new SettingsSource(declaration.Path, declaration.Shape));
                    break;
                case SettingsSource source when descriptor.ServiceType == typeof(SettingsSource):
                    sources.Add(source);
                    break;
            }
        }

        if (EmailOptionsType() is { } email
            && services.Any(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<>).MakeGenericType(email)))
        {
            sources.Add(new SettingsSource(ConfigurationKeys.ToolsEmail, email));
        }

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<Orkeon.Tools.Code.ShellToolOptions>)))
            sources.Add(new SettingsSource(Orkeon.Tools.Code.ShellToolOptions.SectionName, typeof(Orkeon.Tools.Code.ShellToolOptions)));

        if (services.Any(descriptor => descriptor.ServiceType == typeof(ISecretProvider)))
            sources.Add(Secrets);

        return sources;
    }

    /// <summary>
    /// The <c>Secrets</c> section: the second stop of the secret chain <c>AddOrkeonInfrastructure</c>
    /// registers, after the <c>ORKEON_&lt;NAME&gt;</c> environment variables.
    /// </summary>
    public static SettingsSource Secrets { get; } = new("Secrets", typeof(Dictionary<string, string>))
    {
        Description =
            "A secret by its name — `Secrets:TAVILY_API_KEY` —, where the secret chain looks after the " +
            "`ORKEON_<NAME>` environment variable and before a vault. A value written here is in clear in the settings file.",
        Secret = true,
    };

    /// <summary>The catalogue of what <paramref name="services"/> reads (<see cref="SourcesOf"/>).</summary>
    public static SettingsCatalog Describe(IServiceCollection services, SettingsDocumentation documentation) =>
        Describe(SourcesOf(services), documentation);

    /// <summary>
    /// The catalogue of <paramref name="sources"/>: one section per path, the readers of one path
    /// together, each key under the deepest section above it. A section's category is that of
    /// <see cref="SettingsCategories.Of"/>, empty when the table does not file it; no section names a
    /// host or a registration — whoever knows which composition the sources came from adds them.
    /// </summary>
    public static SettingsCatalog Describe(IEnumerable<SettingsSource> sources, SettingsDocumentation documentation)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(documentation);

        var sections = new Dictionary<string, SettingsCatalogSection>(StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, SettingsCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            var description = source.Description ?? documentation.Of(source.Shape);
            if (!sections.TryGetValue(source.Path, out var known) || known.Description.Length == 0)
            {
                sections[source.Path] = new SettingsCatalogSection
                {
                    Path = source.Path,
                    Category = SettingsCategories.Of(source.Path) ?? string.Empty,
                    Description = description,
                };
            }

            foreach (var entry in Entries(source, description, documentation))
            {
                // Two readers of one key: the first that says something of it is kept.
                if (!entries.TryGetValue(entry.Path, out var existing)
                    || (existing.Description.Length == 0 && entry.Description.Length > 0))
                {
                    entries[entry.Path] = entry;
                }
            }
        }

        var paths = sections.Keys.OrderByDescending(path => path.Length).ToList();
        return new SettingsCatalog(
            sections.Values,
            entries.Values.Select(entry => entry with { Section = paths.First(path => IsAtOrBelow(entry.Path, path)) }));
    }

    /// <summary>
    /// The keys of one source. The RAG pipeline's are read off the preset of the default profile —
    /// what a host that sets nothing runs on —, and those the profiles set differently say so.
    /// </summary>
    private static List<SettingsCatalogEntry> Entries(SettingsSource source, string description, SettingsDocumentation documentation)
    {
        if (source.Shape != typeof(RagOptions))
            return Walked(source, Instance(source.Shape), description, documentation);

        var defaultProfile = RagProfilePresets.Parse(RagDefaults.DefaultProfile);
        var entries = Walked(source, RagProfilePresets.Create(defaultProfile), description, documentation);
        var profiled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in Enum.GetValues<RagProfile>())
        {
            var preset = Walked(source, RagProfilePresets.Create(profile), description, documentation)
                .ToDictionary(entry => entry.Path, entry => entry.Default, StringComparer.OrdinalIgnoreCase);
            profiled.UnionWith(entries
                .Where(entry => !string.Equals(preset.GetValueOrDefault(entry.Path), entry.Default, StringComparison.Ordinal))
                .Select(entry => entry.Path));
        }

        // The profile's own name is the one key a profile cannot set for another.
        profiled.Remove($"{source.Path}{ConfigurationPath.KeyDelimiter}{nameof(RagOptions.Profile)}");
        var note = $"under the default profile, `{RagProfilePresets.NameOf(defaultProfile)}`: each profile sets its own";
        return [.. entries.Select(entry => profiled.Contains(entry.Path) && entry.DefaultNote is null ? entry with { DefaultNote = note } : entry)];
    }

    /// <summary>
    /// The keys at <paramref name="path"/> with the values <paramref name="options"/> holds, read as
    /// those of a new instance are for the catalogue: what one host bound, to set beside what
    /// another bound — a settings file that changes nothing gives the same list as no file.
    /// </summary>
    /// <param name="path">The section's configuration path.</param>
    /// <param name="options">The bound options.</param>
    public static IReadOnlyList<SettingsCatalogEntry> ValuesOf(string path, object options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var walk = new Walk(SettingsDocumentation.Empty, secret: false);
        walk.Node(path, options.GetType(), options, string.Empty);
        return walk.Entries;
    }

    private static List<SettingsCatalogEntry> Walked(SettingsSource source, object? defaults, string description, SettingsDocumentation documentation)
    {
        var walk = new Walk(documentation, source.Secret);
        walk.Node(source.Path, source.Shape, defaults, description);
        return walk.Entries;
    }

    private static bool IsAtOrBelow(string path, string section) =>
        path.Length == section.Length
            ? string.Equals(path, section, StringComparison.OrdinalIgnoreCase)
            : path.Length > section.Length
              && path.StartsWith(section + ConfigurationPath.KeyDelimiter, StringComparison.OrdinalIgnoreCase);

    /// <summary>The internal options type of the e-mail tools, reached through their assembly.</summary>
    private static Type? EmailOptionsType() =>
        typeof(Orkeon.Tools.Email.DependencyInjection.EmailToolsServiceCollectionExtensions).Assembly
            .GetType("Orkeon.Tools.Email.Configuration.EmailToolsOptions");

    /// <summary>A new instance of <paramref name="type"/>, whose property values are the defaults, or null when it has none.</summary>
    private static object? Instance(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target.IsAbstract || target.IsInterface || target.IsValueType || target == typeof(string)
            || target.IsArray || target.ContainsGenericParameters
            || target.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes) is null)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(target, nonPublic: true);
        }
        catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>One section's walk: the keys below a path, as the binder would read them.</summary>
    private sealed class Walk(SettingsDocumentation documentation, bool secret)
    {
        private readonly HashSet<Type> _visiting = [];

        public List<SettingsCatalogEntry> Entries { get; } = [];

        public void Node(string path, Type type, object? value, string description)
        {
            var target = Nullable.GetUnderlyingType(type) ?? type;
            if (IsOpen(target))
            {
                Add(path, Any, description);
            }
            else if (IsValue(target))
            {
                AddValue(path, target, value, description);
            }
            else if (DictionaryValueType(target) is { } valueType)
            {
                Entry($"{path}{ConfigurationPath.KeyDelimiter}{Name}", valueType, value, description);
            }
            else if (typeof(IDictionary).IsAssignableFrom(target))
            {
                Add($"{path}{ConfigurationPath.KeyDelimiter}{Name}", Any, description);
            }
            else if (ItemType(target) is { } itemType)
            {
                Items(path, itemType, value, description);
            }
            else
            {
                Properties(path, target, value, description);
            }
        }

        /// <summary>An entry of a dictionary: a named value, or a named section.</summary>
        private void Entry(string path, Type valueType, object? dictionary, string description)
        {
            var target = Nullable.GetUnderlyingType(valueType) ?? valueType;
            if (IsOpen(target) || IsValue(target))
            {
                var type = IsOpen(target) ? Any : TypeName(target);
                Entries.Add(new SettingsCatalogEntry
                {
                    Path = path,
                    Section = string.Empty,
                    Type = type,
                    Values = Values(target),
                    DefaultNote = secret ? null : NamedDefaults(dictionary),
                    Secret = secret || IsSecret(path),
                    Description = description,
                });
                return;
            }

            Node(path, target, Instance(target), description);
        }

        /// <summary>A list: one key holding values, or sections by index.</summary>
        private void Items(string path, Type itemType, object? list, string description)
        {
            var target = Nullable.GetUnderlyingType(itemType) ?? itemType;
            if (IsOpen(target) || IsValue(target))
            {
                var isSecret = secret || IsSecret(path);
                Entries.Add(new SettingsCatalogEntry
                {
                    Path = path,
                    Section = string.Empty,
                    Type = $"list of {(IsOpen(target) ? Any : TypeName(target))}",
                    Values = Values(target),
                    // A list nothing fills is an empty list, whether its property starts null or empty.
                    Default = isSecret || IsOpen(target) ? null : ListLiteral(list) ?? "[]",
                    Secret = isSecret,
                    Description = description,
                });
                return;
            }

            Node($"{path}{ConfigurationPath.KeyDelimiter}{Index}", target, Instance(target), description);
        }

        private void Properties(string path, Type type, object? instance, string description)
        {
            // A type that contains itself takes anything below its second appearance.
            if (!_visiting.Add(type))
            {
                Add(path, Any, description);
                return;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || property.GetMethod is null)
                    continue;

                // The binder sets a value through its setter only: a value without one is computed,
                // not read. A section or a collection without a setter is filled in place.
                var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (property.SetMethod is not { IsPublic: true } && (IsValue(propertyType) || IsOpen(propertyType)))
                    continue;

                var key = property.GetCustomAttribute<ConfigurationKeyNameAttribute>()?.Name ?? property.Name;
                var value = instance is null ? null : Read(property, instance);
                Node(
                    $"{path}{ConfigurationPath.KeyDelimiter}{key}",
                    property.PropertyType,
                    value ?? (IsValue(propertyType) || IsOpen(propertyType) ? null : Instance(propertyType)),
                    documentation.Of(property));
            }

            _visiting.Remove(type);
        }

        private static object? Read(PropertyInfo property, object instance)
        {
            try
            {
                return property.GetValue(instance);
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        private void AddValue(string path, Type type, object? value, string description)
        {
            var isSecret = secret || IsSecret(path);
            string? literal = null;
            string? note = null;
            if (!isSecret)
            {
                note = MachineDefault(value);
                if (note is null)
                    literal = Literal(value);
            }

            Entries.Add(new SettingsCatalogEntry
            {
                Path = path,
                Section = string.Empty,
                Type = TypeName(type),
                Values = Values(type),
                Default = literal,
                DefaultNote = note,
                Secret = isSecret,
                Description = description,
            });
        }

        private void Add(string path, string type, string description) =>
            Entries.Add(new SettingsCatalogEntry
            {
                Path = path,
                Section = string.Empty,
                Type = type,
                Secret = secret || IsSecret(path),
                Description = description,
            });
    }

    /// <summary>Whether the last name of <paramref name="path"/> is one a secret is kept under.</summary>
    internal static bool IsSecret(string path)
    {
        var cut = path.LastIndexOf(ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
        var key = cut < 0 ? path : path[(cut + 1)..];
        if (key is Name or Index)
            return false;

        return s_secretNames.Contains(key, StringComparer.OrdinalIgnoreCase)
               || key.EndsWith("_API_KEY", StringComparison.OrdinalIgnoreCase)
               || s_secretEndings.Any(ending => key.EndsWith(ending, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A default the machine or the moment gives, in words, never as the value of the machine that
    /// produced the catalogue: a date that is not the type's zero, a text that holds the user's
    /// directory, the temporary directory or the machine's name. A number the machine gives — its
    /// processors — cannot be told from a constant here: the test that holds the produced catalogue
    /// fails on the first machine that counts differently.
    /// </summary>
    private static string? MachineDefault(object? value) => value switch
    {
        DateTime moment when moment != default => "the moment the value is read",
        DateTimeOffset moment when moment != default => "the moment the value is read",
        string text when HoldsMachinePath(text) => "a path of the machine",
        _ => null,
    };

    private static bool HoldsMachinePath(string text)
    {
        if (text.Length == 0)
            return false;

        string?[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar),
            AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar),
            Environment.CurrentDirectory,
            Environment.MachineName,
        ];
        return roots.Any(root => root is { Length: > 1 } && text.Contains(root, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The entries a dictionary holds on a new instance, in words, or null when it is empty.</summary>
    private static string? NamedDefaults(object? dictionary)
    {
        if (dictionary is not IEnumerable entries || dictionary is string)
            return null;

        var named = new List<string>();
        foreach (var entry in entries)
        {
            var type = entry.GetType();
            if (type.GetProperty("Key")?.GetValue(entry) is not { } key)
                continue;

            var value = type.GetProperty("Value")?.GetValue(entry);
            named.Add($"{key} = {Shown(value)}");
        }

        named.Sort(StringComparer.Ordinal);
        return named.Count == 0 ? null : string.Join(", ", named);
    }

    private static string Shown(object? value) => value switch
    {
        null => "null",
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>A value as a JSON literal, or null when there is none to write.</summary>
    private static string? Literal(object? value) => value switch
    {
        null => null,
        string text => JsonSerializer.Serialize(text, s_literals),
        bool flag => flag ? "true" : "false",
        Enum name => JsonSerializer.Serialize(name.ToString(), s_literals),
        float number => float.IsFinite(number) ? number.ToString("R", CultureInfo.InvariantCulture) : null,
        double number => double.IsFinite(number) ? number.ToString("R", CultureInfo.InvariantCulture) : null,
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        sbyte or byte or short or ushort or int or uint or long or ulong =>
            ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
        TimeSpan duration => JsonSerializer.Serialize(duration.ToString("c", CultureInfo.InvariantCulture), s_literals),
        DateTime or DateTimeOffset => null,
        IFormattable formattable => JsonSerializer.Serialize(formattable.ToString(null, CultureInfo.InvariantCulture), s_literals),
        _ => JsonSerializer.Serialize(value.ToString(), s_literals),
    };

    private static string? ListLiteral(object? list)
    {
        if (list is not IEnumerable items || list is string)
            return null;

        var literals = new List<string>();
        foreach (var item in items)
        {
            if (Literal(item) is { } literal)
                literals.Add(literal);
        }

        return $"[{string.Join(", ", literals)}]";
    }

    private static readonly JsonSerializerOptions s_literals = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string[] Values(Type type) => type.IsEnum ? Enum.GetNames(type) : [];

    private static string TypeName(Type type)
    {
        if (type.IsEnum)
            return "enum";
        if (type == typeof(bool))
            return "boolean";
        if (type == typeof(TimeSpan))
            return "duration";
        if (type == typeof(Uri))
            return "uri";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly))
            return "date-time";

        return Type.GetTypeCode(type) switch
        {
            TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 => "integer",
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => "number",
            _ => "string",
        };
    }

    // The four tests below are those of SettingsShape: the catalogue lists what the start validation accepts.

    private static bool IsOpen(Type type) =>
        type == typeof(object)
        || typeof(IConfiguration).IsAssignableFrom(type)
        || type.Namespace is "System.Text.Json" or "System.Text.Json.Nodes";

    private static bool IsValue(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || TypeDescriptor.GetConverter(type).CanConvertFrom(typeof(string));

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
