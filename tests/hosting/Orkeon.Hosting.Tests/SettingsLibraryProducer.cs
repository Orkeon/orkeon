using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Plugins;
using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// Produces the library's part of the settings catalogue — <c>Settings/Catalog/library.json</c>,
/// embedded in <c>Orkeon.Hosting</c> —: what every public registration reads, each section naming
/// the registration a host written in C# reads it through, and what the runner host's own
/// composition reads. Computed: every public extension of <see cref="IServiceCollection"/> in the
/// Orkeon assemblies is called on an empty collection, and the sections it declares are its own.
/// </summary>
internal static class SettingsLibraryProducer
{
    /// <summary>Where the library's file is, from the repository's root.</summary>
    public const string File = $"{SettingsCatalogFiles.RepositoryDirectory}/{SettingsCatalogFiles.Library}";

    private static readonly Lazy<Production> s_production = new(Produce);

    /// <summary>The library's catalogue, as the code of this build declares it.</summary>
    public static SettingsCatalog Catalog => s_production.Value.Catalog;

    /// <summary>The public registrations that could not be called, each with the reason: what the catalogue does not see.</summary>
    public static IReadOnlyList<string> NotCalled => s_production.Value.NotCalled;

    /// <summary>The documentation of every Orkeon assembly beside the running tests.</summary>
    public static SettingsDocumentation Documentation() => SettingsDocumentation.From(ProducedFile.Documentation());

    /// <summary>The configuration every optional registration is made under.</summary>
    public static IConfiguration EverySwitchOn() =>
        new ConfigurationBuilder().AddInMemoryCollection(SettingsCatalogBuilder.EverySwitchOn).Build();

    private sealed record Production(SettingsCatalog Catalog, IReadOnlyList<string> NotCalled);

    private static Production Produce()
    {
        var configuration = EverySwitchOn();
        var sources = new List<SettingsSource>();
        var readers = new Dictionary<string, List<(string Method, int Sections)>>(StringComparer.OrdinalIgnoreCase);
        var notCalled = new List<string>();

        foreach (var method in PublicRegistrations())
        {
            var services = new ServiceCollection();
            if (Arguments(method, services, configuration) is not { } arguments)
            {
                notCalled.Add($"{Shown(method)}: {(method.IsGenericMethodDefinition ? "generic" : "a parameter the producer cannot supply")}");
                continue;
            }

            try
            {
                method.Invoke(null, arguments);
            }
            catch (TargetInvocationException ex)
            {
                notCalled.Add($"{Shown(method)}: threw {ex.InnerException?.GetType().Name}");
                continue;
            }

            var declared = SettingsCatalogBuilder.SourcesOf(services);
            var paths = declared.Select(source => source.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var path in paths)
            {
                if (!readers.TryGetValue(path, out var methods))
                    readers[path] = methods = [];
                methods.Add((method.Name, paths.Count));
            }

            sources.AddRange(declared);
        }

        // Read without a declaration, by a registration that needs a file system to be called.
        sources.Add(new SettingsSource(OrkeonPluginsServiceCollectionExtensions.ConfigurationSection, typeof(OrkeonPluginsOptions)));
        readers[OrkeonPluginsServiceCollectionExtensions.ConfigurationSection] =
            [(nameof(OrkeonPluginsServiceCollectionExtensions.AddOrkeonPlugins), 1)];

        // What the runner host's own composition reads, and no public registration: the sections
        // RunnerHost declares for itself.
        sources.AddRange(SettingsCatalogBuilder.SourcesOf(RunnerHost.Compose(configuration)));

        var described = SettingsCatalogBuilder.Describe(sources, Documentation());
        var catalog = new SettingsCatalog(
            described.Sections.Select(section => section with { Registration = Registration(readers, section.Path) }),
            described.Settings);
        notCalled.Sort(StringComparer.Ordinal);
        return new Production(catalog, notCalled);
    }

    /// <summary>
    /// The registration a section is read through: of those that declare it, the one that declares
    /// the fewest sections — <c>AddOrkeonChromaDb</c> rather than <c>AddOrkeonInfrastructure</c> —,
    /// then the first by name.
    /// </summary>
    private static string? Registration(Dictionary<string, List<(string Method, int Sections)>> readers, string path) =>
        readers.TryGetValue(path, out var methods)
            ? methods.OrderBy(method => method.Sections).ThenBy(method => method.Method, StringComparer.Ordinal).First().Method
            : null;

    /// <summary>Every public extension of <see cref="IServiceCollection"/> the Orkeon assemblies export.</summary>
    private static IEnumerable<MethodInfo> PublicRegistrations() =>
        OrkeonAssemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type is { IsAbstract: true, IsSealed: true })
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => method.IsDefined(typeof(ExtensionAttribute), inherit: false)
                             && method.GetParameters() is [{ ParameterType: var first }, ..]
                             && first == typeof(IServiceCollection))
            .OrderBy(method => method.DeclaringType!.FullName, StringComparer.Ordinal)
            .ThenBy(method => method.Name, StringComparer.Ordinal)
            .ThenBy(method => method.GetParameters().Length);

    /// <summary>The Orkeon assemblies the runner host references, and the two libraries this project adds.</summary>
    private static List<Assembly> OrkeonAssemblies()
    {
        var seen = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>(
        [
            typeof(RunnerHost).Assembly,
            typeof(OrkeonPluginsOptions).Assembly,
            typeof(Orkeon.Cli.Commands.Scripting.DependencyInjection.ConsoleStreamingExtensions).Assembly,
        ]);
        while (pending.TryDequeue(out var assembly))
        {
            if (!seen.TryAdd(assembly.GetName().Name!, assembly))
                continue;

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is { } name && name.StartsWith("Orkeon.", StringComparison.Ordinal) && !seen.ContainsKey(name))
                    pending.Enqueue(Assembly.Load(reference));
            }
        }

        return [.. seen.Values.OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The arguments of a registration called for what it declares: the collection, the configuration,
    /// a parameter's default, null where null is accepted, a delegate that does nothing, a word for a
    /// text (a key, a connection string: nothing is connected) — or null when a parameter is none of
    /// these, or the method is generic.
    /// </summary>
    private static object?[]? Arguments(MethodInfo method, IServiceCollection services, IConfiguration configuration)
    {
        if (method.IsGenericMethodDefinition)
            return null;

        var nullability = new NullabilityInfoContext();
        var parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];
        arguments[0] = services;
        for (var index = 1; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            if (parameter.ParameterType.IsAssignableFrom(typeof(IConfigurationRoot)))
                arguments[index] = configuration;
            else if (parameter.HasDefaultValue)
                arguments[index] = parameter.DefaultValue;
            else if (!parameter.ParameterType.IsValueType && nullability.Create(parameter).WriteState == NullabilityState.Nullable)
                arguments[index] = null;
            else if (typeof(Delegate).IsAssignableFrom(parameter.ParameterType) && NoOp(parameter.ParameterType) is { } nothing)
                arguments[index] = nothing;
            else if (parameter.ParameterType == typeof(string))
                arguments[index] = "catalogue";
            else
                return null;
        }

        return arguments;
    }

    /// <summary>A delegate of <paramref name="type"/> that does nothing and returns its type's default.</summary>
    private static Delegate? NoOp(Type type)
    {
        if (type.GetMethod("Invoke") is not { } invoke || type.ContainsGenericParameters)
            return null;

        var parameters = invoke.GetParameters().Select(parameter => Expression.Parameter(parameter.ParameterType)).ToArray();
        if (parameters.Any(parameter => parameter.IsByRef))
            return null;

        return Expression.Lambda(type, Expression.Default(invoke.ReturnType), parameters).Compile();
    }

    private static string Shown(MethodInfo method) =>
        $"{method.DeclaringType!.Name}.{method.Name}{(method.IsGenericMethodDefinition ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>" : string.Empty)}"
        + $"({string.Join(", ", method.GetParameters().Skip(1).Select(parameter => parameter.ParameterType.Name))})";
}
